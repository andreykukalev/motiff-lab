// Who selects on the score and on the roll, and how (#511 sections 2, 4.1 and 12.3): how a press of each pointer starts a
// selection, and whether it takes a selection's handle. The Pen owns the stylus: while it is on, the stylus (and on the web
// the mouse) writes everywhere, over the handles too, and a finger keeps its own moves. Pure; nothing here touches the DOM.

/** How a press may start a selection: "now" drags at once, "longPress" after a long press, "none" never. */
export type DragStart = "now" | "longPress" | "none";

/**
 * How a press starts a selection: the host's rule for its pointer where it gives one, else the rules before it: the pen
 * writes and the mouse and a finger write while the pen is on; a finger waits for the long press unless it drags at once.
 */
export function dragStartOf(pointerType: string, rule: DragStart | undefined, penOn: boolean, fingerDrags: boolean): DragStart {
    if (rule !== undefined) {
        return rule;
    }
    if (pointerType === "pen" || penOn) {
        return "none";
    }
    return pointerType === "touch" && !fingerDrags ? "longPress" : "now";
}

/**
 * Whether a press on a selection handle takes it (#511 4.1): the host's rule where it gives one, else a finger always and
 * the pen and the mouse unless the Pen is on, where they write over the handles as everywhere.
 */
export function handleTakes(pointerType: string, rule: boolean | undefined, penOn: boolean): boolean {
    return rule ?? (pointerType === "touch" || !penOn);
}

/**
 * Whether the selection's handles show and take a press (v0.5.7, #543 4.2, the lead's decision 3): always before the modes
 * (no mode), and under them only in Select; in Navigate, Edit notes and the Pen the band stays without them.
 */
export function handlesShown(mode: string | null | undefined): boolean {
    return mode === null || mode === undefined || mode === "select";
}

/** A pointer as a gesture keeps it, and as a new press names it. */
export interface PointerKind {
    pointerType: string;
}

/**
 * Whether a press means that the gesture under way lost its end (#543 9): a primary pointer of the same type as the one
 * holding the gesture comes down only once that one has lifted (one pen, one mouse, a first finger with none down), so the
 * gesture is stale, its pointerup or pointercancel lost, and it ends as cancelled instead of blocking every press after it.
 */
export function staleGesture(held: PointerKind, press: PointerKind & { isPrimary: boolean }): boolean {
    return press.isPrimary && press.pointerType === held.pointerType;
}

/** How long a press rests outside the selection before the selection goes, and before a finger selects, in milliseconds. */
export const HOLD_MS = 450;

/** How far the mouse or the stylus moves before a press is a drag, in pixels. */
export const DRAG_DISTANCE = 6;

/** How far a pointer that waits for the hold may move and still be held, in pixels; further, it scrolls. */
export const HOLD_SLOP = 8;

/** A stretch of time in ticks, [start, end). */
export interface TickRange {
    start: number;
    end: number;
}

/**
 * What a press means for the selection (#511 2.1): the place it starts decides. Inside the selection a move edits the edge
 * nearer the press; outside it the press waits, and held still for HOLD_MS it clears the selection, after which a move
 * makes a new one from the press; a move before that leaves the selection alone. Without a selection a press creates one,
 * at once ("now") or after the hold ("longPress"), as before.
 */
export type GesturePhase = "waiting" | "editing" | "cleared" | "creating" | "passed";

export interface SelectionGesture {
    /** How this pointer starts a selection. */
    readonly rule: "now" | "longPress";
    /** The selection when the press began; null for none. */
    readonly selection: TickRange | null;
    /** Where the press began, in ticks, and whether that lies inside the selection. */
    readonly pressTick: number;
    readonly inside: boolean;
    /** The edge an edit moves: the one nearer the press. */
    readonly edge: "start" | "end";
    phase: GesturePhase;
    /** Whether the hold cleared the selection: a selection made after it replaces the one before. */
    cleared: boolean;
}

/**
 * What a step of the gesture asks of the surface: begin a new selection from the press ("create"), begin moving an edge
 * ("edit"), take the selection away now ("clear"), let the pointer go to what it does without a selection ("pass", a
 * finger scrolls), say how to begin a new selection ("hint", a pointer that cannot scroll), or nothing.
 */
export type GestureStep = "create" | "edit" | "clear" | "pass" | "hint" | "none";

/** How the selection changed when the gesture ended, for the owner's history and its toast. */
export type SelectionChange = "create" | "edit" | "replace" | "clear" | "none";

