// Selection by the place of the touch on the score (v0.6.2, #601, the selection study's section D; once the experiment
// `experimental.placeSelection`; only the iPad turns it on, in Select). The rule is selection-places.ts; this layer
// takes the presses while the host says the rule is on, shows what a gesture would do and hands the result to the owner:
// an edge's grip moves its edge, with pale target ticks where it can stop; a move from inside extends the selection, the
// addition dashed; a move from outside draws a new passage (by beats, or bars), narrowed to a hand when the stroke stays
// on one staff, the old selection dashed until it is let go; a tap selects the bar or the note head under it, a tap on
// the page's margin clears, a double tap inside zooms to the selection. Near the top or the bottom of the view the page
// scrolls by itself; a finger beside the stylus scrolls freely meanwhile. The band itself is the selection layer's
// (`showPlaced`); the edges here replace its dot handles. Since v0.6.3 (#608, the approved mockup) a new passage is drawn
// with a thin dashed rectangle from the press to the stylus, from anywhere outside the selection, the margin included, and
// selects what the rectangle covers (selection-rect.ts); the stylus never moves the page while the rule is on.

import type { ScoreBar, ScoreGeometry } from "./score-geometry.js";
import type { ScoreBeats } from "./selection-beats.js";
import { barLines, keyLines } from "./selection-beats.js";
import { type BandHand, MUSIC_REACH, bandBoxes, handRuns, onMusic, setBox } from "./selection-boxes.js";
import { HOLD_MS, type SelectionChange, type TickRange, extendedRange, nextGestureId, staleGesture } from "./selection-gesture.js";
import { barlines, tickAt } from "./selection-handles.js";
import {
    type EdgeHandle,
    type PlaceEnd,
    type PlaceGesture,
    type PlaceRule,
    type Tap,
    doubleTap,
    edgeHandles,
    headAt,
    movedEdge,
    onEdgeHandle,
    placeAt,
    placeChange,
    placeEnded,
    placeHeld,
    placeMoved,
    placePress,
    scrollStep,
    targetLines,
} from "./selection-places.js";
import { type RectSystem, rectPassage, traceBox } from "./selection-rect.js";

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
    /** The part of the window the score shows, top to bottom in window pixels, for the autoscroll; the whole window without it. */
    view?(): { top: number; bottom: number };
    /**
     * The lines the edges stop at (v0.6.4, selection-grid.ts: every sixteenth from each bar's start, a tuplet one block);
     * without it, or while it gives none, the beats and the barlines while selecting by beats, else the barlines.
     */
    grid?(geometry: ScoreGeometry): readonly number[] | null;
}

interface PlaceDrag {
    pointerId: number;
    pointerType: string;
    /** Where the press began, in content pixels and in the window, and where the pointer is now in the window. */
    x: number;
    y: number;
    pressY: number;
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
const TRACE_CLASS = "sel-trace";
const OVERLAYS = [GHOST_CLASS, ADD_CLASS, TARGET_CLASS, TRACE_CLASS];

/** What is pressed for its own sake, never a place of the selection: a bracket, a margin note, a bookmark, a version's label, a Check flag. */
const OWN_TARGETS = ".bracket, .marker, .bmb, .perf-marker, .check-flag";

export class PlaceLayer {
    private drag: PlaceDrag | null = null;
    /**
     * A passage let go and handed to the owner, until the owner's selection comes back (`settle`): the band shows it, and a
     * press meanwhile reads it as the selection, so an edge taken again at once is the edge where it now stands (v0.6.4).
     */
    private placed: PlacedRange | null = null;
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
        root.addEventListener("touchstart", this.onTouchStart, { passive: false });
        root.addEventListener("touchmove", this.onTouchMove, { passive: false });
        window.addEventListener("scroll", this.onScroll, { passive: true });
    }

    /** The owner's selection arrived (or was put back): the passage let go is no longer read in its place. */
    settle(): void {
        this.placed = null;
    }

    /** The selection a press reads: the passage just let go while the owner has not answered yet, else the owner's. */
    private selection(): PlacedRange | null {
        return this.placed ?? this.host.selection();
    }

    /** Whether the click the browser sends now ends a gesture of this layer, and is no tap on the page. */
    swallowsClick(): boolean {
        const swallow = this.swallowNextClick;
        this.swallowNextClick = false;
        return swallow;
    }

