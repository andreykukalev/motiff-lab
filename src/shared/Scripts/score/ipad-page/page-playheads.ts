// The playheads on the iPad's score page. Under the modes the two playheads of the playback model are drawn (`playheads`,
// version 0.5.2, #495 UX-10, Scripts/playback/playback-model.ts) as the web app's score view drew its playheads
// (Scripts/score/playheads.ts), and their handles are dragged (#495 UX-08): the handle taken, its places and its letting go
// go to Swift (`drag`), and where the handles are (`heads`), so a finger there is never the pen's. Before the modes a
// cobalt line stands at the sounding moment with a wash over its bar (testing item 4). While the line is out of sight, the
// bar it is in goes to Swift (`playheadAway`, -1 when it is back), for the chip that returns to it (`returnToPlayhead`).
import { ScorePlayheadMarks, bandOf, playheadsOf, type BandRun } from "../playheads.js";
import { ScoreGrab, ScorePress } from "../playhead-grab.js";
import type { Playhead, ScoreGeometry } from "../score-geometry.js";
import { pressPlaces } from "../../playback/playhead-press.js";
import { viewOf } from "../../playback/playback-model.js";
import { PlayheadDrag } from "../../playback/playhead-drag.js";
import { PRACTICE_ATTRIBUTE, type PracticeShown } from "../../layout/practice-view.js";
import type { PageCommands, PagePlayheads } from "../page-contract.js";
import { READING_LINE_PX, documentPoint, pointerRules, post, state } from "./page-state.js";
import { places, selection } from "./page-selection.js";
import { cardGrabs, cardHeads, drawCardHeads } from "./page-card.js";
import { followAgain, heard } from "./page-sounding.js";

/** The pill's room at the bottom of the view: a playhead under it is out of sight. */
const PILL_ROOM_PX = 84;

/** While the piece plays the handles are measured no more often than this, in milliseconds, so playing stays light. */
const GRIPS_PLAYING_MS = 250;

/**
 * The two playheads of the playback model, drawn as the web app's score view draws its playheads: the black track under
 * the bar at the position, always, and with a selection the cobalt line at the position held to it.
 */
const heads = new ScorePlayheadMarks(document.body, {
    at: (tick) => (state.timeline === null ? undefined : state.geometry?.playhead(tick, (id) => state.timeline?.onsetOf(id))),
    lineBottom: (bar) => state.geometry?.lineInkBottom(bar.system) ?? bar.bottom,
    // A playhead's handle at the selection's edge is taken first; only on the edge's own handle the edge wins and the press
    // goes on to the place layer (Andrei, 2026-11-18, answer 3 on finding 1 of the 0.8 test; since v0.6.3 the whole edge won).
    grab: (handle, event) => {
        if (!places.handleAt(event)) {
            grabs.begin(handle, event);
        }
    },
});

/** Practice view on (#533 11): the blue playhead hidden. */
let practiced = false;

/**
 * The playheads dragged by their handles (#495 UX-08 and UX-10, Scripts/score/playhead-grab.ts): the black one inside the
 * bar it stood in, the blue one inside the selection, on the sixteenth grid. Swift is told the handle taken, its places
 * once a frame and its letting go (`drag`), and runs the playback model. A finger always takes a handle; the Pencil does,
 * except where it writes (the Pen): there it writes, as the design has it.
 */
export const grabs = new ScoreGrab({
    geometry: () => state.geometry,
    onsetOf: (id) => state.timeline?.onsetOf(id),
    selection: () => state.heads?.selection ?? null,
    takes: (pointerType) => state.heads !== null && !state.focus && (pointerType !== "pen" || (state.input?.pen?.tap ?? !state.pen)),
    point: (event) => documentPoint(event),
    moved: () => reportPlace(drawHeads(), !heard.playing),
}, new PlayheadDrag((message) => post({ drag: message })));

/** The long press on the music, made by `installPlayheads`. */
export let press: ScorePress;

