// Main-thread side of playback (S1-W4, S1-W5, decision record 0006): creates the AudioContext,
// loads the sampled piano, forwards transport commands to the audio-thread engine, and reports the
// playback position to Blazor. Blazor imports this module through JS interop.

import type { EngineMessage, EngineReport, LoopPass, PositionReport, SampleRegion, TransportState } from "./playback-processor.js";
import type { ModelEvent, ModelState, Step } from "./playback-model.js";
import { PlaybackOwner, type EnginePlace, type LoopAfter } from "./playback-owner.js";
import { SAMPLE_RATE, decodeMono, peaksOf } from "../import/recording.js";
import { openRecording } from "../storage/recording-store.js";

interface DotNetObject {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

interface PianoManifest {
    samples: { pitch: number; file: string; tuneCents: number }[];
}

/** A timeline from music-core's PlaybackScore, packed into flat number arrays. */
export interface TimelinePayload {
    tempos: number[];
    beats: number[];
    bars: number[];
    end: number;
}

/** Blazor is told the position at most this often while playing; the canvas reads it every frame through currentTick. */
const DOTNET_REPORT_MS = 50;

/** How long play waits at most for the engine to report it started, for example when nothing is left to play. */
const PLAY_CONFIRM_MS = 250;

/** How many loudness bars a second of a recording has on the waveform under the notes. */
export const PEAKS_PER_SECOND = 20;

/** A recording loaded to be heard as it sounds: its length and its loudness, PEAKS_PER_SECOND bars a second. */
export interface RecordingSound {
    milliseconds: number;
    peaks: number[];
}

/** The attack starts where the sample first rises above this level; encoders add silence before it. */
const ONSET_THRESHOLD = 0.003;

const ONSET_LEAD_SECONDS = 0.002;

export class Player {
    private readonly context: AudioContext;
    private readonly node: AudioWorkletNode;
    private readonly dotnet: DotNetObject;
    private last: PositionReport | null = null;
    private lastDotnetReport = 0;
    private lastDotnetState: TransportState | null = null;
    private lastDotnetPass = -1;
    private lastDotnetTick = -1;
    /** Set when the page sends the engine a command, so the position after it goes to Blazor even if it did not move. */
    private dotnetStale = true;
    private startWaiters: (() => void)[] = [];
    private disposed = false;
    private recordingKey: string | null = null;
    private recordingSound: RecordingSound | null = null;
    /** The playback model of v0.5.2 (#495 UX-10, playback-owner.ts): the selection and Цикл, while it holds. */
    private readonly owner = new PlaybackOwner((step) => this.perform(step));
    /** What Blazor was last told of the model, so it hears again only when that changes. */
    private modelReported = "";

    private constructor(context: AudioContext, node: AudioWorkletNode, dotnet: DotNetObject) {
        this.context = context;
        this.node = node;
        this.dotnet = dotnet;
        this.node.port.onmessage = (event: MessageEvent<EngineReport>) => this.onReport(event.data);
    }

    static async create(dotnet: DotNetObject, pianoManifestUrl: string): Promise<Player> {
        // iOS plays Web Audio through the ringer channel unless the session says otherwise.
        const audioSession = (navigator as unknown as { audioSession?: { type: string } }).audioSession;
        if (audioSession !== undefined) {
            audioSession.type = "playback";
        }

        const context = new AudioContext({ latencyHint: "interactive" });
        await context.audioWorklet.addModule(new URL("./playback-processor.js", import.meta.url));
        const node = new AudioWorkletNode(context, "motiff-playback", {
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [2],
        });

        // A gentle limiter: chords at full velocity stay clean without lowering everything else.
        const limiter = new DynamicsCompressorNode(context, { threshold: -6, knee: 6, ratio: 12, attack: 0.002, release: 0.15 });
        node.connect(limiter).connect(context.destination);

        const player = new Player(context, node, dotnet);
        player.unlockOnGesture();
        await player.loadPiano(pianoManifestUrl);
        return player;
    }

    setTimeline(timeline: TimelinePayload): void {
        this.owner.reset();
        this.post({ type: "timeline", ...timeline });
    }

    setSource(id: string, notes: number[]): void {
        this.post({ type: "source", id, notes });
    }

    removeSource(id: string): void {
        this.post({ type: "removeSource", id });
    }

