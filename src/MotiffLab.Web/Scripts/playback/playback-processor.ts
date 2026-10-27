// Playback engine for the audio thread (S1-W4, S1-W5, decision record 0006).
//
// The engine owns musical time: it advances a canonical tick position frame by frame through the
// tempo map, so loops wrap on the exact frame, tempo changes apply at the exact tick, and the main
// thread (Blazor, garbage collection, rendering) cannot delay a note. It renders a small sampled
// piano and a synthesised metronome into the output buffer.
//
// The Engine class has no Web Audio dependency, so tests run it in Node; the processor that wraps
// it is registered only inside an AudioWorkletGlobalScope. This file is a single module on purpose:
// it is loaded with audioWorklet.addModule, and keeping imports out of it avoids differences between
// browsers in how worklet modules load.

export const TICKS_PER_QUARTER = 480;
export const MIN_RATE = 0.25;
export const MAX_RATE = 1.5;
export const MAX_VOICES = 96;

export type TransportState = "stopped" | "countingIn" | "playing";

export interface SampleRegion {
    /** MIDI note the sample was recorded at. */
    pitch: number;
    /** Tuning correction in cents, applied when the sample plays. */
    tuneCents: number;
    /** Mono PCM at the engine's sample rate, starting at the attack. */
    data: Float32Array;
}

export interface TimelineMessage {
    type: "timeline";
    /** Pairs: tick, microseconds per quarter. The first tick is 0. */
    tempos: number[];
    /** Pairs: tick, 1 for a downbeat or 0. */
    beats: number[];
    /** Quads: start tick, duration, beat ticks, beats per bar. */
    bars: number[];
    end: number;
}

export interface SourceMessage {
    type: "source";
    id: string;
    /** Quints ordered by onset: onset tick, duration, pitch, velocity, track. */
    notes: number[];
}

/**
 * One pass of an alternating loop: the sources that play and the tick range [start, end) they loop
 * over. For example the original phrase over its bars, then the variation over the bars it takes.
 */
export interface LoopPass {
    ids: string[];
    start: number;
    end: number;
}

export type EngineMessage =
    | { type: "samples"; regions: SampleRegion[] }
    | TimelineMessage
    | SourceMessage
    | { type: "removeSource"; id: string }
    | { type: "activeSources"; ids: string[] }
    | { type: "play" }
    | { type: "pause" }
    | { type: "seek"; tick: number }
    | { type: "rate"; value: number }
    | { type: "loop"; start: number; end: number }
    | { type: "loopOff" }
    | { type: "alternate"; passes: LoopPass[] }
    | { type: "metronome"; on: boolean }
    | { type: "countIn"; bars: number }
    | { type: "tracks"; muted: number[]; soloed: number[] }
    | { type: "noteOn"; pitch: number; velocity: number }
    | { type: "noteOff"; pitch: number };

/**
 * Notes the player strikes by hand (clicking the roll or the keyboard). They sound at once, over
 * playback or without it, until released; transport, source and track changes leave them alone.
 */
export const LIVE_SOURCE = "live";
const LIVE_TRACK = -1;

export interface PositionReport {
    type: "position";
    /** Canonical tick at the start of `frame`. */
    tick: number;
    state: TransportState;
    /** Audio frame the tick belongs to (the processor's currentFrame plus the offset in the block). */
    frame: number;
    /** Ticks per second at the current tempo and rate; 0 when not playing. */
    ticksPerSecond: number;
    loopStart: number;
    loopEnd: number;
    /** The pass of an alternating loop that is playing, from 0; -1 when the engine is not alternating. */
    pass: number;
}

export type EngineReport = PositionReport | { type: "ended"; tick: number };

interface Timeline {
    tempoTicks: Float64Array;
    tempoUspq: Float64Array;
    beatTicks: Float64Array;
    beatDown: Uint8Array;
    barStart: Float64Array;
    barBeatTicks: Float64Array;
    barBeats: Float64Array;
    end: number;
}

interface Source {
    onset: Float64Array;
    duration: Float64Array;
    pitch: Uint8Array;
    velocity: Uint8Array;
    track: Int32Array;
    cursor: number;
}