    /**
     * Whether a press at a point of the window lands on a handle of the selection's edges (`onEdgeHandle`): there the edge
     * wins over a playhead's handle under it, so the host leaves the press to this layer; elsewhere on the edge the
     * playhead is taken first (Andrei, 2026-11-18, answer 3 on finding 1 of the 0.8 test; before, the whole edge won).
     */
    handleAt(event: PointerEvent): boolean {
        if (!this.host.on() || this.host.rule(event.pointerType) !== "now" || this.selection() === null) {
            return false;
        }
        const at = this.point(event.clientX, event.clientY);
        return onEdgeHandle(edgeHandles(bandBoxes(this.root)), at.x, at.y);
    }

    /** Whether a gesture of this layer holds the pointer now. */
    active(): boolean {
        const phase = this.drag?.gesture.phase;
        return phase === "edge" || phase === "extend" || phase === "draw" || phase === "bar";
    }

    /**
     * Draws the selection's edges over the band the selection layer drew: the band's full height at its start and its end
     * (where a press takes the edge), each with the kit's hollow handle at its middle (v0.6.4, Andrei's spec 6: the handles
     * of before, score.css `.sel-edge`); none while the rule is off, or while a new passage is drawn.
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
        const [start, end] = edgeHandles(boxes);
        const hot = this.active() ? this.drag?.gesture.place : undefined;
        this.root.append(this.edge("start", start, first.top, first.bottom, hot === "edge-start"));
        this.root.append(this.edge("end", end, last.top, last.bottom, hot === "edge-end"));
    }

    dispose(): void {
        this.root.removeEventListener("pointerdown", this.onDown);
        this.root.removeEventListener("pointermove", this.onMove);
        this.root.removeEventListener("pointerup", this.onUp);
        this.root.removeEventListener("pointercancel", this.onCancel);
        this.root.removeEventListener("touchstart", this.onTouchStart);
        this.root.removeEventListener("touchmove", this.onTouchMove);
        window.removeEventListener("scroll", this.onScroll);
        this.end();
    }

    /** An edge's line from `top` to `bottom`, its circle at `handle`: where `handleAt` takes a press on it. */
    private edge(which: "start" | "end", handle: EdgeHandle, top: number, bottom: number, hot: boolean): HTMLElement {
        const edge = document.createElement("div");
        edge.className = `${EDGE_CLASS} ${which}${hot ? " hot" : ""}`;
        edge.setAttribute("aria-hidden", "true");
        edge.style.left = `${handle.x}px`;
        edge.style.top = `${top}px`;
        edge.style.height = `${Math.max(4, bottom - top)}px`;
        edge.style.setProperty("--handle-y", `${handle.y - top}px`);
        return edge;
    }

    private point(clientX: number, clientY: number): { x: number; y: number } {
        const view = this.root.getBoundingClientRect();
        return { x: clientX - view.left + this.root.scrollLeft, y: clientY - view.top + this.root.scrollTop };
    }

    /** The lines an edge stops at: the host's grid; else the beats and the barlines while selecting by beats, else the barlines. */
    private lines(geometry: ScoreGeometry): number[] {
        const grid = this.host.grid?.(geometry) ?? null;
        if (grid !== null && grid.length > 0) {
            return [...grid];
        }
        const beats = this.host.beats.lines();
        return beats !== null ? keyLines(beats, geometry.bars) : barlines(geometry.bars);
    }

    private readonly onDown = (event: PointerEvent): void => {
        // A click that comes after this press is this press's own, never the last gesture's.
        this.swallowNextClick = false;
        if (this.drag !== null && staleGesture(this.drag, event)) {
            this.cancel();
        }
        const rule = this.host.rule(event.pointerType);
        const geometry = this.host.geometry();
        if (this.drag !== null || !this.host.on() || rule === "none" || geometry === null || (event.pointerType === "mouse" && event.button !== 0)) {
            return;
        }
        // A playhead's handle that takes the press stops it before it gets here; one it leaves (an edge's handle over it) is a place.
        if (event.target instanceof Element && event.target.closest(OWN_TARGETS) !== null) {
            return;
        }
        const at = this.point(event.clientX, event.clientY);
        const selection = this.selection();
        const place = placeAt(selection, at.x, at.y, bandBoxes(this.root), onMusic(geometry, at.x, at.y));
        const drag: PlaceDrag = {
            pointerId: event.pointerId, pointerType: event.pointerType, x: at.x, y: at.y, pressY: event.clientY, clientX: event.clientX, clientY: event.clientY,
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
            case "draw":
                return rectPassage({ x: drag.x, y: drag.y }, at, this.rectSystems(geometry, lines), MUSIC_REACH);
            case "bar":
                return this.heldBar(drag);
            default:
                return null;
        }
    }

