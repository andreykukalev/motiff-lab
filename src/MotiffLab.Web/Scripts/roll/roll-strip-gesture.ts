// The selection gesture on the roll's strips (#511 sections 2 and 6.3): the bar numbers and, with the lanes, the loop strip,
// in the drawer and in Expand alike, follow the score's rules (score/selection-gesture.ts). A press inside the selection
// that moves moves the nearer edge as far as the pointer goes; a press outside held still takes the selection away, and a
// move after that selects anew from the press; a move before the hold leaves the selection alone: a finger moves the roll,
// another pointer is told to hold. A tap does what it did. Without a selection a press selects at once, as before. The roll
// draws and sends the range; this decides what the press is.

import {
    HOLD_MS,
    type GestureStep,
    type SelectionChange,
    type SelectionGesture,
    type TickRange,
    gestureChange,
    heldGesture,
    movedGesture,
    nextGestureId,
    pressGesture,
} from "../score/selection-gesture.js";

/** What the gesture asks of the roll. */
export interface StripHost {
    /** The selection now, in ticks; null for none. */
    selection(): TickRange | null;
    /** A new range from the press: the roll drags it on and sends it, as this gesture's change. */
    beginRange(pointer: number, x: number, tick: number, how: SelectionChange, gesture: string): void;
    /** An edge moved from inside: the other edge stays at `anchor`; the moving one is `offset` ticks from the pointer. */
    beginEdge(pointer: number, anchor: number, offset: number, gesture: string): void;
    /** A finger moved off the selection before the hold: it moves the roll. */
    beginPan(pointer: number, x: number, y: number): void;
    /** The hold took the selection away, as a step of this gesture. */
    clear(gesture: string): void;
    /** The gesture was cancelled after its hold took the selection away: the selection comes back. */
    cancelled(gesture: string): void;
    /** A pointer that cannot move the roll moved off the selection before the hold. */
    hint(): void;
    /** A press let go where it was, before the hold, at a tick and a height, inside the selection or not. */
    tap(tick: number, y: number, inside: boolean): void;
}

interface StripPress {
    pointer: number;
    x: number;
    y: number;
    finger: boolean;
    gesture: SelectionGesture;
    id: string;
    timer: number;
}

export class RollStripGesture {
    private press: StripPress | null = null;

    constructor(private readonly host: StripHost) {}

    /** A press on a strip, at a tick across the roll; every pointer selects here at once, as before (#479 item 23). */
    begin(event: PointerEvent, x: number, y: number, tick: number): void {
        this.cancel();
        const selection = this.host.selection();
        const inside = selection !== null && tick >= selection.start && tick < selection.end;
        const press: StripPress = { pointer: event.pointerId, x, y, finger: event.pointerType === "touch", gesture: pressGesture(selection, tick, inside, "now"), id: nextGestureId(), timer: 0 };
        press.timer = window.setTimeout(() => this.take(press, heldGesture(press.gesture)), HOLD_MS);
        this.press = press;
        if (selection === null) {
            // Without a selection a press selects at once, a tap selecting its bar, as before.
            press.gesture.phase = "creating";
            this.host.beginRange(press.pointer, x, tick, "create", press.id);
        }
    }

    /** A move of the press: true while it is this gesture's and still deciding, so nothing else takes it. */
    move(pointer: number, x: number, y: number): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        const phase = press.gesture.phase;
        if (phase === "waiting" || phase === "cleared") {
            this.take(press, movedGesture(press.gesture, Math.hypot(x - press.x, y - press.y)));
        }
        return this.press === press && (press.gesture.phase === "waiting" || press.gesture.phase === "cleared");
    }

    /** The press let go: true when it was this gesture's; a tap before the hold acts as a tap. */
    end(pointer: number): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        this.finish();
        if (press.gesture.phase === "waiting") {
            this.host.tap(press.gesture.pressTick, press.y, press.gesture.inside);
        }
        // A range or an edge the press began is ended and sent by the roll; a clear was sent at the hold.
        return gestureChange(press.gesture) === "none" || press.gesture.phase === "cleared";
    }

    /** The press was cancelled (a second finger, the system): a selection the hold took away comes back. */
    cancel(): void {
        const press = this.press;
        if (press === null) {
            return;
        }
        this.finish();
        if (press.gesture.cleared) {
            this.host.cancelled(press.id);
        }
    }

    private finish(): void {
        if (this.press !== null) {
            clearTimeout(this.press.timer);
        }
        this.press = null;
    }

    private take(press: StripPress, step: GestureStep): void {
        if (this.press !== press) {
            return;
        }
        const gesture = press.gesture;
        switch (step) {
            case "create":
                this.host.beginRange(press.pointer, press.x, gesture.pressTick, gesture.cleared ? "replace" : "create", press.id);
                return;
            case "edit": {
                const selection = gesture.selection;
                if (selection !== null) {
                    const moving = gesture.edge === "start" ? selection.start : selection.end;
                    this.host.beginEdge(press.pointer, gesture.edge === "start" ? selection.end - 1 : selection.start, moving - gesture.pressTick, press.id);
                }
                return;
            }
            case "clear":
                this.host.clear(press.id);
                return;
            case "pass":
            case "hint":
                this.finish();
                if (press.finger) {
                    this.host.beginPan(press.pointer, press.x, press.y);
                } else if (step === "hint") {
                    this.host.hint();
                }
                return;
            default:
        }
    }
}
