// Selection by where you touch on the roll's Select (#601; report E and F): a press is read by its place, by the score's
// one rule (selection/selection-places.ts), over the whole roll, and nothing waits for a hold. Since v0.6.4 (Andrei's spec 3-5
// and 19:55Z: one selection, the same on the score and the roll) the selection is a span of time on the workspace, never a
// matter of the notes under the pointer: from outside a drag draws a dashed rectangle, and the span is what its left and
// right sides cover on the grid (selection/selection-grid.ts: sixteenths, a tuplet one block), notes or none; from inside it
// extends; an edge, taken anywhere down the roll, moves that boundary alone, to the grid line nearest the pointer. All of it
// the same on the notes and on the bar numbers. A tap on a note picks that note, a tap elsewhere outside selects its bar, as
// on the score; a drag of a picked note moves the picked notes as an edit (a draft with Cancel and Save); the lasso (the
// head's «Рамка | Лассо») still picks the notes it rings, and a span when it rings none. A double tap inside lays the span
// across the roll (Fit). The roll draws and sends; this decides what the press is. Nothing here touches the DOM.

import { doubleTap, drawnRange, movedEdge, placeAt, placeChange, placeEnded, placeMoved, placePress, type Place, type PlaceEnd, type PlaceGesture, type Tap } from "../selection/selection-places.js";
import { DRAG_DISTANCE, extendedRange, nextGestureId, type TickRange } from "../selection/selection-gesture.js";
import type { RollNote } from "./roll-model.js";
import { addedPicks, keptSteps, moveSteps, notesInShape, pickedSpan, type PickShape, type RollPoint, type TickSpan } from "./roll-place.js";

/** The selection as the roll shows it: its span, the notes lit in it, and whether it was chosen by notes. */
export interface PlaceSelection extends TickSpan {
    lit: ReadonlySet<number>;
    picked: boolean;
}

/** A pointer on the roll: on screen, and in ticks and pitch (a real number, a note's row centre on its pitch). */
export interface PlacePoint extends RollPoint {
    x: number;
    y: number;
}

/** A move of picked notes in whole steps: sixteenths across, semitones up. */
export interface MoveSteps {
    ticks: number;
    pitches: number;
}

/** How a span of time changes the selection, as the roll's other gestures send it. */
export type SpanChange = "create" | "replace" | "edit";

/** How notes picked change the selection: anew (`replace`, the old one kept for Undo, or `create`), added, or narrowed (`edit`). */
export type PickChange = SpanChange | "add";

/** What the gesture asks of the roll. */
export interface PlaceHost {
    selection(): PlaceSelection | null;
    notes(): readonly RollNote[];
    /** The note drawn under a point, if any. */
    noteAt(point: PlacePoint): number | undefined;
    xOf(tick: number): number;
    /** The bar and beat lines in time order, where spans snap. */
    lines(): readonly number[];
    shape(): PickShape;
    now(): number;
    /** Notes picked: anew, added to those picked, or those left of the picked at an edge. */
    pickNotes(indices: number[], change: PickChange, gesture: string): void;
    /** A span of time without narrowing to notes. */
    pickSpan(span: TickSpan, how: SpanChange, gesture: string): void;
    /** The first step of a move of picked notes: the draft opens. */
    moveBegan(): void;
    /** Picked notes let go moved by whole steps. */
    moveNotes(indices: number[], steps: MoveSteps, gesture: string): void;
    /** A tap that picks no note: on the bar numbers (`strip`) or on empty space, inside the span or outside it. */
    tap(point: PlacePoint, strip: boolean, inside: boolean): void;
    /** The span laid across the roll (a double tap inside). */
    fit(): void;
}

/** What the roll draws while a press goes on. */
export type PlacePreview =
    | { kind: "shape"; shape: PickShape; path: readonly RollPoint[]; picks: readonly number[]; span: TickSpan | null; old: TickSpan | null }
    | { kind: "span"; span: TickSpan; old: TickSpan | null; trace: readonly [RollPoint, RollPoint] | null; edge: "start" | "end" | null }
    | { kind: "move"; indices: readonly number[]; steps: MoveSteps };

