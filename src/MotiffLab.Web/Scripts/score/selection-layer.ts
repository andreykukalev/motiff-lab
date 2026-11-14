// The selection on the score (decision record 0018, section 3; design/kit/ds.css .band, .handle, .menu): a cobalt
// band over the selected bars on each system they touch, round handles on the first and last barline that drag
// the edges from barline to barline, the selection gesture (#511 2, selection-gesture.ts: a drag from inside moves the
// nearer edge, a hold outside clears, a move after it selects anew; with no selection a drag with the mouse, or a long
// press and a drag with a finger, selects), and the contextual menu placed above the band. The band and the handles are
// drawn in the score's content pixels, as the brackets are; the menu belongs to Blazor and is only placed here.
// Selection by beats (#349): selection-beats.ts. Where the menu goes: selection-menu.ts.

import { BRACKET_LIFT, type Run, type ScoreBar, type ScoreGeometry } from "./score-geometry.js";
import { NUMBER_HEIGHT, menuClear, menuPlace } from "./selection-menu.js";
import { BEAT_CLASS, type Beat, type BeatSource, ScoreBeats, keyLines, spanOf } from "./selection-beats.js";
import { SelectionHandles, barlines, tickAt } from "./selection-handles.js";
import { type InteractionMode, inside } from "./score-modes.js";
import {
    type DragStart,
    HOLD_MS,
    type SelectionChange,
    type SelectionGesture,
    type TickRange,
    dragStartOf,
    editedRange,
    gestureChange,
    handleTakes,
    handlesShown,
    heldGesture,
    movedGesture,
    nextGestureId,
    pressGesture,
    staleGesture,
} from "./selection-gesture.js";

export { menuClear, menuPlace, dragStartOf, handleTakes, type DragStart };

const BAND_CLASS = "sel-band";
const HANDLE_CLASS = "sel-handle";

/** What the layer needs from the score view. */
export interface SelectionHost extends BeatSource {
    geometry(): ScoreGeometry | null;
    /** Whether the pen takes the pointer now: then the mouse and a finger write rather than select. */
    penOn(): boolean;
    /**
     * Bars chosen by a drag across the score, or by moving a handle: where the first and the last bar start; how the
     * selection changed and the gesture that changed it, whose changes the owner keeps as one step (#511 11).
     */
    chosen(first: number, last: number, byHandle: boolean, how: SelectionChange, gesture: string): void;
    /** The top of the marks drawn just above a band's first run (the bookmarks), in window pixels, for the menu to clear; null for none. */
    marksTop?(band: { left: number; right: number; top: number }): number | null;
    /** Beats chosen by a drag or a handle while selecting by beats: the passage from `start` to `end`, in ticks. */
    chosenSpan?(start: number, end: number, byHandle: boolean, how: SelectionChange, gesture: string): void;
    /** A press held outside the selection took it away (#511 2.1, scenario B), as one step of that gesture. */
    cleared?(gesture: string): void;
    /** The gesture that changed the selection was cancelled (a second finger, the system): it is no step (#511 11). */
    cancelled?(gesture: string): void;
    /** A pointer that cannot scroll moved off the selection before the hold: say how to begin a new one (#511 2.1, scenario C). */
    hint?(): void;
    /** The mode under the rules of version 0.5 (score-modes.ts); null or missing keeps the rules before them. */
    mode?(): InteractionMode | null;
    /** Whether a finger drags at once, without the long press: the iPad in Select, where two fingers scroll. */
    fingerDrags?(): boolean;
    /**
     * How a press of this pointer type starts a selection, where the host decides it (the iPad's stylus model, #479
     * item 18): at once, after a long press (in any mode, never from inside the selection) or not at all. Undefined
     * keeps the rules above.
     */
    dragStart?(pointerType: string): DragStart | undefined;
    /** Whether a press of this pointer type on a handle takes it, where the host decides it (the iPad, #511 4.1). */
    takesHandle?(pointerType: string): boolean | undefined;
}

/** The selection as Blazor gives it: its span in ticks and what a screen reader says for each handle. */
export interface ScoreSelection {
    start: number;
    end: number;
    startLabel: string;
    endLabel: string;
}

