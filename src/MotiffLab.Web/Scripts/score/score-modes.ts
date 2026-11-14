// The rules of version 0.5 on the score (the experiment Two playheads and modes, Settings · Experimental; product specs
// two-playheads and interaction-modes, «Правила выделения по режимам»): one group of modes in the bar, Navigate (H),
// Select (S), Edit notes (E) and Pen (P), each with its own cursor. The selection goes by beats and changes only in Select,
// by a drag or its handles, which show only there (v0.5.7, #543 2-4); a click never changes it but puts a playhead on the
// beat under it; in Navigate a drag moves the score under the mouse as a hand would, and a long press of a finger or the pen
// puts the black playhead at the place under it (playhead-grab.ts ScorePress). Which beat is music-core's (the beat lines of the playback score).

import type { ScoreBar, ScoreGeometry } from "./score-geometry.js";
import { type Beat, ScoreBeats } from "./selection-beats.js";

export type InteractionMode = "navigate" | "select" | "edit" | "pen";

/** The modes in the bar's order. */
export const MODES: readonly InteractionMode[] = ["navigate", "select", "edit", "pen"];

/** How far the mouse moves before a press in Navigate moves the score, in pixels (as a press becomes a drag across bars). */
export const PAN_DISTANCE = 6;

/** A click this far above or below a bar's staves still lands on the bar, in pixels. */
const STAFF_REACH = 24;

/** The class the score takes while the mouse moves it in Navigate: the hand closes. */
export const PANNING_CLASS = "panning";

/** A mode named by the bar, or null for none (the rules before version 0.5). */
export function modeOf(value: string | null | undefined): InteractionMode | null {
    return MODES.find((mode) => mode === value) ?? null;
}

/** Whether a point lies inside one of the boxes, all in the score's content pixels. */
export function inside(boxes: readonly { left: number; top: number; right: number; bottom: number }[], x: number, y: number): boolean {
    return boxes.some((box) => x >= box.left && x <= box.right && y >= box.top && y <= box.bottom);
}

/** The bar a click lands on: across its barlines, and on its staves or just above or below them; none on the paper around. */
export function barUnder(geometry: ScoreGeometry, x: number, y: number): ScoreBar | undefined {
    const bar = geometry.barNear(x, y);
    return bar !== undefined && x >= bar.left && x <= bar.right && y >= bar.top - STAFF_REACH && y <= bar.bottom + STAFF_REACH ? bar : undefined;
}

/**
 * The beat a click puts a playhead on: the beat under the point, from one of music-core's beat lines to the next
 * (two-playheads.md, question 3: to the nearest beat). None off the bars, or without beat lines.
 */
export function beatUnder(geometry: ScoreGeometry, beats: ScoreBeats, x: number, y: number): Beat | undefined {
    const lines = beats.lines();
    return lines === null || barUnder(geometry, x, y) === undefined ? undefined : beats.near(geometry, lines, x, y);
}

interface Pan {
    pointerId: number;
    x: number;
    y: number;
    top: number;
    left: number;
    moving: boolean;
}

/** In Navigate the mouse moves the score: pressed and dragged, the page follows it; the click that ends the drag is not a click. */
export class ScorePan {
    private pan: Pan | null = null;
    private swallowNextClick = false;

    constructor(
        private readonly host: HTMLElement,
        private readonly owner: { mode(): InteractionMode | null; scrolled(): void },
    ) {
        host.addEventListener("pointerdown", this.onPointerDown);
        host.addEventListener("pointermove", this.onPointerMove);
        host.addEventListener("pointerup", this.onPointerUp);
        host.addEventListener("pointercancel", this.onPointerUp);
    }

    /** Whether the click the browser sends now ends a drag, and is not a click on the score. */
    swallowsClick(): boolean {
        const swallow = this.swallowNextClick;
        this.swallowNextClick = false;
        return swallow;
    }

    dispose(): void {
        this.host.removeEventListener("pointerdown", this.onPointerDown);
        this.host.removeEventListener("pointermove", this.onPointerMove);
        this.host.removeEventListener("pointerup", this.onPointerUp);
        this.host.removeEventListener("pointercancel", this.onPointerUp);
    }

