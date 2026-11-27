// The roll's fingers (the input router's, roll-input.ts; the audit's W3.12): two fingers on the notes pinch the zoom, and in
// touch reading one finger moves the roll through the music and the rows. A second finger lets go of what the first began.

import { MAX_ROW, ROW_AT_100 } from "./roll-model.js";
import { CLICK_SLOP } from "./roll-press.js";
import { GUTTER, MAX_ZOOM, READING_MIN_ROW, type RollViewport } from "./roll-viewport.js";
import { pinchStep } from "./roll-zoom.js";
import { staleGesture } from "../selection/selection-gesture.js";
import type { InputHost } from "./roll-input.js";

/**
 * A pinch: the fingers' spread (with the zoom on each axis, as last measured, #521 4), the zoom and the rows when it started,
 * the fingers' middle and the view then, and with the zoom on each axis how much it has zoomed each one since it began.
 */
interface Pinch {
    distance: number;
    zoom: number;
    rowScale: number;
    dx: number;
    dy: number;
    midX: number;
    midY: number;
    scroll: number;
    top: number;
    time: number;
    pitch: number;
}

/** A pointer moving the roll: where it went down, the scroll and top row then, and whether it has moved past a tap. */
export interface Pan {
    pointer: number;
    x: number;
    y: number;
    scroll: number;
    top: number;
    moving: boolean;
}

/** What the fingers read of the roll. */
export type FingersHost = Pick<InputHost, "view" | "canvas" | "touchReading" | "axisZoom" | "changed">;

/** What the fingers ask of the router: every press let go, what a second finger cancels, a finger's tap. */
export interface FingersRouter {
    cancelPresses(): void;
    letGo(): void;
    tapped(pan: Pan): void;
}

/**
 * A pointer moving the roll: the time follows it sideways, and the rows up and down when they do not all fit; nothing until
 * it moves past a tap. True when it moved the view.
 */
export function movePan(view: RollViewport, pan: Pan, x: number, y: number): boolean {
    if (!pan.moving && Math.hypot(x - pan.x, y - pan.y) < CLICK_SLOP) {
        return false;
    }
    pan.moving = true;
    view.byHand();
    view.scrollTo(pan.scroll - view.ticksAcross(x - pan.x));
    const { row, above, below } = view.pitchRange();
    if (above || below) {
        view.setPitchTop(pan.top + Math.round((y - pan.y) / row));
    }
    return true;
}

/** The fingers on the notes: a pinch, and a finger moving the roll in touch reading. */
export class RollFingers {
    private readonly touches = new Map<number, { x: number; y: number }>();
    private pinch: Pinch | null = null;
    private pan: Pan | null = null;

    constructor(
        private readonly host: FingersHost,
        private readonly router: FingersRouter,
    ) {}

    /** A pointer begins to move the roll; `moving` when it moves it from the start (the strips' finger off the selection). */
    beginPan(pointer: number, x: number, y: number, moving = false): void {
        const view = this.host.view;
        this.pan = { pointer, x, y, scroll: view.scroll, top: view.pitchRange().high, moving };
    }

    /** The finger moving the roll stops, or only the one of this pointer. */
    dropPan(pointer?: number): void {
        if (pointer === undefined || this.pan?.pointer === pointer) {
            this.pan = null;
        }
    }

    /** No pinch or pan goes on. */
    cancel(): void {
        this.pinch = null;
        this.pan = null;
    }

    /** A finger's press was cancelled: it is no longer counted down. */
    forget(pointer: number): void {
        this.touches.delete(pointer);
    }

    /**
     * Two fingers on the notes pinch the zoom (testing item 13): the second finger cancels what the first began (a press,
     * a drag) and starts the pinch. True when the press is part of a pinch.
     */
    down(event: PointerEvent, x: number, y: number): boolean {
        if (event.pointerType !== "touch") {
            return false;
        }
        if (this.touches.size > 0 && !this.touches.has(event.pointerId) && staleGesture({ pointerType: "touch" }, event)) {
            // A first finger with fingers still counted down: their end was lost (#543 9). Without this every finger after
            // would be taken for a pinch's second and the roll would answer no single finger again.
            this.touches.clear();
            this.router.cancelPresses();
        }
        this.touches.set(event.pointerId, { x, y });
        if (this.touches.size < 2) {
            return false;
        }
        this.pan = null;
        this.router.letGo();
        this.host.canvas.setPointerCapture(event.pointerId);
        const [a, b] = [...this.touches.values()];
        const view = this.host.view;
        this.pinch = {
            distance: Math.max(1, Math.hypot(a.x - b.x, a.y - b.y)),
            zoom: view.zoom,
            rowScale: view.rowScale,
            dx: a.x - b.x,
            dy: a.y - b.y,
            midX: (a.x + b.x) / 2,
            midY: (a.y + b.y) / 2,
            scroll: view.scroll,
            top: view.pitchRange().high,
            time: 1,
            pitch: 1,
        };
        this.host.changed();
        return true;
    }