interface Voice {
    sourceId: string;
    pitch: number;
    track: number;
    data: Float32Array;
    read: number;
    step: number;
    gain: number;
    level: number;
    /** Frame in the current block where the voice starts (0 once it has started). */
    startAt: number;
    /** Frame in the current block where the release starts, or Infinity. */
    releaseAt: number;
    released: boolean;
    endTick: number;
    order: number;
}

interface Click {
    startAt: number;
    age: number;
    frequency: number;
    gain: number;
}

const ATTACK_FRAMES = 32;
const RELEASE_SECONDS = 0.12;
const CLICK_SECONDS = 0.05;
const SILENCE = 1e-4;
const MASTER_GAIN = 0.55;

/** Rounding slack, in frames, so an event that falls exactly on a frame is not pushed to the next one by float error. */
const FRAME_EPSILON = 1e-6;

/** Index of the last value at or before x in an ascending array, or -1. */
function lastAtOrBefore(values: Float64Array, x: number): number {
    let lo = 0;
    let hi = values.length - 1;
    let found = -1;
    while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        if (values[mid] <= x) {
            found = mid;
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }
    return found;
}

/** Index of the first value at or after x in an ascending array, or the length. */
function firstAtOrAfter(values: Float64Array, x: number): number {
    let lo = 0;
    let hi = values.length;
    while (lo < hi) {
        const mid = (lo + hi) >> 1;
        if (values[mid] < x) {
            lo = mid + 1;
        } else {
            hi = mid;
        }
    }
    return lo;
}

export class Engine {
    readonly sampleRate: number;
    reportInterval: number;

    private readonly regionByPitch: (SampleRegion | null)[] = new Array(128).fill(null);
    private readonly stepByPitch = new Float64Array(128);
    private readonly sources = new Map<string, Source>();
    private active: string[] = [];
    private timeline: Timeline | null = null;

    private state: TransportState = "stopped";
    private position = 0;
    private rate = 1;
    private loopStart = 0;
    private loopEnd = 0;
    /** An alternating loop: each pass plays its sources over its range, then the next pass starts on the same frame. */
    private passes: LoopPass[] | null = null;
    private pass = -1;
    private metronome = false;
    private countInBars = 0;
    private muted = new Set<number>();
    private soloed = new Set<number>();

    private beatCursor = 0;
    private countInTotal = 0;
    private countInDone = 0;
    private countInElapsed = 0;
    private countInFramesPerBeat = 0;
    private countInBeatsPerBar = 0;

    private voices: Voice[] = [];
    private clicks: Click[] = [];
    private voiceOrder = 0;
    private readonly releaseFactor: number;
    private framesSinceReport = 0;
    /** While rendering: the block's first frame and how far into it the transport has advanced. */
    private blockFrame: number | null = null;
    private blockOffset = 0;
    private reports: EngineReport[] = [];

    constructor(sampleRate: number) {
        this.sampleRate = sampleRate;
        this.reportInterval = Math.round(sampleRate / 30);
        this.releaseFactor = Math.exp(-1 / (RELEASE_SECONDS * sampleRate));
    }

    get transportState(): TransportState {
        return this.state;
    }

    get tick(): number {
        return this.position;
    }

    get voiceCount(): number {
        return this.voices.length;
    }

    /** Reports produced since the last call: positions about 30 times a second, state changes, the end. */
    takeReports(): EngineReport[] {
        const reports = this.reports;
        this.reports = [];
        return reports;
    }

