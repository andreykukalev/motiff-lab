// Takes the sound out of an audio or video recording in the browser, for transcription (0014): the
// file is decoded here, mixed to mono at 22,050 Hz and cut to the first minutes the server hears, so
// only that sound is sent, never the video. Browsers decode what they can play; when one cannot
// decode a file, the caller sends the file itself and the server's ffmpeg reads it.

/** Enough for Transkun, which listens up to 8 kHz, at half the size of its own 44,100 Hz; the server resamples. */
export const SAMPLE_RATE = 22050;

/** The sound of a recording as a 16-bit mono WAV file, how many seconds it holds, and whether the recording was longer. */
export interface Sound {
    wav: Uint8Array;
    seconds: number;
    cut: boolean;
}

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
 * The first `maxSeconds` of the sound of the file picked in `input`, or null when this browser cannot
 * decode it (or has no file there). Decoding happens on this device; nothing is sent from here.
 */
export async function soundOf(input: HTMLInputElement, maxSeconds: number): Promise<Sound | null> {
    const file = input.files?.[0];
    if (!file || typeof OfflineAudioContext === "undefined") {
        return null;
    }
    let decoded: AudioBuffer;
    try {
        // A context at this rate decodes straight to it; it is never started.
        const context = new OfflineAudioContext(1, 1, SAMPLE_RATE);
        decoded = await context.decodeAudioData(await file.arrayBuffer());
    } catch {
        return null;
    }
    const length = Math.min(decoded.length, Math.floor(maxSeconds * SAMPLE_RATE));
    if (length === 0) {
        return null;
    }
    const channels: Float32Array[] = [];
    for (let i = 0; i < decoded.numberOfChannels; i++) {
        channels.push(decoded.getChannelData(i));
    }
    return {
        wav: encodeWav(mixToMono(channels, length), SAMPLE_RATE),
        seconds: length / SAMPLE_RATE,
        cut: decoded.length > length,
    };
}
