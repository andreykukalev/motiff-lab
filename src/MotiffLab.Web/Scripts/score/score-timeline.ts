// When each engraved note sounds, on the canonical timeline (S1-W7, decision record 0008). Built
// from Verovio's timemap, which gives positions in quarter notes, so it matches the playback
// engine's ticks whatever the tempo map. Kept free of the DOM so Node can test it.

/** One moment in Verovio's timemap: the notes that start and stop there. */
export interface TimemapEntry {
    qstamp: number;
    on?: string[];
    off?: string[];
}

/** An engraved note (or one piece of a tied note) and the ticks it sounds for. */
export interface NoteSpan {
    id: string;
    onset: number;
    end: number;
}

export const TICKS_PER_QUARTER = 480;

export class ScoreTimeline {
    /** Ordered by onset, then id. */
    readonly spans: readonly NoteSpan[];
    private readonly longest: number;
    private readonly onsets: Map<string, number>;

    private constructor(spans: NoteSpan[]) {
        spans.sort((a, b) => a.onset - b.onset || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
        this.spans = spans;
        this.longest = spans.reduce((longest, span) => Math.max(longest, span.end - span.onset), 0);
        this.onsets = new Map();
        for (const span of spans) {
            if (!this.onsets.has(span.id)) {
                this.onsets.set(span.id, span.onset);
            }
        }
    }

    static fromTimemap(entries: readonly TimemapEntry[]): ScoreTimeline {
        const open = new Map<string, number>();
        const spans: NoteSpan[] = [];
        let last = 0;
        for (const entry of entries) {
            const tick = Math.round(entry.qstamp * TICKS_PER_QUARTER);
            last = Math.max(last, tick);
            for (const id of entry.off ?? []) {
                const onset = open.get(id);
                if (onset !== undefined) {
                    open.delete(id);
                    spans.push({ id, onset, end: Math.max(tick, onset + 1) });
                }
            }
            for (const id of entry.on ?? []) {
                open.set(id, tick);
            }
        }
        // A note the timemap never stops lasts to the end.
        for (const [id, onset] of open) {
            spans.push({ id, onset, end: Math.max(last, onset + 1) });
        }
        return new ScoreTimeline(spans);
    }

    /** Ids of the notes sounding at a tick: started at or before it and not yet ended. */
    soundingAt(tick: number): string[] {
        let low = 0;
        let high = this.spans.length;
        while (low < high) {
            const middle = (low + high) >>> 1;
            if (this.spans[middle].onset <= tick) {
                low = middle + 1;
            } else {
                high = middle;
            }
        }

        const sounding: string[] = [];
        for (let i = low - 1; i >= 0 && this.spans[i].onset > tick - this.longest - 1; i--) {
            if (this.spans[i].end > tick) {
                sounding.push(this.spans[i].id);
            }
        }
        return sounding.reverse();
    }

    /** Where a note starts, for moving playback to it. */
    onsetOf(id: string): number | undefined {
        return this.onsets.get(id);
    }
}
