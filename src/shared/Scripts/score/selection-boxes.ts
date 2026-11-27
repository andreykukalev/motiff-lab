// The selection's boxes on the score, shared by the selection layer (selection-layer.ts) and the place rule's layer
// (selection-place-layer.ts): the band's runs as the page shows them, a box set on an element, the staves a hand narrows
// the band to (v0.6.2, #601), and whether a point lies on the music or on the page's margin.

import type { Rect } from "./page-reader.js";
import type { Run, ScoreBar, ScoreGeometry } from "./score-geometry.js";
import type { PlaceBox } from "../selection/selection-places.js";

export const BAND_CLASS = "sel-band";

/** The bars from one to another in time order, whichever comes first. */
export function ordered(a: ScoreBar, b: ScoreBar): [ScoreBar, ScoreBar] {
    return a.tick <= b.tick ? [a, b] : [b, a];
}

/** Sets an element's box to a run, never smaller than 4 pixels each way. */
export function setBox(element: HTMLElement, run: Rect): void {
    element.style.left = `${run.left}px`;
    element.style.top = `${run.top}px`;
    element.style.width = `${Math.max(4, run.right - run.left)}px`;
    element.style.height = `${Math.max(4, run.bottom - run.top)}px`;
}

/** The band's runs as drawn under `host`, in time order, in the score's content pixels. */
export function bandBoxes(host: HTMLElement): PlaceBox[] {
    return [...host.querySelectorAll<HTMLElement>(`:scope > .${BAND_CLASS}`)].map((band) => {
        const left = parseFloat(band.style.left);
        const top = parseFloat(band.style.top);
        return { left, top, right: left + parseFloat(band.style.width), bottom: top + parseFloat(band.style.height) };
    });
}

/** The hand a selection is narrowed to: "right" the upper staff, "left" the lower; anything else both. */
export type BandHand = "right" | "left" | null | undefined;

/**
 * The staves (MEI numbers) a band over the bar at `tick` covers for a hand: the upper of two staves for the right hand,
 * the lower for the left; undefined (every staff) for both hands or a system that is not two staves.
 */
export function handStaves(geometry: ScoreGeometry, tick: number, hand: BandHand): number[] | undefined {
    const staves = hand === "right" || hand === "left" ? geometry.barAt(tick)?.staves : undefined;
    if (staves === undefined || staves.length !== 2) {
        return undefined;
    }
    return [staves[hand === "right" ? 0 : 1].n];
}

/** How far beside a system a press still lands on its music, in pixels, before it is on the page's margin. */
export const MUSIC_REACH = 24;

/** Whether a point lies on the music: on a system, or within MUSIC_REACH of one. */
export function onMusic(geometry: ScoreGeometry, x: number, y: number): boolean {
    return geometry.systems.some((system) => x >= system.left - MUSIC_REACH && x <= system.right + MUSIC_REACH && y >= system.top - MUSIC_REACH && y <= system.bottom + MUSIC_REACH);
}

/** The runs of a passage on the score, narrowed to its hand: what a band, a ghost or an addition draws. */
export function handRuns(geometry: ScoreGeometry, start: number, end: number, hand: BandHand): Run[] {
    return geometry.band(start, end, undefined, handStaves(geometry, start, hand));
}