    handle(message: EngineMessage): void {
        switch (message.type) {
            case "samples":
                this.setSamples(message.regions);
                break;
            case "timeline":
                this.setTimeline(message);
                break;
            case "source":
                this.setSource(message);
                break;
            case "removeSource":
                this.sources.delete(message.id);
                this.active = this.active.filter((id) => id !== message.id);
                this.releaseWhere((voice) => voice.sourceId === message.id, 0);
                break;
            case "activeSources":
                this.stopAlternating();
                this.setActive(message.ids);
                break;
            case "play":
                this.play();
                break;
            case "pause":
                this.pause();
                break;
            case "seek":
                this.seek(message.tick);
                break;
            case "rate":
                this.rate = Math.min(MAX_RATE, Math.max(MIN_RATE, message.value));
                this.requestReport();
                break;
            case "loop":
                this.stopAlternating();
                this.setLoop(message.start, message.end);
                break;
            case "alternate":
                this.alternate(message.passes);
                break;
            case "loopOff":
                this.stopAlternating();
                this.loopStart = 0;
                this.loopEnd = 0;
                this.requestReport();
                break;
            case "metronome":
                this.metronome = message.on;
                break;
            case "countIn":
                this.countInBars = Math.max(0, Math.floor(message.bars));
                break;
            case "tracks":
                this.muted = new Set(message.muted);
                this.soloed = new Set(message.soloed);
                this.releaseWhere((voice) => voice.sourceId !== LIVE_SOURCE && !this.isAudible(voice.track), 0);
                break;
            case "noteOn":
                if (message.pitch >= 0 && message.pitch < 128) {
                    this.startVoice(LIVE_SOURCE, message.pitch, Math.min(127, Math.max(1, message.velocity)), LIVE_TRACK, Infinity, 0);
                }
                break;
            case "noteOff":
                this.releaseWhere((voice) => voice.sourceId === LIVE_SOURCE && voice.pitch === message.pitch, 0);
                break;
        }
    }

    /**
     * Renders one block, overwriting the output arrays. Reports are stamped with `blockFrame`
     * plus the offset in the block.
     */
    render(left: Float32Array, right: Float32Array | null, blockFrame: number): void {
        const frames = left.length;
        let done = 0;
        this.blockFrame = blockFrame;
        while (done < frames) {
            this.blockOffset = done;
            if (this.state === "playing") {
                done += this.advancePlaying(done, frames - done);
            } else if (this.state === "countingIn") {
                done += this.advanceCountIn(done, frames - done);
            } else {
                done = frames;
            }
        }

        left.fill(0);
        this.renderVoices(left, frames);
        this.renderClicks(left, frames);
        for (let i = 0; i < frames; i++) {
            left[i] *= MASTER_GAIN;
        }
        right?.set(left);

        this.blockFrame = null;

        this.framesSinceReport += frames;
        if (this.framesSinceReport >= this.reportInterval) {
            this.framesSinceReport -= this.reportInterval;
            this.report(blockFrame + frames);
        }
    }

    private setSamples(regions: SampleRegion[]): void {
        const sorted = [...regions].sort((a, b) => a.pitch - b.pitch);
        for (let pitch = 0; pitch < 128; pitch++) {
            let best: SampleRegion | null = null;
            for (const region of sorted) {
                if (best === null || Math.abs(region.pitch - pitch) < Math.abs(best.pitch - pitch)) {
                    best = region;
                }
            }
            this.regionByPitch[pitch] = best;
            this.stepByPitch[pitch] = best === null ? 0 : Math.pow(2, ((pitch - best.pitch) * 100 + best.tuneCents) / 1200);
        }
    }

    private setTimeline(message: TimelineMessage): void {
        const tempoCount = message.tempos.length / 2;
        const beatCount = message.beats.length / 2;
        const barCount = message.bars.length / 4;
        const timeline: Timeline = {
            tempoTicks: new Float64Array(tempoCount),
            tempoUspq: new Float64Array(tempoCount),
            beatTicks: new Float64Array(beatCount),
            beatDown: new Uint8Array(beatCount),
            barStart: new Float64Array(barCount),
            barBeatTicks: new Float64Array(barCount),
            barBeats: new Float64Array(barCount),
            end: message.end,
        };
        for (let i = 0; i < tempoCount; i++) {
            timeline.tempoTicks[i] = message.tempos[2 * i];
            timeline.tempoUspq[i] = message.tempos[2 * i + 1];
        }
        for (let i = 0; i < beatCount; i++) {
            timeline.beatTicks[i] = message.beats[2 * i];
            timeline.beatDown[i] = message.beats[2 * i + 1];
        }
        for (let i = 0; i < barCount; i++) {
            timeline.barStart[i] = message.bars[4 * i];
            timeline.barBeatTicks[i] = message.bars[4 * i + 2];
            timeline.barBeats[i] = message.bars[4 * i + 3];
        }
        if (tempoCount === 0) {
            timeline.tempoTicks = Float64Array.of(0);
            timeline.tempoUspq = Float64Array.of(500_000);
        }

        this.timeline = timeline;
        this.stopAlternating();
        this.releaseWhere(() => true, 0);
        this.state = "stopped";
        this.position = 0;
        this.loopStart = 0;
        this.loopEnd = 0;
        this.relocate(false);
        this.requestReport();
    }

