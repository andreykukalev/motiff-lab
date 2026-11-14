// The metronome (decision record 0023; v0.5.5, #521 section 15): one for the score and the PDF view. It clicks at once,
// with or without Play, at the piece tempo (never times the playback pace) until the player gives it a tempo of its own,
// by holding its button and sliding or on the PDF view's pill; that tempo and the beats to the bar are kept per piece on
// the device. While the score plays, its own clicks go silent and the playback engine clicks the beats heard, so the two
// never drift apart at another pace; once playback stops it clicks its own tempo again. The click is the playback
// engine's (playback-processor.ts): a short decaying sine, higher and louder on the first beat of the bar. Clicks are
// scheduled on the audio clock a little ahead, so a busy page does not make them stumble.

import { HOLD_MS, heldStill } from "../layout/long-press.js";
import { token } from "../layout/tokens.js";

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

/** The piece tempo while nothing states one: music-core's PlaybackTempo.PieceDefaultPerMinute (version 0.5.5). */
export const PIECE_DEFAULT_BPM = 80;

/** The beats to the bar the pill goes through. */
export const BEAT_CHOICES: readonly number[] = [2, 3, 4, 6];

/** A tempo kept within the range offered, as a whole number of beats a minute. */
export function clampBpm(bpm: number): number {
    return Math.min(MAX_BPM, Math.max(MIN_BPM, Math.round(Number.isFinite(bpm) ? bpm : PIECE_DEFAULT_BPM)));
}

/**
 * The tempo a slide sets after the hold: one beat a minute for each `step` pixels, up or right faster, down or left
 * slower, within the range offered. As the iPad's MetronomeTempo.dragged.
 */
export function draggedBpm(from: number, dx: number, dy: number, step: number): number {
    return step > 0 ? clampBpm(from + Math.round((dx - dy) / step)) : clampBpm(from);
}

/** What is kept for a piece on the device: the player's own tempo, or null while the metronome follows the piece tempo. */
export interface KeptMetronome {
    own: number | null;
    beats: number;
}

/** "bpm,beats" for a tempo the player gave the metronome, "-,beats" while it follows the piece tempo; null for anything else. */
export function readKept(value: string | null | undefined): KeptMetronome | null {
    const parts = value?.split(",") ?? [];
    if (parts.length !== 2 || !/^\d+$/.test(parts[1]) || !(parts[0] === "-" || /^\d+$/.test(parts[0]))) {
        return null;
    }
    const beats = Number(parts[1]);
    return { own: parts[0] === "-" ? null : clampBpm(Number(parts[0])), beats: BEAT_CHOICES.includes(beats) ? beats : 4 };
}

export function keptText(own: number | null, beats: number): string {
    return `${own ?? "-"},${beats}`;
}

/** The metronome's tempo and beats for the piece open: the rules, apart from the sound. */
export class MetronomeSetting {
    piece: string | null = null;
    pieceBpm = PIECE_DEFAULT_BPM;
    own: number | null = null;
    beats = 4;

    /** The tempo heard: the player's own for the piece, else the piece tempo. */
    get bpm(): number {
        return this.own ?? this.pieceBpm;
    }

    /**
     * The piece's setting as kept, at its piece tempo in beats a minute and its first bar's beats. A view that has neither
     * (the PDF view) keeps those given last for the same piece; a piece never given one is at the default of 80.
     */
    load(piece: string | null, kept: string | null, pieceBpm: number | null = null, barBeats: number | null = null): void {
        if (piece === null || piece !== this.piece) {
            this.pieceBpm = PIECE_DEFAULT_BPM;
            this.beats = 4;
        }
        this.piece = piece;
        if (pieceBpm !== null) {
            this.pieceBpm = clampBpm(pieceBpm);
        }
        const read = piece === null ? null : readKept(kept);
        this.own = read?.own ?? null;
        if (read !== null) {
            this.beats = read.beats;
        } else if (barBeats !== null && BEAT_CHOICES.includes(barBeats)) {
            this.beats = barBeats;
        }
    }

    /** A new tempo or beats to the bar, and what to keep for the piece: the piece tempo itself is following the piece again. */
    change(bpm: number, beats: number): string {
        const clamped = clampBpm(bpm);
        this.own = clamped === this.pieceBpm ? null : clamped;
        this.beats = BEAT_CHOICES.includes(beats) ? beats : 4;
        return keptText(this.own, this.beats);
    }
}

