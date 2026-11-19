// The pen's colour bars (finding 31, v0.10 d-5; only the iPad uses it): the tool colours the score exactly as Select
// selects it, by the same place rule (selection-place-layer.ts with no selection, so every press is a new passage): a drag
// draws the dashed rectangle and colours what it covers on the selection's grid (selection-grid.ts, a tuplet one block),
// a tap on a bar colours the bar and a tap on a note head the beats round the note, as Select picks it. What it colours
// goes to Swift, which keeps it as music-core's highlights; nothing here changes the selection. The eraser takes the
// colour from the cells of the grid it passes over where a highlight is drawn. Pure; nothing here touches the DOM.

import type { Rect } from "./score-geometry.js";
import type { TickRange } from "./selection-gesture.js";
import { drawnRange } from "./selection-places.js";

/** A highlight as the page draws it: its span in ticks and the boxes of its band. */
export interface PaintedBand extends TickRange {
    runs: readonly Rect[];
}

/** A point in the score's content pixels. */
export interface Point {
    x: number;
    y: number;
}

/**
 * What a tap on a note head colours: the beats round the note, from the line at or before its onset to the line at or
 * after its end (`lines`, the beat lines with the barlines), as Select's tap on a note takes the beats round it.
 */
export function noteUnit(onset: number, end: number, lines: readonly number[]): TickRange | undefined {
    return drawnRange(onset, Math.max(onset, end), lines);
}

/** The spans in time order, those that overlap or touch made one. */
export function mergedSpans(spans: readonly TickRange[]): TickRange[] {
    const merged: TickRange[] = [];
    for (const span of [...spans].filter((each) => each.end > each.start).sort((a, b) => a.start - b.start)) {
        const last = merged[merged.length - 1];
        if (last !== undefined && span.start <= last.end) {
            last.end = Math.max(last.end, span.end);
        } else {
            merged.push({ ...span });
        }
    }
    return merged;
}

/**
 * The stretches the eraser takes the colour from: for each of its points on a highlight's band, the cell of the grid under
 * it (`cellAt`) within that highlight's span, all of them merged. Points off every band take nothing.
 */
export function rubbedColour(points: readonly Point[], bands: readonly PaintedBand[], cellAt: (point: Point) => TickRange | undefined): TickRange[] {
    const spans: TickRange[] = [];
    for (const point of points) {
        const under = bands.filter((band) => band.runs.some((run) => point.x >= run.left && point.x <= run.right && point.y >= run.top && point.y <= run.bottom));
        const cell = under.length === 0 ? undefined : cellAt(point);
        if (cell === undefined) {
            continue;
        }
        for (const band of under) {
            const start = Math.max(cell.start, band.start);
            const end = Math.min(cell.end, band.end);
            if (end > start) {
                spans.push({ start, end });
            }
        }
    }
    return mergedSpans(spans);
}