    private setSource(message: SourceMessage): void {
        const count = message.notes.length / 5;
        const source: Source = {
            onset: new Float64Array(count),
            duration: new Float64Array(count),
            pitch: new Uint8Array(count),
            velocity: new Uint8Array(count),
            track: new Int32Array(count),
            cursor: 0,
        };
        for (let i = 0; i < count; i++) {
            source.onset[i] = message.notes[5 * i];
            source.duration[i] = message.notes[5 * i + 1];
            source.pitch[i] = message.notes[5 * i + 2];
            source.velocity[i] = message.notes[5 * i + 3];
            source.track[i] = message.notes[5 * i + 4];
        }

        this.sources.set(message.id, source);
        this.releaseWhere((voice) => voice.sourceId === message.id, 0);
        source.cursor = firstAtOrAfter(source.onset, this.position);
        if (this.state === "playing" && this.active.includes(message.id)) {
            this.chase(message.id, source);
        }
    }

    /** A/B switching and layering: the position is kept, and notes already sounding in a newly active source are struck. */
    private setActive(ids: string[]): void {
        const next = ids.filter((id) => this.sources.has(id));
        const added = next.filter((id) => !this.active.includes(id));
        this.active = next;
        this.releaseWhere((voice) => voice.sourceId !== LIVE_SOURCE && !next.includes(voice.sourceId), 0);
        for (const id of added) {
            const source = this.sources.get(id)!;
            source.cursor = firstAtOrAfter(source.onset, this.position);
            if (this.state === "playing") {
                this.chase(id, source);
            }
        }
    }

    private play(): void {
        if (this.timeline === null || this.state !== "stopped") {
            return;
        }

        if (this.hasLoop() && (this.position < this.loopStart || this.position >= this.loopEnd)) {
            this.position = this.loopStart;
        } else if (this.position >= this.timeline.end) {
            this.position = 0;
        }
        this.relocate(false);

        if (this.countInBars > 0) {
            const bar = Math.max(0, lastAtOrBefore(this.timeline.barStart, this.position));
            const beatTicks = this.timeline.barBeatTicks.length > 0 ? this.timeline.barBeatTicks[bar] : TICKS_PER_QUARTER;
            const beatsPerBar = this.timeline.barBeats.length > 0 ? this.timeline.barBeats[bar] : 4;
            this.countInBeatsPerBar = beatsPerBar;
            this.countInTotal = beatsPerBar * this.countInBars;
            this.countInDone = 0;
            this.countInElapsed = 0;
            this.countInFramesPerBeat = beatTicks / this.ticksPerFrame(this.tempoIndex(this.position));
            this.state = "countingIn";
        } else {
            this.startPlaying();
        }
        this.requestReport();
    }

    private startPlaying(): void {
        this.state = "playing";
        for (const id of this.active) {
            this.chase(id, this.sources.get(id)!);
        }
    }

    private pause(): void {
        if (this.state === "stopped") {
            return;
        }
        this.state = "stopped";
        this.releaseWhere((voice) => voice.sourceId !== LIVE_SOURCE, 0);
        this.requestReport();
    }

    private seek(tick: number): void {
        const end = this.timeline?.end ?? 0;
        this.position = Math.min(Math.max(0, tick), end);
        this.releaseWhere((voice) => voice.sourceId !== LIVE_SOURCE, 0);
        this.relocate(this.state === "playing");
        this.requestReport();
    }

    private setLoop(start: number, end: number): void {
        const limit = this.timeline?.end ?? 0;
        const loopStart = Math.min(Math.max(0, start), limit);
        const loopEnd = Math.min(Math.max(0, end), limit);
        if (loopEnd <= loopStart) {
            this.loopStart = 0;
            this.loopEnd = 0;
        } else {
            this.loopStart = loopStart;
            this.loopEnd = loopEnd;
        }
        this.requestReport();
    }

