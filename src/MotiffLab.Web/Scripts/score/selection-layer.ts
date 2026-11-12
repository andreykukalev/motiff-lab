// The selection on the score (decision record 0018, section 3; design/kit/ds.css .band, .handle, .menu): a cobalt
// band over the selected bars on each system they touch, round handles on the first and last barline that drag
// the edges from barline to barline, a drag across bars with the mouse (or a long press, then a drag, with a
// finger) that selects them, and the contextual menu placed above the band. The band and the handles are drawn
// in the score's content pixels, as the brackets are; the menu belongs to Blazor and is only placed here.
// Selection by beats (#349): selection-beats.ts. Where the menu goes: selection-menu.ts.

import { BRACKET_LIFT, type Run, type ScoreBar, type ScoreGeometry } from "./score-geometry.js";
import { NUMBER_HEIGHT, menuClear, menuPlace } from "./selection-menu.js";
import { BEAT_CLASS, type Beat, type BeatSource, ScoreBeats, keyLines, spanOf, stepEdge } from "./selection-beats.js";
import { type InteractionMode, inside } from "./score-modes.js";

export { menuClear, menuPlace };

/** How far the mouse moves before a press becomes a drag across bars, in pixels. */
export const DRAG_DISTANCE = 6;

/** How long a finger rests on a bar before it selects it and can drag on, in milliseconds. */
export const LONG_PRESS_MS = 450;

/** How far a finger may move during the long press before the press is a scroll instead, in pixels. */
const LONG_PRESS_SLOP = 8;

const BAND_CLASS = "sel-band";
const HANDLE_CLASS = "sel-handle";

/** What the layer needs from the score view. */
export interface SelectionHost extends BeatSource {
    geometry(): ScoreGeometry | null;
    /** Whether the pen takes the pointer now: then the mouse and a finger write rather than select. */
    penOn(): boolean;
    /** Bars chosen by a drag across the score, or by moving a handle: where the first and the last bar start. */
    chosen(first: number, last: number, byHandle: boolean): void;
    /** The top of the marks drawn just above a band's first run (the bookmarks), in window pixels, for the menu to clear; null for none. */
    marksTop?(band: { left: number; right: number; top: number }): number | null;
    /** Beats chosen by a drag or a handle while selecting by beats: the passage from `start` to `end`, in ticks. */
    chosenSpan?(start: number, end: number, byHandle: boolean): void;
    /** The mode under the rules of version 0.5 (score-modes.ts); null or missing keeps the rules before them. */
    mode?(): InteractionMode | null;
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
    /** Where the press started, in content pixels. */
    x: number;
    y: number;
    touch: boolean;
    /** Set once the press is a drag: the bar it started on, and the one under the pointer now. */
    anchor: ScoreBar | null;
    current: ScoreBar | null;
    /** Selecting by beats: the beat the drag started on, and the one under the pointer now. */
    anchorBeat: Beat | null;
    currentBeat: Beat | null;
    timer: number;
}

interface HandleDrag {
    pointerId: number;
    edge: "start" | "end";
    first: ScoreBar;
    last: ScoreBar;
    /** The passage the band shows while the handle moves, in ticks: whole bars, or beats while selecting by beats. */
    start: number;
    end: number;
}

export class SelectionLayer {
    private selection: ScoreSelection | null = null;
    private menu: HTMLElement | null = null;
    private drag: Drag | null = null;
    private handleDrag: HandleDrag | null = null;
    /** The click the browser sends after a drag is not a click on the score. */
    private swallowNextClick = false;
    /** The menu's size changes with what it holds; it is placed again then. */
    private readonly menuSize = new ResizeObserver(() => this.placeMenu());
    /** Selecting by beats, while the score view gives beat lines. */
    private readonly beats: ScoreBeats;

    constructor(
        private readonly host: HTMLElement,
        private readonly owner: SelectionHost,
    ) {
        this.beats = new ScoreBeats(owner);
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
        if (geometry === null || selection === null) {
            this.clear();
            this.placeMenu();
            return;
        }
        const handle = this.handleDrag;
        if (handle !== null) {
            this.draw(geometry, handle.start, handle.end, null);
        } else {
            this.draw(geometry, selection.start, selection.end, selection);
        }
        this.placeMenu();
    }

