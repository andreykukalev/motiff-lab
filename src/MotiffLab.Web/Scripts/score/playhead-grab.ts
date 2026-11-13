// A playhead dragged on the score (v0.5.2, #495 UX-08 and UX-10, the architect's design playback-model-v0.5.2, slice I3):
// the pen or a finger on the black dot or the cobalt knob takes the playhead, not the bar under it. The press is captured by
// the handle and goes no further, so the selection layer and the tap on a beat never see it and no other bar is selected.
// The point goes to a tick along the bar's onsets (ScoreGeometry.tickAt, the inverse of the playhead's place), then to the
// sixteenth grid (snapTick): the black playhead stays in the bar it stood in when the drag began (design question В5,
// another bar is a tap away), the blue one inside the selection. The page tells Swift through PlayheadDrag.

import type { Handle, Span } from "../playback/playback-model.js";
import { PlayheadDrag, snapTick } from "../playback/playhead-drag.js";
import type { ScoreBar, ScoreGeometry } from "./score-geometry.js";

/** What a drag on the score asks of the page. */
export interface ScoreGrabHost {
    geometry(): ScoreGeometry | null;
    onsetOf(id: string): number | undefined;
    /** The selection the blue playhead keeps to, or null. */
    selection(): Span | null;
    /** The position now, the black playhead's place: the bar it stands in is the bar its drag keeps to. */
    position(): number;
    /** Whether a press by this pointer ("touch", "pen", "mouse") may take a playhead now: not one that writes. */
    takes(pointerType: string): boolean;
    /** The event's point in the document's pixels, as the geometry places the bars. */
    point(event: PointerEvent): { x: number; y: number };
    /** The place dragged to changed: the playheads are drawn again. */
    moved(): void;
}

/**
 * The tick a playhead dragged to a point goes to, or undefined off the bars: the black one along the bar its drag began in,
 * the blue one along the bar under the point, held to the selection, the bar the selection starts or ends in when the
 * point is beyond it. On the sixteenth grid of that bar.
 */
export function scoreDragTick(geometry: ScoreGeometry, onsetOf: (id: string) => number | undefined, handle: Handle, start: ScoreBar | null,
    point: { x: number; y: number }, selection: Span | null): number | undefined {
    let bar = handle === "black" ? start ?? undefined : geometry.barNear(point.x, point.y);
    if (bar === undefined || (handle === "blue" && selection === null)) {
        return undefined;
    }
    let tick = geometry.tickAt(bar, point.x, onsetOf);
    if (handle === "blue" && selection !== null && (tick < selection.start || tick >= selection.end)) {
        tick = tick < selection.start ? selection.start : selection.end - 1;
        bar = geometry.barAt(tick) ?? bar;
    }
    return snapTick(tick, { start: bar.tick, end: bar.end }, handle === "blue" ? selection : null);
}

/** A press that moves less than this, in pixels, is a tap on the handle: it moves nothing. */
const TAP_SLOP = 4;

/** A held handle: its element, which has the pointer captured, the bar the black playhead keeps to, and where it was pressed. */
interface Grabbed {
    element: HTMLElement;
    pointer: number;
    handle: Handle;
    bar: ScoreBar | null;
    from: { x: number; y: number } | null;
}

/** The drag of the score's playheads by their handles. */
export class ScoreGrab {
    private grabbed: Grabbed | null = null;

    constructor(
        private readonly host: ScoreGrabHost,
        readonly drag: PlayheadDrag,
    ) {}

    /** A press on a handle (ScorePlayheadMarks' grab): it takes the playhead and keeps the pointer until it lets go. */
    begin(handle: Handle, event: PointerEvent): void {
        const geometry = this.host.geometry();
        const element = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
        if (geometry === null || element === null || (event.pointerType === "mouse" && event.button !== 0) || !this.host.takes(event.pointerType)) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        if (!this.drag.grab(handle, event.pointerId)) {
            return;
        }
        const position = this.host.position();
        const bar = handle === "black" ? geometry.playhead(position, (id) => this.host.onsetOf(id))?.bar ?? geometry.barAt(position) ?? null : null;
        this.grabbed = { element, pointer: event.pointerId, handle, bar, from: this.host.point(event) };
        element.setPointerCapture(event.pointerId);
        element.addEventListener("pointermove", this.onMove);
        for (const name of ["pointerup", "pointercancel", "lostpointercapture"] as const) {
            element.addEventListener(name, this.onEnd);
        }
    }

    /** Lets the playhead go, as when the view goes or the playheads are turned off mid-drag. */
    cancel(): void {
        const grabbed = this.grabbed;
        if (grabbed !== null) {
            this.end(grabbed);
        }
    }

    private readonly onMove = (event: PointerEvent): void => {
        const grabbed = this.grabbed;
        const geometry = this.host.geometry();
        if (grabbed === null || event.pointerId !== grabbed.pointer || geometry === null) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        const point = this.host.point(event);
        if (grabbed.from !== null && Math.hypot(point.x - grabbed.from.x, point.y - grabbed.from.y) < TAP_SLOP) {
            return;
        }
        grabbed.from = null;
        const tick = scoreDragTick(geometry, (id) => this.host.onsetOf(id), grabbed.handle, grabbed.bar, point, this.host.selection());
        if (tick !== undefined) {
            this.drag.move(event.pointerId, tick);
            this.host.moved();
        }
    };

    private readonly onEnd = (event: PointerEvent): void => {
        const grabbed = this.grabbed;
        if (grabbed !== null && event.pointerId === grabbed.pointer) {
            event.stopPropagation();
            this.end(grabbed);
        }
    };

    private end(grabbed: Grabbed): void {
        this.grabbed = null;
        const { element } = grabbed;
        element.removeEventListener("pointermove", this.onMove);
        for (const name of ["pointerup", "pointercancel", "lostpointercapture"] as const) {
            element.removeEventListener(name, this.onEnd);
        }
        if (element.hasPointerCapture(grabbed.pointer)) {
            element.releasePointerCapture(grabbed.pointer);
        }
        this.drag.release(grabbed.pointer);
        this.host.moved();
    }
}
