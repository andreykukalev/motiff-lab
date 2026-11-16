// Takes the sound out of an audio or video recording in the browser, for transcription (0014): the
// file is decoded here and mixed to mono at 22,050 Hz, the player sees its length and loudness and
// chooses the stretch to hear, and only that stretch's sound is sent, never the video. The decoded
// sound stays in this module, as 16-bit samples, until the next recording or `forget`. A WAV file, and the AAC or MP3
// sound of an MP4, M4A or MOV file where the browser has WebCodecs, are decoded a block at a time (decode.ts), so a
// recording of an hour fits; anything else is decoded whole, as long as that fits this device. When a browser cannot
// decode a file, the caller sends the file itself and the server's ffmpeg reads it.

import { SAMPLE_RATE } from "./resample.js";
import { decodeMono, decodePicked, encodeWav, mixToMono, mostWholeMinutes, wavOf } from "./sound.js";

export { SAMPLE_RATE, decodeMono, encodeWav, mixToMono };

/** The sound of a stretch of a recording as a 16-bit mono WAV file, and how many seconds it holds. */
export interface Sound {
    wav: Uint8Array;
    seconds: number;
}

/**
 * A decoded recording: its length, how loud it is along the way, an address the page can play it from, and a
 * fingerprint of its sound, by which parts already heard are found again. With `problem` "tooLong" it was not
 * decoded: decoding it whole would take more memory than this device has to spare, and only its length is told.
 */
export interface Loaded {
    milliseconds: number;
    peaks: number[];
    url: string;
    fingerprint: string;
    problem?: "tooLong";
    mostMinutes?: number;
}

/** How many loudness bars the overview of a recording has, whatever its length. */
export const PEAK_COUNT = 400;

let held: Int16Array | null = null;
let heldUrl: string | null = null;

/**
 * The loudest sample in each of `count` equal stretches of `samples`, between 0 and 1 once multiplied by `scale`
 * (1 / 32768 for 16-bit samples), for drawing the shape of a recording; fewer when there are fewer samples.
 */
export function peaksOf(samples: Float32Array | Int16Array, count: number, scale = 1): number[] {
    const bars = Math.min(count, samples.length);
    const peaks: number[] = [];
    for (let bar = 0; bar < bars; bar++) {
        const from = Math.floor((samples.length * bar) / bars);
        const to = Math.floor((samples.length * (bar + 1)) / bars);
        let peak = 0;
        for (let i = from; i < to; i++) {
            peak = Math.max(peak, Math.abs(samples[i]));
        }
        peaks.push(Math.min(1, Math.round(peak * scale * 1000) / 1000));
    }
    return peaks;
}

/** How many samples of the held sound, at SAMPLE_RATE, lie before a millisecond of it. */
function sampleAt(milliseconds: number, length: number): number {
    return Math.max(0, Math.min(length, Math.round((milliseconds * SAMPLE_RATE) / 1000)));
}

/** The held sound from one millisecond to another as a WAV file, or null when no recording is held. */
export function soundBetween(fromMilliseconds: number, toMilliseconds: number): Sound | null {
    if (!held) {
        return null;
    }
    const from = sampleAt(fromMilliseconds, held.length);
    const to = Math.max(from, sampleAt(toMilliseconds, held.length));
    return { wav: wavOf(held.subarray(from, to), SAMPLE_RATE), seconds: (to - from) / SAMPLE_RATE };
}

/** The stretch of a level of loudness (RecordingParts.LevelMilliseconds in music-core). */
export const LEVEL_MILLISECONDS = 100;

/**
 * How loud the held sound is in each 100 ms from one millisecond to another, as whole numbers (the mean of the
 * samples' size, 0 to 32767), for music-core to put the seams of a long recording at quiet moments. Empty when
 * nothing is held.
 */
export function levelsBetween(fromMilliseconds: number, toMilliseconds: number): number[] {
    if (!held) {
        return [];
    }
    const levels: number[] = [];
    for (let at = fromMilliseconds; at < toMilliseconds; at += LEVEL_MILLISECONDS) {
        const from = sampleAt(at, held.length);
        const to = sampleAt(Math.min(toMilliseconds, at + LEVEL_MILLISECONDS), held.length);
        let sum = 0;
        for (let i = from; i < to; i++) {
            sum += Math.abs(held[i]);
        }
        levels.push(to > from ? Math.round(sum / (to - from)) : 0);
    }
    return levels;
}

