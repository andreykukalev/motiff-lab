// Decodes a long recording a block at a time (0014), so the browser never holds its whole decoded sound at its own
// rate and channel count, only the mono 16-bit sound at 22,050 Hz that is sent: a WAV file is read straight, and the
// AAC or MP3 sound of an MP4, M4A or MOV file goes through WebCodecs' AudioDecoder where the browser has it. Anything
// else, or a browser without AudioDecoder, is decoded whole by the caller. Nothing leaves the device.

import { type Source, READ_BYTES, bytesOf, decodeMp4, mp4Track, text } from "./mp4.js";
import { MonoResampler } from "./resample.js";

export type { Source };

/** Where a WAV file's samples are and how they are written. */
export interface WavLayout {
    float: boolean;
    channels: number;
    rate: number;
    bits: number;
    dataOffset: number;
    dataBytes: number;
}

type WavFormat = Omit<WavLayout, "dataOffset" | "dataBytes">;

/** What a "fmt " chunk at `at` says, or null when its samples are neither PCM nor 32-bit float. */
function wavFormat(view: DataView, at: number): WavFormat | null {
    const tag = view.getUint16(at + 8, true);
    // An extensible format names its own in the first two bytes of its sub-format.
    const code = tag === 0xfffe && at + 34 <= view.byteLength ? view.getUint16(at + 32, true) : tag;
    const bits = view.getUint16(at + 22, true);
    const pcm = code === 1 && [8, 16, 24, 32].includes(bits);
    const float = code === 3 && bits === 32;
    return pcm || float ? { float, channels: view.getUint16(at + 10, true), rate: view.getUint32(at + 12, true), bits } : null;
}

/** The layout of a "data" chunk at `at` read with `format`, or null without a usable format. */
function dataLayout(format: WavFormat | null, at: number, size: number, fileSize: number): WavLayout | null {
    if (!format || format.channels <= 0 || format.rate <= 0) {
        return null;
    }
    // A file written as it was recorded may not have its length filled in.
    const dataBytes = size === 0 || size === 0xffffffff ? fileSize - (at + 8) : Math.min(size, fileSize - (at + 8));
    return { ...format, dataOffset: at + 8, dataBytes };
}

/** The layout of a WAV file from its first bytes, or null when it is not a WAV file this reads (PCM or float). */
export function wavLayout(bytes: Uint8Array, fileSize: number): WavLayout | null {
    if (bytes.length < 12 || text(bytes, 0) !== "RIFF" || text(bytes, 8) !== "WAVE") {
        return null;
    }
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    let format: WavFormat | null = null;
    for (let at = 12; at + 8 <= bytes.length;) {
        const id = text(bytes, at);
        const size = view.getUint32(at + 4, true);
        if (id === "fmt " && at + 24 <= bytes.length) {
            format = wavFormat(view, at);
            if (!format) {
                return null;
            }
        } else if (id === "data") {
            return dataLayout(format, at, size, fileSize);
        }
        at += 8 + size + (size % 2);
    }
    return null;
}

/** One sample of a WAV file's data between -1 and 1. */
function wavSample(view: DataView, at: number, layout: WavLayout): number {
    if (layout.float) {
        return view.getFloat32(at, true);
    }
    switch (layout.bits) {
        case 8:
            return (view.getUint8(at) - 128) / 128;
        case 16:
            return view.getInt16(at, true) / 32768;
        case 24:
            return ((view.getUint8(at) | (view.getUint8(at + 1) << 8) | (view.getInt8(at + 2) << 16)) / 8388608);
        default:
            return view.getInt32(at, true) / 2147483648;
    }
}

/** A WAV file's sound, read a block at a time. */
export async function decodeWav(source: Source, layout: WavLayout): Promise<Int16Array> {
    const resampler = new MonoResampler(layout.rate);
    const frameBytes = (layout.bits / 8) * layout.channels;
    const step = Math.max(frameBytes, Math.floor(READ_BYTES / frameBytes) * frameBytes);
    const end = layout.dataOffset + layout.dataBytes - (layout.dataBytes % frameBytes);
    for (let at = layout.dataOffset; at < end; at += step) {
        const bytes = await bytesOf(source, at, Math.min(end, at + step));
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
        const frames = Math.floor(bytes.length / frameBytes);
        const channels = Array.from({ length: layout.channels }, () => new Float32Array(frames));
        for (let frame = 0; frame < frames; frame++) {
            for (let channel = 0; channel < layout.channels; channel++) {
                channels[channel][frame] = wavSample(view, frame * frameBytes + (channel * layout.bits) / 8, layout);
            }
        }
        resampler.push(channels, frames);
    }
    return resampler.finish();
}

/** The sound of a file decoded a block at a time, or null when it is not one these decoders read. */
export async function decodeInBlocks(source: Source): Promise<Int16Array | null> {
    if (typeof source.slice !== "function" || source.size < 12) {
        return null;
    }
    const head = await bytesOf(source, 0, 64 * 1024);
    const wav = wavLayout(head, source.size);
    if (wav) {
        return decodeWav(source, wav);
    }
    if (typeof AudioDecoder === "undefined") {
        return null;
    }
    const track = await mp4Track(source);
    return track ? decodeMp4(source, track) : null;
}