    /** The systems as a drawn rectangle reads them: each with its staves and its lines, where the band would start or end. */
    private rectSystems(geometry: ScoreGeometry, lines: readonly number[]): RectSystem[] {
        return geometry.systems.map((system) => {
            const bars = system.bars.map((index) => geometry.bars[index]);
            const at = new Map<number, number>();
            for (const bar of bars) {
                for (const line of barLines(lines, bar)) {
                    if (!at.has(line)) {
                        at.set(line, this.host.beats.lineX(bar, line));
                    }
                }
            }
            const staves = bars[0]?.staves ?? [];
            return { left: system.left, right: system.right, top: system.top, bottom: system.bottom, staves, lines: [...at].map(([tick, x]) => ({ tick, x })) };
        });
    }

    /** A finger held on a bar selects that bar; a move after the hold draws from the press as the stylus does (placeMoved). */
    private heldBar(drag: PlaceDrag): PlacedRange | null {
        const bar = drag.bar;
        if (bar === undefined || !Number.isFinite(bar.end)) {
            return null;
        }
        return { start: bar.tick, end: bar.end, hand: null };
    }

    /** The old selection dashed while a new one is drawn, the addition dashed while extending, the targets while an edge moves. */
    private drawOverlays(geometry: ScoreGeometry, drag: PlaceDrag, at: { x: number; y: number }): void {
        this.clearOverlays();
        const selection = drag.selection;
        const preview = drag.preview;
        const phase = drag.gesture.phase;
        // The old selection dashed while a new one would replace it; while the rectangle covers no music it shows as it is.
        if (selection !== null && preview !== null && (phase === "draw" || phase === "bar")) {
            this.boxes(GHOST_CLASS, handRuns(geometry, selection.start, selection.end, selection.hand));
        }
        if (phase === "draw") {
            this.boxes(TRACE_CLASS, [traceBox({ x: drag.x, y: drag.y }, at)], false);
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

    /** Boxes over the score, at least 4 pixels each way (`least`), or exactly as given (the rectangle drawn). */
    private boxes(className: string, runs: readonly { left: number; right: number; top: number; bottom: number }[], least = true): void {
        for (const run of runs) {
            const box = document.createElement("div");
            box.className = className;
            box.setAttribute("aria-hidden", "true");
            if (least) {
                setBox(box, run);
            } else {
                box.style.left = `${run.left}px`;
                box.style.top = `${run.top}px`;
                box.style.width = `${run.right - run.left}px`;
                box.style.height = `${run.bottom - run.top}px`;
            }
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

    /**
     * While a gesture holds the pointer near the top or the bottom of the part of the window the score shows (under the
     * floating bar, over the pill), and has moved toward that edge, the page scrolls a step a frame.
     */
    private autoScroll(): void {
        if (this.scrolling !== 0) {
            return;
        }
        const frame = (): void => {
            const drag = this.drag;
            const view = this.host.view?.() ?? { top: 0, bottom: window.innerHeight };
            const step = drag === null || !this.active() ? 0 : scrollStep(drag.clientY, view.top, view.bottom, undefined, undefined, drag.pressY);
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
        if (keep) {
            this.placed = drag.preview;
        }
        if (drag.gesture.rule === "now" || end !== "none") {
            // A stylus' tap is this layer's: it never places the playhead too. WebKit sends the click a while after the
            // pointer lifts, so it is swallowed whenever it comes, until the next press.
            this.swallowNextClick = true;
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

    /**
     * While the rule is on the stylus selects and never moves the page (v0.6.3, #608): its touch is kept from WebKit's own
     * scrolling, zoom and click from the start, but on what is pressed for its own sake (a bookmark, a margin note), which
     * a tap opens. A finger's touches stay the page's: it scrolls beside the stylus.
     */
    private readonly onTouchStart = (event: TouchEvent): void => {
        const target = event.target instanceof Element ? event.target : null;
        if (event.cancelable && this.host.on() && stylusTouch(event) && target?.closest(OWN_TARGETS) == null) {
            event.preventDefault();
        }
    };

    /** A finger that moves the selection does not move the page, nor does the stylus while the rule is on; a finger may scroll beside it. */
    private readonly onTouchMove = (event: TouchEvent): void => {
        if (event.cancelable && ((this.active() && this.drag?.pointerType === "touch") || (this.host.on() && stylusTouch(event)))) {
            event.preventDefault();
        }
    };
}

/** Whether a touch event moves the stylus (WebKit names the Apple Pencil's touches "stylus"). */
function stylusTouch(event: TouchEvent): boolean {
    return [...event.changedTouches].some((touch) => (touch as Touch & { touchType?: string }).touchType === "stylus");
}

function same(a: PlacedRange, b: PlacedRange | null): boolean {
    return b !== null && a.start === b.start && a.end === b.end && (a.hand ?? null) === (b.hand ?? null);
}

