// Dragging a playhead (v0.5.2, #495 UX-08 and UX-10, the architect's design playback-model-v0.5.2, slice I3): the pen or a
// finger on a playhead's handle moves the position along the music, not the screen. The place under the pointer goes to
// the sixteenth grid counted from its bar's start (the note editor's grid) and stays inside the bar, and inside the
// selection for the blue playhead (snapTick). The view shows the place at once and tells its host the newest one once a
// frame (PlayheadDrag); the host's player runs the playback model's T14–T16 (playback-model.ts). Shared by the score and
// the roll, on the iPad and on the web (slice W3). Nothing here touches the DOM, so Node tests it.

import type { Handle, Span } from "./playback-model.js";

/** The grid a dragged playhead goes to: a sixteenth, 120 ticks at 480 to the quarter, counted from the bar's start. */
export const DRAG_GRID = 120;

/** After a drag the view keeps showing the place it was let go at until the player reports it, at most this long, in ms. */
const HOLD_MS = 800;

/**
 * Where a playhead dragged to `tick` goes: to the nearest sixteenth counted from the start of `bar`, no later than the
 * bar's last sixteenth, and inside `limits` [start, end) when given (the selection, for the blue playhead): at its start
 * at the earliest and at its last sixteenth before its end at the latest. A whole tick.
 */
export function snapTick(tick: number, bar: Span, limits: Span | null = null): number {
    const steps = Math.round((tick - bar.start) / DRAG_GRID);
    const lastStep = Math.max(0, Math.ceil((bar.end - bar.start) / DRAG_GRID) - 1);
    let low = bar.start;
    let high = bar.start + lastStep * DRAG_GRID;
    if (limits !== null) {
        low = Math.max(low, limits.start);
        // The last sixteenth of the bar that starts before the end of the limits.
        const before = Math.ceil((limits.end - bar.start) / DRAG_GRID) - 1;
        high = Math.min(high, bar.start + before * DRAG_GRID);
    }
    const snapped = bar.start + Math.min(Math.max(steps, 0), lastStep) * DRAG_GRID;
    return Math.round(high < low ? low : Math.min(Math.max(snapped, low), high));
}

/** The bar that holds a tick among bars in time order, each [start, end); the last one for a tick at or past the end. */
export function barOf(bars: readonly Span[], tick: number): Span | undefined {
    return bars.find((bar) => tick >= bar.start && tick < bar.end) ?? (tick < (bars[0]?.start ?? 0) ? bars[0] : bars.at(-1));
}

/** What a view tells its host about a drag, in order: the handle taken, the places it goes to, and its letting go. */
export type DragMessage =
    | { phase: "grab"; handle: Handle }
    | { phase: "move"; handle: Handle; tick: number }
    | { phase: "release"; handle: Handle };

/** One held playhead: the pointer that holds it, and the place shown and sent (none before it moves), a send waiting. */
interface Held {
    handle: Handle;
    pointer: number;
    shown: number | null;
    sent: number | null;
    waiting: boolean;
}

/**
 * A playhead held by one pointer: the place it was dragged to is shown at once, and the newest place goes to the host once
 * a frame, never twice the same; letting go sends the last place first. After it, the view goes on showing where it was let
 * go until the player reports the position there, or a moment has passed.
 */
export class PlayheadDrag {
    private held: Held | null = null;
    private kept: { tick: number; until: number } | null = null;

    constructor(
        private readonly send: (message: DragMessage) => void,
        private readonly nextFrame: (run: () => void) => void = (run) => void requestAnimationFrame(run),
        private readonly now: () => number = () => performance.now(),
    ) {}

    /** The playhead held now, or null. */
    get handle(): Handle | null {
        return this.held?.handle ?? null;
    }

    /** Whether this pointer holds the playhead. */
    holds(pointer: number): boolean {
        return this.held?.pointer === pointer;
    }

    /**
     * A pointer takes a playhead; false when another one is held already. Until it moves, the player's place shows and the
     * host hears nothing, so a tap on a handle neither pauses nor moves anything.
     */
    grab(handle: Handle, pointer: number): boolean {
        if (this.held !== null) {
            return false;
        }
        this.kept = null;
        this.held = { handle, pointer, shown: null, sent: null, waiting: false };
        return true;
    }

    /** The held playhead dragged to a tick already on the grid: shown now, sent with the next frame; the first move takes it. */
    move(pointer: number, tick: number): void {
        const held = this.held;
        if (held === null || held.pointer !== pointer || tick === held.shown) {
            return;
        }
        if (held.shown === null) {
            this.send({ phase: "grab", handle: held.handle });
        }
        held.shown = tick;
        if (!held.waiting) {
            held.waiting = true;
            this.nextFrame(() => this.flush(held));
        }
    }

    /** The pointer lets go, or loses the playhead (a cancel, a lost capture, the view going): the last place, then the end. */
    release(pointer?: number): void {
        const held = this.held;
        if (held === null || (pointer !== undefined && held.pointer !== pointer)) {
            return;
        }
        this.held = null;
        if (held.shown === null) {
            return;
        }
        this.flush(held);
        this.kept = { tick: held.shown, until: this.now() + HOLD_MS };
        this.send({ phase: "release", handle: held.handle });
    }

    /** The tick to draw the playheads at: the place dragged to while held and just after, else the player's. */
    heard(reported: number): number {
        if (this.held !== null) {
            return this.held.shown ?? reported;
        }
        const kept = this.kept;
        if (kept === null) {
            return reported;
        }
        if (Math.abs(reported - kept.tick) < 1 || this.now() >= kept.until) {
            this.kept = null;
            return reported;
        }
        return kept.tick;
    }

    private flush(held: Held): void {
        held.waiting = false;
        const shown = held.shown;
        if (shown !== null && shown !== held.sent) {
            held.sent = shown;
            this.send({ phase: "move", handle: held.handle, tick: shown });
        }
    }
}
