// Selection by where you touch on the roll (#601, the experiment experimental.placeSelection; selection report E and F):
// the pure part. In Select a box or a lasso drawn on empty space picks notes, a press is read by its place (an edge of the
// selected span, inside it, outside it: the score's rule, score/selection-places.ts), picked notes dragged move by
// sixteenths and semitones, and the span of picked notes goes out to the beats round them. Positions are in the roll's
// own units: ticks across, and down the keys a pitch as a real number, so a note's row centre is its pitch. Nothing here
// touches the DOM.

import type { RollNote } from "./roll-model.js";

/** How the roll picks notes in Select: a box (the default) or a lasso (report «Рамка или лассо»). */
export type PickShape = "box" | "lasso";

/** A point on the roll: a tick and a pitch, a note's row centre being its pitch. */
export interface RollPoint {
    tick: number;
    pitch: number;
}

/** A span of time on the roll, [start, end) in ticks. */
export interface TickSpan {
    start: number;
    end: number;
}

/** A sixteenth at 480 ticks a quarter: the step a picked note moves by across, as in Edit notes. */
export const MOVE_STEP = 120;

/** A note's centre on the roll: the middle of its time, on its pitch. */
export function noteCentre(note: Pick<RollNote, "onset" | "end" | "pitch">): RollPoint {
    return { tick: (note.onset + note.end) / 2, pitch: note.pitch };
}

/** Whether a point lies inside a closed path (even-odd rule); a path of fewer than three points holds nothing. */
export function insidePath(point: RollPoint, path: readonly RollPoint[]): boolean {
    if (path.length < 3) {
        return false;
    }
    let inside = false;
    for (let i = 0, j = path.length - 1; i < path.length; j = i++) {
        const a = path[i];
        const b = path[j];
        if (a.pitch > point.pitch !== b.pitch > point.pitch && point.tick < ((b.tick - a.tick) * (point.pitch - a.pitch)) / (b.pitch - a.pitch) + a.tick) {
            inside = !inside;
        }
    }
    return inside;
}

/**
 * The notes a shape drawn on the roll picks, by index in time order: those whose centre lies inside it (the mockup's rule:
 * a note the box only grazes is not picked). A box spans from the path's first point to its last, either way round; a
 * lasso is the path closed.
 */
export function notesInShape(notes: readonly Pick<RollNote, "onset" | "end" | "pitch">[], shape: PickShape, path: readonly RollPoint[]): number[] {
    if (path.length < 2) {
        return [];
    }
    const from = path[0];
    const to = path[path.length - 1];
    const [start, end] = from.tick <= to.tick ? [from.tick, to.tick] : [to.tick, from.tick];
    const [low, high] = from.pitch <= to.pitch ? [from.pitch, to.pitch] : [to.pitch, from.pitch];
    const picked: number[] = [];
    notes.forEach((note, index) => {
        const centre = noteCentre(note);
        const inside = shape === "box"
            ? centre.tick >= start && centre.tick <= end && centre.pitch >= low && centre.pitch <= high
            : insidePath(centre, path);
        if (inside) {
            picked.push(index);
        }
    });
    return picked;
}

/** The notes picked before and those a box from inside the span adds, once each, in time order. */
export function addedPicks(before: Iterable<number>, added: Iterable<number>): number[] {
    return [...new Set([...before, ...added])].sort((a, b) => a - b);
}

/**
 * The span of the notes picked, out to the beats round them (report E: «отрезок = их время, по долям наружу»): from the
 * last beat line at or before the first onset to the first at or after the last end; past the lines, the notes' own time.
 * `lines` are the bar and beat lines in time order. Null for no notes.
 */
export function pickedSpan(notes: readonly Pick<RollNote, "onset" | "end">[], picked: readonly number[], lines: readonly number[]): TickSpan | null {
    if (picked.length === 0) {
        return null;
    }
    const onset = Math.min(...picked.map((index) => notes[index].onset));
    const end = Math.max(...picked.map((index) => notes[index].end));
    const before = lines.filter((line) => line <= onset);
    const after = lines.find((line) => line >= end && line > onset);
    return { start: before.length > 0 ? before[before.length - 1] : onset, end: after ?? end };
}

/**
 * The picked notes still inside a span whose edge moved (report E: «сузится до попавших»): those whose onset lies in
 * [start, end). An edge moved outward picks no new notes; a plain span (no picks) stays plain.
 */
export function narrowedPicks(notes: readonly Pick<RollNote, "onset">[], picked: readonly number[], span: TickSpan): number[] {
    return picked.filter((index) => notes[index].onset >= span.start && notes[index].onset < span.end);
}

/** A drag of picked notes in steps: whole sixteenths across and whole semitones up (a drag up is a positive pitch). */
export function moveSteps(ticks: number, pitches: number): { ticks: number; pitches: number } {
    return { ticks: Math.round(ticks / MOVE_STEP) * MOVE_STEP, pitches: Math.round(pitches) };
}

/** Where a note moved by whole steps goes: never before the piece's start nor off the keys (MIDI 0–127). */
export function movedNote(note: Pick<RollNote, "onset" | "end" | "pitch">, steps: { ticks: number; pitches: number }): { onset: number; pitch: number; length: number } {
    return {
        onset: Math.max(0, note.onset + steps.ticks),
        pitch: Math.min(127, Math.max(0, note.pitch + steps.pitches)),
        length: note.end - note.onset,
    };
}

/**
 * A move that keeps every picked note on the keys and after the start: the steps cut so the lowest, highest and earliest
 * notes stop at the edge rather than bunch up (the group keeps its shape).
 */
export function keptSteps(notes: readonly Pick<RollNote, "onset" | "pitch">[], picked: readonly number[], steps: { ticks: number; pitches: number }): { ticks: number; pitches: number } {
    if (picked.length === 0) {
        return { ticks: 0, pitches: 0 };
    }
    const earliest = Math.min(...picked.map((index) => notes[index].onset));
    const lowest = Math.min(...picked.map((index) => notes[index].pitch));
    const highest = Math.max(...picked.map((index) => notes[index].pitch));
    return {
        ticks: Math.max(steps.ticks, 0 - earliest),
        pitches: Math.min(127 - highest, Math.max(0 - lowest, steps.pitches)),
    };
}