    private readonly onPointerDown = (event: PointerEvent): void => {
        if (event.pointerType !== "mouse" || event.button !== 0 || this.owner.mode() !== "navigate") {
            return;
        }
        // The handles, the brackets and every control over the score are their own targets.
        if (event.target instanceof Element && event.target.closest("button, a, input") !== null) {
            return;
        }
        this.pan = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, top: this.host.scrollTop, left: this.host.scrollLeft, moving: false };
    };

    private readonly onPointerMove = (event: PointerEvent): void => {
        const pan = this.pan;
        if (pan === null || event.pointerId !== pan.pointerId) {
            return;
        }
        const dx = event.clientX - pan.x;
        const dy = event.clientY - pan.y;
        if (!pan.moving) {
            if (Math.hypot(dx, dy) < PAN_DISTANCE) {
                return;
            }
            pan.moving = true;
            this.host.classList.add(PANNING_CLASS);
            try {
                this.host.setPointerCapture(pan.pointerId);
            } catch {
                // The pointer is gone already; the drag ends with it.
            }
        }
        this.host.scrollTop = pan.top - dy;
        this.host.scrollLeft = pan.left - dx;
        this.owner.scrolled();
    };

    private readonly onPointerUp = (event: PointerEvent): void => {
        const pan = this.pan;
        if (pan === null || event.pointerId !== pan.pointerId) {
            return;
        }
        this.pan = null;
        this.host.classList.remove(PANNING_CLASS);
        if (pan.moving) {
            this.swallowNextClick = true;
            // A click may not follow (the pointer left the score); the next real click is not swallowed.
            window.setTimeout(() => (this.swallowNextClick = false), 0);
        }
    };
}

/** What the rules need from the score view. */
export interface RulesHost {
    geometry(): ScoreGeometry | null;
    /** Where a tick sounds across its bar on screen (the playhead's place). */
    tickX(tick: number): number | undefined;
    onsetOf(id: string): number | undefined;
    pitchOf(id: string): number | undefined;
    /** The player moved the score by hand: playback leaves it where they put it for a while. */
    scrolled(): void;
    /** A click on a beat, from one beat line to the next, in ticks; Shift held or not. */
    beatClicked(start: number, end: number, extend: boolean): void;
    /** A click in Edit notes on a note: when it starts and its key (-1 for a chord's stem). */
    noteToEdit(onset: number, pitch: number): void;
}

/** The rules of version 0.5 on one score: the mode, music-core's beat lines, the hand in Navigate and what a click does. */
export class ScoreRules {
    private current: InteractionMode | null = null;
    private lines: number[] | null = null;
    readonly beats: ScoreBeats;
    private readonly pan: ScorePan;

    constructor(
        private readonly host: HTMLElement,
        private readonly owner: RulesHost,
    ) {
        this.beats = new ScoreBeats({ beats: () => this.beatLines(), tickX: (tick) => owner.tickX(tick) });
        this.pan = new ScorePan(host, { mode: () => this.current, scrolled: () => owner.scrolled() });
    }

    /** The mode and the beat lines (ticks, in time order); a null mode turns the rules off. */
    set(mode: string | null, lines: readonly number[] | null): void {
        this.current = modeOf(mode);
        this.lines = lines !== null && lines.length > 0 ? [...lines] : null;
        if (this.current === null) {
            delete this.host.dataset.mode;
        } else {
            this.host.dataset.mode = this.current;
        }
    }

    mode(): InteractionMode | null {
        return this.current;
    }

    /** The beat lines the selection snaps to while the rules hold; null otherwise. */
    beatLines(): readonly number[] | null {
        return this.current === null ? null : this.lines;
    }

    /** Handles a click on the score that no mark took; false while the rules are off, so the click is the view's as before. */
    click(event: MouseEvent, swallowed: boolean): boolean {
        if (this.current === null) {
            return false;
        }
        // Both are asked, so neither keeps a click to swallow for later.
        const panned = this.pan.swallowsClick();
        if (swallowed || panned || this.current === "pen") {
            return true;
        }
        const geometry = this.owner.geometry();
        if (geometry === null) {
            return true;
        }
        if (this.current === "edit" && this.editNote(event.target)) {
            return true;
        }
        const view = this.host.getBoundingClientRect();
        const x = event.clientX - view.left + this.host.scrollLeft;
        const y = event.clientY - view.top + this.host.scrollTop;
        const beat = beatUnder(geometry, this.beats, x, y);
        if (beat !== undefined) {
            this.owner.beatClicked(beat.start, beat.end, event.shiftKey);
        }
        return true;
    }

    dispose(): void {
        this.pan.dispose();
    }

    /** In Edit notes a click on a note (or a chord's stem) opens its correction; false when it fell beside the notes. */
    private editNote(target: EventTarget | null): boolean {
        const hit = target instanceof Element ? target.closest("g.note, g.chord") : null;
        const note = hit?.matches("g.chord") ? hit.querySelector("g.note") : hit;
        const onset = note?.id ? this.owner.onsetOf(note.id) : undefined;
        if (note == null || onset === undefined) {
            return false;
        }
        const pitch = hit?.matches("g.note") ? this.owner.pitchOf(note.id) : undefined;
        this.owner.noteToEdit(onset, pitch ?? -1);
        return true;
    }
}
