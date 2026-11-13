// The zoom of the iPad's roll on each axis (v0.5.2, #479 item 23, approved mockup): the time as bars in view, "− 9 bars +"
// with Fit along the bottom, and the pitch as octaves in view, "+ 3 octaves −" over the keyboard column. A pinch zooms the
// axis it spreads along: across the time, up and down the pitch, both on a diagonal. The values are counts the player reads
// (Andrei chose words over percentages); the iPad writes them in its own words. Pure arithmetic; nothing here touches the DOM.

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

/**
 * Which axes a pinch zooms, from how its two fingers lie when it starts: across the time when they lie more side by side
 * than one over the other by two to one, up and down the pitch the other way round, and both on a diagonal.
 */
export function pinchAxes(dx: number, dy: number): { time: boolean; pitch: boolean } {
    const across = Math.abs(dx);
    const up = Math.abs(dy);
    return { time: across * 2 >= up, pitch: up * 2 >= across };
}

/**
 * How much a pinch has zoomed each axis it zooms: the fingers' spread along it now over its spread when the pinch began; 1 for
 * an axis the pinch does not zoom. A spread under `least` pixels when it began reads as no spread.
 */
export function pinchFactors(start: { dx: number; dy: number }, now: { dx: number; dy: number }, least = 24): { time: number; pitch: number } {
    const axes = pinchAxes(start.dx, start.dy);
    const factor = (from: number, to: number): number => (Math.abs(from) < least ? 1 : Math.max(1, Math.abs(to)) / Math.abs(from));
    return { time: axes.time ? factor(start.dx, now.dx) : 1, pitch: axes.pitch ? factor(start.dy, now.dy) : 1 };
}

/** What the roll tells its host about its zoom: the counts on the controls and which steps are left. */
export interface AxisZoom {
    bars: number;
    octaves: number;
    timeIn: boolean;
    timeOut: boolean;
    pitchIn: boolean;
    pitchOut: boolean;
}

/** Whether two reports say the same, so the host hears only of a change. */
export function sameZoom(a: AxisZoom | null, b: AxisZoom): boolean {
    return a !== null && a.bars === b.bars && a.octaves === b.octaves && a.timeIn === b.timeIn && a.timeOut === b.timeOut
        && a.pitchIn === b.pitchIn && a.pitchOut === b.pitchOut;
}
