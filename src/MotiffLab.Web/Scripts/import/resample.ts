// Turns decoded sound of any rate and channel count into the mono 16-bit sound at 22,050 Hz that is sent for
// transcription (0014), a block at a time, so a long recording never has to be decoded whole: the channels are
// averaged, a short low-pass filter keeps what lies above 22,050 Hz's half from folding back into what Transkun hears,
// and samples in between are interpolated. Pure; nothing leaves the device.

/** Enough for Transkun, which listens up to 8 kHz, at half the size of its own 44,100 Hz; the server resamples. */
export const SAMPLE_RATE = 22050;

/** Taps either side of the filter's middle: 25 in all. */
const REACH = 12;

/** Samples held in each block of the result, so a long recording grows without copying what it holds. */
const BLOCK = 1 << 20;

/** A sample between -1 and 1 as a 16-bit one, louder samples clipped, as a WAV file writes it. */
export function toInt16(sample: number): number {
    const clipped = Math.max(-1, Math.min(1, sample));
    return Math.trunc(clipped < 0 ? clipped * 0x8000 : clipped * 0x7fff);
}

/** A windowed-sinc low-pass filter passing up to `cutoff` of the sample rate (0 to 0.5), its taps adding up to 1. */
export function lowPass(cutoff: number, reach: number): Float32Array {
    const taps = new Float32Array(reach * 2 + 1);
    let sum = 0;
    for (let i = -reach; i <= reach; i++) {
        const sinc = i === 0 ? 2 * cutoff : Math.sin(2 * Math.PI * cutoff * i) / (Math.PI * i);
        const window = 0.42 + 0.5 * Math.cos((Math.PI * i) / (reach + 1)) + 0.08 * Math.cos((2 * Math.PI * i) / (reach + 1));
        taps[i + reach] = sinc * window;
        sum += taps[i + reach];
    }
    for (let i = 0; i < taps.length; i++) {
        taps[i] /= sum;
    }
    return taps;
}

/** Mixes and resamples blocks of sound to mono at SAMPLE_RATE as they come, and hands back the whole once finished. */
export class MonoResampler {
    private readonly ratio: number;
    private readonly taps: Float32Array | null;
    private readonly reach: number;
    /** Input the filter still needs, with `reach` silent samples before the first. */
    private input: Float32Array;
    /** Filtered input not yet resampled, from the filtered sample `filteredStart`. */
    private filtered = new Float32Array(0);
    private filteredStart = 0;
    private frames = 0;
    private written = 0;
    private readonly blocks: Int16Array[] = [];
    private block = new Int16Array(BLOCK);
    private used = 0;

    constructor(readonly inputRate: number) {
        if (!(inputRate > 0)) {
            throw new RangeError("The sample rate must be positive.");
        }
        this.ratio = inputRate / SAMPLE_RATE;
        this.taps = inputRate > SAMPLE_RATE ? lowPass((0.45 * SAMPLE_RATE) / inputRate, REACH) : null;
        this.reach = this.taps ? REACH : 0;
        this.input = new Float32Array(this.reach);
    }

    /** How many samples of the result there are so far. */
    get length(): number {
        return this.written;
    }

    /** Adds `frames` samples of each channel, the channels averaged into one. */
    push(channels: readonly Float32Array[], frames: number): void {
        if (frames <= 0 || channels.length === 0) {
            return;
        }
        const mono = new Float32Array(frames);
        for (const channel of channels) {
            for (let i = 0; i < frames; i++) {
                mono[i] += channel[i] ?? 0;
            }
        }
        if (channels.length > 1) {
            for (let i = 0; i < frames; i++) {
                mono[i] /= channels.length;
            }
        }
        this.frames += frames;
        this.filter(mono);
        this.emit(false);
    }

    /** The whole sound resampled: as many samples as the input's length at SAMPLE_RATE. */
    finish(): Int16Array {
        this.filter(new Float32Array(this.reach * 2));
        this.emit(true);
        const whole = new Int16Array(this.written);
        let at = 0;
        for (const block of [...this.blocks, this.block.subarray(0, this.used)]) {
            whole.set(block.subarray(0, Math.min(block.length, this.written - at)), at);
            at += block.length;
        }
        return whole;
    }

    /** Filters what can be filtered of the input held and the samples added. */
    private filter(samples: Float32Array): void {
        const input = new Float32Array(this.input.length + samples.length);
        input.set(this.input);
        input.set(samples, this.input.length);
        const width = this.reach * 2;
        const count = Math.max(0, input.length - width);
        const out = new Float32Array(count);
        const taps = this.taps;
        for (let j = 0; j < count; j++) {
            if (taps) {
                let sum = 0;
                for (let k = 0; k <= width; k++) {
                    sum += taps[k] * input[j + k];
                }
                out[j] = sum;
            } else {
                out[j] = input[j];
            }
        }
        this.input = input.subarray(count);
        const filtered = new Float32Array(this.filtered.length + count);
        filtered.set(this.filtered);
        filtered.set(out, this.filtered.length);
        this.filtered = filtered;
    }

    /** Writes the samples of the result the filtered input reaches; at the end, all of them. */
    private emit(last: boolean): void {
        const total = last ? Math.round((this.frames * SAMPLE_RATE) / this.inputRate) : Number.MAX_SAFE_INTEGER;
        const available = this.filteredStart + this.filtered.length;
        while (this.written < total) {
            const at = this.written * this.ratio;
            const whole = Math.floor(at);
            if (!last && whole + 1 >= available) {
                break;
            }
            const a = this.sample(whole);
            const b = this.sample(whole + 1);
            this.put(toInt16(a + (b - a) * (at - whole)));
        }
        // What the next sample of the result no longer needs.
        const drop = Math.min(this.filtered.length, Math.max(0, Math.floor(this.written * this.ratio) - this.filteredStart));
        this.filtered = this.filtered.subarray(drop);
        this.filteredStart += drop;
    }

    private sample(index: number): number {
        const i = index - this.filteredStart;
        return i >= 0 && i < this.filtered.length ? this.filtered[i] : 0;
    }

    private put(value: number): void {
        if (this.used === this.block.length) {
            this.blocks.push(this.block);
            this.block = new Int16Array(BLOCK);
            this.used = 0;
        }
        this.block[this.used++] = value;
        this.written++;
    }
}
