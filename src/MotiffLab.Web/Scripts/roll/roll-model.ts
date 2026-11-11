// What the piano roll and keyboard show (S1-W2, S1-W3): the notes of a piece on the canonical
// timeline, its bar and beat grid, the lanes notes are coloured by, and the notes sounding at a
// tick. Kept free of the DOM so Node can test it.

/** How the roll groups notes for colour and pattern. */
export type ColourBy = "track" | "voice" | "hand";

/** No hand (MIDI, or a part without a grand staff). */
export const HAND_NONE = 0;
export const HAND_LEFT = 1;
export const HAND_RIGHT = 2;

/** Set when the note itself (for example a MIDI note's end) is inferred, not imported. */
export const FLAG_NOTE_INFERRED = 1;
/** Set when the note's hand is inferred, not imported. */
export const FLAG_HAND_INFERRED = 2;

/** Fields per note in RollPayload.notes. */
export const NOTE_FIELDS = 7;

/** Fields per bar in RollPayload.bars. */
export const BAR_FIELDS = 4;

/**
 * A piece packed into flat arrays by music-core (Playback/PianoRollData.cs). Each note is onset, duration, pitch,
 * track, voice (0 when the source has none), hand and flags; each bar is onset, duration, beat
 * length and number.
 */
export interface RollPayload {
    ids: string[];
    notes: number[];
    bars: number[];
    end: number;
}

export interface RollNote {
    id: string;
    onset: number;
    end: number;
    pitch: number;
    track: number;
    /** 0 when the source has no voices. */
    voice: number;
    hand: number;
    noteInferred: boolean;
    handInferred: boolean;
}

export interface RollBar {
    number: number;
    onset: number;
    end: number;
    beatTicks: number;
}

export interface GridLine {
    tick: number;
    /** The bar number on a bar line; null on a beat line. */
    bar: number | null;
}

/** A group of notes drawn alike: one track, or one voice of a track. */
export interface Lane {
    key: number;
    label: string;
}

export const LOWEST_KEY = 21;
export const HIGHEST_KEY = 108;

const NAMES = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

const BLACK = [false, true, false, true, false, false, true, false, true, false, true, false];

export function isBlackKey(pitch: number): boolean {
    return BLACK[((pitch % 12) + 12) % 12];
}

/** Scientific pitch name with middle C as C4, matching music-core's Pitch.ToString. */
export function pitchName(pitch: number): string {
    return `${NAMES[((pitch % 12) + 12) % 12]}${Math.floor(pitch / 12) - 1}`;
}

/** The pitch class alone, "C♯", for a note's name inside it before music-core sent the spelled names. */
export function pitchClassName(pitch: number): string {
    return NAMES[((pitch % 12) + 12) % 12];
}

/**
 * The rows (testing items 11 and 13): 22 px at 100 %, tall enough for a name inside the note, scaled with the zoom between
 * 14 and 40 px.
 */
export const ROW_AT_100 = 22;
export const MIN_ROW = 14;
export const MAX_ROW = 40;

export function rowFor(scale: number): number {
    return Math.min(MAX_ROW, Math.max(MIN_ROW, Math.round(ROW_AT_100 * scale)));
}

/** A note's name goes inside it only where it fits (testing item 12); a narrower note shows its name when chosen. */
export function nameFits(noteWidth: number, textWidth: number): boolean {
    return noteWidth >= textWidth + 6;
}

/**
 * The rows shown, from the notes' lowest to highest row: all of them, widened round the notes to fill the height when they
 * take fewer rows than fit, or a window of as many as fit from the top row given (the top of the notes until scrolled).
 */
export function rowWindow(low: number, high: number, visible: number, top: number | null): { low: number; high: number; above: boolean; below: boolean } {
    const count = high - low + 1;
    if (count <= visible) {
        const first = Math.max(0, Math.min(127 - visible + 1, low - Math.floor((visible - count) / 2)));
        return { low: first, high: Math.min(127, first + visible - 1), above: false, below: false };
    }
    const shown = Math.min(high, Math.max(low + visible - 1, top ?? high));
    return { low: shown - visible + 1, high: shown, above: shown < high, below: shown - visible + 1 > low };
}

/** "Fit" (testing item 13): the horizontal zoom, in pixels per quarter, that lays a stretch of ticks across a width. */
export function fitZoom(ticks: number, width: number, min: number, max: number): number {
    return Math.min(max, Math.max(min, (width * 480) / Math.max(1, ticks)));
}

/** "Fit": the row height that shows a number of rows in a height, within the rows' limits. */
export function fitRow(rows: number, height: number): number {
    return Math.min(MAX_ROW, Math.max(MIN_ROW, Math.floor(height / Math.max(1, rows))));
}

