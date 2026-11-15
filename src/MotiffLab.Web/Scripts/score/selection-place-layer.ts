// Selection by the place of the touch on the score (v0.6.2, #601, the selection study's section D; the experiment
// `experimental.placeSelection`, which only the iPad passes, in Select). The rule is selection-places.ts; this layer
// takes the presses while the host says the rule is on, shows what a gesture would do and hands the result to the owner:
// an edge's grip moves its edge, with pale target ticks where it can stop; a move from inside extends the selection, the
// addition dashed; a move from outside draws a new passage (by beats, or bars), narrowed to a hand when the stroke stays
// on one staff, the old selection dashed until it is let go; a tap selects the bar or the note head under it, a tap on
// the page's margin clears, a double tap inside zooms to the selection. Near the top or the bottom of the view the page
// scrolls by itself; a finger beside the stylus scrolls freely meanwhile. The band itself is the selection layer's
// (`showPlaced`); the edges here replace its dot handles.

import type { ScoreBar, ScoreGeometry } from "./score-geometry.js";
import type { ScoreBeats } from "./selection-beats.js";
import { keyLines } from "./selection-beats.js";
import { type BandHand, bandBoxes, handRuns, onMusic, setBox } from "./selection-boxes.js";
import { HOLD_MS, type SelectionChange, type TickRange, extendedRange, nextGestureId, staleGesture } from "./selection-gesture.js";
import { barlines, tickAt } from "./selection-handles.js";
import {
    type PlaceEnd,
    type PlaceGesture,
    type PlaceRule,
    type Tap,
    doubleTap,
    drawnRange,
    headAt,
    movedEdge,
    placeAt,
    placeChange,
    placeEnded,
    placeHeld,
    placeMoved,
    placePress,
    scrollStep,
    strokeHand,
    targetLines,
} from "./selection-places.js";

/** A passage with the hand it is narrowed to. */
export interface PlacedRange extends TickRange {
    hand: BandHand;
}

/** What the layer needs from the page. */
export interface PlaceHost {
    geometry(): ScoreGeometry | null;
    readonly beats: ScoreBeats;
    /** Whether the rule takes the presses now: the switch on, in Select, not in focus. */
    on(): boolean;
    /** How a press of this pointer starts a selection: at once, a finger beside the stylus after the hold, or not. */
    rule(pointerType: string): PlaceRule | "none";
    /** The selection now; null for none. */
    selection(): PlacedRange | null;
    /** What the band shows while a gesture runs; null for the selection again. */
    show(range: PlacedRange | null): void;
    /** A passage chosen (an edge moved, extended, drawn, a bar), and how the selection changed. */
    chosen(range: PlacedRange, how: SelectionChange, gesture: string): void;
    /** A note head tapped: the note's engraved id. */
    picked(id: string, how: SelectionChange, gesture: string): void;
    /** A tap on the page's margin took the selection away. */
    cleared(gesture: string): void;
    /** A double tap inside the selection: zoom to it, or back. */
    zoom(): void;
}

interface PlaceDrag {
    pointerId: number;
    pointerType: string;
    /** Where the press began, in content pixels, and where the pointer is now in the window. */
    x: number;
    y: number;
    clientX: number;
    clientY: number;
    gesture: PlaceGesture;
    id: string;
    selection: PlacedRange | null;
    pressTick: number;
    bar: ScoreBar | undefined;
    /** What the gesture would select now; null while it shows nothing new. */
    preview: PlacedRange | null;
    timer: number;
}

const GHOST_CLASS = "sel-ghost";
const ADD_CLASS = "sel-add";
const EDGE_CLASS = "sel-edge";
const TARGET_CLASS = "sel-target";
const OVERLAYS = [GHOST_CLASS, ADD_CLASS, TARGET_CLASS];

export class PlaceLayer {
    private drag: PlaceDrag | null = null;
    private lastTap: Tap | null = null;
    private swallowNextClick = false;
    private scrolling = 0;

    constructor(
        private readonly root: HTMLElement,
        private readonly host: PlaceHost,
    ) {
        root.addEventListener("pointerdown", this.onDown);
        root.addEventListener("pointermove", this.onMove);
        root.addEventListener("pointerup", this.onUp);
        root.addEventListener("pointercancel", this.onCancel);
        root.addEventListener("touchmove", this.onTouchMove, { passive: false });
        window.addEventListener("scroll", this.onScroll, { passive: true });
    }

    /** Whether the click the browser sends now ends a gesture of this layer, and is no tap on the page. */
    swallowsClick(): boolean {
        const swallow = this.swallowNextClick;
        this.swallowNextClick = false;
        return swallow;
    }

