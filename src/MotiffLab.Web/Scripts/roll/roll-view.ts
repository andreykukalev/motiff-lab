// What keeps the roll's view, its zoom on each axis and the stretch in view, as the player left it (v0.5.2, #495 BUG-04):
// only the player's own zoom, scroll and Fit, or a new selection, move it. The notes coming back after an edit, the same
// selection sent again, and fitting every selection turned on again (Expand) leave it where it is. Pure; no DOM here.

import type { Span } from "./roll-playhead.js";

/** How a selection shown differs from the one shown before: moved when it starts elsewhere or none was shown, changed when either end differs. */
export function selectionShift(before: Span | null, start: number, end: number): { moved: boolean; changed: boolean } {
    const moved = before === null || before.start !== start;
    return { moved, changed: moved || before.end !== end };
}

/**
 * Whether fitting every selection, turned on or off, fits the roll at once: only when it turns on, and not when the roll is
 * read by touch, where every new selection is fitted anyway and a view as Fit made it is fitted again on a new size, so the
 * zoom the player set in the drawer stays on Expand.
 */
export function fitsWhenTurned(was: boolean, on: boolean, touchReading: boolean): boolean {
    return on && !was && !touchReading;
}

/** The view as the roll keeps it: the left edge in ticks, the highest row shown (null for the top of the notes), whether Fit made it, and the selection's span. */
export interface RollView {
    scroll: number;
    pitchTop: number | null;
    fitted: boolean;
    span: Span | null;
}

/**
 * The view a load of the notes starts from. The same piece's notes again (`keep`: a note edited, a version taken in) keep
 * the view and the selection's span, so the selection sent back after them is not a new one; the editor keeps its place
 * as before; another piece starts at its beginning.
 */
export function viewOnLoad(before: RollView, keep: boolean, editing: boolean): RollView {
    if (keep) {
        return { ...before };
    }
    if (editing) {
        return { scroll: before.scroll, pitchTop: before.pitchTop, fitted: false, span: null };
    }
    return { scroll: 0, pitchTop: null, fitted: false, span: null };
}
