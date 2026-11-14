// A playhead dragged on the score (v0.5.2, #495 UX-08 and UX-10, the architect's design playback-model-v0.5.2, slice I3):
// the pen or a finger on the black dot or the cobalt knob takes the playhead, not the bar under it. The press is captured by
// the handle and goes no further, so the selection layer and the tap on a beat never see it and no other bar is selected.
// The point goes to a tick along the bar's onsets (ScoreGeometry.tickAt, the inverse of the playhead's place), then to the
// sixteenth grid (snapTick): the black playhead along the bar under the pointer, across bars and systems (v0.5.7, #543 5,
// the lead's decision 4: design question B5's "the bar it stood in" is gone), the blue one inside the selection. In Navigate
// a long press on the music takes the black playhead there and scrubs on as the pointer moves (ScorePress, #543 3,
// Scripts/playback/playhead-press.ts). The page tells its player through PlayheadDrag: Swift on the iPad, the TypeScript
// Player straight on the web (slice W3).

import type { Handle, Span } from "../playback/playback-model.js";
import { PlayheadDrag, snapTick } from "../playback/playhead-drag.js";
import { PlayheadPress } from "../playback/playhead-press.js";
import type { ScoreGeometry } from "./score-geometry.js";
import { barUnder } from "./score-modes.js";

/** What a drag on the score asks of the page. */
export interface ScoreGrabHost {
    geometry(): ScoreGeometry | null;
    onsetOf(id: string): number | undefined;
    /** The selection the blue playhead keeps to, or null. */
    selection(): Span | null;
    /** Whether a press by this pointer ("touch", "pen", "mouse") may take a playhead now: not one that writes. */
    takes(pointerType: string): boolean;
    /** The event's point in the document's pixels, as the geometry places the bars. */
    point(event: PointerEvent): { x: number; y: number };
    /** The place dragged to changed: the playheads are drawn again. */
    moved(): void;
}

/**
 * The tick a playhead dragged to a point goes to, or undefined off the bars: along the bar under the point (on the system
 * nearest it), the blue one held to the selection, the bar the selection starts or ends in when the point is beyond it.
 * On the sixteenth grid of that bar.
 */
