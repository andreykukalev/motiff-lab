// A press on the score by the selection gesture of version 0.6.1 (#511 2, #582 8 and 9, selection-gesture.ts): a drag
// from inside extends the selection on the side it goes, never shrinking it; a hold on a bar selects that bar, in place
// of the selection there was, and a move after it takes it on; with no selection a drag with the mouse or the stylus
// selects at once. It runs only while the place rule is off (selection-place-layer.ts takes the presses while it is
// on). The layer (selection-layer.ts) draws what the press would select (`preview`); this takes the pointer and tells
// the owner what the press chose, cleared or cancelled.

import type { ScoreBar, ScoreGeometry } from "./score-geometry.js";
import { type Beat, type ScoreBeats, keyLines, spanOf } from "./selection-beats.js";
import { HANDLE_CLASS, type SelectionHandles, barlines, tickAt } from "./selection-handles.js";
import { inside } from "./score-modes.js";
import { bandBoxes, ordered } from "./selection-boxes.js";
import {
    HOLD_MS,
    type SelectionChange,
    type SelectionGesture,
    type TickRange,
    dragStartOf,
    extendedRange,
    gestureChange,
    heldGesture,
    movedGesture,
    nextGestureId,
    pressGesture,
    staleGesture,
} from "../selection/selection-gesture.js";
import type { ScoreSelection, SelectionHost } from "./selection-layer.js";

/** What the press needs from the layer. */
export interface PressSurface {
    readonly owner: SelectionHost;
    readonly beats: ScoreBeats;
    /** The handles' drag, which a press waits for, and which a lost end abandons. */
    readonly handles: SelectionHandles;
    selection(): ScoreSelection | null;
    point(event: PointerEvent | MouseEvent): { x: number; y: number };
    /** Whether a gesture of the layer holds the pointer now: this press, a handle or the place rule's. */
    capturing(): boolean;
    /** Draws the band again, with what the press would select. */
    place(): void;
}

interface Drag {
    pointerId: number;
    pointerType: string;
    /** Where the press started, in content pixels. */
    x: number;
    y: number;
    /** What the press means for the selection (selection-gesture.ts), and its name for the owner. */
    gesture: SelectionGesture;
    id: string;
    /** While an edge moves from inside: the selection as it would be now. */
    edited: TickRange | null;
    /** Set once the press creates: the bar it started on, and the one under the pointer now. */
    anchor: ScoreBar | null;
    current: ScoreBar | null;
    /** Selecting by beats: the beat the drag started on, and the one under the pointer now. */
    anchorBeat: Beat | null;
    currentBeat: Beat | null;
    timer: number;
}

export class SelectionPress {
    private drag: Drag | null = null;
    /** The click the browser sends after a drag is not a click on the score. */
    private swallowNextClick = false;

    /** Listens to the presses on `host` from now on, until `dispose`. */
    constructor(
        private readonly host: HTMLElement,
        private readonly surface: PressSurface,
    ) {
        host.addEventListener("pointerdown", this.onPointerDown);
        host.addEventListener("pointermove", this.onPointerMove);
        host.addEventListener("pointerup", this.onPointerUp);
        host.addEventListener("pointercancel", this.onPointerCancel);
        // Not passive: once a long press has started a drag, the finger moves the selection, not the page.
        host.addEventListener("touchmove", this.onTouchMove, { passive: false });
        host.addEventListener("contextmenu", this.onContextMenu);
    }

    /** Stops listening, and stops the hold's timer of a press under way. */
    dispose(): void {
        this.host.removeEventListener("pointerdown", this.onPointerDown);
        this.host.removeEventListener("pointermove", this.onPointerMove);
        this.host.removeEventListener("pointerup", this.onPointerUp);
        this.host.removeEventListener("pointercancel", this.onPointerCancel);
        this.host.removeEventListener("touchmove", this.onTouchMove);
        this.host.removeEventListener("contextmenu", this.onContextMenu);
        if (this.drag !== null) {
            clearTimeout(this.drag.timer);
        }
    }

    /** What the press would select now: an edge's passage, or a new one; null for none. */
    preview(): TickRange | null {
        const drag = this.drag;
        if (drag === null) {
            return null;
        }
        if (drag.edited !== null) {
            return drag.edited;
        }
        if (drag.anchorBeat !== null && drag.currentBeat !== null) {
            return spanOf(drag.anchorBeat, drag.currentBeat);
        }
        if (drag.anchor !== null && drag.current !== null) {
            const [first, last] = ordered(drag.anchor, drag.current);
            return { start: first.tick, end: last.end };
        }
        return null;
    }

    /** Whether the press holds the pointer: it moves an edge, makes a new selection or has cleared it. */
    holds(): boolean {
        const phase = this.drag?.gesture.phase;
        return phase === "editing" || phase === "extending" || phase === "creating" || phase === "cleared";
    }