/** What the page shows of the metronome; `kept` is what to keep for the piece after a change. */
export interface MetronomeState {
    running: boolean;
    bpm: number;
    beats: number;
    own: number | null;
    kept?: string;
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
    readonly setting = new MetronomeSetting();
    private context: AudioContext | null = null;
    private accent: AudioBuffer | null = null;
    private beat: AudioBuffer | null = null;
    private timer = 0;
    private nextTime = 0;
    private nextBeat = 0;
    private taps: number[] = [];
    private readonly shown: number[] = [];
    private running = false;
    /** Whether the score plays now: the metronome's own clicks are silent then. */
    private locked = false;

    constructor(private dotnet: DotNetRef) {}

    /** Who shows the beats now: the one metronome serves the score and the PDF view. */
    bind(dotnet: DotNetRef): void {
        this.dotnet = dotnet;
    }

    state(kept?: string): MetronomeState {
        return { running: this.running, bpm: this.setting.bpm, beats: this.setting.beats, own: this.setting.own, kept };
    }

    load(piece: string | null, kept: string | null, pieceBpm: number | null, barBeats: number | null): MetronomeState {
        this.setting.load(piece, kept, pieceBpm, barBeats);
        this.following();
        return this.state();
    }

    async toggle(): Promise<MetronomeState> {
        if (this.running) {
            return this.stop();
        }
        this.running = await this.sound();
        if (this.running && !this.locked) {
            this.startClicks();
        }
        return this.state();
    }

    /** A new tempo or beats to the bar, heard from the next click, with what to keep for the piece. */
    change(bpm: number, beats: number): MetronomeState {
        const kept = this.setting.change(bpm, beats);
        this.following();
        return this.state(kept);
    }

    /** A tap of the tempo button: the tempo of the last few taps, once there are two; null before. */
    tap(): MetronomeState | null {
        const now = performance.now();
        this.taps = [...this.taps.filter((time) => now - time <= 8000), now];
        const tapped = tappedBpm(this.taps);
        return tapped === null ? null : this.change(tapped, this.setting.beats);
    }

    /** Silent while the score plays; when it stops, the clicks start again from the first beat of a bar. */
    lock(playing: boolean): void {
        if (playing === this.locked) {
            return;
        }
        this.locked = playing;
        if (!this.running) {
            return;
        }
        if (playing) {
            this.stopTimer();
            void this.dotnet.invokeMethodAsync("OnMetronomeBeat", -1);
        } else {
            this.startClicks();
        }
    }

    /** The audio context running, and the clicks made; false when the browser gives no sound. */
    private async sound(): Promise<boolean> {
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
        return true;
    }

    /** Clicks from the first beat of a bar. */
    private startClicks(): void {
        if (this.context === null) {
            return;
        }
        this.stopTimer();
        this.nextTime = this.context.currentTime + 0.05;
        this.nextBeat = 0;
        this.timer = window.setInterval(this.schedule, TICK_MS);
        this.schedule();
    }

    /** The next click follows a new tempo or beats. */
    private following(): void {
        if (this.nextBeat >= this.setting.beats) {
            this.nextBeat = 0;
        }
    }

