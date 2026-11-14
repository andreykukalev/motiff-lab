// The playhead the player moves on the roll (Andrei, v0.2): its handle under the bar numbers, or on the iPad its whole line,
// dragged scrubs (playing on from the new place while playing, only moving while paused), and the arrow keys or VoiceOver's
// swipes nudge it by a beat. It stays inside the loop, or the selection, when there is one. The bar numbers keep selecting
// bars, so the two never fight. The page does the moving (OnRollSeek); the roll shows the place at once and keeps showing
// it until the player reports it. Nothing here touches the DOM until a control is made, so Node can test the rest.

import { fill, type RollBar, type RollLabels } from "./roll-model.js";

/** A stretch of ticks [start, end). */
export interface Span {
    start: number;
    end: number;
}

/** A press that moves less than this, in pixels, is a tap, not a drag. */
const TAP_SLOP = 4;
/** While dragging, the page is told the new place at most this often, in milliseconds; the last place always goes. */
const SEEK_EVERY_MS = 50;
/** After the drag, the roll keeps showing the place it was let go at until the player is this near it, in ticks… */
const CAUGHT_UP_TICKS = 240;
/** …or this long has passed, in milliseconds. */
const HOLD_MS = 800;

/**
 * Where the playhead may go: inside the span when there is one (before its end, so a loop does not run past its end),
 * else anywhere from the start of the piece to its end. Whole ticks.
 */
export function placeWithin(tick: number, span: Span | null, end: number): number {
    const whole = Math.round(tick);
    if (span !== null && span.end > span.start) {
        return Math.min(Math.max(whole, span.start), span.end - 1);
    }
    return Math.min(Math.max(whole, 0), Math.max(0, end));
}

/** Every beat line of the bars in time order, a bar's start included; a bar with no beat length counts as one beat. */
function* beatLines(bars: readonly RollBar[]): Generator<number> {
    for (const bar of bars) {
        const step = bar.beatTicks > 0 ? bar.beatTicks : bar.end - bar.onset;
        for (let beat = bar.onset; step > 0 && beat < bar.end; beat += step) {
            yield beat;
        }
    }
}

/** The beat a tick lies in: the last beat line at or before it, the first bar's start for a tick before it (a tap's beat, T12). */
export function beatAt(bars: readonly RollBar[], tick: number): number {
    let at = bars[0]?.onset ?? 0;
    for (const beat of beatLines(bars)) {
        if (beat > tick) {
            break;
        }
        at = beat;
    }
    return at;
}

/** The beat line after (1) or before (-1) a tick, on the bars' beats; the tick itself when there is none that way. */
export function beatFrom(bars: readonly RollBar[], tick: number, direction: 1 | -1): number {
    let before = tick;
    for (const beat of beatLines(bars)) {
        if (beat > tick + 0.5) {
            return direction > 0 ? beat : before;
        }
        if (beat < tick - 0.5) {
            before = beat;
        }
    }
    return direction > 0 ? tick : before;
}

/**
 * Where a nudge a beat on (1) or back (-1) takes the playhead from a tick: the next beat inside the span, back to its start
 * at most; null when it cannot move that way.
 */
export function nudged(context: Pick<PlayheadContext, "bars" | "span" | "end">, from: number, direction: 1 | -1): number | null {
    const beat = beatFrom(context.bars, from, direction);
    const target = placeWithin(beat, context.span, context.end);
    if ((direction > 0 && target !== beat) || target === Math.round(from)) {
        return null;
    }
    return target;
}

/** Where a tick is, in words: "Bar 12, beat 3"; empty with no bars. */
export function placeInWords(bars: readonly RollBar[], tick: number, labels: Pick<RollLabels, "playheadAt">): string {
    const bar = bars.find((each) => tick >= each.onset && tick < each.end) ?? bars[bars.length - 1];
    if (bar === undefined) {
        return "";
    }
    const inside = Math.min(Math.max(tick, bar.onset), bar.end - 1);
    const beat = bar.beatTicks > 0 ? Math.floor((inside - bar.onset) / bar.beatTicks) + 1 : 1;
    return fill(labels.playheadAt, bar.number, beat);
}

/**
 * Whether a press takes the playhead: within half a touch target of its line, on the handle under the bar numbers, or with
 * `line` (the iPad's touch reading) anywhere down the line.
 */
export function onPlayhead(x: number, y: number, playheadX: number, header: number, touch: number, line: boolean): boolean {
    if (Math.abs(x - playheadX) > touch / 2 || y < header) {
        return false;
    }
    return line || y < header + touch;
}

/** What the playhead control asks of the roll: where it may go and where it is. */
export interface PlayheadContext {
    bars: readonly RollBar[];
    /** The loop, else the selection; null for neither. */
    span: Span | null;
    end: number;
    /** Where the player is now. */
    tick: number;
}

/**
 * The playhead's control: a range input over the handle, which the keyboard and a screen reader use (its value is the
 * place, its text the bar and beat), and the drag of the handle by a pointer.
 */
export class PlayheadControl {
    readonly element: HTMLInputElement;
    private readonly context: () => PlayheadContext | null;
    private readonly seek: (tick: number, play: boolean) => void;
    private readonly changed: () => void;
    private press: { pointer: number; x: number; moved: boolean; sentAt: number; sent: number } | null = null;
    /** The place shown before the player reports it: while dragging, and a moment after. */
    private shown: { tick: number; until: number } | null = null;
    private words = "";