    /** Whether a hold took the selection away, until the press ends. */
    cleared(): boolean {
        return this.drag?.gesture.phase === "cleared";
    }

    /** Whether the click the browser sends now ends a drag, and is not a click on the score. */
    swallowsClick(): boolean {
        const swallow = this.swallowNextClick;
        this.swallowNextClick = false;
        return swallow;
    }

    private readonly onPointerDown = (event: PointerEvent): void => {
        const owner = this.surface.owner;
        const handles = this.surface.handles;
        // A gesture whose end was lost (#543 9) ends as cancelled before the new press, so it never blocks it.
        if (this.drag !== null && staleGesture(this.drag, event)) {
            this.cancelDrag(this.drag);
        }
        if (handles.drag !== null && staleGesture(handles.drag, event)) {
            handles.abandon();
        }
        if (this.drag !== null || handles.drag !== null || owner.places?.() === true) {
            return;
        }
        // A finger waits for the long press, unless it drags at once (the iPad in Select): then it drags as a mouse does.
        const rule = owner.dragStart?.(event.pointerType);
        const start = dragStartOf(event.pointerType, rule, owner.penOn(), owner.fingerDrags?.() === true);
        if (start === "none" || (start === "now" && event.button !== 0)) {
            return;
        }
        // A phrase's mark and a handle are their own targets.
        if (event.target instanceof Element && event.target.closest(`.phrase-mark, .${HANDLE_CLASS}`) !== null) {
            return;
        }
        const geometry = owner.geometry();
        const at = this.surface.point(event);
        if (geometry === null || !this.mayStart(rule !== undefined)) {
            return;
        }
        const chosen = this.surface.selection();
        const selection = chosen === null ? null : { start: chosen.start, end: chosen.end };
        // The score's rules (#582 items 8 and 9): a hold selects the bar under it, a move from inside extends the selection.
        const gesture = pressGesture(selection, tickAt(geometry, at.x, at.y), this.insideBand(at), start, true);
        const drag: Drag = { pointerId: event.pointerId, pointerType: event.pointerType, x: at.x, y: at.y, gesture, id: nextGestureId(), edited: null, anchor: null, current: null, anchorBeat: null, currentBeat: null, timer: 0 };
        drag.timer = window.setTimeout(() => this.take(drag, heldGesture(drag.gesture)), HOLD_MS);
        this.drag = drag;
    };

    /** Under the rules of version 0.5 only Select selects with a press; where the host rules the pointer (`ruled`, the iPad), its rule says. */
    private mayStart(ruled: boolean): boolean {
        const mode = this.surface.owner.mode?.() ?? null;
        return mode === null || ruled || mode === "select";
    }

    /** Whether a point lies on the band, in the score's content pixels. */
    private insideBand(at: { x: number; y: number }): boolean {
        return inside(bandBoxes(this.host), at.x, at.y);
    }

    /** Does what a step of the gesture asks (selection-gesture.ts). */
    private take(drag: Drag, step: ReturnType<typeof heldGesture>): void {
        if (this.drag !== drag) {
            return;
        }
        switch (step) {
            case "create":
                this.startDrag(drag, false);
                return;
            case "bar":
                this.startDrag(drag, true);
                return;
            case "edit":
            case "extend":
                this.capture(drag);
                drag.edited = drag.gesture.selection;
                this.surface.place();
                return;
            case "clear":
                // The band goes at once, so the player sees the hold took it; the owner takes the selection away.
                this.capture(drag);
                this.surface.place();
                this.surface.owner.cleared?.(drag.id);
                return;
            case "pass":
            case "hint":
                clearTimeout(drag.timer);
                this.drag = null;
                if (step === "hint") {
                    this.surface.owner.hint?.();
                }
                return;
            default:
        }
    }

    private capture(drag: Drag): void {
        try {
            this.host.setPointerCapture(drag.pointerId);
        } catch {
            // The pointer is gone already; the drag ends with it.
        }
    }

    /** A new selection from the press: from the beat under it, or with `wholeBar` (a hold, #582 8) from its whole bar. */
    private startDrag(drag: Drag, wholeBar: boolean): void {
        const geometry = this.surface.owner.geometry();
        const bar = geometry?.barNear(drag.x, drag.y);
        if (this.drag !== drag || geometry == null || bar === undefined) {
            return;
        }
        drag.anchor = bar;
        drag.current = bar;
        const beats = this.surface.beats;
        const lines = beats.lines();
        if (lines !== null) {
            const barSpan = Number.isFinite(bar.end) ? { start: bar.tick, end: bar.end } : undefined;
            drag.anchorBeat = (wholeBar ? barSpan : undefined) ?? beats.near(geometry, lines, drag.x, drag.y) ?? null;
            drag.currentBeat = drag.anchorBeat;
        }
        this.capture(drag);
        this.surface.place();
    }

