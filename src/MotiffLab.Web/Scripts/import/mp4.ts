// The sound of an MP4, M4A or MOV file, a phone's or camera's recording, decoded a block at a time (0014): the file's
// table of samples is read from its "moov" box, the AAC or MP3 frames are read from the file in blocks of a few
// megabytes and handed to WebCodecs' AudioDecoder, and what it decodes is mixed and resampled as it comes. Only the
// first sound track is read. Nothing leaves the device.

import { MonoResampler } from "./resample.js";

/** A picked file, or the part of it the decoders read. */
export interface Source {
    readonly size: number;
    slice(start: number, end: number): { arrayBuffer(): Promise<ArrayBuffer> };
}

/** How much of the file is read at a time. */
export const READ_BYTES = 4 * 1024 * 1024;

/** The largest table of samples read: an hour's AAC needs about 2 MB. */
const MOST_MOOV_BYTES = 64 * 1024 * 1024;

/** Frames waiting in the decoder before more are handed to it. */
const MOST_QUEUED = 64;

export async function bytesOf(source: Source, start: number, end: number): Promise<Uint8Array> {
    return new Uint8Array(await source.slice(start, Math.min(end, source.size)).arrayBuffer());
}

export function text(bytes: Uint8Array, at: number): string {
    return String.fromCharCode(bytes[at], bytes[at + 1], bytes[at + 2], bytes[at + 3]);
}

/** A box of an MP4 file: its type and where its contents start and end, in the bytes read. */
interface Box {
    type: string;
    body: number;
    end: number;
}

/** The boxes from one byte to another of what was read. */
function boxesIn(view: DataView, from: number, to: number): Box[] {
    const found: Box[] = [];
    for (let at = from; at + 8 <= to;) {
        let size = view.getUint32(at);
        let header = 8;
        if (size === 1 && at + 16 <= to) {
            size = view.getUint32(at + 8) * 2 ** 32 + view.getUint32(at + 12);
            header = 16;
        } else if (size === 0) {
            size = to - at;
        }
        if (size < header) {
            break;
        }
        const type = String.fromCharCode(view.getUint8(at + 4), view.getUint8(at + 5), view.getUint8(at + 6), view.getUint8(at + 7));
        found.push({ type, body: at + header, end: Math.min(to, at + size) });
        at += size;
    }
    return found;
}

/** The box at the end of a path of box types inside another, or null. */
function boxAt(view: DataView, inside: Box, path: readonly string[]): Box | null {
    let box: Box | null = inside;
    for (const type of path) {
        box = box ? boxesIn(view, box.body, box.end).find(child => child.type === type) ?? null : null;
    }
    return box;
}

/** A sound track's frames: where each lies in the file, how long it is, and how to decode them. */
export interface Mp4Track {
    codec: string;
    description: Uint8Array | null;
    sampleRate: number;
    channels: number;
    timescale: number;
    offsets: Float64Array;
    sizes: Uint32Array;
    times: Float64Array;
}

/** Where the top-level "moov" box is in the file, read from the boxes' headers alone. */
async function findMoov(source: Source): Promise<{ start: number; size: number } | null> {
    for (let at = 0; at + 8 <= source.size;) {
        const head = await bytesOf(source, at, at + 16);
        const view = new DataView(head.buffer, head.byteOffset, head.byteLength);
        let size = view.getUint32(0);
        if (size === 1 && head.length >= 16) {
            size = view.getUint32(8) * 2 ** 32 + view.getUint32(12);
        } else if (size === 0) {
            size = source.size - at;
        }
        if (size < 8) {
            return null;
        }
        if (text(head, 4) === "moov") {
            return size <= MOST_MOOV_BYTES ? { start: at, size } : null;
        }
        at += size;
    }
    return null;
}

/** The length of a descriptor in an "esds" box, and where its contents start. */
function descriptor(bytes: Uint8Array, at: number): { tag: number; body: number; end: number } {
    const tag = bytes[at];
    let length = 0;
    let i = at + 1;
    for (let n = 0; n < 4; n++, i++) {
        length = (length << 7) | (bytes[i] & 0x7f);
        if ((bytes[i] & 0x80) === 0) {
            i++;
            break;
        }
    }
    return { tag, body: i, end: i + length };
}

