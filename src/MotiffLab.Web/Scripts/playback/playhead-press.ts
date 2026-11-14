// A long press that puts the black playhead where it is held (v0.5.7, #543 sections 2.1, 3.1 and 5.5, the lead's decisions 1
// and 2): in Navigate (and with the Pen for a pointer that does not write) a press held still for HOLD_MS on the music takes
// the piece's playhead to the place under it, on the sixteenth grid of the bar there (the view gives the tick), and never
// selects. Moved on without lifting, it scrubs as a playhead taken by its handle does (the playback model's T14-T16): the
// music stands while it is held and plays on from the new place when it played. Shared by the score and the roll, on the
// iPad and on the web; nothing here touches the DOM, so Node tests it, and the timers are the host's.

import type { PlayheadDrag } from "./playhead-drag.js";
import { HOLD_MS, HOLD_SLOP } from "../score/selection-gesture.js";

/**
 * Whether a long press of this pointer puts the black playhead: the host's rule where it gives one (the iPad's InputRules,
 * `hold`), else in Navigate a finger or the pen; the mouse keeps its own moves there (a drag moves the page, a click places).
 */
export function pressPlaces(pointerType: string, rule: boolean | undefined, mode: string | null): boolean {
    if (rule !== undefined) {
        return rule;
    }
    return mode === "navigate" && (pointerType === "touch" || pointerType === "pen");
}

/** A point in the view's own pixels. */
export interface PressPoint {
    x: number;
    y: number;
}

/** What the press asks of its view. */
export interface PressHost {
    /**
     * The black playhead's place for a point, already on the grid; undefined off the music. `first` for the press itself,
     * which must be on the music; after it the place follows the pointer wherever it goes.
     */
    tickAt(point: PressPoint, first: boolean): number | undefined;
    /** The press took the playhead: the view keeps the pointer and the page still under it until it lifts. */
    took?(pointer: number): void;
    /** The place shown changed, or the press ended: the playheads are drawn again. */
    moved(): void;
}

/** The host's timers, so a test runs the hold by hand. */
export interface PressTimers {
    set(run: () => void, ms: number): number;
    clear(id: number): void;
}

const WINDOW_TIMERS: PressTimers = {
    set: (run, ms) => setTimeout(run, ms) as unknown as number,
    clear: (id) => clearTimeout(id),
};

/** One press: its pointer, where it came down, its hold timer, and whether it holds the playhead. */
interface Press {
    pointer: number;
    from: PressPoint;
    timer: number;
    holding: boolean;
}

/**
 * The long press of one pointer at a time. Before the hold a move beyond HOLD_SLOP lets it go (the page scrolls); after it
 * the playhead follows the pointer; lifting, a cancel or a lost capture lets the playhead go where it is. A new primary
 * press while one is under way means the old pointer is gone without its end (a lost pointerup): it ends first, so a stale
 * press never blocks the next one (#543 9).
 */
export class PlayheadPress {
    private press: Press | null = null;

    constructor(
        private readonly host: PressHost,
        readonly drag: PlayheadDrag,
        private readonly timers: PressTimers = WINDOW_TIMERS,
    ) {}

    /** Whether a press holds the playhead now. */
    get holding(): boolean {
        return this.press?.holding === true;
    }

    /** Whether a press waits for its hold now. */
    get pending(): boolean {
        return this.press !== null && !this.press.holding;
    }

    /** Whether this pointer is the press under way. */
    owns(pointer: number): boolean {
        return this.press?.pointer === pointer;
    }

    /** A pointer comes down at a point on the music; false when another press is under way and this one is not primary. */
    down(pointer: number, point: PressPoint, primary = true): boolean {
        if (this.press !== null) {
            if (!primary) {
                return false;
            }
            this.end();
        }
        const press: Press = { pointer, from: point, timer: 0, holding: false };
        press.timer = this.timers.set(() => this.hold(press, point), HOLD_MS);
        this.press = press;
        return true;
    }

    /** The pointer moved: before the hold, far enough lets the press go; after it, the playhead follows. True while it holds. */
    move(pointer: number, point: PressPoint): boolean {
        const press = this.press;
        if (press === null || press.pointer !== pointer) {
            return false;
        }
        if (!press.holding) {
            if (Math.hypot(point.x - press.from.x, point.y - press.from.y) > HOLD_SLOP) {
                this.end();
            }
            return false;
        }
        const tick = this.host.tickAt(point, false);
        if (tick !== undefined) {
            this.drag.move(pointer, tick);
            this.host.moved();
        }
        return true;
    }

    /** The pointer lifted, was cancelled or lost its capture: true when the press held the playhead (its click is no tap). */
    up(pointer: number): boolean {
        if (this.press === null || this.press.pointer !== pointer) {
            return false;
        }
        return this.end();
    }

    /** Ends the press under way, as when the view goes or the rules change; true when it held the playhead. */
    end(): boolean {
        const press = this.press;
        if (press === null) {
            return false;
        }
        this.press = null;
        this.timers.clear(press.timer);
        if (press.holding) {
            this.drag.release(press.pointer);
            this.host.moved();
        }
        return press.holding;
    }

    /** The hold: the playhead goes to the place under the press, taken at once so playback stands while it is held (T14). */
    private hold(press: Press, point: PressPoint): void {
        if (this.press !== press) {
            return;
        }
        const tick = this.host.tickAt(point, true);
        if (tick === undefined || !this.drag.grab("black", press.pointer, true)) {
            this.press = null;
            return;
        }
        press.holding = true;
        this.drag.move(press.pointer, tick);
        this.host.took?.(press.pointer);
        this.host.moved();
    }
}
