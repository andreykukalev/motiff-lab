// The roll's two playheads dragged (v0.5.2, #495 UX-08 and UX-10, the architect's design playback-model-v0.5.2, slice I3),
// on the iPad's roll with the lanes: the marker in the loop strip, the blue playhead inside the selection and the black one
// outside it (#543 6); the black one also by its line down the notes, across the whole piece (design question В5). Both go to the sixteenth grid of the bar they lie
// in (snapTick), as on the score; the roll sends them through PlayheadDrag. On the web's roll (slice W3) both are taken by
// their dot or knob on the bar numbers' edge instead (knobHeadAt). Pure, so Node tests it.

import type { Handle, Span } from "../playback/playback-model.js";
import { barOf, snapTick } from "../playback/playhead-drag.js";

/** Where the roll's playhead is across it (rollPlaces), and the loop strip between the bar numbers and the notes, in pixels. */
export interface LaneHeads {
    black: number;
    /** At the black one's place while it is inside the selection, the marker then the blue one's; else null. */
    blue: number | null;
    /** The loop strip's top and its bottom, where the notes begin. */
    stripTop: number;
    stripBottom: number;
    /** How far across from a playhead a press still takes it: half a touch target. */
    reach: number;
}

/**
 * The playhead a press takes (#543 6): by its marker in the loop strip, the blue one while it is blue and else the black
 * one, whatever its colour; down its line under the strip the black one; null for neither.
 */
export function laneHeadAt(x: number, y: number, heads: LaneHeads): Handle | null {
    const inStrip = y >= heads.stripTop && y < heads.stripBottom;
    if (inStrip && heads.blue !== null && Math.abs(x - heads.blue) <= heads.reach) {
        return "blue";
    }
    if ((inStrip || y >= heads.stripBottom) && Math.abs(x - heads.black) <= heads.reach) {
        return "black";
    }
    return null;
}

/**
 * On the web's roll, without the lanes (#495 UX-08, slice W3): the playhead a press takes by its dot or knob on the bar
 * numbers' lower edge (roll-modes.ts, playheadKnobs, one of them since #543 6), the nearer one when it is within `reach`
 * of both, the blue one where they lie as near; null for neither.
 */
export function knobHeadAt(x: number, y: number, knobs: { black: { x: number; y: number } | null; blue: { x: number; y: number } | null }, reach: number): Handle | null {
    const near = (knob: { x: number; y: number } | null): number =>
        knob === null || Math.abs(x - knob.x) > reach || Math.abs(y - knob.y) > reach ? Infinity : Math.hypot(x - knob.x, y - knob.y);
    const blue = near(knobs.blue);
    const black = near(knobs.black);
    if (blue === Infinity && black === Infinity) {
        return null;
    }
    return blue <= black ? "blue" : "black";
}

/** Where a playhead was drawn across the roll, in pixels, at a moment, in ms. */
export interface DrawnAt {
    at: number;
    x: number;
}

/**
 * The place across a playhead that a press is measured from (#533 4): playing, the line runs on while a finger goes to it,
 * so the finger lands where the line was a moment ago, and at a fast zoom outside the touch target. The line then reaches
 * back over the stretch it was drawn across since `since` (ms), never more than `most` pixels behind or ahead of where it is
 * now (`x`): the point of that stretch nearest the press. Standing still, or with nothing drawn lately, it is where it is.
 * For laneHeadAt and knobHeadAt, which measure their reach from it.
 */
export function reachedFrom(press: number, x: number, drawn: readonly DrawnAt[], since: number, most: number): number {
    let low = x;
    let high = x;
    for (const place of drawn) {
        if (place.at >= since) {
            low = Math.min(low, place.x);
            high = Math.max(high, place.x);
        }
    }
    low = Math.max(low, x - most);
    high = Math.min(high, x + most);
    return Math.min(Math.max(press, low), high);
}

/**
 * Where a roll playhead dragged to a tick goes: the black one anywhere in the piece, the blue one inside the selection, each
 * on the sixteenth grid of the bar it then lies in (bars in time order, each [start, end)); undefined for the blue one
 * without a selection.
 */
export function laneDragTick(handle: Handle, tick: number, bars: readonly Span[], selection: Span | null, end: number): number | undefined {
    if (handle === "blue" && selection === null) {
        return undefined;
    }
    const limits = handle === "blue" && selection !== null ? selection : { start: 0, end: Math.max(1, end) };
    const inside = Math.min(Math.max(tick, limits.start), limits.end - 1);
    const bar = barOf(bars, inside);
    return bar === undefined ? Math.round(inside) : snapTick(inside, bar, limits);
}