/** The bars from one to another in time order, whichever comes first. */
export function ordered(a: ScoreBar, b: ScoreBar): [ScoreBar, ScoreBar] {
    return a.tick <= b.tick ? [a, b] : [b, a];
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

export class SelectionLayer {
    private selection: ScoreSelection | null = null;
    private menu: HTMLElement | null = null;
    private drag: Drag | null = null;
    /** What pressing and dragging the handles does (selection-handles.ts). */
    private readonly handles: SelectionHandles;
    /** The click the browser sends after a drag is not a click on the score. */
    private swallowNextClick = false;
    /** The menu's size changes with what it holds; it is placed again then. */
    private readonly menuSize = new ResizeObserver(() => this.placeMenu());
    /** Selecting by beats, while the score view gives beat lines. */
    readonly beats: ScoreBeats;

    constructor(
        private readonly host: HTMLElement,
        readonly owner: SelectionHost,
    ) {
        this.beats = new ScoreBeats(owner);
        this.handles = new SelectionHandles({
            owner,
            beats: this.beats,
            selection: () => this.selection,
            point: (event) => this.point(event),
            show: (geometry, start, end) => this.draw(geometry, start, end, null),
            place: () => this.place(),
            placeMenu: () => this.placeMenu(),
        });
        host.addEventListener("pointerdown", this.onPointerDown);
        host.addEventListener("pointermove", this.onPointerMove);
        host.addEventListener("pointerup", this.onPointerUp);
        host.addEventListener("pointercancel", this.onPointerCancel);
        // Not passive: once a long press has started a drag, the finger moves the selection, not the page.
        host.addEventListener("touchmove", this.onTouchMove, { passive: false });
        host.addEventListener("contextmenu", this.onContextMenu);
    }

    /** Shows the selection, or none. */
    setSelection(selection: ScoreSelection | null): void {
        this.selection = selection;
        this.place();
    }

    /** The menu Blazor draws for the selection; the layer places it over the band. */
    setMenu(menu: HTMLElement | null): void {
        this.menuSize.disconnect();
        this.menu = menu;
        if (menu !== null) {
            this.menuSize.observe(menu);
        }
        this.placeMenu();
    }

    /** Whether the click the browser sends now ends a drag, and is not a click on the score. */
    swallowsClick(): boolean {
        const swallow = this.swallowNextClick;
        this.swallowNextClick = false;
        return swallow;
    }

    /** Draws the band and the handles again where the bars are now, after a new layout. */
    place(): void {
        const geometry = this.owner.geometry();
        const selection = this.selection;
        const preview = geometry === null ? null : this.preview();
        if (geometry !== null && preview !== null) {
            this.draw(geometry, preview.start, preview.end, null);
            this.placeMenu();
            return;
        }
        if (geometry === null || selection === null || this.drag?.gesture.phase === "cleared") {
            this.clear();
            this.placeMenu();
            return;
        }
        this.draw(geometry, selection.start, selection.end, selection);
        this.placeMenu();
    }

    /** What a gesture under way shows instead of the selection: a handle's or an edge's passage, or a new one; null for none. */
    private preview(): TickRange | null {
        const handle = this.handles.drag;
        if (handle !== null) {
            return { start: handle.start, end: handle.end };
        }
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

    /** Moves the menu with the band as the score scrolls; hidden while the band is out of sight or a drag is on. */
    placeMenu(): void {
        const menu = this.menu;
        if (menu === null) {
            return;
        }
        const bands = this.host.querySelectorAll<HTMLElement>(`:scope > .${BAND_CLASS}`);
        const dragging = this.capturing();
        if (bands.length === 0 || dragging || this.selection === null) {
            menu.hidden = true;
            return;
        }
        const first = bands[0].getBoundingClientRect();
        const last = bands[bands.length - 1].getBoundingClientRect();
        const view = this.host.getBoundingClientRect();
        if (last.bottom < view.top || first.top > view.bottom) {
            menu.hidden = true;
            return;
        }
        menu.hidden = false;
        const size = menu.getBoundingClientRect();
        // Bookmarks over the band push the menu up: it sits as far above them as above the bracket number.
        const marks = this.owner.marksTop?.(first) ?? null;
        const top = marks === null ? first.top : Math.min(first.top, marks + BRACKET_LIFT + NUMBER_HEIGHT);
        // The drawers and sheets lie over the menu, so it keeps clear of them: to the right of the Contents, to the left of a
        // sheet on the right (Insights, Check in landscape), above a drawer or sheet over the foot (the roll, a sheet in portrait).
        const clear = menuClear([...document.querySelectorAll<HTMLElement>("[data-menu-clear]")], window.innerWidth, window.innerHeight);
        const place = menuPlace({ left: first.left, right: first.right, top }, last, size, { width: window.innerWidth, height: window.innerHeight, ...clear });
        menu.style.left = `${place.left}px`;
        menu.style.top = `${place.top}px`;
        menu.style.setProperty("--pointer", `${place.pointer}px`);
        menu.dataset.below = place.below ? "true" : "false";
    }

    dispose(): void {
        this.host.removeEventListener("pointerdown", this.onPointerDown);
        this.host.removeEventListener("pointermove", this.onPointerMove);
        this.host.removeEventListener("pointerup", this.onPointerUp);
        this.host.removeEventListener("pointercancel", this.onPointerCancel);
        this.host.removeEventListener("touchmove", this.onTouchMove);
        this.host.removeEventListener("contextmenu", this.onContextMenu);
        this.menuSize.disconnect();
        if (this.drag !== null) {
            clearTimeout(this.drag.timer);
        }
        this.handles.abandon();
        this.clear();
    }

    private clear(): void {
        for (const old of this.host.querySelectorAll(`:scope > .${BAND_CLASS}, :scope > .${HANDLE_CLASS}, :scope > .${BEAT_CLASS}`)) {
            old.remove();
        }
    }

    /** The band over [start, end), and the handles when `selection` gives their names (a drag in progress shows the band alone). */
    private draw(geometry: ScoreGeometry, start: number, end: number, selection: ScoreSelection | null): void {
        // A handle being dragged keeps its element, and with it the pointer capture.
        const kept = this.handles.drag !== null ? [...this.host.querySelectorAll<HTMLElement>(`:scope > .${HANDLE_CLASS}`)] : [];
        for (const old of this.host.querySelectorAll(`:scope > .${BAND_CLASS}, :scope > .${BEAT_CLASS}`)) {
            old.remove();
        }
        if (this.handles.drag === null) {
            for (const old of this.host.querySelectorAll(`:scope > .${HANDLE_CLASS}`)) {
                old.remove();
            }
        }
        const runs = geometry.band(start, end);
        this.host.append(...this.beats.cut(geometry, runs, start, end));
        for (const run of runs) {
            const band = document.createElement("div");
            band.className = BAND_CLASS;
            band.setAttribute("aria-hidden", "true");
            setBox(band, run);
            this.host.append(band);
        }
        if (runs.length === 0) {
            return;
        }
        const first = runs[0];
        const last = runs[runs.length - 1];
        if (kept.length > 0) {
            for (const handle of kept) {
                const run = handle.dataset.edge === "start" ? first : last;
                handle.style.left = `${handle.dataset.edge === "start" ? run.left : run.right}px`;
                handle.style.top = `${(run.top + run.bottom) / 2}px`;
            }
            return;
        }
        // Under the modes only Select shows the handles (#543 4.2): elsewhere none is drawn, so none takes a press.
        if (selection !== null && handlesShown(this.owner.mode?.())) {
            this.host.append(this.handle("start", first.left, (first.top + first.bottom) / 2, selection.startLabel));
            this.host.append(this.handle("end", last.right, (last.top + last.bottom) / 2, selection.endLabel));
        }
    }

    private handle(edge: "start" | "end", x: number, y: number, label: string): HTMLElement {
        const handle = document.createElement("button");
        handle.type = "button";
        handle.className = `${HANDLE_CLASS} ${edge}`;
        handle.dataset.edge = edge;
        handle.setAttribute("aria-label", label);
        handle.style.left = `${x}px`;
        handle.style.top = `${y}px`;
        handle.addEventListener("pointerdown", this.handles.onDown);
        handle.addEventListener("keydown", this.handles.onKey);
        return handle;
    }

    private point(event: PointerEvent | MouseEvent): { x: number; y: number } {
        const view = this.host.getBoundingClientRect();
        return { x: event.clientX - view.left + this.host.scrollLeft, y: event.clientY - view.top + this.host.scrollTop };
    }

    /** Whether a gesture holds the pointer now: it moves an edge or a handle, makes a new selection or has cleared it. */
    private capturing(): boolean {
        const phase = this.drag?.gesture.phase;
        return this.handles.drag !== null || phase === "editing" || phase === "creating" || phase === "cleared";
    }

    private readonly onPointerDown = (event: PointerEvent): void => {
        // A gesture whose end was lost (#543 9) ends as cancelled before the new press, so it never blocks it.
        if (this.drag !== null && staleGesture(this.drag, event)) {
            this.cancelDrag(this.drag);
        }
        if (this.handles.drag !== null && staleGesture(this.handles.drag, event)) {
            this.handles.abandon();
        }
        if (this.drag !== null || this.handles.drag !== null) {
            return;
        }
        // A finger waits for the long press, unless it drags at once (the iPad in Select): then it drags as a mouse does.
        const rule = this.owner.dragStart?.(event.pointerType);
        const start = dragStartOf(event.pointerType, rule, this.owner.penOn(), this.owner.fingerDrags?.() === true);
        if (start === "none" || (start === "now" && event.button !== 0)) {
            return;
        }
        // A bracket, a handle and anything Blazor put over the score are their own targets.
        if (event.target instanceof Element && event.target.closest(`.bracket, .${HANDLE_CLASS}`) !== null) {
            return;
        }
        const geometry = this.owner.geometry();
        const at = this.point(event);
        if (geometry === null || !this.mayStart(rule !== undefined)) {
            return;
        }
        const selection = this.selection === null ? null : { start: this.selection.start, end: this.selection.end };
        const gesture = pressGesture(selection, tickAt(geometry, at.x, at.y), this.insideBand(at), start);
        const drag: Drag = { pointerId: event.pointerId, pointerType: event.pointerType, x: at.x, y: at.y, gesture, id: nextGestureId(), edited: null, anchor: null, current: null, anchorBeat: null, currentBeat: null, timer: 0 };
        drag.timer = window.setTimeout(() => this.take(drag, heldGesture(drag.gesture)), HOLD_MS);
        this.drag = drag;
    };

    /** Under the rules of version 0.5 only Select selects with a press; where the host rules the pointer (`ruled`, the iPad), its rule says. */
    private mayStart(ruled: boolean): boolean {
        const mode = this.owner.mode?.() ?? null;
        return mode === null || ruled || mode === "select";
    }

    /** Whether a point lies on the band, in the score's content pixels. */
    private insideBand(at: { x: number; y: number }): boolean {
        const bands = [...this.host.querySelectorAll<HTMLElement>(`:scope > .${BAND_CLASS}`)].map((band) => {
            const left = parseFloat(band.style.left);
            const top = parseFloat(band.style.top);
            return { left, top, right: left + parseFloat(band.style.width), bottom: top + parseFloat(band.style.height) };
        });
        return inside(bands, at.x, at.y);
    }

    /** Does what a step of the gesture asks (selection-gesture.ts). */
    private take(drag: Drag, step: ReturnType<typeof heldGesture>): void {
        if (this.drag !== drag) {
            return;
        }
        switch (step) {
            case "create":
                this.startDrag(drag);
                return;
            case "edit":
                this.capture(drag);
                drag.edited = drag.gesture.selection;
                this.place();
                return;
            case "clear":
                // The band goes at once, so the player sees the hold took it; the owner takes the selection away.
                this.capture(drag);
                this.place();
                this.owner.cleared?.(drag.id);
                return;
            case "pass":
            case "hint":
                clearTimeout(drag.timer);
                this.drag = null;
                if (step === "hint") {
                    this.owner.hint?.();
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

    private startDrag(drag: Drag): void {
        const geometry = this.owner.geometry();
        const bar = geometry?.barNear(drag.x, drag.y);
        if (this.drag !== drag || geometry == null || bar === undefined) {
            return;
        }
        drag.anchor = bar;
        drag.current = bar;
        const lines = this.beats.lines();
        if (lines !== null) {
            drag.anchorBeat = this.beats.near(geometry, lines, drag.x, drag.y) ?? null;
            drag.currentBeat = drag.anchorBeat;
        }
        this.capture(drag);
        this.place();
    }

    private readonly onPointerMove = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointerId) {
            return;
        }
        const at = this.point(event);
        this.take(drag, movedGesture(drag.gesture, Math.hypot(at.x - drag.x, at.y - drag.y)));
        const geometry = this.owner.geometry();
        if (this.drag !== drag || geometry === null) {
            return;
        }
        if (drag.gesture.phase === "editing") {
            this.moveEdited(geometry, drag, at);
            return;
        }
        if (drag.anchor === null) {
            return;
        }
        const lines = this.beats.lines();
        if (lines !== null && drag.anchorBeat !== null) {
            const beat = this.beats.near(geometry, lines, at.x, at.y);
            if (beat !== undefined && (beat.start !== drag.currentBeat?.start || beat.end !== drag.currentBeat?.end)) {
                drag.currentBeat = beat;
                this.place();
            }
            return;
        }
        const bar = geometry.barNear(at.x, at.y);
        if (bar !== undefined && bar !== drag.current) {
            drag.current = bar;
            this.place();
        }
    };

    /** An edge moved from inside the selection: as far as the pointer moved, to the nearest barline or beat line (#511 2.1, A). */
    private moveEdited(geometry: ScoreGeometry, drag: Drag, at: { x: number; y: number }): void {
        const from = drag.gesture.selection;
        if (from === null) {
            return;
        }
        const beatLines = this.beats.lines();
        const lines = beatLines !== null ? keyLines(beatLines, geometry.bars) : barlines(geometry.bars);
        const edited = editedRange(from, drag.gesture.edge, drag.gesture.pressTick, tickAt(geometry, at.x, at.y), lines);
        if (edited.start !== drag.edited?.start || edited.end !== drag.edited?.end) {
            drag.edited = edited;
            this.place();
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
            this.place();
            return;
        }
        this.commit(range, how, drag.id, how === "edit");
    };

    /** What a gesture ending now selected: the edited selection when it changed, or the new one; null for nothing to choose. */
    private ended(drag: Drag): TickRange | null {
        if (drag.gesture.phase === "editing") {
            const from = drag.gesture.selection;
            const edited = drag.edited;
            return edited === null || from === null || (edited.start === from.start && edited.end === from.end) ? null : edited;
        }
        if (drag.anchorBeat !== null && drag.currentBeat !== null && this.owner.chosenSpan !== undefined) {
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
        const byBeats = this.beats.lines() === null ? undefined : this.owner.chosenSpan;
        if (byBeats !== undefined) {
            byBeats.call(this.owner, range.start, range.end, byHandle, how, gesture);
            return;
        }
        const geometry = this.owner.geometry();
        const first = geometry?.barAt(range.start);
        const last = geometry?.barAt(Math.max(range.start, range.end - 1));
        if (first !== undefined && last !== undefined) {
            this.owner.chosen(first.tick, last.tick, byHandle, how, gesture);
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
            this.owner.cancelled?.(drag.id);
        }
        this.place();
    }

    private readonly onTouchMove = (event: TouchEvent): void => {
        if (this.capturing()) {
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

function setBox(element: HTMLElement, run: Run): void {
    element.style.left = `${run.left}px`;
    element.style.top = `${run.top}px`;
    element.style.width = `${Math.max(4, run.right - run.left)}px`;
    element.style.height = `${Math.max(4, run.bottom - run.top)}px`;
}
