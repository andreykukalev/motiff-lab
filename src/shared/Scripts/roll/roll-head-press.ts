// The playheads taken by hand on the roll (the input router's, roll-input.ts; the audit's W3.12): the line taken
// (roll-playhead.ts RollPlayhead), a playhead taken by its handle and dragged on its grid (roll-head-drag.ts laneDragTick,
// sent through playback/playhead-drag.ts), a navigating press held still that puts the black playhead there (HoldToPlace),
// and the view going along while a playhead is held at an edge. Which playhead a press takes is PianoRoll.playheadUnder's.

import { measures } from "./roll-palette.js";
import { laneDragTick } from "./roll-head-drag.js";
import { CLICK_SLOP } from "./roll-press.js";
import { edgeScroll } from "./roll-view.js";
import { GUTTER } from "./roll-viewport.js";
import { HoldToPlace } from "../playback/hold-place.js";
import type { Handle } from "../playback/playback-model.js";
import type { InputHost } from "./roll-input.js";

/** What a playhead's press reads of the roll. */
export type HeadPressHost = Pick<InputHost, "view" | "editor" | "playhead" | "laneDrag" | "model" | "width" | "hasPlayer" | "playing" | "playheadSpan" | "headBand" | "changed">;

/** The playheads' presses on the notes canvas. */
export class RollHeadPress {
    /** A navigating press held still (#543 3.1, lead decision 2): the black playhead goes where it is held, then follows it. */
    private readonly hold: HoldToPlace;
    /** Where the press on a playhead's handle began, until it moves past a tap. */
    private lanePress: number | null = null;
    /** A playhead dragged on the roll, by which pointer and where across: held at an edge, the view goes that way (#521 1.2). */
    private headDrag: { pointer: number; x: number } | null = null;

    /** `took` hears the pointer of a press a hold made the playhead's: what else it began goes. */
    constructor(
        private readonly host: HeadPressHost,
        took: (pointer: number) => void,
    ) {
        this.hold = new HoldToPlace((pointer, x) => this.holdPlaced(pointer, x, took));
    }

    /** A navigating press begins to wait for its hold. */
    waitForHold(pointer: number, x: number, y: number): void {
        this.hold.begin(pointer, x, y);
    }

    /** Every move comes here first, so a press waiting for its hold hears it. */
    heardMove(pointer: number, x: number, y: number): void {
        this.hold.move(pointer, x, y);
    }

    /** A pointer went up: a hold that fired made the press the playhead's (release lets it go); one still waiting is a tap. */
    heardUp(pointer: number): void {
        this.hold.end(pointer);
    }

    /** The pointer's press ended: no playhead is held at an edge for it any more. */
    letGo(): void {
        this.headDrag = null;
    }

    /** A playhead whose pointer the roll lost is let go, and a hold waiting for it (#495 UX-08). */
    lost(pointer: number): void {
        if (this.hold.waits(pointer)) {
            this.hold.cancel();
        }
        this.host.laneDrag.release(pointer);
    }

    /** No hold waits and no playhead is held, sending nothing more. */
    cancel(): void {
        this.hold.cancel();
        this.host.playhead.cancel();
        this.host.laneDrag.release();
        this.headDrag = null;
    }

    /**
     * A playhead taken (PianoRoll.playheadUnder): by its handle (#533 4), while the music plays it stands as soon as it is
     * held, the press telling the player at once (T14), and plays on from where it is let go (T16); stopped, a tap on it moves
     * nothing.
     */
    begin(head: Handle | "line", pointer: number, x: number): void {
        if (head === "line") {
            this.host.playhead.begin(pointer, x);
            return;
        }
        this.host.laneDrag.grab(head, pointer, this.host.playing());
        this.lanePress = x;
    }

    /**
     * A move of the press on a playhead's handle, past a tap: it goes to the tick under it, held as its handle is, the blue
     * one to the selection (the web's from the player's model, the same band).
     */
    moveHandle(pointer: number, x: number): boolean {
        const laneDrag = this.host.laneDrag;
        if (laneDrag.handle === null || this.host.model() === null || !laneDrag.holds(pointer)) {
            return false;
        }
        if (this.lanePress !== null && Math.abs(x - this.lanePress) < CLICK_SLOP) {
            return true;
        }
        this.lanePress = null;
        this.headDrag = { pointer, x };
        this.dragTo(pointer, x);
        return true;
    }

