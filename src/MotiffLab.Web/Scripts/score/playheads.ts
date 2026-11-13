// The playheads of version 0.5 on the score (the experiment Two playheads and modes, Settings · Experimental; product spec
// two-playheads, as the mockup v0.5.2 settled it, #479 item 19). The place in the piece is variant A: a short track under
// the system that plays, only as wide as the bar, filled up to the place, with a dot on it, in the notation's dark ink;
// the dot is solid while the piece plays and hollow when it stands. Nothing covers the notes and no bar fades. The
// selection's playhead is a cobalt line with a knob inside the selection's band: cobalt is the selection's colour only.
// The web app has two places (the piece's and the selection's, placesOf); the iPad draws both playheads of the playback
// model (v0.5.2, #495 UX-10, Scripts/playback/playback-model.ts, playheadsOf): the black track at the position, always,
// and with a selection the cobalt line at the position held to it, standing at the selection's end once playback passed it.
// On the iPad the black dot and the cobalt knob are handles the pen or a finger drags (#495 UX-08, playhead-grab.ts). The
// line spans the staves; the stylesheet reaches it beyond them, its knob clear of the top staff line (#495 UX-07).

import type { Handle } from "../playback/playback-model.js";
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
    /** Whether the selection's playhead stands at the selection's end: it is drawn at the band's right edge (edgeOf). */
    selectionAtEnd?: boolean;
    /** The playhead the score follows and reports out of sight; by default the one that plays. */
    follow?: "global" | "selection";
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

/** What playheadsOf reads from the playback model's view (playback-model.ts, viewOf). */
export interface ModelPlaces {
    black: number;
    blue: number | null;
    playing: boolean;
}

/**
 * The two playheads of the playback model (version 0.5.2, #495 UX-10): the black track at the position, always, its dot solid
 * while it plays; with a selection the cobalt line at the blue place, at the band's right edge when it stands at the end.
 * The score follows the black one, which is where the music is.
 */
export function playheadsOf(view: ModelPlaces, selection: TickSpan | null): PlayheadPlaces {
    return {
        global: view.black,
        selection: view.blue,
        globalPlays: view.playing,
        selectionAtEnd: selection !== null && view.blue === selection.end,
        follow: "global",
    };
}

/**
 * Where a playhead standing at the selection's end goes: where the end lies in its bar, or, when the end is a bar line, at
 * the right edge of the bar before it, as the band ends there (the next bar may start another line). `atEnd` and
 * `beforeEnd` are the places of the end and of the tick before it.
 */
export function edgeOf(atEnd: Playhead | undefined, beforeEnd: Playhead | undefined, end: number): Playhead | undefined {
    if (atEnd !== undefined && atEnd.bar.tick !== end) {
        return atEnd;
    }
    return beforeEnd === undefined ? atEnd : { ...beforeEnd, x: beforeEnd.bar.right };
}

/** The track keeps this far from the bar lines at each side, and this far under the lowest ink of its system, in pixels. */
const TRACK_INSET = 3;
const TRACK_GAP = 10;

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
    /** A press on a playhead's handle (the black dot, the cobalt knob); without it the handles take no press (the web). */
    grab?(handle: Handle, event: PointerEvent): void;
}

/** A playhead's handle where it is on screen: its centre in the client's pixels. */
export interface Grip {
    handle: Handle;
    x: number;
    y: number;
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
        const selection = this.selectionAt(places);
        this.placeGlobal(global, places.globalPlays);
        this.placeSelection(selection);
        const follow = places.follow ?? (places.globalPlays || places.selection === null ? "global" : "selection");
        return follow === "global" ? global : selection;
    }

    private selectionAt(places: PlayheadPlaces): Playhead | undefined {
        const tick = places.selection;
        if (tick === null) {
            return undefined;
        }
        return places.selectionAtEnd === true ? edgeOf(this.owner.at(tick), this.owner.at(tick - 1), tick) : this.owner.at(tick);
    }

    hide(): void {
        hideAll(this.track?.element ?? null, this.line);
    }

    /** The handles shown, with their centres in the client's pixels, for the iPad to leave a touch there to them. */
    grips(): Grip[] {
        const shown: [Handle, Element | null | undefined, HTMLElement | null][] = [
            ["black", this.track?.dot, this.track?.element ?? null],
            ["blue", this.line?.firstElementChild, this.line],
        ];
        const grips: Grip[] = [];
        for (const [handle, element, mark] of shown) {
            if (element !== null && element !== undefined && mark !== null && !mark.hidden && mark.isConnected) {
                const box = element.getBoundingClientRect();
                grips.push({ handle, x: box.left + box.width / 2, y: box.top + box.height / 2 });
            }
        }
        return grips;
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
        const track = (this.track ??= trackMark(this.grabber("black")));
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
        const line = (this.line ??= lineMark(this.grabber("blue")));
        show(this.host, line);
        transform(line, at.x - 1, at.top);
        size(line, null, at.bottom - at.top);
    }

    /** What a press on a handle does: the host's grab, or nothing when the host takes none. */
    private grabber(handle: Handle): ((event: PointerEvent) => void) | null {
        const owner = this.owner;
        return owner.grab === undefined ? null : (event) => owner.grab?.(handle, event);
    }
}

/** The track with its fill and its dot, the dot a handle when the host grabs. */
function trackMark(grab: ((event: PointerEvent) => void) | null): { element: HTMLElement; fill: HTMLElement; dot: HTMLElement } {
    const element = mark("gtrack");
    const fill = mark("fill");
    const dot = mark("dot");
    fill.hidden = false;
    dot.hidden = false;
    grip(dot, grab);
    element.append(fill, dot);
    return { element, fill, dot };
}

/** Makes a mark a handle that takes a press; the iPad's score.css gives it a touch target and keeps the page still there. */
function grip(element: HTMLElement, grab: ((event: PointerEvent) => void) | null): void {
    if (grab !== null) {
        element.classList.add("grip");
        element.addEventListener("pointerdown", grab);
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

function lineMark(grab: ((event: PointerEvent) => void) | null): HTMLElement {
    const line = mark("ph-sel");
    const knob = mark("knob");
    knob.hidden = false;
    grip(knob, grab);
    line.append(knob);
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
