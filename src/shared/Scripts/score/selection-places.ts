// Selection by the place of the touch (v0.6.2, #601, the selection study's sections C and F; once the experiment
// `experimental.placeSelection`; only the iPad uses it): what a press does is decided once, by where
// it lands, never by how long it is held. From an edge (within EDGE_REACH of it, over the whole height of the band) it
// moves that edge; from inside it extends the selection, which never shrinks (v0.6.1 item 9); from outside, on the music,
// it draws a new selection, the old one shown as a ghost until it is let go, and so it does from the page's margin (v0.6.3);
// a tap outside selects what is under it (a bar, a note), a tap on the page's margin takes the selection away, a double
// tap inside zooms to it. A finger beside the
// stylus scrolls; held still on a bar it selects that bar (the way back for a finger), and moved after the hold it draws
// from where it pressed, as the stylus does (Andrei, 2026-11-17). One rule for the score and the
// roll (Scripts/roll imports it). Pure; nothing here touches the DOM.

import { DRAG_DISTANCE, HOLD_SLOP, type SelectionChange, type TickRange, editedRange } from "./selection-gesture.js";

/** Where a press lands: on an edge of the selection, inside it, on the music outside it, or on the page's margin. */
export type Place = "edge-start" | "edge-end" | "inside" | "outside" | "margin";

/** How far from an edge a press still takes it, across, in pixels (points on the iPad); an edge wins over inside. */
export const EDGE_REACH = 20;

/** How soon a second tap makes a double tap, in milliseconds, and how near the first it lands, in pixels. */
export const DOUBLE_TAP_MS = 320;
export const DOUBLE_TAP_REACH = 24;

/** A box of the selection on screen (a run of its band, one per system), in the press's pixels. */
export interface PlaceBox {
    left: number;
    right: number;
    top: number;
    bottom: number;
}

/** How far an edge of a run reaches inward: the reach, but no more than a quarter of the run, so its inside stays. */
function inward(box: PlaceBox, reach: number): number {
    return Math.min(reach, Math.max(0, box.right - box.left) / 4);
}

/**
 * How far a press at (x, y) is from an edge line at `edgeX` of `box` whose inside lies `towardInside` (1 right, -1 left),
 * or Infinity when it is out of the edge's reach: `reach` outward and across the box's height with `reach` above and below
 * (the circles sit at the top and the bottom), less inward (`inward`).
 */
function edgeDistance(box: PlaceBox, edgeX: number, towardInside: 1 | -1, x: number, y: number, reach: number): number {
    if (y < box.top - reach || y > box.bottom + reach) {
        return Infinity;
    }
    const into = (x - edgeX) * towardInside;
    return into <= inward(box, reach) && -into <= reach ? Math.abs(x - edgeX) : Infinity;
}

/**
 * The place of a press at (x, y) (section F): the selection's `boxes` in time order (its start is the first box's left,
 * its end the last box's right); `onMusic` false for a press off the systems, on the page's margin. An edge wins over
 * inside within its reach, the nearer of two; a run narrower than two reaches gives its edges room outward. Without a
 * selection every press is outside, or on the margin.
 */
export function placeAt(span: TickRange | null, x: number, y: number, boxes: readonly PlaceBox[], onMusic = true, reach = EDGE_REACH): Place {
    if (span !== null && boxes.length > 0) {
        const first = boxes[0];
        const last = boxes[boxes.length - 1];
        const toStart = edgeDistance(first, first.left, 1, x, y, reach);
        const toEnd = edgeDistance(last, last.right, -1, x, y, reach);
        if (toStart !== Infinity || toEnd !== Infinity) {
            return toStart <= toEnd ? "edge-start" : "edge-end";
        }
        if (boxes.some((box) => x > box.left && x < box.right && y > box.top && y < box.bottom)) {
            return "inside";
        }
    }
    return onMusic ? "outside" : "margin";
}

/** A handle of the selection's edges (SEL-04): its centre, in the press's pixels. */
export interface EdgeHandle {
    x: number;
    y: number;
}

/** The handles of a selection drawn as `boxes` in time order: at the middle of its start's edge and of its end's. */
export function edgeHandles(boxes: readonly PlaceBox[]): EdgeHandle[] {
    if (boxes.length === 0) {
        return [];
    }
    const first = boxes[0];
    const last = boxes[boxes.length - 1];
    return [{ x: first.left, y: (first.top + first.bottom) / 2 }, { x: last.right, y: (last.top + last.bottom) / 2 }];
}

/**
 * Whether a press at (x, y) lands on a handle of the selection's edges, within `reach` of its centre. There the edge is
 * taken; elsewhere on an edge a playhead's handle is taken first, so a playhead standing at the selection's start can be
 * grabbed there (Andrei, 2026-11-18, answer 3 on finding 1 of the 0.8 test).
 */
