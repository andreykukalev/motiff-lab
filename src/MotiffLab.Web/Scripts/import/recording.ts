// Takes the sound out of an audio or video recording in the browser, for transcription (0014): the
// file is decoded here and mixed to mono at 22,050 Hz, the player sees its length and loudness and
// chooses the stretch to hear, and only that stretch's sound is sent, never the video. The decoded
// sound stays in this module until the next recording or `forget`. Browsers decode what they can play;
// when one cannot decode a file, the caller sends the file itself and the server's ffmpeg reads it.

/** Enough for Transkun, which listens up to 8 kHz, at half the size of its own 44,100 Hz; the server resamples. */
export const SAMPLE_RATE = 22050;

/** The sound of a stretch of a recording as a 16-bit mono WAV file, and how many seconds it holds. */
export interface Sound {
    wav: Uint8Array;
    seconds: number;
}

/** A decoded recording: its length, how loud it is along the way, and an address the page can play it from. */
export interface Loaded {
    milliseconds: number;
    peaks: number[];
    url: string;
}

/** How many loudness bars the overview of a recording has, whatever its length. */
export const PEAK_COUNT = 400;

let held: Float32Array | null = null;
let heldUrl: string | null = null;

/** The channels averaged into one, over the first `length` samples. */
export function mixToMono(channels: readonly Float32Array[], length: number): Float32Array {
    const mono = new Float32Array(length);
    if (channels.length === 0) {
        return mono;
    }
    for (const channel of channels) {
        const count = Math.min(length, channel.length);
        for (let i = 0; i < count; i++) {
            mono[i] += channel[i];
        }
    }
    for (let i = 0; i < length; i++) {
        mono[i] /= channels.length;
    }
    return mono;
}

/** Samples between -1 and 1 as a 16-bit PCM WAV file; louder samples are clipped. */
export function encodeWav(samples: Float32Array, sampleRate: number): Uint8Array {
    const bytes = new Uint8Array(44 + samples.length * 2);
    const view = new DataView(bytes.buffer);
    const text = (offset: number, value: string) => {
        for (let i = 0; i < value.length; i++) {
            view.setUint8(offset + i, value.charCodeAt(i));
        }
    };
    text(0, "RIFF");
    view.setUint32(4, 36 + samples.length * 2, true);
    text(8, "WAVE");
    text(12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true); // PCM
    view.setUint16(22, 1, true); // mono
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * 2, true);
    view.setUint16(32, 2, true);
    view.setUint16(34, 16, true);
    text(36, "data");
    view.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++) {
        const sample = Math.max(-1, Math.min(1, samples[i]));
        view.setInt16(44 + i * 2, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true);
    }
    return bytes;
}

/**
 * The loudest sample in each of `count` equal stretches of `samples`, between 0 and 1, for drawing the
 * shape of a recording; fewer when there are fewer samples.
 */
export function peaksOf(samples: Float32Array, count: number): number[] {
    const bars = Math.min(count, samples.length);
    const peaks: number[] = [];
    for (let bar = 0; bar < bars; bar++) {
        const from = Math.floor((samples.length * bar) / bars);
        const to = Math.floor((samples.length * (bar + 1)) / bars);
        let peak = 0;
        for (let i = from; i < to; i++) {
            const value = Math.abs(samples[i]);
            if (value > peak) {
                peak = value;
            }
        }
        peaks.push(Math.min(1, Math.round(peak * 1000) / 1000));
    }
    return peaks;
}

/** The held sound from one millisecond to another as a WAV file, or null when no recording is held. */
export function soundBetween(fromMilliseconds: number, toMilliseconds: number): Sound | null {
    if (!held) {
        return null;
    }
    const from = Math.max(0, Math.min(held.length, Math.round((fromMilliseconds * SAMPLE_RATE) / 1000)));
    const to = Math.max(from, Math.min(held.length, Math.round((toMilliseconds * SAMPLE_RATE) / 1000)));
    return { wav: encodeWav(held.subarray(from, to), SAMPLE_RATE), seconds: (to - from) / SAMPLE_RATE };
}

/** Lets go of the held recording and its address. */
export function forget(): void {
    held = null;
    if (heldUrl && typeof URL.revokeObjectURL === "function") {
        URL.revokeObjectURL(heldUrl);
    }
    heldUrl = null;
}

/**
 * Decodes the file picked in `input` on this device and holds its sound, or returns null when this browser
 * cannot decode it (or has no file there, or the file is larger than `maxBytes`). Nothing is sent from here.
 */
export async function load(input: HTMLInputElement, maxBytes: number): Promise<Loaded | null> {
    forget();
    const file = input.files?.[0];
    if (!file || file.size > maxBytes || typeof OfflineAudioContext === "undefined") {
        return null;
    }
    let mono: Float32Array;
    try {
        // A context at this rate decodes straight to it; it is never started.
        const context = new OfflineAudioContext(1, 1, SAMPLE_RATE);
        const decoded = await context.decodeAudioData(await file.arrayBuffer());
        const channels: Float32Array[] = [];
        for (let i = 0; i < decoded.numberOfChannels; i++) {
            channels.push(decoded.getChannelData(i));
        }
        mono = mixToMono(channels, decoded.length);
    } catch {
        return null;
    }
    if (mono.length === 0) {
        return null;
    }
    held = mono;
    heldUrl = URL.createObjectURL(file);
    return { milliseconds: Math.round((mono.length * 1000) / SAMPLE_RATE), peaks: peaksOf(mono, PEAK_COUNT), url: heldUrl };
}

/** Where the player is in the recording, in milliseconds. */
export function positionOf(player: HTMLMediaElement): number {
    return Math.round(player.currentTime * 1000);
}

let stopAt: (() => void) | null = null;

/** Plays the recording from one millisecond and pauses it at another, so the player hears the chosen stretch. */
export async function playBetween(player: HTMLMediaElement, fromMilliseconds: number, toMilliseconds: number): Promise<void> {
    if (stopAt) {
        player.removeEventListener("timeupdate", stopAt);
    }
    const listener = () => {
        if (player.currentTime * 1000 >= toMilliseconds) {
            player.pause();
            player.removeEventListener("timeupdate", listener);
            stopAt = null;
        }
    };
    stopAt = listener;
    player.addEventListener("timeupdate", listener);
    player.currentTime = fromMilliseconds / 1000;
    try {
        await player.play();
    } catch {
        // The browser would not play without a tap on the player itself; its own controls still work.
    }
}
