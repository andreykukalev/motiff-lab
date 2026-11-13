// The roll's two playheads dragged (v0.5.2, #495 UX-08 and UX-10, the architect's design playback-model-v0.5.2, slice I3),
// on the iPad's roll with the lanes: the blue playhead by its handle in the loop strip, inside the selection; the black one
// by its line down the notes, across the whole piece (design question В5). Both go to the sixteenth grid of the bar they lie
// in (snapTick), as on the score; the roll sends them through PlayheadDrag. Pure, so Node tests it.

import type { Handle, Span } from "../playback/playback-model.js";
import { barOf, snapTick } from "../playback/playhead-drag.js";

/** Where the roll's playheads are across it, and the loop strip between the bar numbers and the notes, in pixels. */
export interface LaneHeads {
    black: number;
    /** Null without a selection. */
    blue: number | null;
    /** The loop strip's top and its bottom, where the notes begin. */
    stripTop: number;
    stripBottom: number;
    /** How far across from a playhead a press still takes it: half a touch target. */
    reach: number;
}

/** The playhead a press takes: the blue one by its handle in the loop strip, the black one down its line; null for neither. */
export function laneHeadAt(x: number, y: number, heads: LaneHeads): Handle | null {
    if (heads.blue !== null && y >= heads.stripTop && y < heads.stripBottom && Math.abs(x - heads.blue) <= heads.reach) {
        return "blue";
    }
    if (y >= heads.stripBottom && Math.abs(x - heads.black) <= heads.reach) {
        return "black";
    }
    return null;
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