    /** Whether a gesture of this layer holds the pointer now. */
    active(): boolean {
        const phase = this.drag?.gesture.phase;
        return phase === "edge" || phase === "extend" || phase === "draw" || phase === "bar";
    }

    /**
     * Draws the selection's edges over the band the selection layer drew (section D, Кромки): a rule the band's full height
     * at its start and its end, with a circle at the top of the start and at the bottom of the end; none while the rule is
     * off, or while a new passage is drawn.
     */
    decorate(): void {
        for (const old of this.root.querySelectorAll(`:scope > .${EDGE_CLASS}`)) {
            old.remove();
        }
        const boxes = bandBoxes(this.root);
        if (!this.host.on() || boxes.length === 0 || this.drag?.gesture.phase === "draw") {
            return;
        }
        const first = boxes[0];
        const last = boxes[boxes.length - 1];
        const hot = this.active() ? this.drag?.gesture.place : undefined;
        this.root.append(this.edge("start", first.left, first.top, first.bottom, hot === "edge-start"));
        this.root.append(this.edge("end", last.right, last.top, last.bottom, hot === "edge-end"));
    }

    dispose(): void {
        this.root.removeEventListener("pointerdown", this.onDown);
        this.root.removeEventListener("pointermove", this.onMove);
        this.root.removeEventListener("pointerup", this.onUp);
        this.root.removeEventListener("pointercancel", this.onCancel);
        this.root.removeEventListener("touchmove", this.onTouchMove);
        window.removeEventListener("scroll", this.onScroll);
        this.end();
    }

    private edge(which: "start" | "end", x: number, top: number, bottom: number, hot: boolean): HTMLElement {
        const edge = document.createElement("div");
        edge.className = `${EDGE_CLASS} ${which}${hot ? " hot" : ""}`;
        edge.setAttribute("aria-hidden", "true");
        edge.style.left = `${x}px`;
        edge.style.top = `${top}px`;
        edge.style.height = `${Math.max(4, bottom - top)}px`;
        return edge;
    }

    private point(clientX: number, clientY: number): { x: number; y: number } {
        const view = this.root.getBoundingClientRect();
        return { x: clientX - view.left + this.root.scrollLeft, y: clientY - view.top + this.root.scrollTop };
    }

    /** The lines an edge stops at: the beats and the barlines while selecting by beats, else the barlines. */
    private lines(geometry: ScoreGeometry): number[] {
        const beats = this.host.beats.lines();
        return beats !== null ? keyLines(beats, geometry.bars) : barlines(geometry.bars);
    }

    private readonly onDown = (event: PointerEvent): void => {
        if (this.drag !== null && staleGesture(this.drag, event)) {
            this.cancel();
        }
        const rule = this.host.rule(event.pointerType);
        const geometry = this.host.geometry();
        if (this.drag !== null || !this.host.on() || rule === "none" || geometry === null || (event.pointerType === "mouse" && event.button !== 0)) {
            return;
        }
        if (event.target instanceof Element && event.target.closest(".bracket, .grip, .marker, .bmb, .perf-marker") !== null) {
            return;
        }
        const at = this.point(event.clientX, event.clientY);
        const selection = this.host.selection();
        const place = placeAt(selection, at.x, at.y, bandBoxes(this.root), onMusic(geometry, at.x, at.y));
        const drag: PlaceDrag = {
            pointerId: event.pointerId, pointerType: event.pointerType, x: at.x, y: at.y, clientX: event.clientX, clientY: event.clientY,
            gesture: placePress(place, rule), id: nextGestureId(), selection, pressTick: tickAt(geometry, at.x, at.y), bar: geometry.barNear(at.x, at.y), preview: null, timer: 0,
        };
        if (rule === "longPress") {
            drag.timer = window.setTimeout(() => this.step(drag, placeHeld(drag.gesture)), HOLD_MS);
        }
        this.drag = drag;
    };