    constructor(context: () => PlayheadContext | null, seek: (tick: number, play: boolean) => void, changed: () => void) {
        this.context = context;
        this.seek = seek;
        this.changed = changed;
        const input = document.createElement("input");
        input.type = "range";
        input.className = "roll-playhead";
        input.min = "0";
        input.step = "1";
        // Over the handle, so the focus and VoiceOver's frame sit on it; the canvas under it takes the pointer.
        Object.assign(input.style, {
            position: "absolute",
            zIndex: "1",
            width: "var(--h-touch)",
            height: "var(--h-touch)",
            margin: "0",
            opacity: "0",
            pointerEvents: "none",
        });
        input.addEventListener("keydown", this.onKeyDown);
        input.addEventListener("input", this.onInput);
        input.addEventListener("focus", changed);
        input.addEventListener("blur", changed);
        this.element = input;
    }

    get focused(): boolean {
        return document.activeElement === this.element;
    }

    get dragging(): boolean {
        return this.press?.moved === true;
    }

    /** The tick to draw the playhead at: the place being dragged to, else the player's. */
    heard(now: number): number {
        const shown = this.shown;
        if (shown === null) {
            return now;
        }
        if (this.press === null && (Math.abs(now - shown.tick) <= CAUGHT_UP_TICKS || performance.now() >= shown.until)) {
            this.shown = null;
            return now;
        }
        return shown.tick;
    }

    /** A press on the playhead begins. */
    begin(pointer: number, x: number): void {
        this.press = { pointer, x, moved: false, sentAt: Number.NEGATIVE_INFINITY, sent: Number.NaN };
    }

    /** A move of the press on the playhead: past a tap it drags the playhead there. False when the press is not this one. */
    move(pointer: number, x: number, tickAt: number): boolean {
        const press = this.press;
        if (press === null || press.pointer !== pointer) {
            return false;
        }
        if (!press.moved && Math.abs(x - press.x) < TAP_SLOP) {
            return true;
        }
        press.moved = true;
        const context = this.context();
        if (context === null) {
            return true;
        }
        const target = placeWithin(tickAt, context.span, context.end);
        this.show(target);
        const now = performance.now();
        if (target !== press.sent && now - press.sentAt >= SEEK_EVERY_MS) {
            press.sent = target;
            press.sentAt = now;
            this.seek(target, false);
        }
        return true;
    }

    /** The press on the playhead ends: "dragged" when it moved, "tapped" when not, null when it is not this one. */
    end(pointer: number): "dragged" | "tapped" | null {
        const press = this.press;
        if (press === null || press.pointer !== pointer) {
            return null;
        }
        this.press = null;
        if (!press.moved) {
            return "tapped";
        }
        const shown = this.shown;
        if (shown !== null) {
            shown.until = performance.now() + HOLD_MS;
            if (shown.tick !== press.sent) {
                this.seek(shown.tick, false);
            }
        }
        return "dragged";
    }

    cancel(): void {
        this.press = null;
        this.shown = null;
    }

    /** Moves the playhead a beat on (1) or back (-1), inside the span: back to its start at most, never past its last beat. */
    nudge(direction: 1 | -1): void {
        const context = this.context();
        if (context === null) {
            return;
        }
        const from = this.shown?.tick ?? context.tick;
        const target = nudged(context, from, direction);
        if (target !== null) {
            this.go(target, false);
        }
    }

    /** Moves the playhead to a tick, and plays from there with `play`; the roll shows it there until the player reports it. */
    go(tick: number, play: boolean): void {
        this.shown = { tick, until: performance.now() + HOLD_MS };
        this.changed();
        this.seek(tick, play);
    }

    /** Puts the control over the handle and gives it the place, in numbers and in words. */
    place(left: number, top: number, touch: number, context: PlayheadContext, labels: Pick<RollLabels, "playhead" | "playheadAt">): void {
        const style = this.element.style;
        style.left = `${Math.round(left - touch / 2)}px`;
        style.top = `${Math.round(top)}px`;
        const span = context.span ?? { start: 0, end: context.end };
        this.element.min = String(span.start);
        this.element.max = String(Math.max(span.start, span.end - 1));
        this.element.value = String(Math.round(this.heard(context.tick)));
        this.element.setAttribute("aria-label", labels.playhead);
        const words = placeInWords(context.bars, this.heard(context.tick), labels);
        if (words !== this.words) {
            this.words = words;
            this.element.setAttribute("aria-valuetext", words);
        }
    }

    private show(tick: number): void {
        this.shown = { tick, until: Number.POSITIVE_INFINITY };
        this.changed();
    }

    private readonly onKeyDown = (event: KeyboardEvent): void => {
        const step = event.key === "ArrowRight" || event.key === "ArrowUp" ? 1 : event.key === "ArrowLeft" || event.key === "ArrowDown" ? -1 : 0;
        if (step === 0) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        this.nudge(step);
    };

    /** A screen reader's increment or decrement (VoiceOver's swipe up or down) changes the value: a beat that way. */
    private readonly onInput = (): void => {
        const context = this.context();
        if (context === null) {
            return;
        }
        const from = Math.round(this.shown?.tick ?? context.tick);
        const value = Number(this.element.value);
        this.element.value = String(from);
        if (value !== from) {
            this.nudge(value > from ? 1 : -1);
        }
    };
}
