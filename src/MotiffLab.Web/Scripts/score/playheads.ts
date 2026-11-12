// The two playheads of version 0.5 on the score (the experiment Two playheads and modes, Settings · Experimental; product
// spec two-playheads, as the mockup «Указатели и режимы» settled it). The global playhead is «Полоса»: a neutral column one
// beat wide behind the notes with a cap above the system, solid while the bar's Play plays and hollow when stopped. The
// selection playhead is a cobalt line with a knob inside the selection's band. While the global Play plays the other bars
// fade to about a third (a sub-option, on by default); the selection's Play fades nothing. One player, two places: the
// player's place is the one that has it, the other waits where Blazor says.

import type { Playhead, ScoreBar } from "./score-geometry.js";
import { barLines } from "./selection-beats.js";

/** What Blazor says (Score/ScorePlayheads.cs). */
export interface PlayheadsState {
    /** The selection has the player: the player's place is the selection playhead's, the global one waits at `global`. */
    selectionHolds: boolean;
    global: number;
    /** Where the selection playhead waits while the piece has the player; null without a selection. */
    selection: number | null;
    fade: boolean;
}

/** Where each playhead goes this frame, in ticks; null hides it. */
export interface PlayheadPlaces {
    global: number | null;
    selection: number | null;
    /** Whether the global Play plays: the cap fills and the other bars may fade. */
    globalPlays: boolean;
    fade: boolean;
}

/** The places of the two playheads from what Blazor said and the player's place. */
export function placesOf(state: PlayheadsState, playerTick: number, playing: boolean): PlayheadPlaces {
    const globalPlays = playing && !state.selectionHolds;
    return {
        global: state.selectionHolds ? state.global : playerTick,
        selection: state.selectionHolds ? playerTick : state.selection,
        globalPlays,
        fade: state.fade && globalPlays,
    };
}

/** The beat a tick falls in among a bar's beat lines: from the last line at or before it to the next; undefined without two lines. */
export function beatOf(lines: readonly number[], tick: number): { start: number; end: number } | undefined {
    let index = -1;
    for (let i = 0; i < lines.length - 1; i++) {
        if (lines[i] <= tick) {
            index = i;
        }
    }
    return index < 0 ? undefined : { start: lines[index], end: lines[index + 1] };
}

/** The column when the bar has no beat lines: this wide round the playhead's place, in pixels (the mockup's 28). */
const COLUMN_WIDTH = 28;
/** The cap stands this far above the system's top staff line; the column reaches this far above and below the staves. */
const CAP_RISE = 20;
const COLUMN_REACH = 6;
/** The selection playhead reaches this far above and below the staves, its knob on top. */
const LINE_REACH = 8;

export const FADING_CLASS = "fading";
export const LIT_CLASS = "lit";

const CAP_PATH = "M1 1h12L7 9z";

/** What the playheads need from the score view. */
export interface PlayheadsHost {
    /** Where a tick sounds on screen: across its bar and the system's staves. */
    at(tick: number): Playhead | undefined;
    /** music-core's beat lines, in ticks; null while there are none. */
    beatLines(): readonly number[] | null;
    /** Where a beat line of a bar is across. */
    lineX(bar: ScoreBar, line: number): number;
    /** A bar's drawing, by its id. */
    element(id: string): Element | null;
}

/** The two playheads drawn over one score. */
export class ScorePlayheadMarks {
    private column: HTMLElement | null = null;
    private cap: HTMLElement | null = null;
    private line: HTMLElement | null = null;
    private lit: Element | null = null;

    constructor(
        private readonly host: HTMLElement,
        private readonly owner: PlayheadsHost,
    ) {}

    /** Draws both playheads; returns where the one that plays is, for the score to follow and to say when it is out of sight. */
    place(places: PlayheadPlaces): Playhead | undefined {
        const global = places.global === null ? undefined : this.owner.at(places.global);
        const selection = places.selection === null ? undefined : this.owner.at(places.selection);
        this.placeGlobal(global, places);
        this.placeSelection(selection);
        this.fade(places.fade ? global?.bar : undefined);
        return places.globalPlays ? global : selection;
    }