    stop(): MetronomeState {
        this.running = false;
        this.stopTimer();
        void this.dotnet.invokeMethodAsync("OnMetronomeBeat", -1);
        return this.state();
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
            this.nextTime += 60 / this.setting.bpm;
            this.nextBeat = (this.nextBeat + 1) % this.setting.beats;
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

let shared: Metronome | null = null;

/** The one metronome of the page, shown by `dotnet`. */
export function createMetronome(dotnet: DotNetRef): Metronome {
    shared ??= new Metronome(dotnet);
    shared.bind(dotnet);
    return shared;
}

/**
 * The metronome's buttons (data-metronome-hold, on the score's pill and the PDF view's bar): a tap is the button's own
 * click; held still for HOLD_MS, the button shows the tempo (OnMetronomeHold), a slide sets it with draggedBpm, and the
 * release keeps it (OnMetronomeHeld) and is not also a click. A finger, the Pencil and a mouse alike. The press is the
 * button's alone, so the pill's own long press (its settings) does not open from it.
 */
export class MetronomeHold {
    private press: { id: number; x: number; y: number; timer: number; held: boolean; from: number; bpm: number } | null = null;
    private swallowClick = false;

    constructor(
        private readonly metronome: Metronome,
        private readonly target: DotNetRef,
    ) {
        document.addEventListener("pointerdown", this.onDown, true);
        document.addEventListener("pointermove", this.onMove, true);
        document.addEventListener("pointerup", this.onUp, true);
        document.addEventListener("pointercancel", this.onCancel, true);
        document.addEventListener("contextmenu", this.onContextMenu, true);
        document.addEventListener("click", this.onClick, true);
    }

    dispose(): void {
        this.onCancel();
        document.removeEventListener("pointerdown", this.onDown, true);
        document.removeEventListener("pointermove", this.onMove, true);
        document.removeEventListener("pointerup", this.onUp, true);
        document.removeEventListener("pointercancel", this.onCancel, true);
        document.removeEventListener("contextmenu", this.onContextMenu, true);
        document.removeEventListener("click", this.onClick, true);
    }

    private static button(target: EventTarget | null): HTMLElement | null {
        return target instanceof Element ? target.closest<HTMLElement>("[data-metronome-hold]") : null;
    }

    private readonly onDown = (event: PointerEvent): void => {
        this.swallowClick = false;
        const button = MetronomeHold.button(event.target);
        if (button === null || !event.isPrimary || event.button !== 0 || button.matches(":disabled")) {
            return;
        }
        event.stopPropagation();
        this.onCancel();
        const timer = window.setTimeout(() => {
            if (this.press === null) {
                return;
            }
            this.press.held = true;
            this.press.from = this.metronome.setting.bpm;
            this.press.bpm = this.press.from;
            void this.target.invokeMethodAsync("OnMetronomeHold", this.press.bpm);
        }, HOLD_MS);
        this.press = { id: event.pointerId, x: event.clientX, y: event.clientY, timer, held: false, from: 0, bpm: 0 };
    };

    private readonly onMove = (event: PointerEvent): void => {
        const press = this.press;
        if (press === null || event.pointerId !== press.id) {
            return;
        }
        const dx = event.clientX - press.x;
        const dy = event.clientY - press.y;
        if (!press.held) {
            if (!heldStill(dx, dy)) {
                this.onCancel();
            }
            return;
        }
        event.preventDefault();
        const bpm = draggedBpm(press.from, dx, dy, parseFloat(token("s-1")));
        if (bpm !== press.bpm) {
            press.bpm = bpm;
            void this.target.invokeMethodAsync("OnMetronomeHold", bpm);
        }
    };

    private readonly onUp = (event: PointerEvent): void => {
        const press = this.press;
        if (press === null || event.pointerId !== press.id) {
            return;
        }
        window.clearTimeout(press.timer);
        this.press = null;
        if (press.held) {
            this.swallowClick = true;
            void this.target.invokeMethodAsync("OnMetronomeHeld", press.bpm);
        }
    };

    private readonly onCancel = (): void => {
        const press = this.press;
        if (press === null) {
            return;
        }
        window.clearTimeout(press.timer);
        this.press = null;
        if (press.held) {
            void this.target.invokeMethodAsync("OnMetronomeHold", null);
        }
    };

    /** The system's long-press menu does not open on the button, nor the pill's settings. */
    private readonly onContextMenu = (event: MouseEvent): void => {
        if (MetronomeHold.button(event.target) !== null) {
            event.preventDefault();
            event.stopPropagation();
        }
    };

    /** The release after a hold is not also a tap on the button. */
    private readonly onClick = (event: MouseEvent): void => {
        if (!this.swallowClick) {
            return;
        }
        this.swallowClick = false;
        if (MetronomeHold.button(event.target) !== null) {
            event.preventDefault();
            event.stopPropagation();
        }
    };
}

let hold: MetronomeHold | null = null;

/** The metronome's buttons held and slid, for the page's one metronome; once for the page. */
export function holdMetronome(dotnet: DotNetRef): void {
    hold ??= new MetronomeHold(createMetronome(dotnet), dotnet);
}