export function onEdgeHandle(handles: readonly EdgeHandle[], x: number, y: number, reach = EDGE_REACH): boolean {
    return handles.some((handle) => Math.hypot(x - handle.x, y - handle.y) <= reach);
}

/** How a pointer selects: at once (the stylus, the mouse, one finger without a stylus), or a finger that scrolls and holds. */
export type PlaceRule = "now" | "longPress";

/** A press under the place rule: where it landed, how its pointer selects, and what it has become. */
export interface PlaceGesture {
    readonly place: Place;
    readonly rule: PlaceRule;
    /** "pressed" until it moves or is held; then what it does: move an edge, extend, draw anew, select the bar held. */
    phase: "pressed" | "edge" | "extend" | "draw" | "bar" | "passed";
}

/** What a step of the gesture asks of the surface: move an edge, extend, draw a new one, select the bar held, let it go to scrolling, or nothing. */
export type PlaceStep = "edge" | "extend" | "draw" | "bar" | "pass" | "none";

export function placePress(place: Place, rule: PlaceRule): PlaceGesture {
    return { place, rule, phase: "pressed" };
}

/** What a moving or held press on a place does: the edge moves, the inside extends, the outside draws. */
function placeStep(place: Place, held: boolean): "edge" | "extend" | "draw" | "bar" | "none" {
    switch (place) {
        case "edge-start":
        case "edge-end":
            return "edge";
        case "inside":
            return "extend";
        case "outside":
            return held ? "bar" : "draw";
        case "margin":
            // A stroke from the page's margin draws too (Andrei, v0.6.3: «Да, рисует»), between the systems as beside them.
            return held ? "none" : "draw";
        default:
            return "none";
    }
}

/**
 * The pointer moved `distance` pixels from the press. Past DRAG_DISTANCE a pointer that selects at once does what its
 * place says (from the margin it draws, as from outside); a finger that holds scrolls once it moves past HOLD_SLOP before
 * the hold, and held on a bar, past HOLD_SLOP after it draws from where it pressed, as the stylus does (Andrei, 2026-11-17).
 */
export function placeMoved(gesture: PlaceGesture, distance: number): PlaceStep {
    if (distance < (gesture.rule === "now" ? DRAG_DISTANCE : HOLD_SLOP)) {
        return "none";
    }
    if (gesture.phase === "bar") {
        gesture.phase = "draw";
        return "draw";
    }
    if (gesture.phase !== "pressed") {
        return "none";
    }
    const step = gesture.rule === "now" ? placeStep(gesture.place, false) : "none";
    gesture.phase = step === "none" ? "passed" : step;
    return step === "none" ? "pass" : step;
}

/**
 * The press was held still for the long press: only a finger beside the stylus waits for it (the stylus never does). On a
 * bar outside it selects that bar, a move then draws from the press (placeMoved); on an edge or inside it moves the edge
 * or extends, as the stylus does at once.
 */
export function placeHeld(gesture: PlaceGesture): PlaceStep {
    if (gesture.phase !== "pressed" || gesture.rule !== "longPress") {
        return "none";
    }
    const step = placeStep(gesture.place, true);
    if (step !== "none") {
        gesture.phase = step;
    }
    return step;
}

/** What a gesture ending now did: moved an edge, extended, drew or held a bar; a tap outside, inside or on the margin; nothing. */
export type PlaceEnd = "edge" | "extend" | "draw" | "bar" | "tap" | "tapInside" | "clear" | "none";

/**
 * The gesture ends (section F, Отпускание): a gesture that moved or held ends as what it did. A tap by a pointer that
 * selects at once selects what is under it outside, takes the selection away on the margin, and inside does nothing but
 * count for a double tap; a tap on an edge does nothing. A finger's tap is the page's own (it never selects here).
 */
export function placeEnded(gesture: PlaceGesture): PlaceEnd {
    switch (gesture.phase) {
        case "edge":
        case "extend":
        case "draw":
        case "bar":
            return gesture.phase;
        case "pressed":
            if (gesture.rule !== "now") {
                return "none";
            }
            return gesture.place === "outside" ? "tap" : gesture.place === "margin" ? "clear" : gesture.place === "inside" ? "tapInside" : "none";
        default:
            return "none";
    }
}

/** How the selection changed by the end of a gesture, for the owner's history and its toast; `had` whether there was one. */
export function placeChange(end: PlaceEnd, had: boolean): SelectionChange {
    switch (end) {
        case "edge":
        case "extend":
            return "edit";
        case "draw":
        case "bar":
        case "tap":
            return had ? "replace" : "create";
        case "clear":
            return had ? "clear" : "none";
        default:
            return "none";
    }
}