    setActiveSources(ids: string[]): void {
        this.post({ type: "activeSources", ids });
    }

    /**
     * Starts playback, and resolves once the engine reports it has started (or after PLAY_CONFIRM_MS).
     * Some browsers (Safari) pass messages to the audio thread only while the page's thread is free,
     * so callers wait for this before heavy work such as rendering (#66).
     */
    async play(): Promise<void> {
        await this.resume();
        const started = this.starting();
        this.post({ type: "play" });
        await started;
    }

    pause(): void {
        this.post({ type: "pause" });
    }

    seek(tick: number): void {
        this.post({ type: "seek", tick });
    }

    setRate(value: number): void {
        this.post({ type: "rate", value });
    }

    /** Loops a range; with `once`, plays it once and stops back at its start. While the model holds, another owner takes the loop. */
    setLoop(start: number, end: number, once = false): void {
        this.owner.take();
        this.post({ type: "loop", start, end, once });
    }

    clearLoop(): void {
        this.post({ type: "loopOff" });
    }

    /** Loops each pass in turn, its sources over its range, from the start of the first (#43). */
    setAlternation(passes: LoopPass[]): void {
        this.owner.take();
        this.post({ type: "alternate", passes });
    }

    // The playback model (v0.5.2, #495 UX-10, slice W1): Blazor sends the events, the rules decide, the engine gets the steps.
    // Each event answers the engine's loop after it as Blazor keeps it: null untouched, [] none, [start, end] a passage.

    /** Whether the model holds: the experiment on and the page not in focus. Turned on, Blazor hears where it stands. */
    holdModel(on: boolean): void {
        this.owner.held = on;
        this.modelReported = "";
        this.reportModel();
    }

    /** T9–T11: the selection as the range it plays, or none (null). The position never moves. */
    setSelection(start: number | null, end: number | null): number[] | null {
        return this.apply({ type: "select", selection: start === null || end === null ? null : { start, end } });
    }

    /** T1–T3: the one Play button, Space or a pedal; resolves once the engine plays when it was told to. */
    async press(): Promise<number[] | null> {
        if (this.state() !== "stopped") {
            return this.apply({ type: "press" });
        }
        await this.resume();
        const started = this.starting();
        const loop = this.apply({ type: "press" });
        await started;
        return loop;
    }

    /** T7, T8: Цикл on or off; the same as it is changes nothing. */
    setSelectionLoop(on: boolean): number[] | null {
        return on === this.owner.state(this.place()).loop ? null : this.apply({ type: "cycle" });
    }

    /** T12: the black playhead moved by a tap (a beat, a bar, a note, a fact's place): out of the selection, Цикл turns off. */
    moveTo(tick: number): number[] | null {
        return this.apply({ type: "seek", at: tick });
    }

    /** The engine loops the model's passage again once another owner of the loop has ended. */
    holdLoop(): number[] {
        const loop = this.owner.hold(this.place());
        return loop === null ? [] : [loop.start, loop.end];
    }

    /** The model's state now, the position as heard; null while it does not hold. */
    model(): ModelState | null {
        return this.owner.held ? this.owner.state({ tick: this.currentTick(), transport: this.state() }) : null;
    }

    setMetronome(on: boolean): void {
        this.post({ type: "metronome", on });
    }

    setCountIn(bars: number): void {
        this.post({ type: "countIn", bars });
    }

    setTracks(muted: number[], soloed: number[]): void {
        this.post({ type: "tracks", muted, soloed });
    }

    /**
     * Loads the sound kept on the device under `key` (0024, scenario 2), with where the places of its notes are heard
     * (`ticks` paired with `milliseconds`); null when no sound is kept there or this browser cannot decode it. The sound
     * plays in place of the notes once `hearRecording(true)`.
     */
    async loadRecording(key: string, ticks: number[], milliseconds: number[]): Promise<RecordingSound | null> {
        // The same recording is already with the engine: its samples went there and need not be decoded again.
        if (key === this.recordingKey && this.recordingSound !== null) {
            return this.recordingSound;
        }
        const blob = await openRecording(key);
        const mono = blob === null ? null : await decodeMono(await blob.arrayBuffer());
        if (mono === null) {
            this.forgetRecording();
            return null;
        }
        const length = Math.round((mono.length * 1000) / SAMPLE_RATE);
        const sound = { milliseconds: length, peaks: peaksOf(mono, Math.max(1, Math.round((length / 1000) * PEAKS_PER_SECOND))) };
        this.post({ type: "recording", samples: mono, sampleRate: SAMPLE_RATE, ticks, milliseconds }, [mono.buffer]);
        this.recordingKey = key;
        this.recordingSound = sound;
        return sound;
    }