export function scoreDragTick(geometry: ScoreGeometry, onsetOf: (id: string) => number | undefined, handle: Handle,
    point: { x: number; y: number }, selection: Span | null): number | undefined {
    let bar = geometry.barNear(point.x, point.y);
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

/** A held handle: its element, which has the pointer captured, and where it was pressed. */
interface Grabbed {
    element: HTMLElement;
    pointer: number;
    handle: Handle;
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
        const grabbed: Grabbed = { element, pointer: event.pointerId, handle, from: this.host.point(event) };
        this.grabbed = grabbed;
        element.addEventListener("pointermove", this.onMove);
        for (const name of ["pointerup", "pointercancel", "lostpointercapture"] as const) {
            element.addEventListener(name, this.onEnd);
        }
        if (!capture(element, event.pointerId)) {
            // The pointer is gone already: nothing stays held (#543 9).
            this.end(grabbed);
            return;
        }
        // Held, the black playhead shows its line across the staves at once (#543 5).
        this.host.moved();
    }

    /** Whether a playhead is held now, by a handle or a long press. */
    get held(): Handle | null {
        return this.drag.handle;
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
        const tick = scoreDragTick(geometry, (id) => this.host.onsetOf(id), grabbed.handle, point, this.host.selection());
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

/** Takes the pointer to an element; false when the pointer is gone already. */
function capture(element: Element, pointer: number): boolean {
    try {
        element.setPointerCapture(pointer);
        return true;
    } catch {
        return false;
    }
}

/** What a long press on the score asks of the page. */
export interface ScorePressHost {
    geometry(): ScoreGeometry | null;
    onsetOf(id: string): number | undefined;
    /** Whether a long press of this pointer puts the black playhead now (pressPlaces, with the model holding). */
    places(event: PointerEvent): boolean;
    /** The event's point in the document's pixels, as the geometry places the bars. */
    point(event: PointerEvent): { x: number; y: number };
    /** The place changed, or the press ended: the playheads are drawn again. */
    moved(): void;
}

/** The marks over the score that are their own targets: a long press there is theirs. */
const OWN_TARGETS = "button, a, input, .grip, .sel-handle, .bracket";

/**
 * The long press in Navigate on the score (#543 3.1 and 5.5): held still on a bar it takes the black playhead to the place
 * under the pointer, on the sixteenth grid, and moved on it scrubs across bars and systems; it never selects. While it holds,
 * the page under it keeps still and the click its release makes is no tap on a beat.
 */
export class ScorePress {
    readonly press: PlayheadPress;
    private swallowNextClick = false;

    constructor(
        private readonly element: HTMLElement,
        private readonly host: ScorePressHost,
        drag: PlayheadDrag,
    ) {
        this.press = new PlayheadPress({
            tickAt: (point, first) => this.tickAt(point, first),
            took: (pointer) => {
                if (!capture(element, pointer)) {
                    this.press.end();
                }
            },
            moved: () => host.moved(),
        }, drag);
        element.addEventListener("pointerdown", this.onDown);
        element.addEventListener("pointermove", this.onMove);
        for (const name of ["pointerup", "pointercancel", "lostpointercapture"] as const) {
            element.addEventListener(name, this.onUp);
        }
        // Not passive: once the press holds, the finger moves the playhead, not the page.
        element.addEventListener("touchmove", this.onTouchMove, { passive: false });
        element.addEventListener("contextmenu", this.onContextMenu);
    }

    /** Whether the click the browser sends now ends a long press, and is not a tap on the score. */
    swallowsClick(): boolean {
        const swallow = this.swallowNextClick;
        this.swallowNextClick = false;
        return swallow;
    }

    /** Lets the press go, as when the view goes, the rules change or the playheads are turned off mid-press. */
    cancel(): void {
        this.press.end();
    }

    dispose(): void {
        this.cancel();
        this.element.removeEventListener("pointerdown", this.onDown);
        this.element.removeEventListener("pointermove", this.onMove);
        for (const name of ["pointerup", "pointercancel", "lostpointercapture"] as const) {
            this.element.removeEventListener(name, this.onUp);
        }
        this.element.removeEventListener("touchmove", this.onTouchMove);
        this.element.removeEventListener("contextmenu", this.onContextMenu);
    }

    /** The press itself must be on a bar; after it the place follows the pointer to the bar nearest it. */
    private tickAt(point: { x: number; y: number }, first: boolean): number | undefined {
        const geometry = this.host.geometry();
        if (geometry === null || (first && barUnder(geometry, point.x, point.y) === undefined)) {
            return undefined;
        }
        return scoreDragTick(geometry, (id) => this.host.onsetOf(id), "black", point, null);
    }

    private readonly onDown = (event: PointerEvent): void => {
        this.swallowNextClick = false;
        if (event.target instanceof Element && event.target.closest(OWN_TARGETS) !== null) {
            return;
        }
        if (this.host.places(event)) {
            this.press.down(event.pointerId, this.host.point(event), event.isPrimary);
        }
    };

    private readonly onMove = (event: PointerEvent): void => {
        if (this.press.move(event.pointerId, this.host.point(event))) {
            event.preventDefault();
        }
    };

    private readonly onUp = (event: PointerEvent): void => {
        if (!this.press.up(event.pointerId) || event.type !== "pointerup") {
            return;
        }
        this.swallowNextClick = true;
        // A click may not follow (the pointer left the score); the next real click is not swallowed.
        setTimeout(() => (this.swallowNextClick = false), 0);
    };

    private readonly onTouchMove = (event: TouchEvent): void => {
        if (this.press.holding && event.cancelable) {
            event.preventDefault();
        }
    };

    /** A long press on a touch screen brings no browser menu while it may take the playhead. */
    private readonly onContextMenu = (event: MouseEvent): void => {
        if (this.press.holding || this.press.pending) {
            event.preventDefault();
        }
    };
}