interface Press {
    pointer: number;
    from: PlacePoint;
    to: PlacePoint;
    strip: boolean;
    place: Place;
    rule: PlaceGesture;
    gesture: string;
    selection: PlaceSelection | null;
    path: RollPoint[];
    /** The picked notes a drag on one of them moves; null for any other press. */
    moving: number[] | null;
    steps: MoveSteps;
    /** Whether a move of picked notes has gone past a tap, and whether its first step opened the draft. */
    slid: boolean;
    began: boolean;
}

export class RollPlaceGesture {
    private press: Press | null = null;
    private lastTap: Tap | null = null;

    constructor(private readonly host: PlaceHost) {}

    /** Whether a press is going on. */
    get active(): boolean {
        return this.press !== null;
    }

    /** Whether a drag draws a new span or box now, so the roll leaves its own band of the selection to it. */
    get drawing(): boolean {
        const preview = this.preview();
        return preview !== null && preview.kind !== "move";
    }

    /**
     * A press at a point, on the bar numbers (`strip`) or the notes. Every pointer that reaches here selects at once (the
     * score's rule "now"): a finger beside a stylus never does, it moves the roll.
     */
    begin(pointer: number, point: PlacePoint, strip: boolean): void {
        const selection = this.host.selection();
        const span = selection === null ? null : { start: selection.start, end: selection.end };
        const box = span === null ? [] : [{ left: this.host.xOf(span.start), right: this.host.xOf(span.end), top: -Infinity, bottom: Infinity }];
        const place = placeAt(span, point.x, point.y, box);
        const rule = placePress(place, "now");
        this.press = { pointer, from: point, to: point, strip, place, rule, gesture: nextGestureId(), selection, path: [point], moving: this.movingFrom(point, strip, place, selection), steps: { ticks: 0, pitches: 0 }, slid: false, began: false };
    }