    /**
     * Starts an alternating loop at the start of its first pass: that pass's sources play over its
     * range, and when the range ends the next pass starts on the same frame, with its own sources and
     * range, then the first again. Passes whose range is empty once kept to the piece are dropped. A
     * loop, a loop off, a choice of sources or a new timeline ends it.
     */
    private alternate(passes: LoopPass[]): void {
        const limit = this.timeline?.end ?? 0;
        const kept = passes
            .map((pass) => ({ ids: [...pass.ids], start: Math.min(Math.max(0, pass.start), limit), end: Math.min(Math.max(0, pass.end), limit) }))
            .filter((pass) => pass.end > pass.start);
        if (kept.length === 0) {
            this.stopAlternating();
            this.loopStart = 0;
            this.loopEnd = 0;
            this.requestReport();
            return;
        }

        this.passes = kept;
        this.pass = 0;
        // The seek below releases what was sounding and strikes the first pass's notes already sounding there.
        this.active = kept[0].ids.filter((id) => this.sources.has(id));
        this.loopStart = kept[0].start;
        this.loopEnd = kept[0].end;
        this.seek(kept[0].start);
    }

    private stopAlternating(): void {
        this.passes = null;
        this.pass = -1;
    }

    private hasLoop(): boolean {
        return this.loopEnd > this.loopStart;
    }

    private isAudible(track: number): boolean {
        return !this.muted.has(track) && (this.soloed.size === 0 || this.soloed.has(track));
    }

    private tempoIndex(tick: number): number {
        return Math.max(0, lastAtOrBefore(this.timeline!.tempoTicks, tick));
    }

    private ticksPerFrame(tempoIndex: number): number {
        const microsecondsPerQuarter = this.timeline!.tempoUspq[tempoIndex];
        return (TICKS_PER_QUARTER * this.rate * 1e6) / (microsecondsPerQuarter * this.sampleRate);
    }

    /** Points every cursor at the current position; with chase, strikes notes already sounding there. */
    private relocate(chase: boolean): void {
        for (const [id, source] of this.sources) {
            source.cursor = firstAtOrAfter(source.onset, this.position);
            if (chase && this.active.includes(id)) {
                this.chase(id, source);
            }
        }
        this.beatCursor = this.timeline === null ? 0 : firstAtOrAfter(this.timeline.beatTicks, this.position);
    }

    /** Strikes the notes of a source that began before the position and are still sounding, so a switch or seek is not silent. */
    private chase(sourceId: string, source: Source): void {
        for (let i = 0; i < source.cursor; i++) {
            const end = source.onset[i] + source.duration[i];
            if (source.onset[i] < this.position && end > this.position && this.isAudible(source.track[i])) {
                this.startVoice(sourceId, source.pitch[i], source.velocity[i], source.track[i], end, 0);
            }
        }
    }

    private advanceCountIn(offset: number, available: number): number {
        while (this.countInDone < this.countInTotal) {
            const at = Math.max(0, Math.ceil(this.countInDone * this.countInFramesPerBeat - this.countInElapsed - FRAME_EPSILON));
            if (at >= available) {
                break;
            }
            const accent = this.countInDone % this.countInBeatsPerBar === 0;
            this.startClick(offset + at, accent);
            this.countInDone++;
        }

        const remaining = this.countInTotal * this.countInFramesPerBeat - this.countInElapsed;
        const used = Math.max(0, Math.ceil(remaining - FRAME_EPSILON));
        if (used < available) {
            this.countInElapsed += used;
            this.startPlaying();
            this.blockOffset = offset + used;
            this.requestReport();
            return used;
        }

        this.countInElapsed += available;
        return available;
    }

