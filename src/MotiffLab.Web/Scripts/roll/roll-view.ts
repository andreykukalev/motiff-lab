// What the roll shows and how (v0.5.4, #511 sections 5 and 6): two states kept apart. The player's zoom (the time as pixels
// per quarter, the rows as a share of their height at 100 %) changes only by the zoom controls, a pinch, the wheel and Fit;
// Fit lays the selection across the roll the size it is now and is then just the zoom. The view (the left edge in ticks, the
// highest row shown) changes by the player's own scrolling, by Fit, and by playback, which carries the view along
// continuously once the playhead passes FOLLOW_AT of the width, and brings a playhead that jumped out of view back once,
// smoothly. A selection made, moved or cleared, a loop turned on or off, the notes coming back, a new size and Expand change
// neither, except that a new selection wholly out of view is brought into it, the zoom as it is. Pure; no DOM here.

import type { Span } from "./roll-playhead.js";

/** How a selection shown differs from the one shown before: moved when it starts elsewhere or none was shown, changed when either end differs. */
export function selectionShift(before: Span | null, start: number, end: number): { moved: boolean; changed: boolean } {
    const moved = before === null || before.start !== start;
    return { moved, changed: moved || before.end !== end };
}

/** The view as the roll keeps it: the left edge in ticks, the highest row shown (null for the top of the notes), and the selection's span. */
export interface RollView {
    scroll: number;
    pitchTop: number | null;
    span: Span | null;
}

/**
 * The view a load of the notes starts from. The same piece's notes again (`keep`: a note edited, a version taken in) keep
 * the view and the selection's span, so the selection sent back after them is not a new one; the editor keeps its place
 * as before; another piece starts at its beginning. The zoom is the player's and never changes with a load.
 */
export function viewOnLoad(before: RollView, keep: boolean, editing: boolean): RollView {
    if (keep) {
        return { ...before };
    }
    if (editing) {
        return { scroll: before.scroll, pitchTop: before.pitchTop, span: null };
    }
    return { scroll: 0, pitchTop: null, span: null };
}

/** The player's zoom and the view together: what Fit and a glide set at once. */
export interface ViewFrame {
    /** Pixels per quarter note. */
    zoom: number;
    /** The rows' height as a share of their height at 100 %. */
    rowScale: number;
    /** The left edge, in ticks. */
    scroll: number;
    /** The highest row shown. */
    pitchTop: number;
}

/**
 * A frame read back from the device (PDF | Notes, #511 section 10): four finite numbers, the zoom and the rows' scale above
 * zero; null for anything else, so a frame kept by an older build or changed by hand is not drawn from.
 */
export function frameOf(value: unknown): ViewFrame | null {
    if (typeof value !== "object" || value === null) {
        return null;
    }
    const { zoom, rowScale, scroll, pitchTop } = value as Record<string, unknown>;
    const numbers = [zoom, rowScale, scroll, pitchTop];
    if (!numbers.every((number) => typeof number === "number" && Number.isFinite(number))) {
        return null;
    }
    const frame = { zoom, rowScale, scroll, pitchTop } as ViewFrame;
    return frame.zoom > 0 && frame.rowScale > 0 ? frame : null;
}

const TICKS_PER_QUARTER = 480;

/** The ticks a width of pixels shows at a zoom. */
export function ticksIn(width: number, zoom: number): number {
    return (Math.max(1, width) * TICKS_PER_QUARTER) / Math.max(1e-6, zoom);
}

/** The rows from the lowest to the highest pitch: an octave at least (minSpan semitones), with one free row above and below. */
export function paddedPitches(lowest: number, highest: number, minSpan = 12): { low: number; high: number } {
    const middle = (lowest + highest) / 2;
    const low = Math.min(lowest, Math.floor(middle - minSpan / 2));
    const high = Math.max(highest, Math.ceil(middle + minSpan / 2));
    return { low: Math.max(0, low - 1), high: Math.min(127, high + 1) };
}