    /** A move of the press: true when it was this gesture's. */
    move(pointer: number, point: PlacePoint): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        press.to = point;
        const distance = Math.hypot(point.x - press.from.x, point.y - press.from.y);
        if (press.moving !== null) {
            this.moveSteps(press, distance);
            return true;
        }
        placeMoved(press.rule, distance);
        if (press.rule.phase !== "pressed") {
            press.path.push(point);
        }
        return true;
    }

    /** The press let go: true when it was this gesture's; what it did goes to the host. */
    end(pointer: number): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        this.press = null;
        if (press.moving !== null) {
            this.endMove(press);
            return true;
        }
        const ended = placeEnded(press.rule);
        if (ended === "edge" || ended === "extend" || ended === "draw") {
            this.dragged(press, ended);
        } else {
            this.tapped(press, ended);
        }
        return true;
    }

    /** The press cancelled (a second finger, a change of tool): nothing is sent, and no draft opens. */
    cancel(): void {
        this.press = null;
    }

    /** What to draw for the press now; null with none, or before it is a drag. */
    preview(): PlacePreview | null {
        const press = this.press;
        if (press === null) {
            return null;
        }
        if (press.moving !== null) {
            return press.began ? { kind: "move", indices: press.moving, steps: press.steps } : null;
        }
        const phase = press.rule.phase;
        if (phase !== "edge" && phase !== "extend" && phase !== "draw") {
            return null;
        }
        const old = press.selection === null || phase !== "draw" ? null : { start: press.selection.start, end: press.selection.end };
        if (this.lassoes(press)) {
            const picks = this.shapePicks(press);
            return { kind: "shape", shape: "lasso", path: press.path, picks, span: pickedSpan(this.host.notes(), picks, this.host.lines()) ?? this.spanOf(press), old };
        }
        const span = this.spanOf(press);
        const trace = phase === "draw" && !press.strip ? ([press.from, press.to] as const) : null;
        const edge = phase === "edge" ? (press.place === "edge-start" ? "start" : "end") : null;
        return span === null ? null : { kind: "span", span, old, trace, edge };
    }

    /** The picked notes a press on one of them moves: on the notes, a note of a selection chosen by notes, not at an edge. */
    private movingFrom(point: PlacePoint, strip: boolean, place: Place, selection: PlaceSelection | null): number[] | null {
        if (strip || selection === null || !selection.picked || place === "edge-start" || place === "edge-end") {
            return null;
        }
        const index = this.host.noteAt(point);
        return index !== undefined && selection.lit.has(index) ? [...selection.lit].sort((a, b) => a - b) : null;
    }

    private moveSteps(press: Press, distance: number): void {
        press.slid ||= distance >= DRAG_DISTANCE;
        if (!press.slid) {
            return;
        }
        const wanted = moveSteps(press.to.tick - press.from.tick, press.to.pitch - press.from.pitch);
        press.steps = keptSteps(this.host.notes(), press.moving ?? [], wanted);
        if (!press.began && (press.steps.ticks !== 0 || press.steps.pitches !== 0)) {
            // The draft opens with the first step, so a press that moves nothing leaves none open.
            press.began = true;
            this.host.moveBegan();
        }
    }

    private endMove(press: Press): void {
        if (press.began && (press.steps.ticks !== 0 || press.steps.pitches !== 0)) {
            this.host.moveNotes(press.moving ?? [], press.steps, press.gesture);
        } else if (!press.slid) {
            this.tapped(press, "tap");
        }
    }

    /**
     * A press let go where it was (the score's rule: "tap" outside, "tapInside", "none" on an edge): a note tapped is picked;
     * inside the span a double tap fits it and a single one is the host's tap inside (report F); on an edge nothing;
     * elsewhere the host's tap.
     */
    private tapped(press: Press, ended: PlaceEnd): void {
        const index = press.strip ? undefined : this.host.noteAt(press.from);
        const tap: Tap = { time: this.host.now(), x: press.from.x, y: press.from.y };
        const last = this.lastTap;
        this.lastTap = null;
        const had = press.selection !== null;
        if (index !== undefined && ended !== "none") {
            this.host.pickNotes([index], placeChange("tap", had) === "replace" ? "replace" : "create", press.gesture);
        } else if (ended === "tapInside" && doubleTap(last, tap)) {
            this.host.fit();
        } else if (ended === "tapInside") {
            this.lastTap = tap;
            this.host.tap(press.from, press.strip, true);
        } else if (ended === "tap") {
            this.host.tap(press.from, press.strip, false);
        }
    }

    /**
     * A drag let go: the span it made is the selection, whatever notes it covers (v0.6.4, Andrei's spec 4): a rectangle drawn,
     * an extension, an edge moved (a selection of picked notes becomes the span its edge now gives, so the edge stays where it
     * was let go). Only the lasso picks the notes it rings, and its span when it rings none.
     */
    private dragged(press: Press, ended: "edge" | "extend" | "draw"): void {
        const change = placeChange(ended, press.selection !== null);
        if (this.lassoes(press)) {
            const picks = this.shapePicks(press);
            if (picks.length > 0) {
                this.host.pickNotes(picks, ended === "extend" ? "add" : change === "replace" ? "replace" : "create", press.gesture);
                return;
            }
        }
        const span = this.spanOf(press);
        if (span === null || (press.selection !== null && span.start === press.selection.start && span.end === press.selection.end)) {
            return;
        }
        this.host.pickSpan(span, change === "replace" || change === "create" ? change : "edit", press.gesture);
    }

    /** Whether the press rings notes with the lasso: on the notes, not at an edge, with the head's Lasso chosen. */
    private lassoes(press: Press): boolean {
        return !press.strip && press.rule.phase !== "edge" && this.host.shape() === "lasso";
    }

    /**
     * The notes the box or the lasso takes; from inside the span with the notes of the selection: those picked, or every
     * note lit in a plain span (a box inside a span adds to what it holds).
     */
    private shapePicks(press: Press): number[] {
        const taken = notesInShape(this.host.notes(), this.host.shape(), press.path);
        return press.rule.phase === "extend" && press.selection !== null ? addedPicks(press.selection.lit, taken) : taken;
    }

    /** The span a drag makes now, on the grid's lines (an edge to the nearest, a new one outward); null when there is none. */
    private spanOf(press: Press): TickRange | null {
        const lines = this.host.lines();
        const selection = press.selection;
        const tick = Math.max(0, press.to.tick);
        if (selection !== null && press.rule.phase === "edge") {
            return movedEdge(selection, press.place === "edge-start" ? "start" : "end", press.from.tick, tick, lines);
        }
        if (selection !== null && press.rule.phase === "extend") {
            return extendedRange(selection, tick, lines);
        }
        return drawnRange(press.from.tick, tick, lines) ?? null;
    }
}