/**
 * A fingerprint of 16-bit sound: its length and two 32-bit hashes of every sample, as hex. The same sound decoded
 * again gives the same fingerprint, so the parts of it already heard are found again (0014).
 */
export function fingerprintOf(samples: Int16Array): string {
    let a = 0x811c9dc5;
    let b = 0x9e3779b9;
    for (let i = 0; i < samples.length; i++) {
        a = Math.imul(a ^ (samples[i] & 0xffff), 0x01000193);
        b = Math.imul(b ^ samples[i], 0x85ebca6b) ^ (b >>> 13);
    }
    const hex = (value: number) => (value >>> 0).toString(16).padStart(8, "0");
    return `${samples.length.toString(16)}-${hex(a)}${hex(b)}`;
}

/**
 * The sound of the recording held as a mono WAV file, for keeping it on the device when the recording becomes a
 * source (0024, scenario 2): small next to a video, and any browser plays it back. Null when none is held.
 */
export function heldRecording(): Blob | null {
    return held ? new Blob([wavOf(held, SAMPLE_RATE) as BlobPart], { type: "audio/wav" }) : null;
}

/**
 * The held sound from one millisecond to another as a mono WAV file (version 0.5, a recording of several pieces): the
 * stretch sent to be heard, kept on the device so its pieces can each have their own sound. Null when none is held.
 */
export function heldStretch(fromMilliseconds: number, toMilliseconds: number): Blob | null {
    const sound = soundBetween(fromMilliseconds, toMilliseconds);
    return sound ? new Blob([sound.wav as BlobPart], { type: "audio/wav" }) : null;
}

/** Lets go of the held recording and its address, and unwires its editor. */
export function forget(): void {
    detach();
    held = null;
    if (heldUrl && typeof URL.revokeObjectURL === "function") {
        URL.revokeObjectURL(heldUrl);
    }
    heldUrl = null;
}

/**
 * Decodes the file picked in `input` on this device and holds its sound, or returns null when this browser
 * cannot decode it (or has no file there). Nothing is sent from here.
 */
export async function load(input: HTMLInputElement, maxBytes: number): Promise<Loaded | null> {
    forget();
    const file = input.files?.[0];
    if (!file) {
        return null;
    }
    const mono = await decodePicked(file, maxBytes);
    if (typeof mono === "number") {
        return { milliseconds: mono, peaks: [], url: "", fingerprint: "", problem: "tooLong", mostMinutes: mostWholeMinutes() };
    }
    if (mono === null) {
        return null;
    }
    held = mono;
    heldUrl = URL.createObjectURL(file);
    return {
        milliseconds: Math.round((mono.length * 1000) / SAMPLE_RATE),
        peaks: peaksOf(mono, PEAK_COUNT, 1 / 32768),
        url: heldUrl,
        fingerprint: fingerprintOf(mono),
    };
}

/** Where the player is in the recording, in milliseconds. */
export function positionOf(player: HTMLMediaElement): number {
    return Math.round(player.currentTime * 1000);
}