/** The edge of the selection nearer a tick: the start for a press in its first half, else the end. */
export function nearerEdge(selection: TickRange, tick: number): "start" | "end" {
    return tick - selection.start <= selection.end - tick ? "start" : "end";
}

/** A press of a pointer that selects, at a tick inside the selection or not. */
export function pressGesture(selection: TickRange | null, pressTick: number, inside: boolean, rule: "now" | "longPress"): SelectionGesture {
    const within = selection !== null && inside;
    return { rule, selection, pressTick, inside: within, edge: selection === null ? "end" : nearerEdge(selection, pressTick), phase: "waiting", cleared: false };
}

/**
 * The press has been held still for HOLD_MS. With `keep` (the roll while the music plays, #521 16.4) a hold outside the
 * selection leaves it: the pointer goes to what it does without a selection ("pass", a finger moves the roll), and letting
 * go does nothing.
 */
export function heldGesture(gesture: SelectionGesture, keep = false): GestureStep {
    if (gesture.phase !== "waiting") {
        return "none";
    }
    if (keep && gesture.selection !== null && !gesture.inside) {
        gesture.phase = "passed";
        return "pass";
    }
    if (gesture.selection === null) {
        if (gesture.rule === "longPress") {
            gesture.phase = "creating";
            return "create";
        }
        return "none";
    }
    if (gesture.inside) {
        if (gesture.rule === "longPress") {
            gesture.phase = "editing";
            return "edit";
        }
        return "none";
    }
    gesture.phase = "cleared";
    gesture.cleared = true;
    return "clear";
}

/** The pointer has moved `distance` pixels from where it was pressed. */
export function movedGesture(gesture: SelectionGesture, distance: number): GestureStep {
    const reach = gesture.rule === "now" ? DRAG_DISTANCE : HOLD_SLOP;
    if (distance < reach || (gesture.phase !== "waiting" && gesture.phase !== "cleared")) {
        return "none";
    }
    if (gesture.phase === "cleared" || (gesture.selection === null && gesture.rule === "now")) {
        gesture.phase = "creating";
        return "create";
    }
    if (gesture.inside && gesture.rule === "now") {
        gesture.phase = "editing";
        return "edit";
    }
    gesture.phase = "passed";
    return gesture.rule === "now" && gesture.selection !== null ? "hint" : "pass";
}

/** How the selection changed, as the gesture ends now. */
export function gestureChange(gesture: SelectionGesture): SelectionChange {
    switch (gesture.phase) {
        case "editing":
            return "edit";
        case "creating":
            return gesture.cleared ? "replace" : "create";
        case "cleared":
            return "clear";
        default:
            return "none";
    }
}

/**
 * The line an edge of the selection goes to from where the pointer puts it (#521 3): the nearest of `lines` (in time order),
 * so a bar, or a beat, counts once half of it or more is covered and not under half. At exactly half it counts: the end goes
 * on to the next line and the start back to the one before. Undefined without lines.
 */
export function snapEdge(lines: readonly number[], edge: "start" | "end", target: number): number | undefined {
    let best: number | undefined;
    for (const line of lines) {
        const away = Math.abs(line - target);
        const bestAway = best === undefined ? Infinity : Math.abs(best - target);
        const onward = best === undefined || (edge === "end" ? line > best : line < best);
        if (away < bestAway || (away === bestAway && onward)) {
            best = line;
        }
    }
    return best;
}

/**
 * The selection with its `edge` moved by the pointer from `pressTick` to `tick` (no jump: the edge moves as far as the
 * pointer), to the line it snaps to among `lines` (the barlines, or the beat lines while selecting by beats, in time order;
 * snapEdge), never onto or past the other edge.
 */
export function editedRange(selection: TickRange, edge: "start" | "end", pressTick: number, tick: number, lines: readonly number[]): TickRange {
    const target = selection[edge] + tick - pressTick;
    const allowed = lines.filter((line) => (edge === "start" ? line < selection.end : line > selection.start));
    const nearest = snapEdge(allowed, edge, target);
    if (nearest === undefined) {
        return selection;
    }
    return edge === "start" ? { start: nearest, end: selection.end } : { start: selection.start, end: nearest };
}

/** A name for a gesture, unique across the web views of one app (the score and the roll on the iPad are two). */
const REALM = Math.random().toString(36).slice(2, 8);
let gestures = 0;

/** A new gesture's name: every change of the selection one gesture makes carries it, so the owner keeps them as one step. */
export function nextGestureId(): string {
    gestures += 1;
    return `${REALM}-${gestures}`;
}
