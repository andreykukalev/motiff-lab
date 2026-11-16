// When each engraved note sounds, on the canonical timeline (S1-W7, decision record 0008). Built
// from Verovio's timemap, which gives positions in quarter notes, so it matches the playback
// engine's ticks whatever the tempo map. Kept free of the DOM so Node can test it.

/** One moment in Verovio's timemap: the notes, and with includeRests the rests, that start and stop there. */
export interface TimemapEntry {
    qstamp: number;
    on?: string[];
    off?: string[];
    restsOn?: string[];
    restsOff?: string[];
    /** With includeMeasures, the id of the measure that starts here. */
    measureOn?: string;
}

/** An engraved note (or one piece of a tied note) and the ticks it sounds for. */
export interface NoteSpan {
    id: string;
    onset: number;
    end: number;
}

export const TICKS_PER_QUARTER = 480;

const THIRTY_SECOND = TICKS_PER_QUARTER / 8;

export class ScoreTimeline {
    /** Ordered by onset, then id. */
    readonly spans: readonly NoteSpan[];
    /** The engraved rests, when the timemap has them, ordered the same way. */
    readonly rests: readonly NoteSpan[];
    private readonly longest: number;
    private readonly onsets: Map<string, number>;
    /** Where each engraved measure starts (0012), when the timemap has measures. */
    private readonly measures: Map<string, number>;

    private constructor(spans: NoteSpan[], rests: NoteSpan[], measures: Map<string, number> = new Map()) {
        spans.sort(byOnset);
        rests.sort(byOnset);
        this.spans = spans;
        this.rests = rests;
        this.longest = spans.reduce((longest, span) => Math.max(longest, span.end - span.onset), 0);
        this.onsets = new Map();
        for (const span of spans) {
            if (!this.onsets.has(span.id)) {
                this.onsets.set(span.id, span.onset);
            }
        }
        this.measures = measures;
    }

    static fromTimemap(entries: readonly TimemapEntry[]): ScoreTimeline {
        const open = new Map<string, number>();
        const openRests = new Map<string, number>();
        const spans: NoteSpan[] = [];
        const rests: NoteSpan[] = [];
        const measures = new Map<string, number>();
        let last = 0;
        const close = (ids: string[] | undefined, from: Map<string, number>, into: NoteSpan[], tick: number): void => {
            for (const id of ids ?? []) {
                const onset = from.get(id);
                if (onset !== undefined) {
                    from.delete(id);
                    into.push({ id, onset, end: Math.max(tick, onset + 1) });
                }
            }
        };
        for (const entry of entries) {
            const tick = Math.round(entry.qstamp * TICKS_PER_QUARTER);
            last = Math.max(last, tick);
            close(entry.off, open, spans, tick);
            close(entry.restsOff, openRests, rests, tick);
            for (const id of entry.on ?? []) {
                open.set(id, tick);
            }
            for (const id of entry.restsOn ?? []) {
                openRests.set(id, tick);
            }
            if (entry.measureOn !== undefined && !measures.has(entry.measureOn)) {
                measures.set(entry.measureOn, tick);
            }
        }
        // A note the timemap never stops lasts to the end.
        for (const [id, onset] of open) {
            spans.push({ id, onset, end: Math.max(last, onset + 1) });
        }
        for (const [id, onset] of openRests) {
            rests.push({ id, onset, end: Math.max(last, onset + 1) });
        }
        return new ScoreTimeline(spans, rests, measures);
    }

    /** Where an engraved measure starts, or undefined when the timemap had no measures or no such one. */
    measureOnset(id: string): number | undefined {
        return this.measures.get(id);
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

/** A note of a place a fact names (#25), as Blazor sends it: the shared-model id, when it sounds and its MIDI key. */
export interface HighlightNote {
    id: string;
    onset: number;
    end: number;
    pitch: number;
}

/** A place a fact names: a stretch of time [start, end) and its notes, none for a stretch such as a phrase or a silence. */
export interface HighlightPlace {
    start: number;
    end: number;
    notes: HighlightNote[];
}

/**
 * The engraved notes and rests that stand for the places, by their element ids. Engraved ids are
 * Verovio's, not music-core's, so a place's notes are found by when they sound: an engraved note
 * belongs to a place's note when it has the same key and starts while that note sounds, which takes
 * in the pieces of a tied note; without a key, when it starts with the note. A generated score keeps
 * music-core's ids (and adds -2, -3 to tied pieces), which also match when the key agrees, so a note
 * engraved at the nearest 32nd is still found. A place with no notes takes every engraved note and
 * rest that starts in its stretch.
 */
export function highlightedIds(timeline: ScoreTimeline, places: readonly HighlightPlace[], pitchOf: (id: string) => number | undefined): Set<string> {
    const found = new Set<string>();
    for (const place of places) {
        if (place.notes.length === 0) {
            for (const span of [...timeline.spans, ...timeline.rests]) {
                if (span.onset >= place.start && span.onset < place.end) {
                    found.add(span.id);
                }
            }
            continue;
        }
        for (const span of timeline.spans) {
            // A note engraved at the nearest 32nd starts at most half a 32nd from where it sounds.
            if (span.onset < place.start - THIRTY_SECOND || span.onset >= place.end + THIRTY_SECOND) {
                continue;
            }
            const pitch = pitchOf(span.id);
            const matches = place.notes.some((note) =>
                pitch === undefined
                    ? span.onset === note.onset || isPieceOf(span.id, note.id)
                    : pitch === note.pitch && (isPieceOf(span.id, note.id) || (span.onset >= note.onset && span.onset < note.end)));
            if (matches) {
                found.add(span.id);
            }
        }
    }
    return found;
}

/** Whether an engraved id is a shared-model note id, or a later piece of that note tied over ("n_0005-2"). */
function isPieceOf(engraved: string, id: string): boolean {
    return engraved === id || (engraved.startsWith(`${id}-`) && /^\d+$/.test(engraved.slice(id.length + 1)));
}

function byOnset(a: NoteSpan, b: NoteSpan): number {
    return a.onset - b.onset || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
}
