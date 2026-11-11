// The sound of a recording decoded on this device (0014): a block at a time where decode.ts can, otherwise whole by the
// browser's own decoder when that fits the device's memory; and 16-bit mono sound written as a WAV file. Nothing
// leaves the device.

import { decodeInBlocks } from "./decode.js";
import { SAMPLE_RATE, toInt16 } from "./resample.js";

/**
 * The longest recording decoded whole, in minutes, where it cannot be decoded a block at a time: the browser then holds
 * every channel at 22,050 Hz in 32-bit samples, about 21 MB a minute for stereo. A tablet or phone has less to spare.
 */
export const MOST_WHOLE_MINUTES = 120;
export const MOST_WHOLE_MINUTES_TOUCH = 30;

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


/** A WAV file's 44-byte header for `count` 16-bit mono samples at `sampleRate`, and room for them after it. */
function wavFile(count: number, sampleRate: number): { bytes: Uint8Array; view: DataView } {
    const bytes = new Uint8Array(44 + count * 2);
    const view = new DataView(bytes.buffer);
    const text = (offset: number, value: string) => {
        for (let i = 0; i < value.length; i++) {
            view.setUint8(offset + i, value.charCodeAt(i));
        }
    };
    text(0, "RIFF");
    view.setUint32(4, 36 + count * 2, true);
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
    view.setUint32(40, count * 2, true);
    return { bytes, view };
}

/** Samples between -1 and 1 as a 16-bit PCM WAV file; louder samples are clipped. */
export function encodeWav(samples: Float32Array, sampleRate: number): Uint8Array {
    const { bytes, view } = wavFile(samples.length, sampleRate);
    for (let i = 0; i < samples.length; i++) {
        view.setInt16(44 + i * 2, toInt16(samples[i]), true);
    }
    return bytes;
}

/** 16-bit samples as a PCM WAV file. */
export function wavOf(samples: Int16Array, sampleRate: number): Uint8Array {
    const { bytes, view } = wavFile(samples.length, sampleRate);
    for (let i = 0; i < samples.length; i++) {
        view.setInt16(44 + i * 2, samples[i], true);
    }
    return bytes;
}


/** A recording decoded whole by the browser, at SAMPLE_RATE; null when this browser cannot decode it. */
async function decodeWhole(data: ArrayBuffer): Promise<AudioBuffer | null> {
    if (typeof OfflineAudioContext === "undefined") {
        return null;
    }
    try {
        // A context at this rate decodes straight to it; it is never started.
        const context = new OfflineAudioContext(1, 1, SAMPLE_RATE);
        return await context.decodeAudioData(data);
    } catch {
        return null;
    }
}

/** The channels of decoded sound. */
function channelsOf(decoded: AudioBuffer): Float32Array[] {
    return Array.from({ length: decoded.numberOfChannels }, (_, i) => decoded.getChannelData(i));
}

/** A recording's sound decoded on this device and mixed to mono at SAMPLE_RATE; null when this browser cannot decode it. */
export async function decodeMono(data: ArrayBuffer): Promise<Float32Array | null> {
    const decoded = await decodeWhole(data);
    const mono = decoded ? mixToMono(channelsOf(decoded), decoded.length) : null;
    return mono && mono.length > 0 ? mono : null;
}

/** A recording's sound decoded whole and mixed to 16-bit mono at SAMPLE_RATE; null when this browser cannot decode it. */
async function decodeMono16(data: ArrayBuffer): Promise<Int16Array | null> {
    const decoded = await decodeWhole(data);
    if (!decoded || decoded.length === 0) {
        return null;
    }
    const channels = channelsOf(decoded);
    const mono = new Int16Array(decoded.length);
    for (let i = 0; i < mono.length; i++) {
        let sum = 0;
        for (const channel of channels) {
            sum += channel[i] ?? 0;
        }
        mono[i] = toInt16(channels.length > 0 ? sum / channels.length : 0);
    }
    return mono;
}

/** Whether this is a tablet or phone, which has less memory to spare: an iPad says it is a Mac with a touch screen. */
function touchDevice(): boolean {
    if (typeof navigator === "undefined") {
        return false;
    }
    return /iPad|iPhone|Android/.test(navigator.userAgent ?? "") || (/Macintosh/.test(navigator.userAgent ?? "") && (navigator.maxTouchPoints ?? 0) > 1);
}

/** The longest recording, in minutes, this device decodes whole. */
export function mostWholeMinutes(): number {
    return touchDevice() ? MOST_WHOLE_MINUTES_TOUCH : MOST_WHOLE_MINUTES;
}

/** How long a recording is, from its metadata alone, in milliseconds; null when the browser cannot tell. */
async function lengthOf(file: Blob): Promise<number | null> {
    if (typeof Audio === "undefined" || typeof URL.createObjectURL !== "function") {
        return null;
    }
    const url = URL.createObjectURL(file);
    const player = new Audio();
    try {
        return await new Promise<number | null>(resolve => {
            const timer = setTimeout(() => resolve(null), 10_000);
            player.onloadedmetadata = () => {
                clearTimeout(timer);
                resolve(Number.isFinite(player.duration) ? Math.round(player.duration * 1000) : null);
            };
            player.onerror = () => {
                clearTimeout(timer);
                resolve(null);
            };
            player.preload = "metadata";
            player.src = url;
        });
    } finally {
        player.removeAttribute("src");
        URL.revokeObjectURL(url);
    }
}


/**
 * The sound of a picked file, a block at a time where it can be, otherwise whole when the file is at most `maxBytes`
 * and its length fits this device; a number, the recording's length, when it does not fit; null when it cannot be decoded.
 */
export async function decodePicked(file: File, maxBytes: number): Promise<Int16Array | number | null> {
    try {
        const streamed = await decodeInBlocks(file);
        if (streamed && streamed.length > 0) {
            return streamed;
        }
    } catch {
        // Decoded whole below.
    }
    if (file.size > maxBytes) {
        return null;
    }
    const length = await lengthOf(file);
    const most = mostWholeMinutes() * 60_000;
    if (length !== null && length > most) {
        return length;
    }
    return decodeMono16(await file.arrayBuffer());
}