/**
 * Adds the long press on the music (v0.5.7, #543 3 and 5, Scripts/score/playhead-grab.ts ScorePress): where InputRules let
 * a pointer hold the playhead (`hold`: Navigate, and with the Pen a finger that does not write), held still on a bar it
 * puts the black playhead at the place under it and, moved on, scrubs across bars and systems, through the same drag
 * messages as the dot (T14-T16). It never selects.
 */
export function installPlayheads(): void {
    press = new ScorePress(document.body, {
        geometry: () => state.geometry,
        onsetOf: (id) => state.timeline?.onsetOf(id),
        places: (event) => state.heads !== null && !state.focus && pressPlaces(event.pointerType, pointerRules(event.pointerType)?.hold, state.mode),
        point: (event) => documentPoint(event),
        moved: () => reportPlace(drawHeads(), !heard.playing),
    }, grabs.drag);
}

/**
 * The playback model under the modes (version 0.5.2, #495 UX-10): { selection: { start, end } or null, loop: Цикл on },
 * the selection the blue playhead keeps to; null draws the playhead of before the modes.
 */
export function playheads(value: Parameters<PageCommands["playheads"]>[0]): void {
    state.heads = value ?? null;
    if (state.heads === null) {
        grabs.cancel();
        press.cancel();
        heads.hide();
        cardGrabs.cancel();
        cardHeads.hide();
        reportGrips();
    } else {
        placePlayhead(null);
        // A new selection or Цикл moves the blue playhead at once, playing or not (v0.6.3, #608 item 3): stopped, no position
        // comes to draw it again, and it stayed where the last selection had it.
        reportPlace(drawHeads(), !heard.playing);
        drawCardHeads();
    }
}

/**
 * The playback model's two playheads (version 0.5.2): out of focus the black playhead always shows, and the blue one with a
 * selection, at the player's place or, while a handle is dragged and just after, where it was dragged to. Returns where the
 * black one is.
 */
export function drawHeads(): Playhead | undefined {
    let at: Playhead | undefined;
    if (state.focus || state.heads === null) {
        heads.hide();
    } else {
        const chosen = state.heads.selection ?? null;
        const tick = grabs.drag.heard(heard.tick);
        const view = viewOf({ selection: chosen, loop: state.heads.loop === true, tick, transport: heard.playing ? "playing" : "stopped" });
        at = heads.place({ ...playheadsOf(view, chosen), held: grabs.held === "black", band: headsBand(chosen) });
    }
    reportGrips();
    return at;
}

/** The band the blue playhead keeps to, for the selection, the layout and the beats it was worked out for. */
const keptBand: { selection: PagePlayheads["selection"]; geometry: ScoreGeometry | null; beats: number[] | null; runs: BandRun[] | undefined } = { selection: null, geometry: null, beats: null, runs: undefined };

/**
 * The selection's band as the page draws it (Scripts/score/playheads.ts bandOf): the blue playhead is drawn inside it, on
 * the staves of its hand and not past its ends, which a selection by beats puts just before the next beat's notes (v0.6.3,
 * #608 item 3). Worked out again only when one of them changes, not thirty times a second.
 */
function headsBand(chosen: PagePlayheads["selection"]): BandRun[] | undefined {
    if (chosen === null || state.geometry === null) {
        return undefined;
    }
    if (keptBand.selection !== chosen || keptBand.geometry !== state.geometry || keptBand.beats !== state.beats) {
        keptBand.selection = chosen;
        keptBand.geometry = state.geometry;
        keptBand.beats = state.beats;
        keptBand.runs = bandOf(state.geometry, selection.beats, chosen);
    }
    return keptBand.runs;
}

let gripsReported = "";
let gripsAt = -Infinity;

/**
 * Where the playheads' handles are, in the document's pixels, when they have moved a little: a finger there takes the
 * handle, never the pen's stroke (InkInput, #495 UX-08).
 */
function reportGrips(): void {
    if (heard.playing && state.heads !== null && performance.now() - gripsAt < GRIPS_PLAYING_MS) {
        return;
    }
    gripsAt = performance.now();
    // In practice view the blue playhead is hidden, so the Pencil is not kept from writing where it is (#533 11).
    const grips = state.heads === null || state.focus ? [] : heads.grips().filter(({ handle }) => !practiced || handle !== "blue").map(({ handle, x, y }) => ({ handle, x: x + window.scrollX, y: y + window.scrollY }));
    const key = grips.map(({ handle, x, y }) => `${handle} ${Math.round(x / 2)} ${Math.round(y / 2)}`).join(",");
    if (key !== gripsReported) {
        gripsReported = key;
        post({ heads: grips });
    }
}

