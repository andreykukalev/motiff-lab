// The hold and the drag of a phrase's mark (the phrases spec, «Polish of option A» 2 and «Player's boundaries» 3, v1.2):
// a tap selects the phrase (page-taps.ts); held still, the mark lifts and, let go, its menu opens; held and moved, it is
// dragged note by note, the phrases either side of it lit, and let go it asks Swift to move the boundary there, which
// music-core snaps to the nearest note. Moved before the hold, the press lets the page scroll. Nothing here touches the
// DOM or the selection, so Node tests it (tests/shared/phrase-drag.test.mjs); the iPad's page draws by it
// (ipad-page/page-phrase-drag.ts).
import { type PressPoint, type PressTimers, WINDOW_TIMERS } from "../playback/playhead-press.js";
import { phraseAt } from "./phrase-marks.js";
import { HOLD_MS, HOLD_SLOP } from "./selection-gesture.js";

/** A beat as music-core counts it, where it starts: what the drag's caption writes as «такт 5 · доля 1». */
export interface BarBeatPlace {
    at: number;
    bar: number;
    beat: number;
}

/** What a press on a mark asks of its page. */
export interface MarkPressHost {
    /** The press held still: the mark lifts, and its ghost stays where it was. */
    lifted(): void;
    /** The press moves after the hold: the mark follows the point and the target, the caption and the neighbours with it. */
    dragged(point: PressPoint): void;
    /** Let go without a move after the hold: the mark goes back and its menu opens. */
    menu(): void;
    /** Let go after a drag at a point; null when the press was cancelled: the mark goes back. */
    dropped(point: PressPoint | null): void;
}

/** One press: its pointer, where it came down, its hold timer, and how far it got. */
interface Press {
    pointer: number;
    from: PressPoint;
    timer: number;
    /** False for the piece's first phrase, whose start cannot move: held, it only opens its menu. */
    draggable: boolean;
    state: "waiting" | "held" | "dragging";
}

/**
 * The press of one pointer at a time on a phrase's mark. Before HOLD_MS a move beyond HOLD_SLOP lets it go (the page
 * scrolls) and a lift is a tap; once held a move beyond HOLD_SLOP drags it. The click that follows a hold is no tap.
 */
export class MarkPress {
    private press: Press | null = null;
    private swallowNextClick = false;

    constructor(
        private readonly host: MarkPressHost,
        private readonly timers: PressTimers = WINDOW_TIMERS,
    ) {}

    /** Whether the press holds the mark now, held still or dragged: the page must not scroll under it. */
    get holding(): boolean {
        return this.press !== null && this.press.state !== "waiting";
    }

    /** A pointer comes down on a mark (`draggable` false for the piece's first phrase, whose start cannot move). */
    down(pointer: number, point: PressPoint, draggable = true): void {
        this.end(null);
        const press: Press = { pointer, from: point, timer: 0, draggable, state: "waiting" };
        press.timer = this.timers.set(() => this.hold(press), HOLD_MS);
        this.press = press;
    }

    /** The pointer moved: before the hold, far enough lets the press go; after it, far enough drags the mark. */
    move(pointer: number, point: PressPoint): void {
        const press = this.press;
        if (press === null || press.pointer !== pointer) {
            return;
        }
        const far = Math.hypot(point.x - press.from.x, point.y - press.from.y) > HOLD_SLOP;
        if (press.state === "waiting" && far) {
            this.end(null);
        } else if (press.state === "dragging" || (press.state === "held" && far && press.draggable)) {
            press.state = "dragging";
            this.host.dragged(point);
        }
    }

    /** The pointer lifted at a point: held still, the menu; dragged, the drop. */
    up(pointer: number, point: PressPoint): void {
        if (this.press?.pointer === pointer) {
            this.end(point);
        }
    }

    /** The pointer was cancelled or lost: a drag goes back to where it was. */
    cancel(pointer: number): void {
        if (this.press?.pointer === pointer) {
            this.end(null);
        }
    }

    /** Whether the click just made is the end of a hold, which is no tap on the mark. */
    swallowsClick(): boolean {
        const swallowed = this.swallowNextClick;
        this.swallowNextClick = false;
        return swallowed;
    }

    /** Ends the press under way: at `point` a hold opens the menu and a drag drops there; with none, both go back. */
    private end(point: PressPoint | null): void {
        const press = this.press;
        if (press === null) {
            return;
        }
        this.press = null;
        this.timers.clear(press.timer);
        if (press.state === "waiting") {
            return;
        }
        this.swallowNextClick = point !== null;
        if (press.state === "held" && point !== null) {
            this.host.menu();
        } else {
            this.host.dropped(press.state === "dragging" ? point : null);
        }
    }

    /** The hold: the mark lifts. */
    private hold(press: Press): void {
        if (this.press !== press) {
            return;
        }
        press.state = "held";
        this.host.lifted();
    }
}

/** The note a dragged boundary goes to: of a bar's note starts (`onsets`, in time order), the nearest to `tick`, the earlier of two as near. */
export function nearestOnset(onsets: readonly number[], tick: number): number | undefined {
    let best: number | undefined;
    for (const onset of onsets) {
        if (best === undefined || Math.abs(onset - tick) < Math.abs(best - tick)) {
            best = onset;
        }
    }
    return best;
}

/**
 * The two phrases a boundary dragged from `from` to `to` would make (the spec: dragging a mark changes two neighbouring
 * phrases): from the start before it to `to`, and from `to` to the start after it (`end`, the piece's end, after the
 * last); `starts` in time order. A stretch left empty is left out.
 */
export function neighbourSpans(starts: readonly number[], from: number, to: number, end: number): { start: number; end: number }[] {
    const index = starts.indexOf(from);
    if (index < 0) {
        return [];
    }
    const before = index > 0 ? starts[index - 1] : from;
    const after = index + 1 < starts.length ? starts[index + 1] : end;
    return [{ start: before, end: to }, { start: to, end: after }].filter((span) => span.start < span.end);
}

/** The drag's caption at `tick`: the template's {0} the bar and {1} the beat the tick lies in; "" before any beat. */
export function barBeatCaption(places: readonly BarBeatPlace[], tick: number, template: string): string {
    const place = places[phraseAt(places.map((beat) => beat.at), tick)];
    return place === undefined ? "" : template.replace("{0}", String(place.bar)).replace("{1}", String(place.beat));
}
