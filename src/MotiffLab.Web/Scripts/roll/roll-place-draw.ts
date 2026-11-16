// What the roll draws for selection by where you touch (#601; report E, «Что видно»), as the score shows it (v0.6.4, Andrei
// 19:55Z: one look for both): at each edge of the span the kit's hollow handle at the middle of the edge (HANDLE, the
// score's `.sel-edge`, ds.css `.handle`: Andrei's spec 5 and 6, never at a corner), filled while taken; the picked notes
// ringed in the accent; and while a press goes on, the dashed rectangle from the press to the pointer (the score's
// `.sel-trace`) or the lasso, the span it makes, the old selection dashed until it is replaced, and picked notes where a
// drag takes them. Colours come from the kit's tokens through the roll (cobalt: pointing and selection only).

import type { RollNote } from "./roll-model.js";
import type { PlacePreview } from "./roll-place-gesture.js";
import type { RollPoint, TickSpan } from "./roll-place.js";

/** Where the roll draws: ticks and pitches to pixels, its rows, the bar numbers' height and the notes' left and right. */
export interface PlaceFrame {
    xOf(tick: number): number;
    /** The top of a pitch's row. */
    yOf(pitch: number): number;
    row: number;
    header: number;
    left: number;
    right: number;
    height: number;
}

/** The kit's colours the drawing takes: the accent, the band's tint, the handle's surface and the notation's ink. */
export interface PlaceInks {
    accent: string;
    tint: string;
    surface: string;
    ink?: string;
}

/**
 * The selection's handle, the one place its look is set on the roll: the kit's `.handle` (ds.css: 14 px across, a 2 px
 * accent ring on the surface), as the score's `.sel-edge` draws it; `y` where it sits down the edge, the middle of the notes.
 */
export const HANDLE = { diameter: 14, ring: 2, y: (frame: PlaceFrame): number => frame.header + (frame.height - frame.header) / 2 };

const EDGE_WIDTH = 2;
/** The rectangle drawn: the notation's ink at this share, as the score's `.sel-trace` (color-mix 45%). */
const TRACE_ALPHA = 0.45;
const DASH = [4, 3];
const WASH_ALPHA = 0.08;

/** A selected span's handles: at the middle of its start's edge and of its end's, the one taken (`hot`) filled. */
export function drawPlaceEdges(context: CanvasRenderingContext2D, frame: PlaceFrame, span: TickSpan, inks: PlaceInks, hot: "start" | "end" | null = null): void {
    const y = HANDLE.y(frame);
    for (const [edge, tick] of [["start", span.start], ["end", span.end]] as const) {
        const x = frame.xOf(tick);
        if (x < frame.left || x > frame.right) {
            continue;
        }
        const radius = (HANDLE.diameter - HANDLE.ring) / 2;
        context.save();
        context.beginPath();
        context.arc(x, y, radius, 0, 2 * Math.PI);
        context.fillStyle = edge === hot ? inks.accent : inks.surface;
        context.fill();
        context.lineWidth = HANDLE.ring;
        context.strokeStyle = inks.accent;
        context.stroke();
        context.restore();
    }
}

/** The picked notes ringed in the accent, at where a move takes them (`steps`), the others left alone. */
export function drawPickedNotes(context: CanvasRenderingContext2D, frame: PlaceFrame, notes: readonly RollNote[], picked: Iterable<number>, inks: PlaceInks, steps = { ticks: 0, pitches: 0 }): void {
    context.save();
    context.lineWidth = EDGE_WIDTH;
    for (const index of picked) {
        const note = notes[index];
        const x = frame.xOf(note.onset + steps.ticks);
        const w = Math.max(EDGE_WIDTH, frame.xOf(note.end + steps.ticks) - x - 1);
        const y = frame.yOf(note.pitch + steps.pitches) + 1;
        if (x + w < frame.left || x > frame.right || y < frame.header || y > frame.height) {
            continue;
        }
        if (steps.ticks !== 0 || steps.pitches !== 0) {
            context.globalAlpha = 0.5;
            context.fillStyle = inks.accent;
            context.fillRect(x, y, w, Math.max(EDGE_WIDTH, frame.row - 2));
            context.globalAlpha = 1;
        }
        context.strokeStyle = inks.accent;
        context.strokeRect(x - 1.5, y - 1.5, w + 3, Math.max(EDGE_WIDTH, frame.row - 2) + 3);
    }
    context.restore();
}