    hide(): void {
        for (const element of [this.column, this.cap, this.line]) {
            if (element !== null) {
                element.hidden = true;
            }
        }
        this.fade(undefined);
    }

    dispose(): void {
        this.hide();
        for (const element of [this.column, this.cap, this.line]) {
            element?.remove();
        }
    }

    private placeGlobal(at: Playhead | undefined, places: PlayheadPlaces): void {
        if (at === undefined) {
            hideAll(this.column, this.cap);
            return;
        }
        const column = (this.column ??= mark("gcol"));
        const cap = (this.cap ??= capMark());
        const [left, right] = this.columnAcross(at, places.global ?? 0);
        show(this.host, column);
        show(this.host, cap);
        transform(column, left, at.top - COLUMN_REACH);
        size(column, right - left, at.bottom - at.top + 2 * COLUMN_REACH);
        transform(cap, (left + right) / 2, at.top - CAP_RISE);
        cap.classList.toggle("stop", !places.globalPlays);
    }

    /** The column across the beat the tick is in, from music-core's beat lines; a fixed width round the place without them. */
    private columnAcross(at: Playhead, tick: number): [number, number] {
        const lines = this.owner.beatLines();
        const beat = lines === null ? undefined : beatOf(barLines(lines, at.bar), tick);
        if (beat === undefined) {
            return [at.x - COLUMN_WIDTH / 2, at.x + COLUMN_WIDTH / 2];
        }
        const left = this.owner.lineX(at.bar, beat.start);
        const right = this.owner.lineX(at.bar, beat.end);
        return right - left >= 2 ? [left, right] : [at.x - COLUMN_WIDTH / 2, at.x + COLUMN_WIDTH / 2];
    }

    private placeSelection(at: Playhead | undefined): void {
        if (at === undefined) {
            hideAll(this.line);
            return;
        }
        const line = (this.line ??= lineMark());
        show(this.host, line);
        transform(line, at.x - 1, at.top - LINE_REACH);
        size(line, null, at.bottom - at.top + 2 * LINE_REACH);
    }

    /** The other bars fade while the global Play plays; the bar playing stays lit. */
    private fade(bar: ScoreBar | undefined): void {
        this.host.classList.toggle(FADING_CLASS, bar !== undefined);
        const lit = bar === undefined ? null : this.owner.element(bar.id);
        if (lit !== this.lit) {
            this.lit?.classList.remove(LIT_CLASS);
            lit?.classList.add(LIT_CLASS);
            this.lit = lit;
        }
    }
}

/** An overlay over the score that takes no taps and is not read out. */
function mark(className: string): HTMLElement {
    const element = document.createElement("span");
    element.className = className;
    element.setAttribute("aria-hidden", "true");
    element.hidden = true;
    return element;
}

function capMark(): HTMLElement {
    const cap = mark("gcap");
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("width", "14");
    svg.setAttribute("height", "10");
    svg.setAttribute("viewBox", "0 0 14 10");
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", CAP_PATH);
    svg.append(path);
    cap.append(svg);
    return cap;
}

function lineMark(): HTMLElement {
    const line = mark("ph-sel");
    line.append(mark("knob"));
    line.firstElementChild?.removeAttribute("hidden");
    return line;
}

function show(host: HTMLElement, element: HTMLElement): void {
    if (element.parentElement !== host) {
        host.append(element);
    }
    element.hidden = false;
}

function hideAll(...elements: (HTMLElement | null)[]): void {
    for (const element of elements) {
        if (element !== null) {
            element.hidden = true;
        }
    }
}

/** Moved by a transform on a layer of its own, so the page under it is not painted again on every frame (testing item 31). */
function transform(element: HTMLElement, x: number, y: number): void {
    const value = `translate(${x}px, ${y}px)`;
    if (element.style.transform !== value) {
        element.style.transform = value;
    }
}

function size(element: HTMLElement, width: number | null, height: number): void {
    const w = width === null ? "" : `${width}px`;
    const h = `${height}px`;
    if (width !== null && element.style.width !== w) {
        element.style.width = w;
    }
    if (element.style.height !== h) {
        element.style.height = h;
    }
}