/** The .NET page the selection editor reports to. */
interface Page {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

let stopAt: (() => void) | null = null;
let stopPlayer: HTMLMediaElement | null = null;

/** Forgets where playing the selection was to pause, as when the player moves the position by hand. */
function clearStop(): void {
    if (stopAt && stopPlayer) {
        stopPlayer.removeEventListener("timeupdate", stopAt);
    }
    stopAt = null;
    stopPlayer = null;
}

/** Plays the recording from one millisecond and pauses it at another, so the player hears the chosen stretch. */
export async function playBetween(player: HTMLMediaElement, fromMilliseconds: number, toMilliseconds: number): Promise<void> {
    clearStop();
    const listener = () => {
        if (player.currentTime * 1000 >= toMilliseconds) {
            player.pause();
            clearStop();
        }
    };
    stopAt = listener;
    stopPlayer = player;
    player.addEventListener("timeupdate", listener);
    player.currentTime = fromMilliseconds / 1000;
    try {
        await player.play();
    } catch {
        // The browser would not play without a tap; the play button is one.
    }
}

/** Plays from where the player is, or pauses. */
export async function toggle(player: HTMLMediaElement): Promise<void> {
    if (!player.paused) {
        player.pause();
        return;
    }
    try {
        await player.play();
    } catch {
        // As above.
    }
}

/** Moves the play position to a millisecond of the recording, within it. */
export function seek(player: HTMLMediaElement, milliseconds: number): void {
    clearStop();
    const length = Number.isFinite(player.duration) ? player.duration * 1000 : Number.MAX_SAFE_INTEGER;
    player.currentTime = Math.max(0, Math.min(length, milliseconds)) / 1000;
}

/** Moves the play position by some milliseconds, back when negative. */
export function seekBy(player: HTMLMediaElement, milliseconds: number): void {
    seek(player, player.currentTime * 1000 + milliseconds);
}

/** A time in the recording as m:ss.t, or h:mm:ss.t from an hour, as the page writes it. */
export function clock(milliseconds: number): string {
    const tenths = Math.floor(Math.max(0, milliseconds) / 100);
    const seconds = Math.floor(tenths / 10);
    const pad = (n: number) => String(n).padStart(2, "0");
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor(seconds / 60) % 60;
    const whole = hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds % 60)}` : `${Math.floor(seconds / 60)}:${pad(seconds % 60)}`;
    return `${whole}.${tenths % 10}`;
}

/** The millisecond of a recording `length` long under a point `x` across an element from `left`, `width` wide. */
export function millisecondAt(x: number, left: number, width: number, length: number): number {
    if (width <= 0) {
        return 0;
    }
    return Math.round(Math.max(0, Math.min(1, (x - left) / width)) * length);
}

/** Whether a key press belongs to the element focused, which takes typing, rather than to the player. */
function typing(target: EventTarget | null): boolean {
    if (!(target instanceof HTMLElement)) {
        return false;
    }
    if (target.isContentEditable || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement) {
        return true;
    }
    return target instanceof HTMLInputElement && !["checkbox", "radio", "file", "button", "range"].includes(target.type);
}

/** How far an arrow key moves the play position, and an edge of the selection. */
export const ARROW_SEEK_MILLISECONDS = 5000;
export const ARROW_EDGE_MILLISECONDS = 1000;

let detachEditor: (() => void) | null = null;

/** Unwires the selection editor. */
export function detach(): void {
    detachEditor?.();
    detachEditor = null;
}

/**
 * Wires up the selection editor (0014), as in an audio trimmer: dragging an edge of the selection or the whole of
 * it tells the page where it would go, and the page answers by drawing it there; tapping or dragging the strip
 * above the shape, or tapping the shape, moves the play position, which follows playing. The space bar plays or
 * pauses, the arrow keys move the position 5 s, or a focused edge 1 s. The editor holds the selection in
 * `data-from`, `data-to` and `data-length`.
 */
export function attach(editor: HTMLElement, player: HTMLMediaElement, page: Page): void {
    detach();
    const wave = editor.querySelector<HTMLElement>(".wave")!;
    const scrub = editor.querySelector<HTMLElement>(".scrub")!;
    const playhead = editor.querySelector<HTMLElement>(".playhead");
    const knob = editor.querySelector<HTMLElement>(".knob");
    const section = editor.closest("section") ?? editor.parentElement!;
    const number = (name: string) => Number(editor.dataset[name] ?? 0);
    const at = (event: PointerEvent) => {
        const box = wave.getBoundingClientRect();
        return millisecondAt(event.clientX, box.left, box.width, number("length"));
    };

    // One drag report in flight at a time, the latest waiting behind it, so a fast drag never queues up.
    let busy = false;
    let waiting: [string, number] | null = null;
    const report = (edge: string, milliseconds: number) => {
        waiting = [edge, milliseconds];
        if (busy) {
            return;
        }
        const next = () => {
            if (!waiting) {
                busy = false;
                return;
            }
            const [e, m] = waiting;
            waiting = null;
            busy = true;
            page.invokeMethodAsync("DragRange", e, m).catch(() => undefined).finally(next);
        };
        next();
    };

    const draw = () => {
        const length = number("length");
        const position = player.currentTime * 1000;
        const share = length > 0 ? `${Math.min(100, (position * 100) / length)}%` : "0";
        if (playhead) {
            playhead.style.left = share;
        }
        if (knob) {
            knob.style.left = share;
        }
        scrub.setAttribute("aria-valuenow", String(Math.round(position)));
        scrub.setAttribute("aria-valuetext", clock(position));
        const text = section.querySelector<HTMLElement>("[data-position]");
        if (text) {
            text.textContent = clock(position);
        }
    };
    let frame = 0;
    const follow = () => {
        draw();
        frame = player.paused ? 0 : requestAnimationFrame(follow);
    };
    const onPlay = () => {
        page.invokeMethodAsync("PlayingChanged", true).catch(() => undefined);
        if (!frame) {
            frame = requestAnimationFrame(follow);
        }
    };
    const onPause = () => {
        page.invokeMethodAsync("PlayingChanged", false).catch(() => undefined);
        draw();
    };

    let mode: "seek" | "start" | "end" | "press" | "move" | null = null;
    let offset = 0;
    let downX = 0;
    let downAt = 0;
    const onDown = (event: PointerEvent) => {
        if (event.button !== 0 || editor.classList.contains("busy")) {
            return;
        }
        const target = event.target as HTMLElement;
        const milliseconds = at(event);
        if (target.closest(".handle.start")) {
            mode = "start";
        } else if (target.closest(".handle.end")) {
            mode = "end";
        } else if (target.closest(".selection")) {
            mode = "press";
            offset = milliseconds - number("from");
        } else {
            mode = "seek";
            seek(player, milliseconds);
            draw();
        }
        downX = event.clientX;
        downAt = milliseconds;
        editor.setPointerCapture(event.pointerId);
        event.preventDefault();
    };
    const onMove = (event: PointerEvent) => {
        if (!mode) {
            return;
        }
        const milliseconds = at(event);
        if (mode === "press" && Math.abs(event.clientX - downX) > 4) {
            mode = "move";
        }
        if (mode === "seek") {
            seek(player, milliseconds);
            draw();
        } else if (mode === "start" || mode === "end") {
            report(mode, milliseconds);
        } else if (mode === "move") {
            report("move", milliseconds - offset);
        }
    };
    const onUp = (event: PointerEvent) => {
        if (mode === "press") {
            // A tap inside the selection, not a drag, moves the play position there.
            seek(player, downAt);
            draw();
        }
        mode = null;
        if (editor.hasPointerCapture(event.pointerId)) {
            editor.releasePointerCapture(event.pointerId);
        }
    };

    const onKey = (event: KeyboardEvent) => {
        if (!editor.isConnected || event.altKey || event.ctrlKey || event.metaKey || typing(event.target)) {
            return;
        }
        const target = event.target as HTMLElement | null;
        if (event.key === " " || event.code === "Space") {
            // Space plays and pauses wherever the focus is, so a focused button is not pressed by it.
            event.preventDefault();
            if (!event.repeat) {
                void toggle(player);
            }
            return;
        }
        const step = event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0;
        if (step === 0) {
            return;
        }
        event.preventDefault();
        const edge = target?.closest?.(".handle.start") ? "start" : target?.closest?.(".handle.end") ? "end" : null;
        if (edge) {
            const now = number(edge === "start" ? "from" : "to");
            report(edge, now + step * ARROW_EDGE_MILLISECONDS);
        } else {
            seekBy(player, step * ARROW_SEEK_MILLISECONDS);
            draw();
        }
    };
    const onKeyUp = (event: KeyboardEvent) => {
        if ((event.key === " " || event.code === "Space") && !typing(event.target)) {
            event.preventDefault();
        }
    };

    editor.addEventListener("pointerdown", onDown);
    editor.addEventListener("pointermove", onMove);
    editor.addEventListener("pointerup", onUp);
    editor.addEventListener("pointercancel", onUp);
    player.addEventListener("play", onPlay);
    player.addEventListener("pause", onPause);
    player.addEventListener("seeked", draw);
    player.addEventListener("loadedmetadata", draw);
    document.addEventListener("keydown", onKey);
    document.addEventListener("keyup", onKeyUp);
    draw();

    detachEditor = () => {
        editor.removeEventListener("pointerdown", onDown);
        editor.removeEventListener("pointermove", onMove);
        editor.removeEventListener("pointerup", onUp);
        editor.removeEventListener("pointercancel", onUp);
        player.removeEventListener("play", onPlay);
        player.removeEventListener("pause", onPause);
        player.removeEventListener("seeked", draw);
        player.removeEventListener("loadedmetadata", draw);
        document.removeEventListener("keydown", onKey);
        document.removeEventListener("keyup", onKeyUp);
        if (frame) {
            cancelAnimationFrame(frame);
        }
        clearStop();
        player.pause();
    };
}
