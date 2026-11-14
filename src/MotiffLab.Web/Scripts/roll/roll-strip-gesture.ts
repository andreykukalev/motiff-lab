// The selection gesture on the roll's strips (#511 sections 2 and 6.3): the bar numbers and, with the lanes, the loop strip,
// in the drawer and in Expand alike, follow the score's rules (score/selection-gesture.ts). A press inside the selection
// that moves moves the nearer edge as far as the pointer goes; a press outside held still takes the selection away, and a
// move after that selects anew from the press, except while the music plays, when a hold leaves the selection (#521 16.4);
// a move before the hold leaves the selection alone: a finger moves the roll, another pointer is told to hold. A tap does
// what it did. Without a selection a press selects at once, as before. The roll draws and sends the range; this decides
// what the press is.

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
    snapEdge,
} from "../score/selection-gesture.js";

/** A bar of the roll by its number, [onset, end) in ticks. */
export interface NumberedBar {
    onset: number;
    end: number;
    number: number;
}

/**
 * The bars a range on the roll selects, where the selection takes whole bars (the iPad's roll by bars), from the ticks the
 * roll sends (`anchor` and `current`, either way round, the later one the last tick in it): a tap selects its bar; a new range
 * every bar it touches, as before; an edge moved (`edit`, #521 3) goes to the nearer barline, so a bar counts once half of it
 * or more is covered (snapEdge, as the score's edges), never fewer than one bar. Bars in time order; undefined for none.
 */
export function rangeBars(bars: readonly NumberedBar[], anchor: number, current: number, click: boolean, edit: boolean): { first: number; last: number } | undefined {
    const barAt = (tick: number): NumberedBar | undefined => bars.find((bar) => tick < bar.end) ?? bars.at(-1);
    const low = Math.min(anchor, current);
    const high = Math.max(anchor, current);
    if (click || !edit) {
        const first = barAt(low);
        const last = click ? first : barAt(Math.max(low, high - 1));
        return first === undefined || last === undefined ? undefined : { first: first.number, last: last.number };
    }
    const lines = bars.length === 0 ? [] : [...bars.map((bar) => bar.onset), bars[bars.length - 1].end];
    const start = snapEdge(lines, "start", low);
    // The later tick is the last one in the range: its edge lies a tick after it.
    const end = snapEdge(lines, "end", high + 1);
    if (start === undefined || end === undefined) {
        return undefined;
    }
    const inside = bars.filter((bar) => bar.onset >= start && bar.end <= end);
    if (inside.length === 0) {
        // The edges met: the bar that holds most of the range stays.
        const middle = barAt((low + high) / 2);
        return middle === undefined ? undefined : { first: middle.number, last: middle.number };
    }
    return { first: inside[0].number, last: inside[inside.length - 1].number };
}

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
    /** Whether a hold outside the selection leaves it: while the music plays (#521 16.4). */
    keepsSelection(): boolean;
}

interface StripPress {
    pointer: number;
    x: number;
    y: number;
    /** Whether a move off the selection before the hold moves the roll: a finger's, and any pointer's in Navigate. */
    finger: boolean;
    gesture: SelectionGesture;
    id: string;
    timer: number;
}

export class RollStripGesture {
    private press: StripPress | null = null;

    constructor(private readonly host: StripHost) {}

    /**
     * A press on a strip, at a tick across the roll. Every pointer selects here at once, as before (#479 item 23); in
     * Navigate under the modes ("longPress", as the score there) a hold begins what a move begins elsewhere, and a move
     * before it moves the roll.
     */
    begin(event: PointerEvent, x: number, y: number, tick: number, rule: "now" | "longPress" = "now"): void {
        this.cancel();
        const selection = this.host.selection();
        const inside = selection !== null && tick >= selection.start && tick < selection.end;
        const finger = event.pointerType === "touch" || rule === "longPress";
        const press: StripPress = { pointer: event.pointerId, x, y, finger, gesture: pressGesture(selection, tick, inside, rule), id: nextGestureId(), timer: 0 };
        press.timer = window.setTimeout(() => this.take(press, heldGesture(press.gesture, this.host.keepsSelection())), HOLD_MS);
        this.press = press;
        if (selection === null && rule === "now") {
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
