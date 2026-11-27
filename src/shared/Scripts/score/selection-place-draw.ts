// What the score draws for selection by the place of the touch (v0.6.2, #601; the audit's W3.14), as the roll does in
// roll-place-draw.ts: the selection's two edges over the band, each the band's full height with the kit's hollow handle
// at its middle (score.css `.sel-edge`, ds.css `.handle`), and while a gesture runs its overlays: the old selection
// dashed while a new one would replace it (`.sel-ghost`), the rectangle drawn (`.sel-trace`), the addition dashed while
// extending (`.sel-add`) and pale ticks where a moving edge can stop (`.sel-target`). The layer (selection-place-layer.ts)
// takes the presses and says what to show; this only draws it, in the score's content pixels.

import type { Rect } from "./page-reader.js";
import type { ScoreBar, ScoreGeometry } from "./score-geometry.js";
import { handRuns, setBox } from "./selection-boxes.js";
import type { PlacedRange } from "./selection-place-layer.js";
import { type EdgeHandle, type Place, type PlaceBox, type PlaceGesture, edgeHandles, targetLines } from "../selection/selection-places.js";
import { type Point, traceBox } from "../selection/selection-rect.js";

const GHOST_CLASS = "sel-ghost";
const ADD_CLASS = "sel-add";
const EDGE_CLASS = "sel-edge";
const TARGET_CLASS = "sel-target";
const TRACE_CLASS = "sel-trace";
const OVERLAYS = [GHOST_CLASS, ADD_CLASS, TARGET_CLASS, TRACE_CLASS];

/** What a gesture under way shows over the score. */
export interface PlaceShown {
    /** The selection when the gesture began, and what the gesture would select now; null for none. */
    selection: PlacedRange | null;
    preview: PlacedRange | null;
    phase: PlaceGesture["phase"];
    /** Where the press began and where the pointer is now, in content pixels. */
    from: Point;
    at: Point;
    /** The lines a moving edge can stop at; asked only while an edge moves. */
    lines(): readonly number[];
    /** Where a line of a bar stands across it. */
    lineX(bar: ScoreBar, line: number): number;
}

/**
 * The selection's edges over the band's boxes (the first's start, the last's end), the one a gesture holds (`hot`)
 * lit; with no boxes none, and the edges drawn before go.
 */
export function drawEdges(root: HTMLElement, boxes: readonly PlaceBox[], hot: Place | undefined): void {
    for (const old of root.querySelectorAll(`:scope > .${EDGE_CLASS}`)) {
        old.remove();
    }
    if (boxes.length === 0) {
        return;
    }
    const first = boxes[0];
    const last = boxes[boxes.length - 1];
    const [start, end] = edgeHandles(boxes);
    root.append(edge("start", start, first.top, first.bottom, hot === "edge-start"));
    root.append(edge("end", end, last.top, last.bottom, hot === "edge-end"));
}

/** An edge's line from `top` to `bottom`, its circle at `handle`: where the layer's `handleAt` takes a press on it. */
function edge(which: "start" | "end", handle: EdgeHandle, top: number, bottom: number, hot: boolean): HTMLElement {
    const element = document.createElement("div");
    element.className = `${EDGE_CLASS} ${which}${hot ? " hot" : ""}`;
    element.setAttribute("aria-hidden", "true");
    element.style.left = `${handle.x}px`;
    element.style.top = `${top}px`;
    element.style.height = `${Math.max(4, bottom - top)}px`;
    element.style.setProperty("--handle-y", `${handle.y - top}px`);
    return element;
}

/** The old selection dashed while a new one is drawn, the addition dashed while extending, the targets while an edge moves. */
export function drawOverlays(root: HTMLElement, geometry: ScoreGeometry, shown: PlaceShown): void {
    clearOverlays(root);
    const { selection, preview, phase } = shown;
    // The old selection dashed while a new one would replace it; while the rectangle covers no music it shows as it is.
    if (selection !== null && preview !== null && (phase === "draw" || phase === "bar")) {
        boxes(root, GHOST_CLASS, handRuns(geometry, selection.start, selection.end, selection.hand));
    }
    if (phase === "draw") {
        boxes(root, TRACE_CLASS, [traceBox(shown.from, shown.at)], false);
    }
    if (selection !== null && preview !== null && phase === "extend") {
        boxes(root, ADD_CLASS, [
            ...(preview.start < selection.start ? handRuns(geometry, preview.start, selection.start, selection.hand) : []),
            ...(preview.end > selection.end ? handRuns(geometry, selection.end, preview.end, selection.hand) : []),
        ]);
    }
    if (preview !== null && phase === "edge") {
        targets(root, geometry, targetLines(preview, shown.lines()), shown);
    }
}

export function clearOverlays(root: HTMLElement): void {
    for (const old of root.querySelectorAll(OVERLAYS.map((name) => `:scope > .${name}`).join(", "))) {
        old.remove();
    }
}

/** Boxes over the score, at least 4 pixels each way (`least`), or exactly as given (the rectangle drawn). */
function boxes(root: HTMLElement, className: string, runs: readonly Rect[], least = true): void {
    for (const run of runs) {
        const box = document.createElement("div");
        box.className = className;
        box.setAttribute("aria-hidden", "true");
        if (least) {
            setBox(box, run);
        } else {
            box.style.left = `${run.left}px`;
            box.style.top = `${run.top}px`;
            box.style.width = `${run.right - run.left}px`;
            box.style.height = `${run.bottom - run.top}px`;
        }
        root.append(box);
    }
}

/** Pale ticks on the system under the pointer, at each line the moving edge can stop at. */
function targets(root: HTMLElement, geometry: ScoreGeometry, lines: readonly number[], shown: PlaceShown): void {
    const system = geometry.barNear(shown.at.x, shown.at.y)?.system;
    for (const bar of geometry.bars) {
        if (bar.system !== system) {
            continue;
        }
        for (const line of lines) {
            if (line < bar.tick || line >= bar.end) {
                continue;
            }
            const tick = document.createElement("div");
            tick.className = TARGET_CLASS;
            tick.setAttribute("aria-hidden", "true");
            tick.style.left = `${shown.lineX(bar, line)}px`;
            tick.style.top = `${bar.top}px`;
            root.append(tick);
        }
    }
}
