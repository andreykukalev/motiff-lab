// The selection on the score (decision record 0018, section 3; design/kit/ds.css .band, .handle, .menu): a cobalt
// band over the selected bars on each system they touch, round handles on the first and last barline that drag
// the edges from barline to barline (selection-handles.ts), and the contextual menu placed above the band, which the
// player can move aside (menu-drag.ts). The band and the handles are drawn in the score's content pixels, as the
// brackets are; the menu belongs to Blazor and is only placed here. While the place rule is off a press goes to the
// selection gesture of version 0.6.1 (selection-press.ts), and the band shows what it would select.
// Selection by beats (#349): selection-beats.ts. Where the menu goes: selection-menu.ts.

import { BRACKET_LIFT, type ScoreGeometry } from "./score-geometry.js";
import { NUMBER_HEIGHT, menuPlace } from "./selection-menu.js";
import { MenuDragger, menuRoom } from "./menu-drag.js";
import { BEAT_CLASS, type BeatSource, ScoreBeats } from "./selection-beats.js";
import { HANDLE_CLASS, SelectionHandles } from "./selection-handles.js";
import type { InteractionMode } from "./score-modes.js";
import { BAND_CLASS, type BandHand, handRuns, setBox } from "./selection-boxes.js";
import { type DragStart, type SelectionChange, type TickRange, handlesShown } from "../selection/selection-gesture.js";
import { SelectionPress } from "./selection-press.js";

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
    /** Whether the place rule takes the presses now (v0.6.2, the iPad's switch in Select; selection-place-layer.ts): no dots. */
    places?(): boolean;
}

/** The selection as Blazor gives it: its span in ticks and what a screen reader says for each handle. */
export interface ScoreSelection {
    start: number;
    end: number;
    startLabel: string;
    endLabel: string;
    /** The hand the selection is narrowed to (v0.6.2): its band covers that staff only. */
    hand?: BandHand;
}

export class SelectionLayer {
    private selection: ScoreSelection | null = null;
    private menu: HTMLElement | null = null;
    /** What pressing and dragging the handles does (selection-handles.ts). */
    private readonly handles: SelectionHandles;
    /** What a press on the score does while the place rule is off (selection-press.ts). */
    private readonly press: SelectionPress;
    /** The menu's size changes with what it holds; it is placed again then. */
    private readonly menuSize = new ResizeObserver(() => this.placeMenu());
    /** The menu dragged aside by the player (#582 item 7), and where it went until it closes. */
    private readonly menuDrag = new MenuDragger(() => this.placeMenu());
    /** Selecting by beats, while the score view gives beat lines. */
    readonly beats: ScoreBeats;
    /** What the place rule's gesture shows in place of the selection (selection-place-layer.ts); null for none. */
    private placed: (TickRange & { hand: BandHand }) | null = null;

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
        this.press = new SelectionPress(host, {
            owner,
            beats: this.beats,
            handles: this.handles,
            selection: () => this.selection,
            point: (event) => this.point(event),
            capturing: () => this.capturing(),
            place: () => this.place(),
        });
    }

    /** Shows the selection, or none; with none the menu closes, and a place the player gave it is forgotten. */
    setSelection(selection: ScoreSelection | null): void {
        this.selection = selection;
        if (selection === null) {
            this.menuDrag.pin.reset();
        }
        this.place();
    }

    /** The menu Blazor draws for the selection; the layer places it over the band, and the player can drag it aside. */
    setMenu(menu: HTMLElement | null): void {
        this.menuSize.disconnect();
        this.menu = menu;
        this.menuDrag.attach(menu);
        if (menu !== null) {
            this.menuSize.observe(menu);
        }
        this.placeMenu();
    }

    /** The menu was put away (the iPad's, drawn by Swift): the next one opens over its band (#582 item 7). */
    menuClosed(): void {
        this.menuDrag.pin.reset();
        this.placeMenu();
    }

    /** The menu dragged by (dx, dy) from where it stands (the iPad's, whose drag Swift follows), kept inside the window. */
    moveMenuBy(dx: number, dy: number): void {
        this.menuDrag.moveBy(dx, dy);
    }

    /** Whether the click the browser sends now ends a drag, and is not a click on the score. */
    swallowsClick(): boolean {
        return this.press.swallowsClick();
    }

    /** The place rule's gesture shows this passage instead of the selection, or (null) the selection again. */
    showPlaced(range: (TickRange & { hand: BandHand }) | null): void {
        this.placed = range;
        this.place();
    }

    /** Draws the band and the handles again where the bars are now, after a new layout. */
    place(): void {
        const geometry = this.owner.geometry();
        const selection = this.selection;
        const preview = geometry === null ? null : this.preview();
        if (geometry !== null && preview !== null) {
            this.draw(geometry, preview.start, preview.end, null, preview.hand);
            this.placeMenu();
            return;
        }
        if (geometry === null || selection === null || this.press.cleared()) {
            this.clear();
            this.placeMenu();
            return;
        }
        this.draw(geometry, selection.start, selection.end, selection, selection.hand);
        this.placeMenu();
    }

    /** What a gesture under way shows instead of the selection: a handle's or an edge's passage, or a new one; null for none. */
    private preview(): (TickRange & { hand?: BandHand }) | null {
        if (this.placed !== null) {
            return this.placed;
        }
        const handle = this.handles.drag;
        if (handle !== null) {
            return { start: handle.start, end: handle.end };
        }
        return this.press.preview();
    }

    /** Moves the menu with the band as the score scrolls; hidden while the band is out of sight or a drag is on. */
    placeMenu(): void {
        const menu = this.menu;
        if (menu === null) {
            return;
        }
        const bands = this.host.querySelectorAll<HTMLElement>(`:scope > .${BAND_CLASS}`);
        const dragging = this.capturing();
        if (this.selection !== null && !dragging && this.menuDrag.placePinned()) {
            return;
        }
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
        const place = menuPlace({ left: first.left, right: first.right, top }, last, size, menuRoom());
        menu.style.left = `${place.left}px`;
        menu.style.top = `${place.top}px`;
        menu.style.setProperty("--pointer", `${place.pointer}px`);
        menu.dataset.below = place.below ? "true" : "false";
    }

    dispose(): void {
        this.menuDrag.attach(null);
        this.press.dispose();
        this.menuSize.disconnect();
        this.handles.abandon();
        this.clear();
    }

    private clear(): void {
        for (const old of this.host.querySelectorAll(`:scope > .${BAND_CLASS}, :scope > .${HANDLE_CLASS}, :scope > .${BEAT_CLASS}`)) {
            old.remove();
        }
    }

    /** The band over [start, end), and the handles when `selection` gives their names (a drag in progress shows the band alone). */
    private draw(geometry: ScoreGeometry, start: number, end: number, selection: ScoreSelection | null, hand?: BandHand): void {
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
        const runs = handRuns(geometry, start, end, hand);
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
        if (selection !== null && handlesShown(this.owner.mode?.()) && this.owner.places?.() !== true) {
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

    /** Whether a gesture holds the pointer now: the place rule's, a handle's, or a press that moves an edge, makes a new selection or has cleared it. */
    private capturing(): boolean {
        return this.placed !== null || this.handles.drag !== null || this.press.holds();
    }
}
