// The zoom of the iPad's roll on each axis (v0.5.2, #479 item 23, approved mockup): the time as bars in view, "− 9 bars +"
// with Fit along the bottom, and the pitch as octaves in view, "+ 3 octaves −" over the keyboard column. A pinch zooms both
// axes at once through the whole gesture (#521 4): its spread across the time, its spread up and down the pitch, with no
// axis chosen when it begins. The values are counts the player reads (Andrei chose words over percentages); the iPad writes
// them in its own words. Pure arithmetic; nothing here touches the DOM.

/** A bar's stretch of time in ticks, [onset, end). */
export interface BarSpan {
    onset: number;
    end: number;
}

/** The rows of an octave. */
export const OCTAVE_ROWS = 12;

/** The share of bars in view between two ticks, unrounded: each bar counts by the share of it in view. */
export function barShareInView(bars: readonly BarSpan[], from: number, to: number): number {
    let share = 0;
    for (const bar of bars) {
        const length = bar.end - bar.onset;
        if (length > 0 && bar.end > from && bar.onset < to) {
            share += (Math.min(bar.end, to) - Math.max(bar.onset, from)) / length;
        }
    }
    return share;
}

/**
 * How many bars are in view between two ticks, as the control says it: a view of eight and a half bars says 9 and one of two
 * bars and a sliver says 2; at least 1 while any bar is in view, 0 with none.
 */
export function barsInView(bars: readonly BarSpan[], from: number, to: number): number {
    const share = barShareInView(bars, from, to);
    return share > 0 ? Math.max(1, Math.round(share)) : 0;
}

/**
 * The bars a step of the time zoom aims at from the count shown: + (in) shows fewer, − (out) more, by a quarter of the count
 * and at least one bar, so the number on the control changes with every step. Never under 1.
 */
export function stepBars(count: number, direction: 1 | -1): number {
    const shown = Math.max(1, Math.round(count));
    const step = Math.max(1, Math.round(shown / 4));
    return Math.max(1, direction > 0 ? shown - step : shown + step);
}

/** How many octaves the rows in view make: rounded, at least 1. */
export function octavesInView(rows: number): number {
    return Math.max(1, Math.round(rows / OCTAVE_ROWS));
}

/** The octaves a step of the pitch zoom aims at: + (in) one fewer, never under 1; − (out) one more. */
export function stepOctaves(octaves: number, direction: 1 | -1): number {
    return direction > 0 ? Math.max(1, octaves - 1) : octaves + 1;
}

/** The row height that shows a number of octaves in a height, within the rows' limits. */
export function rowForOctaves(octaves: number, height: number, minRow: number, maxRow: number): number {
    return Math.min(maxRow, Math.max(minRow, Math.floor(height / Math.max(1, octaves * OCTAVE_ROWS))));
}

/** A pinch's fingers' spread across and up and down, in pixels: one finger's place less the other's. */
export interface PinchSpread {
    dx: number;
    dy: number;
}

/** Under this spread along an axis, in pixels, a pinch does not zoom that axis: the fingers lie nearly in line across it. */
export const PINCH_LEAST = 40;

/**
 * How much a move of a pinch zooms each axis (#521 4): the fingers' spread along it now over the spread it was last measured
 * at, the time across and the pitch up and down, both at once in every move, whatever way the pinch began; and the spread to
 * measure the next move from. An axis whose spread is under `least` zooms by 1 and keeps the spread it had, so fingers in
 * line across it leave it until they spread along it, and the zoom then follows them from there without a jump.
 */
export function pinchStep(before: PinchSpread, now: PinchSpread, least = PINCH_LEAST): { time: number; pitch: number; spread: PinchSpread } {
    const measured = (value: number): boolean => Math.abs(value) >= least;
    const factor = (from: number, to: number): number => (measured(from) && measured(to) ? Math.abs(to) / Math.abs(from) : 1);
    return {
        time: factor(before.dx, now.dx),
        pitch: factor(before.dy, now.dy),
        spread: { dx: measured(now.dx) ? now.dx : before.dx, dy: measured(now.dy) ? now.dy : before.dy },
    };
}

/** What the roll tells its host about its zoom: the counts on the controls, which steps are left, and whether Zoom Out shows (#521 2.2). */
export interface AxisZoom {
    bars: number;
    octaves: number;
    timeIn: boolean;
    timeOut: boolean;
    pitchIn: boolean;
    pitchOut: boolean;
    zoomOut: boolean;
}

/** Whether two reports say the same, so the host hears only of a change. */
export function sameZoom(a: AxisZoom | null, b: AxisZoom): boolean {
    return a !== null && a.bars === b.bars && a.octaves === b.octaves && a.timeIn === b.timeIn && a.timeOut === b.timeOut
        && a.pitchIn === b.pitchIn && a.pitchOut === b.pitchOut && a.zoomOut === b.zoomOut;
}