    /** The recording sounds in place of the notes, at its own speed, or the notes again. */
    hearRecording(on: boolean): void {
        this.post({ type: "recordingOn", on });
    }

    forgetRecording(): void {
        this.recordingKey = null;
        this.recordingSound = null;
        this.post({ type: "recordingOn", on: false });
        this.post({ type: "recording", samples: null, sampleRate: SAMPLE_RATE, ticks: [], milliseconds: [] });
    }

    /** Strikes a key by hand, over playback or without it, until noteOff (the roll and keyboard call this). */
    noteOn(pitch: number, velocity = 96): void {
        void this.resume();
        this.post({ type: "noteOn", pitch, velocity });
    }

    noteOff(pitch: number): void {
        this.post({ type: "noteOff", pitch });
    }

    /**
     * The tick being heard now: the engine's last report moved on by the time since, less the
     * output latency, and wrapped into the loop. For drawing the piano roll and keyboard every frame.
     */
    currentTick(): number {
        const report = this.last;
        if (report === null) {
            return 0;
        }
        if (report.state !== "playing" || report.ticksPerSecond === 0) {
            return report.tick;
        }

        const reportTime = report.frame / this.context.sampleRate;
        const latency = this.context.outputLatency ?? this.context.baseLatency ?? 0;
        let tick = report.tick + (this.context.currentTime - reportTime - latency) * report.ticksPerSecond;
        if (report.loopEnd > report.loopStart && report.tick >= report.loopStart && report.tick < report.loopEnd) {
            const length = report.loopEnd - report.loopStart;
            tick = report.loopStart + ((((tick - report.loopStart) % length) + length) % length);
        }
        return Math.max(0, tick);
    }

    state(): TransportState {
        return this.last?.state ?? "stopped";
    }

    async dispose(): Promise<void> {
        if (this.disposed) {
            return;
        }
        this.disposed = true;
        this.node.port.onmessage = null;
        this.node.disconnect();
        await this.context.close();
    }

    /** Where the engine is now, as heard. */
    private place(): EnginePlace {
        return { tick: Math.round(this.currentTick()), transport: this.state() };
    }

    /** An event of the model; Blazor hears at once where the engine will be, before the engine reports it. */
    private apply(event: ModelEvent): number[] | null {
        const decided: { loop: LoopAfter; engine: EnginePlace } = this.owner.apply(event, this.place());
        this.reportModel(decided.engine);
        const loop = decided.loop;
        return loop === undefined ? null : loop === null ? [] : [loop.start, loop.end];
    }

    /** A step of the model to the engine. */
    private perform(step: Step): void {
        switch (step.type) {
            case "play":
                this.post({ type: "play" });
                break;
            case "pause":
                this.post({ type: "pause" });
                break;
            case "seek":
                this.post({ type: "seek", tick: step.at });
                break;
            case "loop":
                this.post({ type: "loop", start: step.start, end: step.end });
                break;
            case "loopOff":
                this.post({ type: "loopOff" });
                break;
            case "scrub":
            case "scrubEnd":
                break;
        }
    }

    /** Blazor hears the model's loop, context and the places that do not move with the position, when one changes. */
    private reportModel(engine: EnginePlace = this.place()): void {
        if (!this.owner.held) {
            return;
        }
        const report = this.owner.report(engine);
        const key = JSON.stringify(report);
        if (key !== this.modelReported) {
            this.modelReported = key;
            void this.dotnet.invokeMethodAsync("OnPlaybackModel", report.loop, report.context, report.inside, report.waits, report.at);
        }
    }

    /** Resolves once the engine reports it plays, or after PLAY_CONFIRM_MS. */
    private starting(): Promise<void> {
        return new Promise<void>((resolve) => {
            this.startWaiters.push(resolve);
            setTimeout(resolve, PLAY_CONFIRM_MS);
        });
    }

