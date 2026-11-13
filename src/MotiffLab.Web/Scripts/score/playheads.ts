// The playheads of version 0.5 on the score (the experiment Two playheads and modes, Settings · Experimental; product spec
// two-playheads, as the mockup v0.5.2 settled it, #479 item 19). The place in the piece is variant A: a short track under
// the system that plays, only as wide as the bar, filled up to the place, with a dot on it, in the notation's dark ink;
// the dot is solid while the piece plays and hollow when it stands. Nothing covers the notes and no bar fades. The
// selection's playhead is a cobalt line with a knob inside the selection's band: cobalt is the selection's colour only.
// The web app has two places (the piece's and the selection's, placesOf); the iPad has one, which is the selection's
// cobalt line inside it and the ink track outside it (onePlace).

import type { Playhead, ScoreBar } from "./score-geometry.js";

/** What Blazor says (Score/ScorePlayheads.cs). */
export interface PlayheadsState {
    /** The selection has the player: the player's place is the selection playhead's, the global one waits at `global`. */
    selectionHolds: boolean;
    global: number;
    /** Where the selection playhead waits while the piece has the player; null without a selection. */
    selection: number | null;
}

/** Where each playhead goes this frame, in ticks; null hides it. */
export interface PlayheadPlaces {
    global: number | null;
    selection: number | null;
    /** Whether the piece plays from the global place: its dot is solid. */
    globalPlays: boolean;
}

/** The places of the two playheads from what Blazor said and the player's place. */
export function placesOf(state: PlayheadsState, playerTick: number, playing: boolean): PlayheadPlaces {
    return {
        global: state.selectionHolds ? state.global : playerTick,
        selection: state.selectionHolds ? playerTick : state.selection,
        globalPlays: playing && !state.selectionHolds,
    };
}

/** A stretch of ticks [start, end). */
export interface TickSpan {
    start: number;
    end: number;
}

/**
 * The one playback position of the iPad (version 0.5.2, #479 item 25): inside the selection it is the selection's cobalt
 * line, outside it the ink track, never both.
 */
export function onePlace(selection: TickSpan | null, playerTick: number, playing: boolean): PlayheadPlaces {
    const inside = selection !== null && playerTick >= selection.start && playerTick < selection.end;
    return {
        global: inside ? null : playerTick,
        selection: inside ? playerTick : null,
        globalPlays: playing && !inside,
    };
}

/** The track keeps this far from the bar lines at each side, and this far under the lowest ink of its system, in pixels. */
const TRACK_INSET = 3;
const TRACK_GAP = 10;
/** The selection playhead reaches this far above and below the staves, its knob on top. */
const LINE_REACH = 8;

/** Where the track goes for a place: across its bar, under its system, and how far it is filled. */
export function trackOf(at: Playhead, lineBottom: number): { left: number; top: number; width: number; filled: number } {
    const left = at.bar.left + TRACK_INSET;
    const width = Math.max(0, at.bar.right - at.bar.left - 2 * TRACK_INSET);
    const top = Math.max(at.bottom, lineBottom) + TRACK_GAP;
    return { left, top, width, filled: Math.min(width, Math.max(0, at.x - left)) };
}

/** What the playheads need from the score view. */
export interface PlayheadsHost {
    /** Where a tick sounds on screen: across its bar and the system's staves. */
    at(tick: number): Playhead | undefined;
    /** The lowest ink of the system a bar is on, so the track runs under every note of the line. */
    lineBottom(bar: ScoreBar): number;
}

/** The playheads drawn over one score. */
export class ScorePlayheadMarks {
    private track: { element: HTMLElement; fill: HTMLElement; dot: HTMLElement } | null = null;
    private line: HTMLElement | null = null;

    constructor(
        private readonly host: HTMLElement,
        private readonly owner: PlayheadsHost,
    ) {}

    /** Draws the playheads; returns where the one that plays is, for the score to follow and to say when it is out of sight. */
    place(places: PlayheadPlaces): Playhead | undefined {
        const global = places.global === null ? undefined : this.owner.at(places.global);
        const selection = places.selection === null ? undefined : this.owner.at(places.selection);
        this.placeGlobal(global, places.globalPlays);
        this.placeSelection(selection);
        return places.globalPlays || places.selection === null ? global : selection;
    }

    hide(): void {
        hideAll(this.track?.element ?? null, this.line);
    }

    dispose(): void {
        this.hide();
        this.track?.element.remove();
        this.line?.remove();
    }

    private placeGlobal(at: Playhead | undefined, plays: boolean): void {
        if (at === undefined) {
            hideAll(this.track?.element ?? null);
            return;
        }
        const track = (this.track ??= trackMark());
        const place = trackOf(at, this.owner.lineBottom(at.bar));
        show(this.host, track.element);
        transform(track.element, place.left, place.top);
        size(track.element, place.width, null);
        size(track.fill, place.filled, null);
        transform(track.dot, place.filled, 0);
        track.element.classList.toggle("stop", !plays);
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
}

/** The track with its fill and its dot. */
function trackMark(): { element: HTMLElement; fill: HTMLElement; dot: HTMLElement } {
    const element = mark("gtrack");
    const fill = mark("fill");
    const dot = mark("dot");
    fill.hidden = false;
    dot.hidden = false;
    element.append(fill, dot);
    return { element, fill, dot };
}

/** An overlay over the score that takes no taps and is not read out. */
function mark(className: string): HTMLElement {
    const element = document.createElement("span");
    element.className = className;
    element.setAttribute("aria-hidden", "true");
    element.hidden = true;
    return element;
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

function size(element: HTMLElement, width: number | null, height: number | null): void {
    if (width !== null) {
        const w = `${width}px`;
        if (element.style.width !== w) {
            element.style.width = w;
        }
    }
    if (height !== null) {
        const h = `${height}px`;
        if (element.style.height !== h) {
            element.style.height = h;
        }
    }
}
