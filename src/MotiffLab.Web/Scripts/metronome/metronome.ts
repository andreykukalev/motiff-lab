// A metronome on its own (decision record 0023): clicks at a tempo and a number of beats to the bar, for reading a PDF that
// has no notes to play. The click is the playback engine's (playback-processor.ts): a short decaying sine, higher and louder
// on the first beat of the bar. Clicks are scheduled on the audio clock a little ahead, so a busy page does not make them
// stumble.

interface DotNetRef {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** The slowest and fastest tempo offered, in beats a minute. */
export const MIN_BPM = 30;
export const MAX_BPM = 240;

/** How far ahead clicks are scheduled, and how often the schedule is topped up. */
const AHEAD_SECONDS = 0.12;
const TICK_MS = 25;

const CLICK_SECONDS = 0.05;
const DECAY_SECONDS = 0.012;

/** A tempo kept within the range offered, as a whole number of beats a minute. */
export function clampBpm(bpm: number): number {
    return Math.min(MAX_BPM, Math.max(MIN_BPM, Math.round(Number.isFinite(bpm) ? bpm : 80)));
}

/**
 * The tempo of the last taps: the mean gap of up to the last four, in beats a minute, or null with fewer than two taps or
 * when the last gap is over two seconds (a new start).
 */
export function tappedBpm(times: readonly number[]): number | null {
    if (times.length < 2 || times[times.length - 1] - times[times.length - 2] > 2000) {
        return null;
    }
    const recent = times.slice(-5);
    const gaps = recent.slice(1).map((time, i) => time - recent[i]).filter((gap) => gap > 0 && gap <= 2000);
    if (gaps.length === 0) {
        return null;
    }
    return clampBpm(60000 / (gaps.reduce((sum, gap) => sum + gap, 0) / gaps.length));
}

export class Metronome {
    private context: AudioContext | null = null;
    private accent: AudioBuffer | null = null;
    private beat: AudioBuffer | null = null;
    private timer = 0;
    private nextTime = 0;
    private nextBeat = 0;
    private bpm = 80;
    private beats = 4;
    private taps: number[] = [];
    private readonly shown: number[] = [];

    constructor(private readonly dotnet: DotNetRef) {}

    /** Starts clicking from the first beat of a bar; true when it could. */
    async start(bpm: number, beats: number): Promise<boolean> {
        this.set(bpm, beats);
        try {
            this.context ??= new AudioContext({ latencyHint: "interactive" });
            if (this.context.state !== "running") {
                await this.context.resume();
            }
        } catch (error) {
            console.warn("The metronome could not start its sound.", error);
            return false;
        }
        this.accent ??= this.click(1760, 0.5);
        this.beat ??= this.click(1320, 0.3);
        this.stopTimer();
        this.nextTime = this.context.currentTime + 0.05;
        this.nextBeat = 0;
        this.timer = window.setInterval(this.schedule, TICK_MS);
        this.schedule();
        return true;
    }

    /** Changes the tempo or the beats to the bar while it clicks; the next click follows them. */
    set(bpm: number, beats: number): void {
        this.bpm = clampBpm(bpm);
        this.beats = Math.min(12, Math.max(1, Math.round(beats)));
        if (this.nextBeat >= this.beats) {
            this.nextBeat = 0;
        }
    }

    /** A tap of the tempo button: the tempo of the last few taps, or null until there are two. */
    tap(): number | null {
        const now = performance.now();
        this.taps = [...this.taps.filter((time) => now - time <= 8000), now];
        return tappedBpm(this.taps);
    }

    stop(): void {
        this.stopTimer();
        void this.dotnet.invokeMethodAsync("OnMetronomeBeat", -1);
    }

    dispose(): void {
        this.stopTimer();
        void this.context?.close();
        this.context = null;
    }

    private stopTimer(): void {
        window.clearInterval(this.timer);
        this.timer = 0;
        for (const id of this.shown.splice(0)) {
            window.clearTimeout(id);
        }
    }

    private readonly schedule = (): void => {
        const context = this.context;
        if (context === null || this.timer === 0) {
            return;
        }
        while (this.nextTime < context.currentTime + AHEAD_SECONDS) {
            const source = context.createBufferSource();
            source.buffer = this.nextBeat === 0 ? this.accent : this.beat;
            source.connect(context.destination);
            source.start(this.nextTime);
            // The beat shows on screen when it sounds.
            const beat = this.nextBeat;
            const id = window.setTimeout(() => {
                this.shown.splice(this.shown.indexOf(id), 1);
                void this.dotnet.invokeMethodAsync("OnMetronomeBeat", beat);
            }, Math.max(0, (this.nextTime - context.currentTime) * 1000));
            this.shown.push(id);
            this.nextTime += 60 / this.bpm;
            this.nextBeat = (this.nextBeat + 1) % this.beats;
        }
    };

    /** The playback engine's click as a buffer: a sine at a frequency, dying away fast. */
    private click(frequency: number, gain: number): AudioBuffer {
        const context = this.context!;
        const length = Math.round(CLICK_SECONDS * context.sampleRate);
        const buffer = context.createBuffer(1, length, context.sampleRate);
        const data = buffer.getChannelData(0);
        const omega = (2 * Math.PI * frequency) / context.sampleRate;
        const decay = 1 / (DECAY_SECONDS * context.sampleRate);
        for (let i = 0; i < length; i++) {
            data[i] = Math.sin(omega * i) * Math.exp(-i * decay) * gain;
        }
        return buffer;
    }
}

export function createMetronome(dotnet: DotNetRef): Metronome {
    return new Metronome(dotnet);
}