    /** Moves the menu with the band as the score scrolls; hidden while the band is out of sight or a drag is on. */
    placeMenu(): void {
        const menu = this.menu;
        if (menu === null) {
            return;
        }
        const bands = this.host.querySelectorAll<HTMLElement>(`:scope > .${BAND_CLASS}`);
        const dragging = this.drag?.anchor != null || this.handleDrag !== null;
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
        const kept = this.handleDrag !== null ? [...this.host.querySelectorAll<HTMLElement>(`:scope > .${HANDLE_CLASS}`)] : [];
        for (const old of this.host.querySelectorAll(`:scope > .${BAND_CLASS}, :scope > .${BEAT_CLASS}`)) {
            old.remove();
        }
        if (this.handleDrag === null) {
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
        if (selection !== null) {
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
        handle.addEventListener("pointerdown", this.onHandleDown);
        handle.addEventListener("keydown", this.onHandleKey);
        return handle;
    }

    /** The selection's first and last bar where the score shows them now. */
    private selectedBars(geometry: ScoreGeometry): [ScoreBar, ScoreBar] | null {
        const selection = this.selection;
        if (selection === null) {
            return null;
        }
        const first = geometry.barAt(selection.start);
        const last = geometry.barAt(Math.max(selection.start, selection.end - 1));
        return first !== undefined && last !== undefined ? [first, last] : null;
    }

    private point(event: PointerEvent | MouseEvent): { x: number; y: number } {
        const view = this.host.getBoundingClientRect();
        return { x: event.clientX - view.left + this.host.scrollLeft, y: event.clientY - view.top + this.host.scrollTop };
    }

    private readonly onPointerDown = (event: PointerEvent): void => {
        if (this.drag !== null || this.handleDrag !== null || event.pointerType === "pen" || this.owner.penOn()) {
            return;
        }
        const touch = event.pointerType === "touch";
        if (!touch && event.button !== 0) {
            return;
        }
        // A bracket, a handle and anything Blazor put over the score are their own targets.
        if (event.target instanceof Element && event.target.closest(`.bracket, .${HANDLE_CLASS}`) !== null) {
            return;
        }
        const at = this.point(event);
        if (!this.dragMayStart(at)) {
            return;
        }
        const drag: Drag = { pointerId: event.pointerId, x: at.x, y: at.y, touch, anchor: null, current: null, anchorBeat: null, currentBeat: null, timer: 0 };
        if (touch) {
            drag.timer = window.setTimeout(() => this.startDrag(drag), LONG_PRESS_MS);
        }
        this.drag = drag;
    };

    /** Under the rules of version 0.5 only Select starts a selection with a drag, and never from inside the selection. */
    private dragMayStart(at: { x: number; y: number }): boolean {
        const mode = this.owner.mode?.() ?? null;
        if (mode === null) {
            return true;
        }
        const bands = [...this.host.querySelectorAll<HTMLElement>(`:scope > .${BAND_CLASS}`)].map((band) => {
            const left = parseFloat(band.style.left);
            const top = parseFloat(band.style.top);
            return { left, top, right: left + parseFloat(band.style.width), bottom: top + parseFloat(band.style.height) };
        });
        return mode === "select" && !inside(bands, at.x, at.y);
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
        try {
            this.host.setPointerCapture(drag.pointerId);
        } catch {
            // The pointer is gone already; the drag ends with it.
        }
        this.preview(geometry, drag);
    }

    /** The band over what the drag covers so far: its bars, or its beats while selecting by beats. */
    private preview(geometry: ScoreGeometry, drag: Drag): void {
        if (drag.anchorBeat !== null && drag.currentBeat !== null) {
            const span = spanOf(drag.anchorBeat, drag.currentBeat);
            this.draw(geometry, span.start, span.end, null);
        } else if (drag.anchor !== null && drag.current !== null) {
            const [first, last] = ordered(drag.anchor, drag.current);
            this.draw(geometry, first.tick, last.end, null);
        }
        this.placeMenu();
    }

    private readonly onPointerMove = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointerId) {
            return;
        }
        const at = this.point(event);
        const moved = Math.hypot(at.x - drag.x, at.y - drag.y);
        if (drag.anchor === null) {
            if (drag.touch) {
                // The finger moved before the long press: it scrolls.
                if (moved > LONG_PRESS_SLOP) {
                    clearTimeout(drag.timer);
                    this.drag = null;
                }
                return;
            }
            if (moved < DRAG_DISTANCE) {
                return;
            }
            this.startDrag(drag);
        }
        const geometry = this.owner.geometry();
        const lines = this.beats.lines();
        if (geometry != null && lines !== null && drag.anchorBeat !== null) {
            const beat = this.beats.near(geometry, lines, at.x, at.y);
            if (beat !== undefined && (beat.start !== drag.currentBeat?.start || beat.end !== drag.currentBeat?.end)) {
                drag.currentBeat = beat;
                this.preview(geometry, drag);
            }
            return;
        }
        const bar = geometry?.barNear(at.x, at.y);
        if (geometry == null || bar === undefined || drag.anchor === null || bar === drag.current) {
            return;
        }
        drag.current = bar;
        this.preview(geometry, drag);
    };