/** What Fit needs: the stretch and its notes' pitches, the roll's size now, the margin either side, and the limits. */
export interface FitInput {
    start: number;
    end: number;
    /** The lowest and highest pitch of the notes in the stretch; null when it has none, which leaves the rows. */
    pitches: { lowest: number; highest: number } | null;
    /** The width of the time, and the height of the rows, in pixels. */
    width: number;
    height: number;
    /** The free pixels left and right of the stretch. */
    margin: number;
    zoomLimits: [number, number];
    /** The thinnest and thickest row, in pixels, and a row's height at 100 %. */
    rowLimits: [number, number];
    rowAt100: number;
    /** The view before, whose rows stay when the stretch has no notes. */
    before: ViewFrame;
}

/**
 * Fit (#511 section 5.1): the zoom that lays the stretch across the width with a margin either side, the rows that put its
 * notes in the height with a free row above and below (as far as the rows' limits allow, the middle of them when even the
 * thinnest cannot), and the view on it. What it gives becomes the player's zoom: nothing fits again afterwards.
 */
export function fitFrame(input: FitInput): ViewFrame {
    const [minZoom, maxZoom] = input.zoomLimits;
    const ticks = Math.max(1, input.end - input.start);
    const zoom = Math.min(maxZoom, Math.max(minZoom, (Math.max(1, input.width - 2 * input.margin) * TICKS_PER_QUARTER) / ticks));
    const scroll = Math.round(Math.max(0, input.start - (input.margin * TICKS_PER_QUARTER) / zoom));
    if (input.pitches === null) {
        return { ...input.before, zoom, scroll };
    }
    const { low, high } = paddedPitches(input.pitches.lowest, input.pitches.highest);
    const rows = high - low + 1;
    const [minRow, maxRow] = input.rowLimits;
    const row = Math.min(maxRow, Math.max(minRow, Math.floor(input.height / rows)));
    const spare = Math.floor(input.height / row) - rows;
    return { zoom, rowScale: row / input.rowAt100, scroll, pitchTop: high + Math.floor(spare / 2) };
}

/** Where a new selection sits in the view: the view, its rows shown, and the selection's stretch and pitches. */
export interface RevealInput {
    scroll: number;
    visible: number;
    rows: { low: number; high: number };
    span: Span;
    pitches: { lowest: number; highest: number } | null;
    /** The furthest left edge the view goes to. */
    maxScroll: number;
}

/** The share of the width left before a stretch or a playhead brought into view. */
export const REVEAL_AT = 0.15;

/**
 * Where a new selection brings the view (#511 section 5.1, "do not unexpectedly reposition"): nowhere while any of it is in
 * view, its edges moved, made on the roll or on the score in view; when all of it is out of view (made on the score far
 * away, a phrase stepped to), its start comes in at REVEAL_AT of the width, and its rows to the middle when none of them
 * shows. The zoom stays. Null when the view stays.
 */
export function revealSelection(input: RevealInput): { scroll: number; pitchTop: number | null } | null {
    const inTime = input.span.end > input.scroll && input.span.start < input.scroll + input.visible;
    const pitches = input.pitches;
    const inRows = pitches === null || (pitches.highest >= input.rows.low && pitches.lowest <= input.rows.high);
    if (inTime && inRows) {
        return null;
    }
    const scroll = inTime ? input.scroll : Math.round(Math.min(input.maxScroll, Math.max(0, input.span.start - input.visible * REVEAL_AT)));
    const shown = input.rows.high - input.rows.low + 1;
    const pitchTop = inRows || pitches === null ? null : Math.round((pitches.lowest + pitches.highest) / 2 + shown / 2);
    return { scroll, pitchTop };
}

/** The share of the width the playhead may pass before the view moves with it while playing. */
export const FOLLOW_AT = 0.7;

/** What following the playhead knows: the view, the playhead, and how far the view may go. */
export interface FollowInput {
    scroll: number;
    visible: number;
    tick: number;
    playing: boolean;
    /** Whether the playhead jumped rather than moved on (a seek, a loop going round). */
    jumped: boolean;
    /** The end of what plays (the loop's end while it loops, else the piece's end): the view does not go past it to follow. */
    end: number;
    maxScroll: number;
}

