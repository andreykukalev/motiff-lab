// The rectangle a new selection is drawn with on the score (v0.6.3, #608; the approved mockup's `.trace`, the selection
// study's sections C and D; the lead's decision 2 of v0.6.3): a thin dashed box from where the stylus came down to where
// it is now, and the passage it covers. What it selects is its geometry: the systems it reaches over, time from its left
// and right sides on one system, «as text» across several (from the upper corner on the top system to the lower corner on
// the bottom one), snapped outward to the lines (beats, or barlines), and the staff it stays within (that hand), else both.
// The iPad only (score/selection-place-layer.ts). Pure; nothing here touches the DOM.

import { type Hand, type PlaceBox, type StaffBand, strokeHand } from "./selection-places.js";

/** A point in the score's content pixels. */
export interface Point {
    x: number;
    y: number;
}

/** A line a passage may start or end at on one system: its tick and where it is across. */
export interface RectLine {
    tick: number;
    x: number;
}

/** A system as the rectangle reads it: its box, its staves top to bottom, and its lines in time order. */
export interface RectSystem extends PlaceBox {
    staves: readonly StaffBand[];
    lines: readonly RectLine[];
}

/** A passage the rectangle covers, with the hand it is narrowed to (null for both). */
export interface RectPassage {
    start: number;
    end: number;
    hand: Hand | null;
}

/** The rectangle from the press to the pointer, whichever way it was drawn. */
export function traceBox(from: Point, to: Point): PlaceBox {
    return { left: Math.min(from.x, to.x), right: Math.max(from.x, to.x), top: Math.min(from.y, to.y), bottom: Math.max(from.y, to.y) };
}

/** The systems (in time order) a box reaches over, each system widened by `reach` all round, as a press on the music is. */
export function coveredSystems<T extends PlaceBox>(box: PlaceBox, systems: readonly T[], reach: number): T[] {
    return systems.filter((system) => system.top - reach <= box.bottom && system.bottom + reach >= box.top
        && system.left - reach <= box.right && system.right + reach >= box.left);
}

/** The last line at or left of `x`; the system's first line left of it all. */
function lineAtOrBefore(lines: readonly RectLine[], x: number): number | undefined {
    let found = lines[0]?.tick;
    for (const line of lines) {
        if (line.x <= x) {
            found = line.tick;
        }
    }
    return found;
}

/** The first line at or right of `x` after `start`; the system's last line right of it all. */
function lineAtOrAfter(lines: readonly RectLine[], x: number, start: number): number | undefined {
    const later = lines.filter((line) => line.tick > start);
    return (later.find((line) => line.x >= x) ?? later[later.length - 1])?.tick;
}

/**
 * The passage a rectangle drawn from `from` to `to` covers (section D, Создание, by the rectangle's geometry): null while it
 * reaches over no system. On one system from the line at or before its left side to the line at or after its right side,
 * never shorter than one step, narrowed to a hand when it stays within one of two staves; over several, «as text», from
 * the upper corner's x on the top system to the lower corner's x on the bottom one, both hands.
 */
export function rectPassage(from: Point, to: Point, systems: readonly RectSystem[], reach: number): RectPassage | null {
    const box = traceBox(from, to);
    const covered = coveredSystems(box, systems, reach);
    if (covered.length === 0) {
        return null;
    }
    const first = covered[0];
    const last = covered[covered.length - 1];
    const one = covered.length === 1;
    const upper = from.y <= to.y ? from : to;
    const lower = upper === from ? to : from;
    const start = lineAtOrBefore(first.lines, one ? box.left : upper.x);
    const end = start === undefined ? undefined : lineAtOrAfter(last.lines, one ? box.right : lower.x, start);
    if (start === undefined || end === undefined) {
        return null;
    }
    return { start, end, hand: one ? strokeHand(first.staves, box.top, box.bottom, true) : null };
}