    private readonly onPointerMove = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointerId) {
            return;
        }
        const at = this.surface.point(event);
        this.take(drag, movedGesture(drag.gesture, Math.hypot(at.x - drag.x, at.y - drag.y)));
        const geometry = this.surface.owner.geometry();
        if (this.drag !== drag || geometry === null) {
            return;
        }
        if (drag.gesture.phase === "editing" || drag.gesture.phase === "extending") {
            this.moveEdited(geometry, drag, at);
            return;
        }
        if (drag.anchor === null) {
            return;
        }
        const beats = this.surface.beats;
        const lines = beats.lines();
        if (lines !== null && drag.anchorBeat !== null) {
            const beat = beats.near(geometry, lines, at.x, at.y);
            if (beat !== undefined && (beat.start !== drag.currentBeat?.start || beat.end !== drag.currentBeat?.end)) {
                drag.currentBeat = beat;
                this.surface.place();
            }
            return;
        }
        const bar = geometry.barNear(at.x, at.y);
        if (bar !== undefined && bar !== drag.current) {
            drag.current = bar;
            this.surface.place();
        }
    };

    /** A move from inside: the selection kept whole, extended to the line past which the pointer is (#582 item 9). */
    private moveEdited(geometry: ScoreGeometry, drag: Drag, at: { x: number; y: number }): void {
        const from = drag.gesture.selection;
        if (from === null) {
            return;
        }
        const beatLines = this.surface.beats.lines();
        const lines = beatLines !== null ? keyLines(beatLines, geometry.bars) : barlines(geometry.bars);
        const edited = extendedRange(from, tickAt(geometry, at.x, at.y), lines);
        if (edited.start !== drag.edited?.start || edited.end !== drag.edited?.end) {
            drag.edited = edited;
            this.surface.place();
        }
    }

    private readonly onPointerUp = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointerId) {
            return;
        }
        clearTimeout(drag.timer);
        this.drag = null;
        const how = gestureChange(drag.gesture);
        if (how === "none") {
            return;
        }
        this.swallowNextClick = true;
        // A click may not follow (the pointer left the score); the next real click is not swallowed.
        window.setTimeout(() => (this.swallowNextClick = false), 0);
        const range = how === "clear" ? null : this.ended(drag);
        if (range === null) {
            this.surface.place();
            return;
        }
        this.commit(range, how, drag.id, how === "edit");
    };

    /** What a gesture ending now selected: the edited selection when it changed, or the new one; null for nothing to choose. */
    private ended(drag: Drag): TickRange | null {
        if (drag.gesture.phase === "editing" || drag.gesture.phase === "extending") {
            const from = drag.gesture.selection;
            const edited = drag.edited;
            return edited === null || from === null || (edited.start === from.start && edited.end === from.end) ? null : edited;
        }
        if (drag.anchorBeat !== null && drag.currentBeat !== null && this.surface.owner.chosenSpan !== undefined) {
            return spanOf(drag.anchorBeat, drag.currentBeat);
        }
        if (drag.anchor === null || drag.current === null) {
            return null;
        }
        const [first, last] = ordered(drag.anchor, drag.current);
        return { start: first.tick, end: last.end };
    }

    /** Hands a passage chosen to the owner: by beats while the score selects by them, else as its first and last bar. */
    private commit(range: TickRange, how: SelectionChange, gesture: string, byHandle: boolean): void {
        const owner = this.surface.owner;
        const byBeats = this.surface.beats.lines() === null ? undefined : owner.chosenSpan;
        if (byBeats !== undefined) {
            byBeats.call(owner, range.start, range.end, byHandle, how, gesture);
            return;
        }
        const geometry = owner.geometry();
        const first = geometry?.barAt(range.start);
        const last = geometry?.barAt(Math.max(range.start, range.end - 1));
        if (first !== undefined && last !== undefined) {
            owner.chosen(first.tick, last.tick, byHandle, how, gesture);
        }
    }

    private readonly onPointerCancel = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag !== null && event.pointerId === drag.pointerId) {
            this.cancelDrag(drag);
        }
    };

    /** The gesture ends with no step: a selection the hold took away comes back. */
    private cancelDrag(drag: Drag): void {
        clearTimeout(drag.timer);
        this.drag = null;
        if (drag.gesture.cleared) {
            this.surface.owner.cancelled?.(drag.id);
        }
        this.surface.place();
    }

    private readonly onTouchMove = (event: TouchEvent): void => {
        if (this.surface.capturing()) {
            event.preventDefault();
        }
    };

    /** A long press on a touch screen brings no browser menu while it selects. */
    private readonly onContextMenu = (event: MouseEvent): void => {
        if (this.drag !== null) {
            event.preventDefault();
        }
    };
}