    /** A move of a pinch, or of a finger moving the roll in touch reading; true when it was one. */
    move(event: PointerEvent, x: number, y: number): boolean {
        if (this.touches.has(event.pointerId)) {
            this.touches.set(event.pointerId, { x, y });
        }
        const pinch = this.pinch;
        if (pinch !== null && this.touches.has(event.pointerId)) {
            const [a, b] = [...this.touches.values()];
            const factor = Math.hypot(a.x - b.x, a.y - b.y) / pinch.distance;
            const anchor = Math.max(0, (a.x + b.x) / 2 - GUTTER);
            const view = this.host.view;
            if (this.host.touchReading() && this.host.axisZoom()) {
                this.pinchOnAxes(pinch, a, b);
            } else if (this.host.touchReading()) {
                // Two fingers zoom the time only, so the rows stay where they are.
                view.byHand();
                view.zoomTo(pinch.zoom * factor, view.rowScale, anchor);
            } else {
                view.zoomTo(pinch.zoom * factor, pinch.rowScale * factor, anchor);
            }
            return true;
        }
        if (this.pan !== null && event.pointerId === this.pan.pointer) {
            if (movePan(this.host.view, this.pan, x, y)) {
                this.host.changed();
            }
            return true;
        }
        return false;
    }

    /** The end of a pinch, or of a finger on the roll in touch reading; true when it was one. */
    end(event: PointerEvent): boolean {
        if (this.touches.delete(event.pointerId) && this.pinch !== null) {
            // The pinch ends with its first finger lifted; the other finger does nothing more.
            this.pinch = null;
            return true;
        }
        const pan = this.pan;
        if (pan !== null && event.pointerId === pan.pointer) {
            // A tap or a move through the music: the selection stays as it is; a tap is the router's (Andrei, v0.2).
            this.pan = null;
            if (!pan.moving) {
                this.router.tapped(pan);
            }
            return true;
        }
        return false;
    }

    /**
     * A pinch with the zoom on each axis (#479 item 23, #521 4): in every move the time zooms by the fingers' spread across,
     * round where they began, and the rows by their spread up and down (pinchStep), both at once, so the pinch may turn from
     * one way to the other without lifting; and the roll follows the fingers' middle, so two fingers move it as one does.
     */
    private pinchOnAxes(pinch: Pinch, a: { x: number; y: number }, b: { x: number; y: number }): void {
        const step = pinchStep({ dx: pinch.dx, dy: pinch.dy }, { dx: a.x - b.x, dy: a.y - b.y });
        const view = this.host.view;
        // Within the zoom's limits, so a pinch back from a limit zooms at once.
        pinch.time = Math.min(MAX_ZOOM / pinch.zoom, Math.max(view.minZoom() / pinch.zoom, pinch.time * step.time));
        pinch.pitch = Math.min(MAX_ROW / ROW_AT_100 / pinch.rowScale, Math.max(READING_MIN_ROW / ROW_AT_100 / pinch.rowScale, pinch.pitch * step.pitch));
        pinch.dx = step.spread.dx;
        pinch.dy = step.spread.dy;
        view.byHand();
        view.restoreView(pinch.scroll, pinch.top);
        view.zoomTo(pinch.zoom * pinch.time, pinch.rowScale, Math.max(0, pinch.midX - GUTTER));
        if (pinch.pitch !== 1) {
            view.rowsAround(pinch.rowScale * pinch.pitch, pinch.midY);
        }
        view.scrollTo(view.scroll - view.ticksAcross((a.x + b.x) / 2 - pinch.midX));
        const { high, row, above, below } = view.pitchRange();
        if (above || below) {
            view.setPitchTop(high + Math.round(((a.y + b.y) / 2 - pinch.midY) / row));
        }
        this.host.changed();
    }
}
