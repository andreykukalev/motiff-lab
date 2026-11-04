// The selection on the score (decision record 0018, section 3; design/kit/ds.css .band, .handle, .menu): a cobalt
// band over the selected bars on each system they touch, round handles on the first and last barline that drag
// the edges from barline to barline, a drag across bars with the mouse (or a long press, then a drag, with a
// finger) that selects them, and the contextual menu placed above the band. The band and the handles are drawn
// in the score's content pixels, as the brackets are; the menu belongs to Blazor and is only placed here.

import { BRACKET_LIFT, type Run, type ScoreBar, type ScoreGeometry } from "./score-geometry.js";

/** How far the mouse moves before a press becomes a drag across bars, in pixels. */
export const DRAG_DISTANCE = 6;

/** How long a finger rests on a bar before it selects it and can drag on, in milliseconds. */
export const LONG_PRESS_MS = 450;

/** How far a finger may move during the long press before the press is a scroll instead, in pixels. */
const LONG_PRESS_SLOP = 8;

/** The menu sits this far above the band's bracket number (the kit: 14 px above the selection, clear of bracket numbers). */
const MENU_GAP = 14;

/** The bracket's number stands this tall above the bracket line. */
const NUMBER_HEIGHT = 16;

/** The menu stays this far inside the window's sides, and below the bar at the top. */
const MENU_MARGIN = 16;
const MENU_TOP = 72;

const BAND_CLASS = "sel-band";
const HANDLE_CLASS = "sel-handle";

/** What the layer needs from the score view. */
export interface SelectionHost {
    geometry(): ScoreGeometry | null;
    /** Whether the pen takes the pointer now: then the mouse and a finger write rather than select. */
    penOn(): boolean;
    /** Bars chosen by a drag across the score, or by moving a handle: where the first and the last bar start. */
    chosen(first: number, last: number, byHandle: boolean): void;
    /** The top of the marks drawn just above a band's first run (the bookmarks), in window pixels, for the menu to clear; null for none. */
    marksTop?(band: { left: number; right: number; top: number }): number | null;
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

/**
 * Where the menu goes for a band (all in window pixels): centred over the band's first run, above its
 * bracket number, or under the band's last run when there is no room above; kept inside the window, and right of
 * `window.left` when a drawer covers the left of it.
 * `pointer` is where its little arrow sits across the menu.
 */
export function menuPlace(
    first: { left: number; right: number; top: number },
    last: { bottom: number },
    menu: { width: number; height: number },
    window: { width: number; height: number; left?: number },
): { left: number; top: number; below: boolean; pointer: number } {
    const centre = (first.left + first.right) / 2;
    const start = (window.left ?? 0) + MENU_MARGIN;
    const left = Math.max(start, Math.min(window.width - MENU_MARGIN - menu.width, centre - menu.width / 2));
    const above = first.top - BRACKET_LIFT - NUMBER_HEIGHT - MENU_GAP - menu.height;
    const below = above < MENU_TOP;
    const top = below ? last.bottom + MENU_GAP : above;
    const pointer = Math.max(20, Math.min(menu.width - 20, centre - left));
    return { left, top, below, pointer };
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
    timer: number;
}

interface HandleDrag {
    pointerId: number;
    edge: "start" | "end";
    first: ScoreBar;
    last: ScoreBar;
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

    constructor(
        private readonly host: HTMLElement,
        private readonly owner: SelectionHost,
    ) {
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
            this.draw(geometry, handle.first.tick, handle.last.end, null);
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
        // A drawer over the left of the page (the Contents) keeps the menu to its right.
        const drawers = [...document.querySelectorAll<HTMLElement>("[data-menu-clear='left']")].map((drawer) => drawer.getBoundingClientRect().right);
        const clear = Math.max(0, ...drawers);
        const place = menuPlace({ left: first.left, right: first.right, top }, last, size, { width: window.innerWidth, height: window.innerHeight, left: clear });
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
        for (const old of this.host.querySelectorAll(`:scope > .${BAND_CLASS}, :scope > .${HANDLE_CLASS}`)) {
            old.remove();
        }
    }