/** A press going on: the shape drawn and the span it makes, or the span a drag makes, the old selection dashed. */
export function drawPlacePreview(context: CanvasRenderingContext2D, frame: PlaceFrame, preview: PlacePreview, notes: readonly RollNote[], inks: PlaceInks): void {
    if (preview.kind === "move") {
        drawPickedNotes(context, frame, notes, preview.indices, inks, preview.steps);
        return;
    }
    if (preview.old !== null) {
        dashedSpan(context, frame, preview.old, inks);
    }
    const span = preview.span;
    if (span !== null) {
        const left = Math.max(frame.left, frame.xOf(span.start));
        const right = Math.min(frame.right, frame.xOf(span.end));
        if (right > left) {
            context.fillStyle = inks.tint;
            context.fillRect(left, frame.header, right - left, frame.height - frame.header);
        }
        drawPlaceEdges(context, frame, span, inks, preview.kind === "span" ? preview.edge : null);
    }
    if (preview.kind === "span" && preview.trace !== null) {
        drawTrace(context, frame, preview.trace, inks);
    }
    if (preview.kind === "shape") {
        drawShape(context, frame, preview.path, preview.shape === "box", inks);
        drawPickedNotes(context, frame, notes, preview.picks, inks);
    }
}

/** The old selection's outline, dashed, while a new one is drawn: what Undo brings back (report C, «Заменить»). */
function dashedSpan(context: CanvasRenderingContext2D, frame: PlaceFrame, span: TickSpan, inks: PlaceInks): void {
    const left = frame.xOf(span.start);
    const right = frame.xOf(span.end);
    context.save();
    context.setLineDash(DASH);
    context.strokeStyle = inks.accent;
    context.lineWidth = 1;
    context.strokeRect(Math.round(left) + 0.5, frame.header + 0.5, Math.max(1, right - left - 1), frame.height - frame.header - 1);
    context.restore();
}

/** The rectangle a new selection is drawn with, from the press to the pointer: thin, dashed, the notation's ink (the score's `.sel-trace`). */
function drawTrace(context: CanvasRenderingContext2D, frame: PlaceFrame, [from, to]: readonly [RollPoint, RollPoint], inks: PlaceInks): void {
    const x0 = frame.xOf(from.tick);
    const x1 = frame.xOf(to.tick);
    const y0 = frame.yOf(from.pitch) + frame.row / 2;
    const y1 = frame.yOf(to.pitch) + frame.row / 2;
    context.save();
    context.setLineDash(DASH);
    context.lineWidth = 1;
    context.globalAlpha = TRACE_ALPHA;
    context.strokeStyle = inks.ink ?? inks.accent;
    context.strokeRect(Math.min(x0, x1) + 0.5, Math.min(y0, y1) + 0.5, Math.abs(x1 - x0), Math.abs(y1 - y0));
    context.restore();
}

/** The box (from the path's first point to its last) or the lasso (the path closed), dashed over a light wash. */
function drawShape(context: CanvasRenderingContext2D, frame: PlaceFrame, path: readonly RollPoint[], box: boolean, inks: PlaceInks): void {
    if (path.length < 2) {
        return;
    }
    const centre = (point: RollPoint): [number, number] => [frame.xOf(point.tick), frame.yOf(point.pitch) + frame.row / 2];
    context.save();
    context.beginPath();
    if (box) {
        const [x0, y0] = centre(path[0]);
        const [x1, y1] = centre(path[path.length - 1]);
        context.rect(Math.min(x0, x1) + 0.5, Math.min(y0, y1) + 0.5, Math.abs(x1 - x0), Math.abs(y1 - y0));
    } else {
        path.forEach((point, index) => {
            const [x, y] = centre(point);
            if (index === 0) {
                context.moveTo(x, y);
            } else {
                context.lineTo(x, y);
            }
        });
        context.closePath();
    }
    context.globalAlpha = WASH_ALPHA;
    context.fillStyle = inks.accent;
    context.fill();
    context.globalAlpha = 1;
    context.setLineDash(DASH);
    context.lineWidth = 1;
    context.strokeStyle = inks.accent;
    context.stroke();
    context.restore();
}