/** A tap: when and where it was. */
export interface Tap {
    time: number;
    x: number;
    y: number;
}

/** Whether `now` makes a double tap with the tap before it. */
export function doubleTap(last: Tap | null, now: Tap): boolean {
    return last !== null && now.time - last.time <= DOUBLE_TAP_MS && Math.hypot(now.x - last.x, now.y - last.y) <= DOUBLE_TAP_REACH;
}

/**
 * The passage drawn from outside (section D, Создание): from the line at or before the earlier of the press and the
 * pointer to the line at or after the later one, among `lines` (beats or barlines, in time order), never shorter than
 * one step of them. Undefined without lines around it.
 */
export function drawnRange(from: number, to: number, lines: readonly number[]): TickRange | undefined {
    const low = Math.min(from, to);
    const high = Math.max(from, to);
    const start = [...lines].reverse().find((line) => line <= low);
    if (start === undefined) {
        return undefined;
    }
    const end = lines.find((line) => line >= high && line > start);
    return end === undefined ? undefined : { start, end };
}

/**
 * An edge moved by its grip (section D, Кромки): as far as the pointer from where it was pressed, to the line it snaps to,
 * never onto or past the other edge (editedRange, the edit of before).
 */
export const movedEdge = editedRange;

/** The lines an edge may stop at while it moves (section D, бледные деления-цели): every line outside the passage. */
export function targetLines(selection: TickRange, lines: readonly number[]): number[] {
    return lines.filter((line) => line <= selection.start || line >= selection.end);
}

/** A staff of a system on screen, top to bottom. */
export interface StaffBand {
    top: number;
    bottom: number;
}

/** The share of the gap between two staves that still counts as the nearer staff. */
const GAP_SHARE = 0.3;

/** The staff a point is on: its index, the first above the system and the last below it; -1 in the gap between two. */
export function staffAt(staves: readonly StaffBand[], y: number): number {
    for (let i = 0; i < staves.length; i++) {
        const above = i === 0 ? -Infinity : staves[i].top - (staves[i].top - staves[i - 1].bottom) * GAP_SHARE;
        const below = i === staves.length - 1 ? Infinity : staves[i].bottom + (staves[i + 1].top - staves[i].bottom) * GAP_SHARE;
        if (y >= above && y <= below) {
            return i;
        }
    }
    return -1;
}

/** The hand a stroke narrows the selection to (section D, Нотоносцы). */
export type Hand = "right" | "left";

/**
 * The hand of a stroke from `y0` to `y1` on one system of two staves: within the upper staff the right hand, within the
 * lower the left; across the gap, onto the other staff, on another system or a system that is not two staves, both (null).
 */
export function strokeHand(staves: readonly StaffBand[], y0: number, y1: number, sameSystem: boolean): Hand | null {
    if (!sameSystem || staves.length !== 2) {
        return null;
    }
    const from = staffAt(staves, y0);
    const to = staffAt(staves, y1);
    if (from !== to || from === -1) {
        return null;
    }
    return from === 0 ? "right" : "left";
}

/** The note head under a point within `reach` pixels (section D: ±7 pt), the nearest; undefined for none. */
export interface NoteHead {
    id: string;
    x: number;
    y: number;
}

export const NOTE_REACH = 7;

export function headAt(heads: readonly NoteHead[], x: number, y: number, reach = NOTE_REACH): NoteHead | undefined {
    let best: NoteHead | undefined;
    let distance = Infinity;
    for (const head of heads) {
        const away = Math.max(Math.abs(head.x - x), Math.abs(head.y - y));
        if (away <= reach && away < distance) {
            best = head;
            distance = away;
        }
    }
    return best;
}

/** How near the top or the bottom of the view a gesture scrolls the page, and how far at most in a frame, in pixels. */
export const SCROLL_ZONE = 48;
export const SCROLL_STEP = 18;

/**
 * How far a gesture at `y` (in the window) scrolls the page this frame (section F, Край экрана): up within SCROLL_ZONE
 * of the view's `top`, down within it of its `bottom`, faster nearer the edge; 0 elsewhere. With `from` (where the gesture
 * was pressed) only toward the edge it has moved to, so a stroke begun near an edge and drawn away from it never scrolls.
 */
export function scrollStep(y: number, top: number, bottom: number, zone = SCROLL_ZONE, most = SCROLL_STEP, from?: number): number {
    if (y < top + zone && (from === undefined || y < from)) {
        return -Math.ceil(most * Math.min(1, (top + zone - y) / zone));
    }
    if (y > bottom - zone && (from === undefined || y > from)) {
        return Math.ceil(most * Math.min(1, (y - (bottom - zone)) / zone));
    }
    return 0;
}