    /** The band over [start, end), and the handles when `selection` gives their names (a drag in progress shows the band alone). */
    private draw(geometry: ScoreGeometry, start: number, end: number, selection: ScoreSelection | null): void {
        // A handle being dragged keeps its element, and with it the pointer capture.
        const kept = this.handleDrag !== null ? [...this.host.querySelectorAll<HTMLElement>(`:scope > .${HANDLE_CLASS}`)] : [];
        for (const old of this.host.querySelectorAll(`:scope > .${BAND_CLASS}`)) {
            old.remove();
        }
        if (this.handleDrag === null) {
            for (const old of this.host.querySelectorAll(`:scope > .${HANDLE_CLASS}`)) {
                old.remove();
            }
        }
        const runs = geometry.band(start, end);
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
        const drag: Drag = { pointerId: event.pointerId, x: at.x, y: at.y, touch, anchor: null, current: null, timer: 0 };
        if (touch) {
            drag.timer = window.setTimeout(() => this.startDrag(drag), LONG_PRESS_MS);
        }
        this.drag = drag;
    };

    private startDrag(drag: Drag): void {
        const geometry = this.owner.geometry();
        const bar = geometry?.barNear(drag.x, drag.y);
        if (this.drag !== drag || geometry == null || bar === undefined) {
            return;
        }
        drag.anchor = bar;
        drag.current = bar;
        try {
            this.host.setPointerCapture(drag.pointerId);
        } catch {
            // The pointer is gone already; the drag ends with it.
        }
        this.preview(geometry, bar, bar);
    }

    private preview(geometry: ScoreGeometry, a: ScoreBar, b: ScoreBar): void {
        const [first, last] = ordered(a, b);
        this.draw(geometry, first.tick, last.end, null);
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
        const bar = geometry?.barNear(at.x, at.y);
        if (geometry == null || bar === undefined || drag.anchor === null || bar === drag.current) {
            return;
        }
        drag.current = bar;
        this.preview(geometry, drag.anchor, bar);
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
        if (bars === null || (event.pointerType !== "touch" && event.button !== 0)) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        handle.setPointerCapture(event.pointerId);
        const drag: HandleDrag = { pointerId: event.pointerId, edge: handle.dataset.edge === "start" ? "start" : "end", first: bars[0], last: bars[1] };
        this.handleDrag = drag;
        handle.classList.add("dragging");
        this.placeMenu();
        const move = (moveEvent: PointerEvent): void => {
            if (moveEvent.pointerId !== drag.pointerId) {
                return;
            }
            const now = this.owner.geometry();
            const at = this.point(moveEvent);
            const bar = now?.edgeNear(at.x, at.y, drag.edge);
            if (now == null || bar === undefined) {
                return;
            }
            // An edge never passes the other one: the selection keeps at least a bar.
            if (drag.edge === "start") {
                drag.first = bar.tick <= drag.last.tick ? bar : drag.last;
            } else {
                drag.last = bar.tick >= drag.first.tick ? bar : drag.first;
            }
            this.draw(now, drag.first.tick, drag.last.end, null);
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
            const changed = drag.first !== bars[0] || drag.last !== bars[1];
            if (changed && endEvent.type === "pointerup") {
                this.owner.chosen(drag.first.tick, drag.last.tick, true);
            } else {
                this.place();
            }
        };
        handle.addEventListener("pointermove", move);
        handle.addEventListener("pointerup", end);
        handle.addEventListener("pointercancel", end);
    };

    /** The arrow keys move a handle's edge a bar at a time, as a drag does (WCAG 2.1.1). */
    private readonly onHandleKey = (event: KeyboardEvent): void => {
        const step = event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0;
        const geometry = this.owner.geometry();
        const bars = geometry === null ? null : this.selectedBars(geometry);
        if (step === 0 || geometry === null || bars === null) {
            return;
        }
        event.preventDefault();
        const inTime = uniqueInTime(geometry.bars);
        const edge = (event.currentTarget as HTMLElement).dataset.edge;
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