/** The codec and its configuration from an "esds" box: AAC with its AudioSpecificConfig, or MP3. */
export function codecOf(bytes: Uint8Array, from: number, to: number): { codec: string; description: Uint8Array | null } | null {
    let at = from + 4;
    const es = descriptor(bytes, at);
    if (es.tag !== 3) {
        return null;
    }
    // The stream's id and flags, then what the flags say follows: a stream it depends on, a URL, a clock stream.
    const flags = bytes[es.body + 2];
    at = es.body + 3 + (flags & 0x80 ? 2 : 0);
    at += flags & 0x40 ? 1 + bytes[at] : 0;
    at += flags & 0x20 ? 2 : 0;
    const config = descriptor(bytes, at);
    if (config.tag !== 4 || config.end > to) {
        return null;
    }
    const object = bytes[config.body];
    if (object === 0x69 || object === 0x6b) {
        return { codec: "mp3", description: null };
    }
    const specific = descriptor(bytes, config.body + 13);
    if (object !== 0x40 || specific.tag !== 5 || specific.end > to || specific.end <= specific.body) {
        return null;
    }
    const description = bytes.slice(specific.body, specific.end);
    let type = description[0] >> 3;
    if (type === 31 && description.length > 1) {
        type = 32 + (((description[0] & 7) << 3) | (description[1] >> 5));
    }
    return { codec: `mp4a.40.${type}`, description };
}

/** The first sample description of a track: its codec, channels and rate, from an "mp4a" or ".mp3" entry. */
function sampleEntry(view: DataView, bytes: Uint8Array, stsd: Box): Pick<Mp4Track, "codec" | "description" | "sampleRate" | "channels"> | null {
    const entry = boxesIn(view, stsd.body + 8, stsd.end)[0];
    if (!entry || !["mp4a", ".mp3"].includes(entry.type)) {
        return null;
    }
    // Six reserved bytes and the data reference, then QuickTime's version: 1 and 2 add 16 and 36 bytes.
    const version = view.getUint16(entry.body + 8);
    const channels = view.getUint16(entry.body + 16);
    const sampleRate = view.getUint32(entry.body + 24) >>> 16;
    const children = entry.body + 28 + (version === 1 ? 16 : version === 2 ? 36 : 0);
    if (entry.type === ".mp3") {
        return { codec: "mp3", description: null, sampleRate, channels };
    }
    const inner = boxesIn(view, children, entry.end);
    const wave = inner.find(box => box.type === "wave");
    const esds = inner.find(box => box.type === "esds") ?? (wave ? boxAt(view, wave, ["esds"]) : null);
    const codec = esds ? codecOf(bytes, esds.body, esds.end) : null;
    if (!codec) {
        return null;
    }
    // A QuickTime entry of version 2 keeps its rate and channels elsewhere: the AAC configuration has them too.
    const configured = codec.description ? aacLayout(codec.description) : null;
    return { ...codec, sampleRate: configured?.sampleRate ?? sampleRate, channels: configured?.channels ?? channels };
}

const AAC_RATES = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

/** The rate and channels an AAC AudioSpecificConfig gives, or null when it gives them otherwise. */
export function aacLayout(description: Uint8Array): { sampleRate: number; channels: number } | null {
    if (description.length < 2 || description[0] >> 3 === 31) {
        return null;
    }
    const rate = AAC_RATES[((description[0] & 7) << 1) | (description[1] >> 7)];
    const channels = (description[1] >> 3) & 0x0f;
    return rate && channels > 0 && channels < 8 ? { sampleRate: rate, channels: channels === 7 ? 8 : channels } : null;
}