/** Where following the playhead takes the view: along with it (glue), or once to it after a jump (reveal). */
export interface Follow {
    kind: "glue" | "reveal";
    scroll: number;
}

/**
 * Following the playhead (#511 section 6.1): stopped, the view stays unless the playhead jumped out of it. Playing, once the
 * playhead passes FOLLOW_AT of the width the view moves with it so it stays there, no further than the end of what plays
 * is in view (so a loop that fits stays still); a playhead out of view, after a jump or after the player moved the view
 * away, is brought back once at REVEAL_AT of the width. Null when the view stays. Never a page jump: the roll eases to it.
 */
export function followPlayhead(input: FollowInput): Follow | null {
    if (!input.playing && !input.jumped) {
        return null;
    }
    const offset = input.tick - input.scroll;
    if (offset < 0 || offset > input.visible) {
        return { kind: "reveal", scroll: Math.round(Math.min(input.maxScroll, Math.max(0, input.tick - input.visible * REVEAL_AT))) };
    }
    if (!input.playing || offset <= input.visible * FOLLOW_AT) {
        return null;
    }
    const target = Math.min(input.tick - input.visible * FOLLOW_AT, input.end - input.visible, input.maxScroll);
    return target > input.scroll ? { kind: "glue", scroll: target } : null;
}

/**
 * One frame of the view easing towards where following takes it: the share of the way left after `elapsed` ms shrinks
 * with the time constant `tau`, so a playhead moving at a steady pace is followed at a steady place, and a jump is
 * eased to. At `tau` 0 (reduced motion) it is there at once.
 */
export function chase(from: number, to: number, elapsed: number, tau: number): number {
    if (tau <= 0) {
        return to;
    }
    return to + (from - to) * Math.exp(-Math.max(0, elapsed) / tau);
}

/** A glide from one frame to another, as Fit and a selection brought into view make it: the share of the way at a share of the time. */
export function glideFrame(from: ViewFrame, to: ViewFrame, eased: number): ViewFrame {
    const at = Math.min(1, Math.max(0, eased));
    const between = (a: number, b: number): number => a + (b - a) * at;
    return {
        // The zoom eases by ratio, so each step looks the same size in and out.
        zoom: from.zoom * Math.pow(to.zoom / from.zoom, at),
        rowScale: between(from.rowScale, to.rowScale),
        scroll: between(from.scroll, to.scroll),
        pitchTop: between(from.pitchTop, to.pitchTop),
    };
}

/** A CSS cubic-bezier easing (the kit's `--ease`), as a function of the share of the time; linear for text it cannot read. */
export function easing(text: string): (share: number) => number {
    const match = /cubic-bezier\(\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\)/.exec(text);
    if (match === null) {
        return (share) => Math.min(1, Math.max(0, share));
    }
    const [x1, y1, x2, y2] = match.slice(1).map(Number);
    const curve = (a: number, b: number, t: number): number => 3 * a * t * (1 - t) ** 2 + 3 * b * t * t * (1 - t) + t ** 3;
    return (share) => {
        const x = Math.min(1, Math.max(0, share));
        // The curve's x grows with t on [0, 1]: halve the interval round the t that gives this x.
        let low = 0;
        let high = 1;
        for (let step = 0; step < 24; step++) {
            const middle = (low + high) / 2;
            if (curve(x1, x2, middle) < x) {
                low = middle;
            } else {
                high = middle;
            }
        }
        return curve(y1, y2, (low + high) / 2);
    };
}

/** A timing token's value in milliseconds ("320ms", "0.2s"); the fallback when it cannot be read. */
export function milliseconds(text: string, fallback: number): number {
    const value = Number.parseFloat(text);
    if (!Number.isFinite(value)) {
        return fallback;
    }
    return text.trim().endsWith("ms") ? value : text.trim().endsWith("s") ? value * 1000 : value;
}
