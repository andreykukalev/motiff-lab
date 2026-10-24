// Main-thread side of playback (S1-W4, S1-W5, decision record 0006): creates the AudioContext,
// loads the sampled piano, forwards transport commands to the audio-thread engine, and reports the
// playback position to Blazor. Blazor imports this module through JS interop.

import type { EngineMessage, EngineReport, PositionReport, SampleRegion, TransportState } from "./playback-processor.js";

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
    private startWaiters: (() => void)[] = [];
    private disposed = false;

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
        const started = new Promise<void>((resolve) => {
            this.startWaiters.push(resolve);
            setTimeout(resolve, PLAY_CONFIRM_MS);
        });
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

    setLoop(start: number, end: number): void {
        this.post({ type: "loop", start, end });
    }

    clearLoop(): void {
        this.post({ type: "loopOff" });
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

    private post(message: EngineMessage, transfer: Transferable[] = []): void {
        if (!this.disposed) {
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
        const stateChanged = report.state !== this.lastDotnetState;
        if (stateChanged || now - this.lastDotnetReport >= DOTNET_REPORT_MS) {
            this.lastDotnetReport = now;
            this.lastDotnetState = report.state;
            void this.dotnet.invokeMethodAsync("OnPlaybackPosition", Math.round(this.currentTick()), report.state);
        }
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