/** Which key signature is in force at a tick: the index of the last that starts at or before it, the first before any. */
export function keyIndexAt(keyTicks: readonly number[], tick: number): number {
    let index = 0;
    for (let at = 0; at < keyTicks.length; at++) {
        if (keyTicks[at] <= tick) {
            index = at;
        }
    }
    return index;
}

export function laneKey(note: RollNote, by: ColourBy): number {
    switch (by) {
        case "track":
            return note.track * 1000;
        case "voice":
            return note.track * 1000 + note.voice;
        case "hand":
            // Right hand first, so it takes the first lane colour on a piano piece.
            return note.hand === HAND_RIGHT ? 0 : note.hand === HAND_LEFT ? 1 : 2;
    }
}

/** The words the roll shows, in the player's language; "{0}" and "{1}" stand for numbers or names. */
export interface RollLabels {
    track: string;
    trackVoice: string;
    noVoice: string;
    rightHand: string;
    leftHand: string;
    noHand: string;
    rollAria: string;
    scrollAria: string;
    keysAria: string;
    legendVariation: string;
    legendHighlight: string;
    legendInferred: string;
    legendCheck: string;
    zoomGroup: string;
    zoomIn: string;
    zoomOut: string;
    zoomLevel: string;
    fit: string;
    fitHint: string;
    /** The playhead's control, for the keyboard and a screen reader. */
    playhead: string;
    /** Where the playhead is: "{0}" the bar's number, "{1}" the beat. */
    playheadAt: string;
}

export const ENGLISH_ROLL_LABELS: RollLabels = {
    track: "Track {0}",
    trackVoice: "{0}, voice {1}",
    noVoice: "none",
    rightHand: "Right hand",
    leftHand: "Left hand",
    noHand: "No hand",
    rollAria:
        "Piano roll. Drag anywhere across the roll to select a range, drag an edge of the selection to move it, click a bar number to select that bar, Shift-click a bar number to extend the selection to it. Click a note to hear and select it, Shift-click to add to the selection; click the pitch names or the keyboard to hear a key. Click an empty place or press Escape to clear the selection. Space plays the selection on a loop, or the piece, and pauses. Right and left square brackets select the next or previous bar, with Shift they extend the selection by it. Arrow keys move the note selection, plus and minus zoom.",
    scrollAria: "Scroll the piano roll",
    keysAria: "Keyboard: the keys of the notes being heard are lit and marked with a dot. Click or drag across the keys to play them, also while the piece plays.",
    legendVariation: "Outlined: the variation, linked to the notes it moved from",
    legendHighlight: "Blue box and underline: the places of the fact chosen on Understand",
    legendInferred: "Dashed outline: inferred, not in the file (the note or its hand)",
    legendCheck: "A bar to check: tap its ?",
    zoomGroup: "Zoom",
    zoomIn: "Zoom in",
    zoomOut: "Zoom out",
    zoomLevel: "{0} %",
    fit: "Fit",
    fitHint: "Fit the selection, or the phrase in view, to the roll",
    playhead: "Playhead",
    playheadAt: "Bar {0}, beat {1}",
};

/** A label with its "{0}", "{1}" filled in. */
export function fill(label: string, ...values: (string | number)[]): string {
    return values.reduce<string>((text, value, index) => text.replaceAll(`{${index}}`, String(value)), label);
}

export class RollModel {
    /** Ordered by onset, then pitch, then id. */
    readonly notes: readonly RollNote[];
    readonly bars: readonly RollBar[];
    readonly end: number;
    readonly lowest: number;
    readonly highest: number;
    private readonly longest: number;
    private readonly indexById: Map<string, number>;