    private readonly onPointerUp = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointerId) {
            return;
        }
        clearTimeout(drag.timer);
        this.drag = null;
        if (drag.anchor === null || drag.current === null) {
            return;
        }
        this.swallowNextClick = true;
        // A click may not follow (the pointer left the score); the next real click is not swallowed.
        window.setTimeout(() => (this.swallowNextClick = false), 0);
        if (drag.anchorBeat !== null && drag.currentBeat !== null && this.owner.chosenSpan !== undefined) {
            const span = spanOf(drag.anchorBeat, drag.currentBeat);
            this.owner.chosenSpan(span.start, span.end, false);
            return;
        }
        const [first, last] = ordered(drag.anchor, drag.current);
        this.owner.chosen(first.tick, last.tick, false);
    };

    private readonly onPointerCancel = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag !== null && event.pointerId === drag.pointerId) {
            clearTimeout(drag.timer);
            this.drag = null;
            this.place();
        }
    };

    private readonly onTouchMove = (event: TouchEvent): void => {
        if (this.drag?.anchor != null || this.handleDrag !== null) {
            event.preventDefault();
        }
    };

    /** A long press on a touch screen brings no browser menu while it selects. */
    private readonly onContextMenu = (event: MouseEvent): void => {
        if (this.drag !== null) {
            event.preventDefault();
        }
    };

    private readonly onHandleDown = (event: PointerEvent): void => {
        const handle = event.currentTarget as HTMLElement;
        const geometry = this.owner.geometry();
        const bars = geometry === null ? null : this.selectedBars(geometry);
        const selection = this.selection;
        if (bars === null || selection === null || (event.pointerType !== "touch" && event.button !== 0)) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        handle.setPointerCapture(event.pointerId);
        const drag: HandleDrag = {
            pointerId: event.pointerId, edge: handle.dataset.edge === "start" ? "start" : "end", first: bars[0], last: bars[1], start: selection.start, end: selection.end,
        };
        this.handleDrag = drag;
        handle.classList.add("dragging");
        this.placeMenu();
        const move = (moveEvent: PointerEvent): void => {
            if (moveEvent.pointerId !== drag.pointerId) {
                return;
            }
            const now = this.owner.geometry();
            if (now != null) {
                this.moveEdge(now, drag, this.point(moveEvent));
            }
        };
        const end = (endEvent: PointerEvent): void => {
            if (endEvent.pointerId !== drag.pointerId) {
                return;
            }
            handle.removeEventListener("pointermove", move);
            handle.removeEventListener("pointerup", end);
            handle.removeEventListener("pointercancel", end);
            handle.classList.remove("dragging");
            this.handleDrag = null;
            const byBeats = this.beats.lines() === null ? undefined : this.owner.chosenSpan;
            const changed = byBeats ? drag.start !== selection.start || drag.end !== selection.end : drag.first !== bars[0] || drag.last !== bars[1];
            if (changed && endEvent.type === "pointerup" && byBeats) {
                byBeats.call(this.owner, drag.start, drag.end, true);
            } else if (changed && endEvent.type === "pointerup") {
                this.owner.chosen(drag.first.tick, drag.last.tick, true);
            } else {
                this.place();
            }
        };
        handle.addEventListener("pointermove", move);
        handle.addEventListener("pointerup", end);
        handle.addEventListener("pointercancel", end);
    };

    /** A handle dragged to a point: its edge goes to the barline, or the beat line, nearest it, never past the other edge. */
    private moveEdge(geometry: ScoreGeometry, drag: HandleDrag, at: { x: number; y: number }): void {
        const lines = this.beats.lines();
        if (lines !== null) {
            const line = this.beats.edgeNear(geometry, lines, at.x, at.y);
            if (line !== undefined && (drag.edge === "start" ? line < drag.end : line > drag.start)) {
                drag[drag.edge] = line;
                this.draw(geometry, drag.start, drag.end, null);
            }
            return;
        }
        const bar = geometry.edgeNear(at.x, at.y, drag.edge);
        if (bar === undefined) {
            return;
        }
        // An edge never passes the other one: the selection keeps at least a bar.
        if (drag.edge === "start") {
            drag.first = bar.tick <= drag.last.tick ? bar : drag.last;
        } else {
            drag.last = bar.tick >= drag.first.tick ? bar : drag.first;
        }
        drag.start = drag.first.tick;
        drag.end = drag.last.end;
        this.draw(geometry, drag.start, drag.end, null);
    }

    /** By beats the arrow keys move an edge a beat at a time; false while the selection takes whole bars. */
    private stepByBeats(geometry: ScoreGeometry, edge: "start" | "end", step: number): boolean {
        const lines = this.beats.lines();
        const pick = this.owner.chosenSpan;
        if (lines === null || pick === undefined || this.selection === null) {
            return false;
        }
        const moved = stepEdge(keyLines(lines, geometry.bars), this.selection, edge, step);
        if (moved !== undefined) {
            pick.call(this.owner, moved.start, moved.end, true);
        }
        return true;
    }

    /** The arrow keys move a handle's edge a bar at a time, as a drag does (WCAG 2.1.1). */
    private readonly onHandleKey = (event: KeyboardEvent): void => {
        const step = event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0;
        const geometry = this.owner.geometry();
        const bars = geometry === null ? null : this.selectedBars(geometry);
        if (step === 0 || geometry === null || bars === null) {
            return;
        }
        event.preventDefault();
        const edge = (event.currentTarget as HTMLElement).dataset.edge;
        if (this.stepByBeats(geometry, edge === "start" ? "start" : "end", step)) {
            return;
        }
        const inTime = uniqueInTime(geometry.bars);
        let [first, last] = bars;
        if (edge === "start") {
            const index = inTime.findIndex((bar) => bar.tick === first.tick) + step;
            if (index < 0 || inTime[index].tick > last.tick) {
                return;
            }
            first = inTime[index];
        } else {
            const index = inTime.findIndex((bar) => bar.tick === last.tick) + step;
            if (index >= inTime.length || inTime[index].tick < first.tick) {
                return;
            }
            last = inTime[index];
        }
        this.owner.chosen(first.tick, last.tick, true);
    };
}

/** The bars in time order, one per start (a bar repeated on another page counts once). */
function uniqueInTime(bars: readonly ScoreBar[]): ScoreBar[] {
    const byTick = new Map<number, ScoreBar>();
    for (const bar of bars) {
        if (!byTick.has(bar.tick)) {
            byTick.set(bar.tick, bar);
        }
    }
    return [...byTick.values()].sort((a, b) => a.tick - b.tick);
}

function setBox(element: HTMLElement, run: Run): void {
    element.style.left = `${run.left}px`;
    element.style.top = `${run.top}px`;
    element.style.width = `${Math.max(4, run.right - run.left)}px`;
    element.style.height = `${Math.max(4, run.bottom - run.top)}px`;
}