    private post(message: EngineMessage, transfer: Transferable[] = []): void {
        if (!this.disposed) {
            this.dotnetStale = true;
            this.node.port.postMessage(message, transfer);
        }
    }

    private async resume(): Promise<void> {
        if (this.context.state !== "running") {
            await this.context.resume();
        }
    }

    /** Browsers start audio only after a user gesture; resume on the first one, whatever it is. */
    private unlockOnGesture(): void {
        const unlock = (): void => {
            void this.resume().then(() => {
                if (this.context.state === "running") {
                    for (const type of ["pointerdown", "keydown", "touchend"]) {
                        document.removeEventListener(type, unlock, true);
                    }
                }
            });
        };
        for (const type of ["pointerdown", "keydown", "touchend"]) {
            document.addEventListener(type, unlock, true);
        }
    }

    private async loadPiano(manifestUrl: string): Promise<void> {
        const manifestResponse = await fetch(manifestUrl);
        if (!manifestResponse.ok) {
            throw new Error(`The piano samples could not be loaded (${manifestResponse.status}).`);
        }
        const manifest = (await manifestResponse.json()) as PianoManifest;
        const base = new URL(manifestUrl, document.baseURI);

        const regions = await Promise.all(
            manifest.samples.map(async (sample): Promise<SampleRegion> => {
                const response = await fetch(new URL(sample.file, base));
                if (!response.ok) {
                    throw new Error(`A piano sample could not be loaded (${sample.file}, ${response.status}).`);
                }
                const buffer = await this.context.decodeAudioData(await response.arrayBuffer());
                return { pitch: sample.pitch, tuneCents: sample.tuneCents, data: trimToOnset(buffer) };
            }),
        );

        this.post({ type: "samples", regions }, regions.map((region) => region.data.buffer));
    }

    private onReport(report: EngineReport): void {
        if (report.type === "ended") {
            return;
        }

        this.last = report;
        if (report.state !== "stopped" && this.startWaiters.length > 0) {
            for (const resolve of this.startWaiters.splice(0)) {
                resolve();
            }
        }

        const now = performance.now();
        const tick = Math.round(this.currentTick());
        // A new state or a new pass of an alternating loop goes to Blazor at once, so the views follow what is heard.
        // A position that has not moved, stopped or paused, is not sent again: each report draws the whole page
        // over in Blazor, which would keep the page's thread busy and make scrolling stutter on an iPad.
        const changed = report.state !== this.lastDotnetState || report.pass !== this.lastDotnetPass || this.dotnetStale;
        const moved = tick !== this.lastDotnetTick;
        if (changed || (moved && now - this.lastDotnetReport >= DOTNET_REPORT_MS)) {
            this.lastDotnetReport = now;
            this.lastDotnetState = report.state;
            this.lastDotnetPass = report.pass;
            this.lastDotnetTick = tick;
            this.dotnetStale = false;
            void this.dotnet.invokeMethodAsync("OnPlaybackPosition", tick, report.state, report.pass);
        }
        this.reportModel();
    }
}

/** Mono copy of the decoded sample from just before its attack, so every note starts on time. */
function trimToOnset(buffer: AudioBuffer): Float32Array {
    const channels = Array.from({ length: buffer.numberOfChannels }, (_, i) => buffer.getChannelData(i));
    let peak = 0;
    for (const channel of channels) {
        for (let i = 0; i < channel.length; i++) {
            peak = Math.max(peak, Math.abs(channel[i]));
        }
    }

    const threshold = peak * ONSET_THRESHOLD;
    let onset = 0;
    search: for (let i = 0; i < buffer.length; i++) {
        for (const channel of channels) {
            if (Math.abs(channel[i]) > threshold) {
                onset = i;
                break search;
            }
        }
    }

    const start = Math.max(0, onset - Math.round(ONSET_LEAD_SECONDS * buffer.sampleRate));
    const mono = new Float32Array(buffer.length - start);
    for (const channel of channels) {
        for (let i = 0; i < mono.length; i++) {
            mono[i] += channel[start + i] / channels.length;
        }
    }
    return mono;
}

/** Entry point for Blazor: creates the AudioContext, loads the piano and returns the player. */
export function createPlayer(dotnet: DotNetObject, pianoManifestUrl: string): Promise<Player> {
    return Player.create(dotnet, pianoManifestUrl);
}