    private constructor(notes: RollNote[], bars: RollBar[], end: number) {
        notes.sort((a, b) => a.onset - b.onset || a.pitch - b.pitch || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
        this.notes = notes;
        this.bars = bars;
        this.end = Math.max(end, notes.reduce((last, note) => Math.max(last, note.end), 0));
        this.lowest = notes.reduce((low, note) => Math.min(low, note.pitch), notes.length > 0 ? 127 : 60);
        this.highest = notes.reduce((high, note) => Math.max(high, note.pitch), notes.length > 0 ? 0 : 72);
        this.longest = notes.reduce((longest, note) => Math.max(longest, note.end - note.onset), 0);
        this.indexById = new Map(notes.map((note, index) => [note.id, index]));
    }

    static fromPayload(payload: RollPayload): RollModel {
        const notes: RollNote[] = [];
        for (let i = 0; i < payload.ids.length; i++) {
            const at = i * NOTE_FIELDS;
            const flags = payload.notes[at + 6];
            notes.push({
                id: payload.ids[i],
                onset: payload.notes[at],
                end: payload.notes[at] + payload.notes[at + 1],
                pitch: payload.notes[at + 2],
                track: payload.notes[at + 3],
                voice: payload.notes[at + 4],
                hand: payload.notes[at + 5],
                noteInferred: (flags & FLAG_NOTE_INFERRED) !== 0,
                handInferred: (flags & FLAG_HAND_INFERRED) !== 0,
            });
        }

        const bars: RollBar[] = [];
        for (let at = 0; at + BAR_FIELDS <= payload.bars.length; at += BAR_FIELDS) {
            bars.push({
                onset: payload.bars[at],
                end: payload.bars[at] + payload.bars[at + 1],
                beatTicks: payload.bars[at + 2],
                number: payload.bars[at + 3],
            });
        }
        return new RollModel(notes, bars, payload.end);
    }

    indexOf(id: string): number | undefined {
        return this.indexById.get(id);
    }

    /** Indexes of the notes sounding at a tick: started at or before it and not yet ended. */
    soundingAt(tick: number): number[] {
        const sounding: number[] = [];
        for (let i = this.firstAfter(tick) - 1; i >= 0 && this.notes[i].onset > tick - this.longest - 1; i--) {
            if (this.notes[i].end > tick) {
                sounding.push(i);
            }
        }
        return sounding.reverse();
    }

    /** The note drawn at a tick and pitch, preferring the one that started last; undefined for none. */
    noteAt(tick: number, pitch: number): number | undefined {
        const sounding = this.soundingAt(tick);
        for (let i = sounding.length - 1; i >= 0; i--) {
            if (this.notes[sounding[i]].pitch === pitch) {
                return sounding[i];
            }
        }
        return undefined;
    }

    /** Indexes of the notes that overlap [from, to), for drawing what is in view. */
    inRange(from: number, to: number): number[] {
        const found: number[] = [];
        for (let i = this.firstAfter(to - 1) - 1; i >= 0 && this.notes[i].onset > from - this.longest - 1; i--) {
            if (this.notes[i].end > from) {
                found.push(i);
            }
        }
        return found.reverse();
    }

    /** Bar and beat lines in [from, to], bar lines carrying their number. */
    grid(from: number, to: number): GridLine[] {
        const lines: GridLine[] = [];
        for (const bar of this.bars) {
            if (bar.end < from || bar.onset > to) {
                continue;
            }
            if (bar.onset >= from) {
                lines.push({ tick: bar.onset, bar: bar.number });
            }
            if (bar.beatTicks > 0) {
                for (let beat = bar.onset + bar.beatTicks; beat < bar.end && beat <= to; beat += bar.beatTicks) {
                    if (beat >= from) {
                        lines.push({ tick: beat, bar: null });
                    }
                }
            }
        }
        return lines;
    }

    /** The bar a tick is in, for describing a position. */
    barAt(tick: number): RollBar | undefined {
        return this.bars.find((bar) => tick >= bar.onset && tick < bar.end) ?? this.bars[this.bars.length - 1];
    }

    /** The groups notes are coloured by, in order: tracks, or voices within tracks. */
    lanes(by: ColourBy, labels: RollLabels = ENGLISH_ROLL_LABELS): Lane[] {
        const keys = new Map<number, Lane>();
        const voiced = new Set(this.notes.filter((note) => note.voice > 0).map((note) => note.track));
        for (const note of this.notes) {
            const key = laneKey(note, by);
            if (!keys.has(key)) {
                const track = fill(labels.track, note.track + 1);
                const label =
                    by === "hand"
                        ? [labels.rightHand, labels.leftHand, labels.noHand][key]
                        : by === "voice" && voiced.has(note.track)
                          ? fill(labels.trackVoice, track, note.voice > 0 ? note.voice : labels.noVoice)
                          : track;
                keys.set(key, { key, label });
            }
        }
        return [...keys.values()].sort((a, b) => a.key - b.key);
    }

    /** The note before or after the given one in time order, for moving the selection with the arrow keys. */
    step(index: number | undefined, direction: 1 | -1): number | undefined {
        if (this.notes.length === 0) {
            return undefined;
        }
        if (index === undefined) {
            return direction === 1 ? 0 : this.notes.length - 1;
        }
        return Math.min(this.notes.length - 1, Math.max(0, index + direction));
    }

    private firstAfter(tick: number): number {
        let low = 0;
        let high = this.notes.length;
        while (low < high) {
            const middle = (low + high) >>> 1;
            if (this.notes[middle].onset <= tick) {
                low = middle + 1;
            } else {
                high = middle;
            }
        }
        return low;
    }
}