/**
 * Practice view (#533 11, PracticeView.swift): "on" hides the blue playhead of the selection (score.css), "off" shows it again;
 * the black playhead of the piece stays, dragged and placed by a tap as ever.
 */
export function practice(shown: PracticeShown): void {
    practiced = shown === "on";
    document.body.setAttribute(PRACTICE_ATTRIBUTE, practiced ? "on" : "off");
    gripsAt = -Infinity;
    reportGrips();
}

/**
 * The playhead's line and its bar's wash on the page, made on first use, the bar last reported out of sight, and where
 * the playhead that plays was last shown, for Back to playback.
 */
const playhead: { line: HTMLElement | null; wash: HTMLElement | null; away: number; top: number; shown: boolean } = { line: null, wash: null, away: -1, top: 0, shown: false };

/** Puts the line and the wash at a tick, as the web app's score view does, or hides them for null; `resting` where playing stopped. */
export function placePlayhead(tick: number | null, resting = false): void {
    const timeline = state.timeline;
    const at = tick === null || timeline === null ? undefined : state.geometry?.playhead(tick, (id) => timeline.onsetOf(id));
    if (at === undefined) {
        for (const element of [playhead.line, playhead.wash]) {
            if (element !== null) {
                element.hidden = true;
            }
        }
        reportPlace(undefined, resting);
        return;
    }
    playhead.line ??= overlay("playhead");
    playhead.wash ??= overlay("play-bar");
    const { line, wash } = playhead;
    for (const element of [wash, line]) {
        if (!element.isConnected) {
            document.body.append(element);
        }
        element.hidden = false;
        element.classList.toggle("resting", resting);
    }
    // Moved by a transform on layers of their own, so the page under them is not painted again on every frame (testing item 31).
    setStyle(line, "transform", `translate(${at.x - 1}px, ${at.top - 6}px)`);
    setStyle(line, "height", `${at.bottom - at.top + 12}px`);
    setStyle(wash, "transform", `translate(${at.bar.left}px, ${at.top - 4}px)`);
    setStyle(wash, "width", `${at.bar.right - at.bar.left}px`);
    setStyle(wash, "height", `${at.bottom - at.top + 8}px`);
    reportPlace(at, resting);
}

/** Keeps where the playhead that plays is, for Back to playback, and tells Swift when it is out of sight while playing. */
export function reportPlace(at: Playhead | undefined, resting: boolean): void {
    playhead.shown = at !== undefined;
    if (at === undefined) {
        reportAway(-1);
        return;
    }
    playhead.top = at.top - 6;
    const shownTop = window.scrollY + READING_LINE_PX - 16;
    const shownBottom = window.scrollY + window.innerHeight - PILL_ROOM_PX;
    reportAway(!resting && (at.bottom < shownTop || at.top > shownBottom) ? at.bar.tick : -1);
}

/** Sets a style only when it changes, so an unchanged value does not make the browser lay out or paint again. */
function setStyle(element: HTMLElement, name: "transform" | "height" | "width", value: string): void {
    if (element.style[name] !== value) {
        element.style[name] = value;
    }
}

function overlay(className: string): HTMLElement {
    const element = document.createElement("span");
    element.className = className;
    element.setAttribute("aria-hidden", "true");
    element.hidden = true;
    return element;
}

function reportAway(barTick: number): void {
    if (barTick !== playhead.away) {
        playhead.away = barTick;
        post({ playheadAway: barTick });
    }
}

/** Back to playback: the page scrolls to the line and follows the playing again. */
export function returnToPlayhead(): boolean {
    if (!playhead.shown) {
        return false;
    }
    followAgain();
    window.scrollTo({ top: Math.max(0, playhead.top + 6 - READING_LINE_PX), behavior: "smooth" });
    return true;
}
