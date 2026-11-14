// The modes and the two playheads of version 0.5 on the Piano Roll (the experiment Two playheads and modes, Settings ·
// Experimental; product spec interaction-modes, «Правила выделения по режимам», and two-playheads). One group in the bar
// for the score and the roll. On the roll: Navigate moves the roll under the mouse; Select starts a new selection by beats
// with a drag from outside it (Andrei's exception to mockup a4); Edit notes draws a box round notes to pick them for the
// editor, which is not a selection; Pen draws nothing here. In every mode a drag that starts inside the selection leaves it
// alone, a click places a playhead on the beat under it (inside the selection its own, else the global one) and never
// changes the selection, and the edges still resize it. The playheads are the playback model's (v0.5.2, #495 UX-10,
// playback-model.ts viewOf): the black one an ink line with a dot on the bar numbers, the blue one a cobalt line with a knob;
// no grey column. Nothing here touches the DOM.

import type { InteractionMode } from "../score/score-modes.js";
import type { ModelPlaces } from "../score/playheads.js";
import type { RollNote } from "./roll-model.js";

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

/**
 * Whether a press of this pointer type may change the selection on the roll, its bar numbers, its loop strip and its
 * handles (#511 4.1): while the Pen is on the stylus writes on the score and never selects here; a finger and the mouse do.
 */
export function selectsOnRoll(pointerType: string, penOn: boolean): boolean {
    return pointerType !== "pen" || !penOn;
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

const KNOB_RADIUS = 7;
const DOT_RADIUS = 6;
const HOLLOW = 2;
const RING = 2;

/** A playback line: its colour, where its knob sits, how big it is, and whether it is solid or hollow (stopped). */
export interface PositionMark {
    colour: string;
    knobY: number;
    radius: number;
    solid: boolean;
}

/**
 * The two playheads of the playback model over the roll (v0.5.2, #495 UX-10, viewOf): the black one an ink line with a dot on
 * the bar numbers, solid while it plays and hollow when it stands, always; with a selection the blue one a cobalt line with its
 * knob, at the selection's end once playback passed it. Where they meet, the black dot hangs under the blue knob, so both show.
 */
export function drawPlayheads(context: CanvasRenderingContext2D, frame: RollFrame, view: ModelPlaces, inks: PlayheadInks): void {
    const knobs = playheadKnobs(frame, view);
    drawPositionLine(context, frame, knobs.black.x, { colour: inks.ink, knobY: knobs.black.y, radius: DOT_RADIUS, solid: view.playing }, inks);
    if (knobs.blue !== null) {
        drawPositionLine(context, frame, knobs.blue.x, { colour: inks.accent, knobY: knobs.blue.y, radius: KNOB_RADIUS, solid: true }, inks);
    }
}

/** The centre of a playhead's dot or knob on the roll, in pixels. */
export interface Knob {
    x: number;
    y: number;
}

/**
 * Where drawPlayheads puts the black dot and the blue knob, the handles a press takes (#495 UX-08): both on the bar
 * numbers' lower edge, the dot under the knob where the two playheads meet.
 */
export function playheadKnobs(frame: Pick<RollFrame, "xOf" | "header">, view: Pick<ModelPlaces, "black" | "blue">): { black: Knob; blue: Knob | null } {
    const black = frame.xOf(view.black);
    const blue = view.blue === null ? null : frame.xOf(view.blue);
    const together = blue !== null && Math.round(blue) === Math.round(black);
    return {
        black: { x: black, y: together ? frame.header + KNOB_RADIUS + RING + DOT_RADIUS : frame.header },
        blue: blue === null ? null : { x: blue, y: frame.header },
    };
}

/** A playback line down the notes from the bar numbers, with its knob ringed in the surface; hollow when it stands. */
export function drawPositionLine(context: CanvasRenderingContext2D, frame: RollFrame, at: number, mark: PositionMark, inks: PlayheadInks): void {
    if (at < frame.left || at > frame.right) {
        return;
    }
    const x = Math.round(at);
    context.fillStyle = mark.colour;
    context.fillRect(x - 1, frame.header, 2, frame.height - frame.header);
    const disc = (radius: number, colour: string): void => {
        context.beginPath();
        context.arc(x, mark.knobY, radius, 0, 2 * Math.PI);
        context.fillStyle = colour;
        context.fill();
    };
    disc(mark.radius + RING, inks.surface);
    disc(mark.radius, mark.colour);
    if (!mark.solid) {
        disc(mark.radius - HOLLOW, inks.surface);
    }
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