/** Each frame's place in the file, from the sample table's sizes, chunks and chunk offsets. */
function framePlaces(view: DataView, stbl: Box): { offsets: Float64Array; sizes: Uint32Array } | null {
    const stsz = boxAt(view, stbl, ["stsz"]);
    const stsc = boxAt(view, stbl, ["stsc"]);
    const narrow = boxAt(view, stbl, ["stco"]);
    const stco = narrow ?? boxAt(view, stbl, ["co64"]);
    if (!stsz || !stsc || !stco) {
        return null;
    }
    const sizes = frameSizes(view, stsz);
    const count = sizes.length;
    const chunkAt = (chunk: number) => narrow === null
        ? view.getUint32(stco.body + 8 + chunk * 8) * 2 ** 32 + view.getUint32(stco.body + 12 + chunk * 8)
        : view.getUint32(stco.body + 8 + chunk * 4);
    const chunks = view.getUint32(stco.body + 4);
    const runs = view.getUint32(stsc.body + 4);
    const offsets = new Float64Array(count);
    let frame = 0;
    for (let run = 0; run < runs && frame < count; run++) {
        // A run of chunks with as many frames each, up to the chunk the next run starts at.
        const first = view.getUint32(stsc.body + 8 + run * 12) - 1;
        const next = run + 1 < runs ? view.getUint32(stsc.body + 8 + (run + 1) * 12) - 1 : chunks;
        const perChunk = view.getUint32(stsc.body + 12 + run * 12);
        for (let chunk = first; chunk < next && frame < count; chunk++) {
            frame = placeChunk(offsets, sizes, frame, chunkAt(chunk), perChunk);
        }
    }
    return frame === count ? { offsets, sizes } : null;
}

/** Every frame's size, from an "stsz" box: one size for all, or one each. */
function frameSizes(view: DataView, stsz: Box): Uint32Array {
    const fixed = view.getUint32(stsz.body + 4);
    const sizes = new Uint32Array(view.getUint32(stsz.body + 8));
    for (let i = 0; i < sizes.length; i++) {
        sizes[i] = fixed || view.getUint32(stsz.body + 12 + i * 4);
    }
    return sizes;
}

/** Places the frames of one chunk one after another from where it starts; the next frame not placed. */
function placeChunk(offsets: Float64Array, sizes: Uint32Array, frame: number, start: number, frames: number): number {
    let at = start;
    let next = frame;
    for (let i = 0; i < frames && next < sizes.length; i++, next++) {
        offsets[next] = at;
        at += sizes[next];
    }
    return next;
}

/** When each frame starts, in the track's timescale, from the sample table's durations. */
function frameTimes(view: DataView, stbl: Box, count: number): Float64Array {
    const times = new Float64Array(count);
    const stts = boxAt(view, stbl, ["stts"]);
    if (!stts) {
        return times;
    }
    const runs = view.getUint32(stts.body + 4);
    let frame = 0;
    let time = 0;
    for (let run = 0; run < runs; run++) {
        const frames = view.getUint32(stts.body + 8 + run * 8);
        const delta = view.getUint32(stts.body + 12 + run * 8);
        for (let i = 0; i < frames && frame < count; i++, frame++) {
            times[frame] = time;
            time += delta;
        }
    }
    return times;
}

/** The first sound track of an MP4, M4A or MOV file, or null when the file is not one or its sound is neither AAC nor MP3. */
export async function mp4Track(source: Source): Promise<Mp4Track | null> {
    try {
        const moov = await findMoov(source);
        if (!moov) {
            return null;
        }
        const bytes = await bytesOf(source, moov.start, moov.start + moov.size);
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
        const header = view.getUint32(0) === 1 ? 16 : 8;
        for (const trak of boxesIn(view, header, bytes.length).filter(box => box.type === "trak")) {
            const hdlr = boxAt(view, trak, ["mdia", "hdlr"]);
            const mdhd = boxAt(view, trak, ["mdia", "mdhd"]);
            const stbl = boxAt(view, trak, ["mdia", "minf", "stbl"]);
            const stsd = stbl ? boxAt(view, stbl, ["stsd"]) : null;
            if (!hdlr || !mdhd || !stbl || !stsd || text(bytes, hdlr.body + 8) !== "soun") {
                continue;
            }
            const entry = sampleEntry(view, bytes, stsd);
            const places = framePlaces(view, stbl);
            if (!entry || !places) {
                return null;
            }
            const timescale = view.getUint32(mdhd.body + (view.getUint8(mdhd.body) === 1 ? 20 : 12));
            return { ...entry, ...places, timescale, times: frameTimes(view, stbl, places.sizes.length) };
        }
        return null;
    } catch {
        return null;
    }
}

