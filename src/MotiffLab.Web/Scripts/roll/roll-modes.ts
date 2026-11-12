// The modes and the two playheads of version 0.5 on the Piano Roll (the experiment Two playheads and modes, Settings ·
// Experimental; product spec interaction-modes, «Правила выделения по режимам», and two-playheads). One group in the bar
// for the score and the roll. On the roll: Navigate moves the roll under the mouse; Select starts a new selection by beats
// with a drag from outside it (Andrei's exception to mockup a4); Edit notes draws a box round notes to pick them for the
// editor, which is not a selection; Pen draws nothing here. In every mode a drag that starts inside the selection leaves it
// alone, a click places a playhead on the beat under it (inside the selection its own, else the global one) and never
// changes the selection, and the edges still resize it. The playheads are drawn as on the score: the global one a column one
// beat wide with a cap over the bar numbers, the selection's a cobalt line with a knob. Nothing here touches the DOM.

import type { InteractionMode } from "../score/score-modes.js";
import type { PlayheadsState } from "../score/playheads.js";
import type { RollBar, RollNote } from "./roll-model.js";

/** What a press on the notes or the bar numbers begins under the rules. */
export type RollGesture = "pan" | "range" | "place" | "box";

/**
 * What a press begins: in Navigate the hand; in Select a range from outside the selection; in Edit notes a box; anywhere
 * else, and inside the selection, only a click that places a playhead.
 */
export function gestureFor(mode: InteractionMode, insideSelection: boolean): RollGesture {
    switch (mode) {
        case "navigate":
            return "pan";
        case "select":
            return insideSelection ? "place" : "range";
        case "edit":
            return "box";
        default:
            return "place";
    }
}

/** The beat a tick falls in, from the bars' beats: from its beat line to the next, never past the bar's end. */
export function beatAt(bars: readonly RollBar[], tick: number): { start: number; end: number } | undefined {
    const bar = bars.find((each) => tick >= each.onset && tick < each.end);
    if (bar === undefined) {
        return undefined;
    }
    const step = bar.beatTicks > 0 ? bar.beatTicks : bar.end - bar.onset;
    const start = bar.onset + Math.floor((tick - bar.onset) / step) * step;
    return { start, end: Math.min(start + step, bar.end) };
}

/** A box drawn across the roll, in ticks and keys: from one corner to the other, either way round. */
export interface RollBox {
    fromTick: number;
    toTick: number;
    fromPitch: number;
    toPitch: number;
}

/** The notes a box takes: those that sound inside its time and lie on its keys. */
export function notesInBox(notes: readonly RollNote[], box: RollBox): number[] {
    const [start, end] = box.fromTick <= box.toTick ? [box.fromTick, box.toTick] : [box.toTick, box.fromTick];
    const [low, high] = box.fromPitch <= box.toPitch ? [box.fromPitch, box.toPitch] : [box.toPitch, box.fromPitch];
    const picked: number[] = [];
    notes.forEach((note, index) => {
        if (note.onset < end && note.end > start && note.pitch >= low && note.pitch <= high) {
            picked.push(index);
        }
    });
    return picked;
}

/** The colours the roll reads from the kit's tokens. */
export interface PlayheadInks {
    ink: string;
    accent: string;
    surface: string;
    paper: string;
}

/** Where the roll draws: ticks to pixels, the bar numbers' height and the notes' left and right. */
export interface RollFrame {
    xOf(tick: number): number;
    header: number;
    left: number;
    right: number;
    height: number;
}

const COLUMN_SHARE = 0.2;
const CAP_WIDTH = 12;
const CAP_HEIGHT = 8;
const KNOB_RADIUS = 7;
const RING = 2;

/** The two playheads over the roll: the global column with its cap, and the selection's line with its knob. */
export function drawPlayheads(context: CanvasRenderingContext2D, frame: RollFrame, bars: readonly RollBar[], state: PlayheadsState, playerTick: number, playing: boolean, inks: PlayheadInks): void {
    const global = state.selectionHolds ? state.global : playerTick;
    const selection = state.selectionHolds ? playerTick : state.selection;
    const beat = beatAt(bars, global);
    if (beat !== undefined) {
        drawColumn(context, frame, beat, playing && !state.selectionHolds, inks);
    }
    if (selection !== null) {
        drawSelectionLine(context, frame, frame.xOf(selection), inks);
    }
}

function drawColumn(context: CanvasRenderingContext2D, frame: RollFrame, beat: { start: number; end: number }, plays: boolean, inks: PlayheadInks): void {
    const left = frame.xOf(beat.start);
    const right = frame.xOf(beat.end);
    if (right < frame.left || left > frame.right) {
        return;
    }
    context.save();
    context.globalAlpha = COLUMN_SHARE;
    context.fillStyle = inks.ink;
    context.fillRect(Math.max(left, frame.left), frame.header, Math.min(right, frame.right) - Math.max(left, frame.left), frame.height - frame.header);
    context.restore();
    const middle = (left + right) / 2;
    if (middle < frame.left || middle > frame.right) {
        return;
    }
    const top = frame.header - CAP_HEIGHT - 2;
    context.beginPath();
    context.moveTo(middle - CAP_WIDTH / 2, top);
    context.lineTo(middle + CAP_WIDTH / 2, top);
    context.lineTo(middle, top + CAP_HEIGHT);
    context.closePath();
    context.fillStyle = plays ? inks.ink : inks.paper;
    context.fill();
    context.lineWidth = 1.5;
    context.lineJoin = "round";
    context.strokeStyle = inks.ink;
    context.stroke();
}

function drawSelectionLine(context: CanvasRenderingContext2D, frame: RollFrame, at: number, inks: PlayheadInks): void {
    if (at < frame.left || at > frame.right) {
        return;
    }
    const x = Math.round(at);
    context.fillStyle = inks.accent;
    context.fillRect(x - 1, frame.header, 2, frame.height - frame.header);
    context.beginPath();
    context.arc(x, frame.header, KNOB_RADIUS + RING, 0, 2 * Math.PI);
    context.fillStyle = inks.surface;
    context.fill();
    context.beginPath();
    context.arc(x, frame.header, KNOB_RADIUS, 0, 2 * Math.PI);
    context.fillStyle = inks.accent;
    context.fill();
}

/** The box being drawn in Edit notes: a dashed cobalt outline over a light wash. */
export function drawBox(context: CanvasRenderingContext2D, from: { x: number; y: number }, to: { x: number; y: number }, inks: PlayheadInks): void {
    const left = Math.min(from.x, to.x);
    const top = Math.min(from.y, to.y);
    const width = Math.abs(to.x - from.x);
    const height = Math.abs(to.y - from.y);
    context.save();
    context.globalAlpha = 0.08;
    context.fillStyle = inks.accent;
    context.fillRect(left, top, width, height);
    context.restore();
    context.save();
    context.setLineDash([4, 3]);
    context.lineWidth = 1;
    context.strokeStyle = inks.accent;
    context.strokeRect(left + 0.5, top + 0.5, width, height);
    context.restore();
}