    /** A move of the playhead's line taken (RollPlayhead); while it is dragged, held at an edge the view goes along. */
    moveLine(pointer: number, x: number): boolean {
        const playhead = this.host.playhead;
        if (!playhead.move(pointer, x, this.host.view.tickAtX(Math.max(GUTTER, x)))) {
            return false;
        }
        this.headDrag = playhead.dragging ? { pointer, x } : null;
        return true;
    }

    /** The end of a press on the playhead at x: a drag has moved it; a tap on it in touch reading plays from there. */
    release(pointer: number, x: number, touchReading: boolean): boolean {
        const { laneDrag, playhead } = this.host;
        if (laneDrag.holds(pointer)) {
            laneDrag.release(pointer);
            this.host.changed();
            return true;
        }
        const ended = playhead.end(pointer);
        if (ended === null) {
            return false;
        }
        if (ended === "tapped" && touchReading) {
            this.playFrom(x);
        }
        this.host.changed();
        return true;
    }

    /** A tap inside the loop, or the selection, plays from there (touch reading); anywhere else it does nothing (#338). */
    playFrom(x: number): void {
        const span = this.host.playheadSpan();
        const tick = this.host.view.tickAtX(x);
        if (span !== null && !this.host.editor.editing && tick >= span.start && tick < span.end) {
            this.host.playhead.go(Math.round(tick), true);
        }
    }

    /**
     * While a playhead is dragged on the roll near its left or right edge, the view goes that way, faster the nearer the
     * edge, and the playhead stays under the pointer (#521 1.2), so it goes on past what was in view without being let go.
     */
    stepEdgeScroll(elapsed: number): void {
        const held = this.headDrag;
        const { view, playhead } = this.host;
        if (held === null || this.host.model() === null) {
            return;
        }
        const lane = this.host.laneDrag.holds(held.pointer);
        if (!lane && !playhead.dragging) {
            this.headDrag = null;
            return;
        }
        const delta = edgeScroll(held.x, { left: GUTTER, right: this.host.width(), zone: measures.touch }, view.visibleTicks(), elapsed);
        if (delta === 0) {
            return;
        }
        const before = view.scroll;
        view.scrollTo(before + delta);
        if (view.scroll === before) {
            return;
        }
        if (lane) {
            this.dragTo(held.pointer, held.x);
        } else {
            playhead.move(held.pointer, held.x, view.tickAtX(Math.max(GUTTER, held.x)));
        }
    }

    /**
     * The held playhead goes to the place under a point across the roll, on its grid and inside its band; `place` puts it
     * down there (a long press, #582 item 12), silent over what it jumped.
     */
    private dragTo(pointer: number, x: number, place = false): void {
        const laneDrag = this.host.laneDrag;
        const handle = laneDrag.handle;
        const model = this.host.model();
        if (handle === null || model === null) {
            return;
        }
        const bars = model.bars.map((bar) => ({ start: bar.onset, end: bar.end }));
        const tick = laneDragTick(handle, this.host.view.tickAtX(Math.max(GUTTER, x)), bars, this.host.headBand(), model.end, model.onsets);
        if (tick !== undefined) {
            if (place) {
                laneDrag.place(pointer, tick);
            } else {
                laneDrag.move(pointer, tick);
            }
            this.host.changed();
        }
    }

    /**
     * A navigating press held still (#543 3.1, lead decision 2): the black playhead goes to the place under the pointer on the
     * sixteenth grid (laneDragTick), inside the selection or not, and the press holds it from here: moved on, it scrubs as a
     * taken playhead (T14–T16); playing, the music stands while it is held and plays on from where it is let go. Nothing
     * else the press began goes on, and the selection stays as it is. In Expand it is the passage's playhead, kept inside it.
     */
    private holdPlaced(pointer: number, x: number, took: (pointer: number) => void): void {
        const handle = this.host.view.passage === null ? "black" : "blue";
        if (this.host.model() === null || !this.host.hasPlayer() || !this.host.laneDrag.grab(handle, pointer, this.host.playing())) {
            return;
        }
        took(pointer);
        this.lanePress = null;
        this.headDrag = { pointer, x };
        this.dragTo(pointer, x, true);
    }
}
