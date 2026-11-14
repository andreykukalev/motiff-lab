// The selection's handles on the score (decision record 0018, section 3): a handle dragged moves its edge from barline to
// barline (or beat line to beat line while selecting by beats), never past the other edge, and the arrow keys move it a
// bar or a beat at a time (WCAG 2.1.1). A pointer that writes goes on to the pen (#511 4.1, handleTakes). The layer
// (selection-layer.ts) draws the handles; this holds what pressing and dragging them does.

import type { ScoreBar, ScoreGeometry } from "./score-geometry.js";
import { type ScoreBeats, keyLines, stepEdge } from "./selection-beats.js";
import { handleTakes, nextGestureId } from "./selection-gesture.js";
import type { ScoreSelection, SelectionHost } from "./selection-layer.js";

/** What the handles need from the layer. */
export interface HandleSurface {
    readonly owner: SelectionHost;
    readonly beats: ScoreBeats;
    selection(): ScoreSelection | null;
    point(event: PointerEvent): { x: number; y: number };
    /** The band over [start, end) alone, while a handle moves. */
    show(geometry: ScoreGeometry, start: number, end: number): void;
    place(): void;
    placeMenu(): void;
}

export interface HandleDrag {
    pointerId: number;
    edge: "start" | "end";
    first: ScoreBar;
    last: ScoreBar;
    /** The passage the band shows while the handle moves, in ticks: whole bars, or beats while selecting by beats. */
    start: number;
    end: number;
}

/** The selection's first and last bar where the score shows them now. */
function selectedBars(geometry: ScoreGeometry, selection: ScoreSelection | null): [ScoreBar, ScoreBar] | null {
    if (selection === null) {
        return null;
    }
    const first = geometry.barAt(selection.start);
    const last = geometry.barAt(Math.max(selection.start, selection.end - 1));
    return first !== undefined && last !== undefined ? [first, last] : null;
}

export class SelectionHandles {
    /** The handle being dragged, if one is. */
    drag: HandleDrag | null = null;

    constructor(private readonly surface: HandleSurface) {}

    readonly onDown = (event: PointerEvent): void => {
        const handle = event.currentTarget as HTMLElement;
        const geometry = this.surface.owner.geometry();
        const bars = geometry === null ? null : selectedBars(geometry, this.surface.selection());
        const selection = this.surface.selection();
        if (bars === null || selection === null || (event.pointerType !== "touch" && event.button !== 0)) {
            return;
        }
        // A pointer that writes goes on to the pen's layer, which takes it over the handle.
        if (!handleTakes(event.pointerType, this.surface.owner.takesHandle?.(event.pointerType), this.surface.owner.penOn())) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        handle.setPointerCapture(event.pointerId);
        const drag: HandleDrag = {
            pointerId: event.pointerId, edge: handle.dataset.edge === "start" ? "start" : "end", first: bars[0], last: bars[1], start: selection.start, end: selection.end,
        };
        this.drag = drag;
        handle.classList.add("dragging");
        this.surface.placeMenu();
        const move = (moveEvent: PointerEvent): void => {
            if (moveEvent.pointerId !== drag.pointerId) {
                return;
            }
            const now = this.surface.owner.geometry();
            if (now != null) {
                this.moveEdge(now, drag, this.surface.point(moveEvent));
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
            this.drag = null;
            const byBeats = this.surface.beats.lines() === null ? undefined : this.surface.owner.chosenSpan;
            const changed = byBeats ? drag.start !== selection.start || drag.end !== selection.end : drag.first !== bars[0] || drag.last !== bars[1];
            if (changed && endEvent.type === "pointerup" && byBeats) {
                byBeats.call(this.surface.owner, drag.start, drag.end, true, "edit", nextGestureId());
            } else if (changed && endEvent.type === "pointerup") {
                this.surface.owner.chosen(drag.first.tick, drag.last.tick, true, "edit", nextGestureId());
            } else {
                this.surface.place();
            }
        };
        handle.addEventListener("pointermove", move);
        handle.addEventListener("pointerup", end);
        handle.addEventListener("pointercancel", end);
    };

    /** A handle dragged to a point: its edge goes to the barline, or the beat line, nearest it, never past the other edge. */
    private moveEdge(geometry: ScoreGeometry, drag: HandleDrag, at: { x: number; y: number }): void {
        const lines = this.surface.beats.lines();
        if (lines !== null) {
            const line = this.surface.beats.edgeNear(geometry, lines, at.x, at.y);
            if (line !== undefined && (drag.edge === "start" ? line < drag.end : line > drag.start)) {
                drag[drag.edge] = line;
                this.surface.show(geometry, drag.start, drag.end);
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
        this.surface.show(geometry, drag.start, drag.end);
    }

    /** By beats the arrow keys move an edge a beat at a time; false while the selection takes whole bars. */
    private stepByBeats(geometry: ScoreGeometry, edge: "start" | "end", step: number): boolean {
        const lines = this.surface.beats.lines();
        const pick = this.surface.owner.chosenSpan;
        const selection = this.surface.selection();
        if (lines === null || pick === undefined || selection === null) {
            return false;
        }
        const moved = stepEdge(keyLines(lines, geometry.bars), selection, edge, step);
        if (moved !== undefined) {
            pick.call(this.surface.owner, moved.start, moved.end, true, "edit", nextGestureId());
        }
        return true;
    }

    /** The arrow keys move a handle's edge a bar at a time, as a drag does (WCAG 2.1.1). */
    readonly onKey = (event: KeyboardEvent): void => {
        const step = event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0;
        const geometry = this.surface.owner.geometry();
        const bars = geometry === null ? null : selectedBars(geometry, this.surface.selection());
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
        this.surface.owner.chosen(first.tick, last.tick, true, "edit", nextGestureId());
    };
}

/** The bars in time order, one per start (a bar repeated on another page counts once). */
export function uniqueInTime(bars: readonly ScoreBar[]): ScoreBar[] {
    const byTick = new Map<number, ScoreBar>();
    for (const bar of bars) {
        if (!byTick.has(bar.tick)) {
            byTick.set(bar.tick, bar);
        }
    }
    return [...byTick.values()].sort((a, b) => a.tick - b.tick);
}

/** Every barline of the score, in time order: where each bar starts, and where the last ends. */
export function barlines(bars: readonly ScoreBar[]): number[] {
    const inTime = uniqueInTime(bars);
    const last = inTime[inTime.length - 1];
    return last === undefined ? [] : [...inTime.map((bar) => bar.tick), last.end];
}

/** The tick under a point: across the bar nearest it, in proportion, as the edges move (#511 2.1). */
export function tickAt(geometry: ScoreGeometry, x: number, y: number): number {
    const bar = geometry.barNear(x, y);
    if (bar === undefined) {
        return 0;
    }
    const share = bar.right > bar.left ? Math.min(1, Math.max(0, (x - bar.left) / (bar.right - bar.left))) : 0;
    return bar.tick + share * (bar.end - bar.tick);
}