/** The channels of decoded sound as samples between -1 and 1, whatever form the decoder gives them in. */
function channelsOf(data: AudioData): Float32Array[] {
    const frames = data.numberOfFrames;
    const count = data.numberOfChannels;
    try {
        return Array.from({ length: count }, (_, plane) => {
            const channel = new Float32Array(frames);
            data.copyTo(channel, { planeIndex: plane, format: "f32-planar" });
            return channel;
        });
    } catch {
        return nativeChannels(data);
    }
}

/** The channels as the decoder gives them, for a browser that converts no formats: interleaved or planar, float or 16-bit. */
function nativeChannels(data: AudioData): Float32Array[] {
    const frames = data.numberOfFrames;
    const count = data.numberOfChannels;
    const planar = data.format?.endsWith("-planar") ?? false;
    const scale = data.format?.startsWith("f32") ? 1 : 1 / 32768;
    const width = planar ? 1 : count;
    const channels = Array.from({ length: count }, () => new Float32Array(frames));
    for (let plane = 0; plane < (planar ? count : 1); plane++) {
        const raw = scale === 1 ? new Float32Array(frames * width) : new Int16Array(frames * width);
        data.copyTo(raw, { planeIndex: plane });
        for (let i = 0; i < raw.length; i++) {
            const channel = planar ? plane : i % width;
            channels[channel][planar ? i : Math.floor(i / width)] = raw[i] * scale;
        }
    }
    return channels;
}

/** A track's sound decoded by WebCodecs, or null when this browser cannot decode its codec. */
export async function decodeMp4(source: Source, track: Mp4Track): Promise<Int16Array | null> {
    const config: AudioDecoderConfig = { codec: track.codec, sampleRate: track.sampleRate, numberOfChannels: track.channels };
    if (track.description) {
        config.description = track.description;
    }
    try {
        if (!(await AudioDecoder.isConfigSupported(config)).supported) {
            return null;
        }
    } catch {
        return null;
    }
    let resampler: MonoResampler | null = null;
    let failure: unknown = null;
    const decoder = new AudioDecoder({
        output: data => {
            try {
                resampler ??= new MonoResampler(data.sampleRate);
                resampler.push(channelsOf(data), data.numberOfFrames);
            } finally {
                data.close();
            }
        },
        error: error => {
            failure = error;
        },
    });
    try {
        decoder.configure(config);
        await feed(source, track, decoder, () => failure !== null);
        await decoder.flush();
    } catch {
        return null;
    } finally {
        if (decoder.state !== "closed") {
            decoder.close();
        }
    }
    const done = resampler as MonoResampler | null;
    return failure === null && done ? done.finish() : null;
}

/** Hands the track's frames to the decoder, reading the file a block at a time and waiting while it catches up. */
async function feed(source: Source, track: Mp4Track, decoder: AudioDecoder, failed: () => boolean): Promise<void> {
    const count = track.sizes.length;
    for (let first = 0; first < count && !failed();) {
        // The frames that lie one after another in the file, a block's worth.
        let last = first;
        while (last + 1 < count && track.offsets[last + 1] === track.offsets[last] + track.sizes[last]
            && track.offsets[last + 1] + track.sizes[last + 1] - track.offsets[first] <= READ_BYTES) {
            last++;
        }
        const block = await bytesOf(source, track.offsets[first], track.offsets[last] + track.sizes[last]);
        for (let frame = first; frame <= last; frame++) {
            const at = track.offsets[frame] - track.offsets[first];
            decoder.decode(new EncodedAudioChunk({
                type: "key",
                timestamp: Math.round((track.times[frame] * 1_000_000) / Math.max(1, track.timescale)),
                data: block.subarray(at, at + track.sizes[frame]),
            }));
        }
        while (decoder.decodeQueueSize > MOST_QUEUED && !failed()) {
            await new Promise(resolve => setTimeout(resolve, 5));
        }
        first = last + 1;
    }
}