    private readonly onMove = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointerId) {
            return;
        }
        drag.clientX = event.clientX;
        drag.clientY = event.clientY;
        const at = this.point(event.clientX, event.clientY);
        this.step(drag, placeMoved(drag.gesture, Math.hypot(at.x - drag.x, at.y - drag.y)));
        if (this.drag === drag && this.active()) {
            this.follow(drag);
            this.autoScroll();
        }
    };

    /** Does what a step of the gesture asks: a finger that scrolls lets go; a gesture that takes the pointer holds it. */
    private step(drag: PlaceDrag, step: ReturnType<typeof placeMoved>): void {
        if (this.drag !== drag || step === "none") {
            return;
        }
        if (step === "pass") {
            this.end();
            return;
        }
        clearTimeout(drag.timer);
        try {
            this.root.setPointerCapture(drag.pointerId);
        } catch {
            // The pointer is gone already; the gesture ends with it.
        }
        this.follow(drag);
    }

    /** What the gesture would select with the pointer where it is now, shown on the score. */
    private follow(drag: PlaceDrag): void {
        const geometry = this.host.geometry();
        if (geometry === null) {
            return;
        }
        const at = this.point(drag.clientX, drag.clientY);
        drag.preview = this.previewAt(geometry, drag, at);
        this.host.show(drag.gesture.phase === "extend" ? null : drag.preview);
        this.drawOverlays(geometry, drag, at);
    }

    private previewAt(geometry: ScoreGeometry, drag: PlaceDrag, at: { x: number; y: number }): PlacedRange | null {
        const lines = this.lines(geometry);
        const tick = tickAt(geometry, at.x, at.y);
        const selection = drag.selection;
        switch (drag.gesture.phase) {
            case "edge":
                return selection === null ? null : { ...movedEdge(selection, drag.gesture.place === "edge-start" ? "start" : "end", drag.pressTick, tick, lines), hand: selection.hand };
            case "extend":
                return selection === null ? null : { ...extendedRange(selection, tick, lines), hand: selection.hand };
            case "draw": {
                const drawn = drawnRange(drag.pressTick, tick, lines);
                const now = geometry.barNear(at.x, at.y);
                const hand = drag.bar === undefined ? null : strokeHand(drag.bar.staves, drag.y, at.y, now?.system === drag.bar.system);
                return drawn === undefined ? drag.preview : { ...drawn, hand };
            }
            case "bar":
                return this.heldBar(drag, drawnRange(drag.pressTick, tick, lines));
            default:
                return null;
        }
    }

    /** A finger held on a bar selects that bar; a move after the hold takes it on to the line past the pointer. */
    private heldBar(drag: PlaceDrag, drawn: TickRange | undefined): PlacedRange | null {
        const bar = drag.bar;
        if (bar === undefined || !Number.isFinite(bar.end)) {
            return null;
        }
        const start = Math.min(bar.tick, drawn?.start ?? bar.tick);
        const end = Math.max(bar.end, drawn?.end ?? bar.end);
        return { start, end, hand: null };
    }

    /** The old selection dashed while a new one is drawn, the addition dashed while extending, the targets while an edge moves. */
    private drawOverlays(geometry: ScoreGeometry, drag: PlaceDrag, at: { x: number; y: number }): void {
        this.clearOverlays();
        const selection = drag.selection;
        const preview = drag.preview;
        const phase = drag.gesture.phase;
        if (selection !== null && (phase === "draw" || phase === "bar")) {
            this.boxes(GHOST_CLASS, handRuns(geometry, selection.start, selection.end, selection.hand));
        }
        if (selection !== null && preview !== null && phase === "extend") {
            this.boxes(ADD_CLASS, [
                ...(preview.start < selection.start ? handRuns(geometry, preview.start, selection.start, selection.hand) : []),
                ...(preview.end > selection.end ? handRuns(geometry, selection.end, preview.end, selection.hand) : []),
            ]);
        }
        if (preview !== null && phase === "edge") {
            this.targets(geometry, targetLines(preview, this.lines(geometry)), at);
        }
        this.decorate();
    }

    private boxes(className: string, runs: readonly { left: number; right: number; top: number; bottom: number }[]): void {
        for (const run of runs) {
            const box = document.createElement("div");
            box.className = className;
            box.setAttribute("aria-hidden", "true");
            setBox(box, run);
            this.root.append(box);
        }
    }

    /** Pale ticks on the system under the pointer, at each line the moving edge can stop at. */
    private targets(geometry: ScoreGeometry, lines: readonly number[], at: { x: number; y: number }): void {
        const system = geometry.barNear(at.x, at.y)?.system;
        for (const bar of geometry.bars) {
            if (bar.system !== system) {
                continue;
            }
            for (const line of lines) {
                if (line < bar.tick || line >= bar.end) {
                    continue;
                }
                const tick = document.createElement("div");
                tick.className = TARGET_CLASS;
                tick.setAttribute("aria-hidden", "true");
                tick.style.left = `${this.host.beats.lineX(bar, line)}px`;
                tick.style.top = `${bar.top}px`;
                this.root.append(tick);
            }
        }
    }

    private clearOverlays(): void {
        for (const old of this.root.querySelectorAll(OVERLAYS.map((name) => `:scope > .${name}`).join(", "))) {
            old.remove();
        }
    }

    /** While a gesture holds the pointer near the top or the bottom of the view, the page scrolls a step a frame. */
    private autoScroll(): void {
        if (this.scrolling !== 0) {
            return;
        }
        const frame = (): void => {
            const drag = this.drag;
            const step = drag === null || !this.active() ? 0 : scrollStep(drag.clientY, 0, window.innerHeight);
            if (step === 0) {
                this.scrolling = 0;
                return;
            }
            window.scrollBy(0, step);
            this.scrolling = window.requestAnimationFrame(frame);
        };
        this.scrolling = window.requestAnimationFrame(frame);
    }

    /** The page moved under a pointer held still (a finger scrolls, or the page scrolls by itself): the preview follows. */
    private readonly onScroll = (): void => {
        if (this.drag !== null && this.active()) {
            this.follow(this.drag);
        }
    };

    private readonly onUp = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointerId) {
            return;
        }
        const end = placeEnded(drag.gesture);
        // A passage chosen stays shown until the owner's selection replaces it (`settled`), so the band never jumps back.
        const keep = (end === "edge" || end === "extend" || end === "draw" || end === "bar") && drag.preview !== null && !same(drag.preview, drag.selection);
        this.end(keep ? drag.preview : null);
        if (drag.gesture.rule === "now" || end !== "none") {
            // A stylus' tap is this layer's: it never places the playhead too.
            this.swallowNextClick = true;
            window.setTimeout(() => (this.swallowNextClick = false), 0);
        }
        this.finish(drag, end, this.point(event.clientX, event.clientY), event.timeStamp);
    };

    /** Hands what the gesture did to the owner (section F, Отпускание). */
    private finish(drag: PlaceDrag, end: PlaceEnd, at: { x: number; y: number }, time: number): void {
        const how = placeChange(end, drag.selection !== null);
        const preview = drag.preview;
        switch (end) {
            case "edge":
            case "extend":
            case "draw":
            case "bar":
                if (preview !== null && !same(preview, drag.selection)) {
                    this.host.chosen(preview, how, drag.id);
                }
                return;
            case "tap":
                this.tap(at, how, drag.id);
                return;
            case "clear":
                if (how === "clear") {
                    this.host.cleared(drag.id);
                }
                return;
            case "tapInside": {
                const now = { time, x: at.x, y: at.y };
                const twice = doubleTap(this.lastTap, now);
                this.lastTap = twice ? null : now;
                if (twice) {
                    this.host.zoom();
                }
                return;
            }
            default:
        }
    }

    /** A tap outside the selection: the note head under it, else the bar. */
    private tap(at: { x: number; y: number }, how: SelectionChange, gesture: string): void {
        const geometry = this.host.geometry();
        if (geometry === null) {
            return;
        }
        const head = headAt(geometry.notes.map((note) => ({ id: note.id, x: note.cx, y: note.cy })), at.x, at.y);
        if (head !== undefined) {
            this.host.picked(head.id, how, gesture);
            return;
        }
        const bar = geometry.barNear(at.x, at.y);
        if (bar !== undefined && Number.isFinite(bar.end)) {
            this.host.chosen({ start: bar.tick, end: bar.end, hand: null }, how, gesture);
        }
    }

    private readonly onCancel = (event: PointerEvent): void => {
        if (this.drag !== null && event.pointerId === this.drag.pointerId) {
            this.cancel();
        }
    };

    /** The gesture ends with no change: the selection shows again as it was. */
    private cancel(): void {
        this.end();
    }

    private end(shown: PlacedRange | null = null): void {
        const drag = this.drag;
        if (drag !== null) {
            clearTimeout(drag.timer);
        }
        this.drag = null;
        window.cancelAnimationFrame(this.scrolling);
        this.scrolling = 0;
        this.clearOverlays();
        this.host.show(shown);
    }

    /** A finger that moves the selection does not move the page; the stylus' moves never would, so a finger may scroll beside it. */
    private readonly onTouchMove = (event: TouchEvent): void => {
        if (this.active() && this.drag?.pointerType === "touch") {
            event.preventDefault();
        }
    };
}

function same(a: PlacedRange, b: PlacedRange | null): boolean {
    return b !== null && a.start === b.start && a.end === b.end && (a.hand ?? null) === (b.hand ?? null);
}