    /** Plays up to `available` frames, stopping early at a tempo change, the loop end or the end of the piece. */
    private advancePlaying(offset: number, available: number): number {
        const timeline = this.timeline!;
        const tempo = this.tempoIndex(this.position);
        const perFrame = this.ticksPerFrame(tempo);
        const nextTempo = tempo + 1 < timeline.tempoTicks.length ? timeline.tempoTicks[tempo + 1] : Infinity;
        const looping = this.hasLoop() && this.position < this.loopEnd;
        const stop = looping ? this.loopEnd : timeline.end;
        const boundary = Math.min(nextTempo, stop);

        const toBoundary = this.framesUntil(boundary, perFrame);
        const frames = Math.min(available, toBoundary);
        const until = this.position + frames * perFrame;
        const limit = Math.min(until, boundary);

        this.scheduleNotes(offset, perFrame, limit);
        this.scheduleBeats(offset, perFrame, limit);
        this.scheduleReleases(offset, perFrame, until);

        if (frames < toBoundary) {
            this.position = until;
            return frames;
        }

        // The block reached the boundary; carry the overshoot (under one frame) across it.
        const overshoot = Math.max(0, until - boundary);
        if (boundary === nextTempo && boundary < stop) {
            this.position = boundary + overshoot * (this.ticksPerFrame(tempo + 1) / perFrame);
        } else if (looping) {
            this.releaseWhere((voice) => voice.sourceId !== LIVE_SOURCE, offset + frames);
            const alternating = this.passes !== null;
            if (this.passes !== null) {
                // The next pass starts on this frame, with its own sources and range.
                this.pass = (this.pass + 1) % this.passes.length;
                const next = this.passes[this.pass];
                this.active = next.ids.filter((id) => this.sources.has(id));
                this.loopStart = next.start;
                this.loopEnd = next.end;
            }
            this.position = this.loopStart;
            this.relocate(false);
            this.position += overshoot * (this.ticksPerFrame(this.tempoIndex(this.loopStart)) / perFrame);
            if (alternating) {
                // Tell the page at once which pass is playing, so the views follow it.
                this.blockOffset = offset + frames;
                this.requestReport();
            }
        } else {
            this.state = "stopped";
            this.reports.push({ type: "ended", tick: timeline.end });
            this.position = this.hasLoop() ? this.loopStart : 0;
            this.relocate(false);
            this.blockOffset = offset + frames;
            this.requestReport();
        }
        return frames;
    }

    /** Whole frames from the position to the first frame at or after a tick. */
    private framesUntil(tick: number, perFrame: number): number {
        return Math.max(0, Math.ceil((tick - this.position) / perFrame - FRAME_EPSILON));
    }

    private scheduleNotes(offset: number, perFrame: number, limit: number): void {
        for (const id of this.active) {
            const source = this.sources.get(id)!;
            while (source.cursor < source.onset.length && source.onset[source.cursor] < limit) {
                const i = source.cursor++;
                if (!this.isAudible(source.track[i])) {
                    continue;
                }
                const frame = offset + this.framesUntil(source.onset[i], perFrame);
                this.startVoice(id, source.pitch[i], source.velocity[i], source.track[i], source.onset[i] + source.duration[i], frame);
            }
        }
    }

    private scheduleBeats(offset: number, perFrame: number, limit: number): void {
        const timeline = this.timeline!;
        while (this.beatCursor < timeline.beatTicks.length && timeline.beatTicks[this.beatCursor] < limit) {
            const i = this.beatCursor++;
            if (this.metronome) {
                const frame = offset + this.framesUntil(timeline.beatTicks[i], perFrame);
                this.startClick(frame, timeline.beatDown[i] === 1);
            }
        }
    }

    private scheduleReleases(offset: number, perFrame: number, until: number): void {
        for (const voice of this.voices) {
            if (!voice.released && voice.endTick < until) {
                const frame = offset + this.framesUntil(voice.endTick, perFrame);
                voice.releaseAt = Math.max(frame, voice.startAt);
                voice.released = true;
            }
        }
    }

    private startVoice(sourceId: string, pitch: number, velocity: number, track: number, endTick: number, frame: number): void {
        const region = this.regionByPitch[pitch];
        if (region === null) {
            return;
        }

        // A key struck again is damped first, as on a piano.
        this.releaseWhere((voice) => voice.sourceId === sourceId && voice.pitch === pitch, frame);

        if (this.voices.length >= MAX_VOICES) {
            this.stealVoice();
        }

        const loudness = velocity / 127;
        this.voices.push({
            sourceId,
            pitch,
            track,
            data: region.data,
            read: 0,
            step: this.stepByPitch[pitch],
            gain: 0.08 + 0.92 * loudness * loudness,
            level: 1,
            startAt: frame,
            releaseAt: Infinity,
            released: false,
            endTick,
            order: this.voiceOrder++,
        });
    }

    private stealVoice(): void {
        let victim = 0;
        for (let i = 1; i < this.voices.length; i++) {
            const a = this.voices[i];
            const b = this.voices[victim];
            if (a.released !== b.released ? a.released : a.order < b.order) {
                victim = i;
            }
        }
        this.voices.splice(victim, 1);
    }

