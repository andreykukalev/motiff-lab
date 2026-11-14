// A long press that puts the playhead where it is held (#543 3.1, lead decision 2): in Navigate a press held still for the
// selection's hold time (HOLD_MS, the same press that selects in Select) takes the black playhead to the musical place
// under the pointer, without selecting; if the pointer then moves on, the playhead follows it as a taken one does (T14–T16).
// A press that moves first is the surface's own (it moves the music); a press let go first is a tap. This keeps the timing
// and the slop; the surface says what the press is meanwhile and what the hold does. Shared by the roll and, where it fits,
// the score. Nothing here touches the DOM, so Node tests it.

import { HOLD_MS, HOLD_SLOP } from "../score/selection-gesture.js";

/** The clock a hold waits on: the page's timers, or a test's. */
export interface HoldTimers {
    set(run: () => void, ms: number): unknown;
    clear(timer: unknown): void;
}

const PAGE_TIMERS: HoldTimers = {
    set: (run, ms) => setTimeout(run, ms),
    clear: (timer) => clearTimeout(timer as ReturnType<typeof setTimeout>),
};

interface Waiting {
    pointer: number;
    x: number;
    y: number;
    lastX: number;
    lastY: number;
    timer: unknown;
}

export class HoldToPlace {
    private waiting: Waiting | null = null;
    /** The pointer whose hold fired, until it is let go: its press is the playhead's from then on. */
    private placedBy: number | null = null;

    /** `fired` runs when a press has been held still long enough, with the pointer and where it is now. */
    constructor(
        private readonly fired: (pointer: number, x: number, y: number) => void,
        private readonly timers: HoldTimers = PAGE_TIMERS,
    ) {}

    /** A press begins to wait for its hold; one waiting before is forgotten. */
    begin(pointer: number, x: number, y: number): void {
        this.cancel();
        const waiting: Waiting = { pointer, x, y, lastX: x, lastY: y, timer: null };
        waiting.timer = this.timers.set(() => this.fire(waiting), HOLD_MS);
        this.waiting = waiting;
    }

    /** The pointer moved: further than the slop from where it went down, it no longer waits for the hold. */
    move(pointer: number, x: number, y: number): void {
        const waiting = this.waiting;
        if (waiting === null || waiting.pointer !== pointer) {
            return;
        }
        waiting.lastX = x;
        waiting.lastY = y;
        if (Math.hypot(x - waiting.x, y - waiting.y) > HOLD_SLOP) {
            this.cancel();
        }
    }

    /** Whether this pointer's press is still waiting for its hold. */
    waits(pointer: number): boolean {
        return this.waiting?.pointer === pointer;
    }

    /** The pointer let go: true when its hold had fired, so the release is the playhead's, not a tap. */
    end(pointer: number): boolean {
        const placed = this.placedBy === pointer;
        if (this.waits(pointer)) {
            this.cancel();
        }
        if (placed) {
            this.placedBy = null;
        }
        return placed;
    }

    /** Forgets the press, a cancel, a second finger or a change of tool: nothing is left waiting or held. */
    cancel(): void {
        if (this.waiting !== null) {
            this.timers.clear(this.waiting.timer);
        }
        this.waiting = null;
        this.placedBy = null;
    }

    private fire(waiting: Waiting): void {
        if (this.waiting !== waiting) {
            return;
        }
        this.waiting = null;
        this.placedBy = waiting.pointer;
        this.fired(waiting.pointer, waiting.lastX, waiting.lastY);
    }
}