    private releaseWhere(predicate: (voice: Voice) => boolean, frame: number): void {
        for (const voice of this.voices) {
            if (predicate(voice) && voice.releaseAt > frame) {
                voice.releaseAt = Math.max(frame, voice.startAt);
                voice.released = true;
            }
        }
    }

    private startClick(frame: number, accent: boolean): void {
        this.clicks.push({ startAt: frame, age: 0, frequency: accent ? 1760 : 1320, gain: accent ? 0.5 : 0.3 });
    }

    private renderVoices(out: Float32Array, frames: number): void {
        const release = this.releaseFactor;
        const kept: Voice[] = [];
        for (const voice of this.voices) {
            const data = voice.data;
            const last = data.length - 1;
            let read = voice.read;
            let level = voice.level;
            let alive = true;
            for (let i = voice.startAt; i < frames; i++) {
                if (i >= voice.releaseAt) {
                    level *= release;
                    if (level < SILENCE) {
                        alive = false;
                        break;
                    }
                }
                const index = read | 0;
                if (index >= last) {
                    alive = false;
                    break;
                }
                const fraction = read - index;
                const sample = data[index] + (data[index + 1] - data[index]) * fraction;
                const attack = read < ATTACK_FRAMES ? read / ATTACK_FRAMES : 1;
                out[i] += sample * voice.gain * level * attack;
                read += voice.step;
            }

            if (alive) {
                voice.read = read;
                voice.level = level;
                voice.startAt = 0;
                voice.releaseAt = voice.releaseAt === Infinity ? Infinity : Math.max(0, voice.releaseAt - frames);
                kept.push(voice);
            }
        }
        this.voices = kept;
    }

    private renderClicks(out: Float32Array, frames: number): void {
        const length = Math.round(CLICK_SECONDS * this.sampleRate);
        const decay = 1 / (0.012 * this.sampleRate);
        const kept: Click[] = [];
        for (const click of this.clicks) {
            const omega = (2 * Math.PI * click.frequency) / this.sampleRate;
            let age = click.age;
            for (let i = click.startAt; i < frames && age < length; i++, age++) {
                out[i] += Math.sin(omega * age) * Math.exp(-age * decay) * click.gain;
            }
            if (age < length) {
                click.age = age;
                click.startAt = 0;
                kept.push(click);
            }
        }
        this.clicks = kept;
    }

    /** Reports the transport now: inside a block, at the frame it has reached; between blocks, -1 for the processor to stamp. */
    private requestReport(): void {
        this.report(this.blockFrame === null ? -1 : this.blockFrame + this.blockOffset);
    }

    private report(frame: number): void {
        const playing = this.state === "playing" && this.timeline !== null;
        const ticksPerSecond = playing ? this.ticksPerFrame(this.tempoIndex(this.position)) * this.sampleRate : 0;
        this.reports.push({
            type: "position",
            tick: this.position,
            state: this.state,
            frame,
            ticksPerSecond,
            loopStart: this.loopStart,
            loopEnd: this.loopEnd,
            pass: this.pass,
        });
    }
}

// --- AudioWorklet registration -------------------------------------------------------------------

interface AudioWorkletProcessorLike {
    readonly port: MessagePort;
}

declare const registerProcessor: ((name: string, processor: new () => AudioWorkletProcessorLike) => void) | undefined;
declare const AudioWorkletProcessor: (new () => AudioWorkletProcessorLike) | undefined;
declare const sampleRate: number;
declare const currentFrame: number;

if (typeof registerProcessor === "function" && typeof AudioWorkletProcessor === "function") {
    class PlaybackProcessor extends AudioWorkletProcessor {
        private readonly engine = new Engine(sampleRate);

        constructor() {
            super();
            this.port.onmessage = (event: MessageEvent<EngineMessage>) => {
                this.engine.handle(event.data);
                this.flush();
            };
        }

        process(_inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
            const output = outputs[0];
            if (output !== undefined && output.length > 0) {
                this.engine.render(output[0], output.length > 1 ? output[1] : null, currentFrame);
            }
            this.flush();
            return true;
        }

        private flush(): void {
            for (const report of this.engine.takeReports()) {
                if (report.type === "position" && report.frame < 0) {
                    report.frame = currentFrame;
                }
                this.port.postMessage(report);
            }
        }
    }

    registerProcessor("motiff-playback", PlaybackProcessor);
}
