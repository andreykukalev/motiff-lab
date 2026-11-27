// The score as the selection surface on the iPad's score page (0018 section 3): the web app's own selection layer
// (Scripts/score/selection-layer.ts) draws the band and its handles, and the place layer (selection-place-layer.ts, v0.6.2)
// takes the presses in Select by where they land. What the player chose goes to Swift (`chosen`, `picked`, `cleared`,
// `zoomSelection`), with where Swift's selection menu goes (`menu`) and where the handles are (`handles`); Swift's
// selection comes back (`select`).
import { SelectionLayer } from "../selection-layer.js";
import { PlaceLayer } from "../selection-place-layer.js";
import { gridLines, tupletBlocks, type GridSpan } from "../../selection/selection-grid.js";
import type { Rect } from "../page-reader.js";
import type { ScoreGeometry } from "../score-geometry.js";
import type { PageCommands, PageSelection } from "../page-contract.js";
import { pointerRules, post, state } from "./page-state.js";
import { bookmarksTop } from "./page-bookmarks.js";
import { phraseMarksTop, showSelectedPhrase } from "./page-phrases.js";
import { tint } from "./page-marks.js";

/** The selection layer, made by `installSelection`. */
export let selection: SelectionLayer;
/** The place layer of v0.6.2, made by `installSelection`. */
export let places: PlaceLayer;

/** Swift's selection as it last came (`select`): its span, labels, hand and the notes picked; null for none. */
let chosen: PageSelection | null = null;
/** The tuplets of the engraved pages, each one block for the selection's grid. */
let tuplets: GridSpan[] = [];
/** Whether the selection's handles and menu show (#582 item 10). */
let selectionShown = true;

/**
 * The menu's stand-in: an invisible box the size of Swift's menu, which the selection layer places over the band as
 * it places the web app's menu. Its place goes to Swift, which draws the menu there.
 */
const menu = document.createElement("div");
menu.id = "menu-place";
menu.setAttribute("aria-hidden", "true");
menu.hidden = true;
let menuReported = "";

function reportMenu(): void {
    const place = menu.hidden ? null : { left: parseFloat(menu.style.left) || 0, top: parseFloat(menu.style.top) || 0, below: menu.dataset.below === "true" };
    const key = JSON.stringify(place);
    if (key !== menuReported) {
        menuReported = key;
        post({ menu: place });
    }
}

/** Adds the menu's stand-in and the two selection layers to the page. */
export function installSelection(): void {
    document.body.append(menu);
    selection = new SelectionLayer(document.body, {
        geometry: () => state.geometry,
        marksTop: (band) => highestOf(bookmarksTop(band), phraseMarksTop(band)),
        // While the Pen is on a finger writes, and in focus the page under the veil takes no choice of bars. Once Swift
        // has sent the rules for each pointer (`input`, #479 item 18), they say who selects instead.
        penOn: () => state.focus || (state.input === null && state.pen),
        // How the selection changed and the gesture that changed it go along: Swift keeps a gesture's changes as one step of
        // the selection's history (#511 11).
        chosen: (first, last, _byHandle, how, gesture) => {
            const end = state.geometry?.barAt(last)?.end;
            if (end !== undefined) {
                post({ chosen: { start: first, end, how, gesture } });
            }
        },
        // Selection by beats (#349, once in Settings · Experimental): the beat lines music-core gives, which the drag and the handles
        // snap to, and where a tick sounds across its bar, for a band that starts or ends mid-bar. Under the place rule (v0.6.4)
        // a band is cut where the passage starts and ends inside a bar even before Swift has the beats: the barlines stand in.
        beats: () => state.beats ?? (state.input?.places === true ? barStarts() : null),
        tickX: (tick) => state.geometry?.playhead(tick, (id) => state.timeline?.onsetOf(id))?.x,
        chosenSpan: (start, end, _byHandle, how, gesture) => post({ chosen: { start, end, beats: true, how, gesture } }),
        // The selection gesture (#511 2.1, Scripts/selection/selection-gesture.ts): a hold outside the selection takes it away; a
        // gesture cancelled after that gives it back; a Pencil moved off it before the hold is told to hold.
        cleared: (gesture) => post({ cleared: { gesture } }),
        cancelled: (gesture) => post({ gestureCancelled: gesture }),
        hint: () => post({ holdHint: true }),
        // The modes of version 0.5 (once in Settings · Experimental): only Select starts a selection with a drag, never from inside it,
        // and there a finger drags at once, as two fingers scroll.
        mode: () => state.mode,
        fingerDrags: () => state.mode === "select",
        dragStart: (pointerType) => (state.focus ? "none" : pointerRules(pointerType)?.select),
        // A finger takes a handle; the Pencil does where it does not write (#511 4.1): in the Pen it writes over the handles.
        takesHandle: (pointerType) => pointerRules(pointerType)?.handles,
        // Selection by the place of the touch (v0.6.2, #601): the place layer below takes the presses, and draws the edges.
        places: () => placesOn(),
    });
    /**
     * Selection by the place of the touch (v0.6.2, #601, Scripts/score/selection-place-layer.ts): an edge, inside or outside
     * decides what a press does, with no hold for the Pencil; what it chose goes to Swift as `chosen` (with its hand and how
     * the selection changed), a note head tapped as `picked`, a tap on the margin as `cleared`, a double tap inside as
     * `zoomSelection` (the band's box in the document's pixels).
     */
    places = new PlaceLayer(document.body, {
        geometry: () => state.geometry,
        beats: selection.beats,
        on: () => placesOn(),
        rule: (pointerType) => {
            // A mouse or a trackpad, which Swift names no rule for, selects at once, as the stylus does.
            const rule = pointerRules(pointerType)?.select ?? (pointerType === "mouse" ? "now" : undefined);
            return rule === "now" || rule === "longPress" ? rule : "none";
        },
        selection: () => (chosen ? { start: chosen.start, end: chosen.end, hand: chosen.hand ?? null } : null),
        show: (range) => selection.showPlaced(range),
        chosen: (range, how, gesture) => post({ chosen: { start: range.start, end: range.end, beats: true, places: true, hand: range.hand, how, gesture } }),
        picked: (id, how, gesture) => post({ picked: { id: noteIdOf(id), how, gesture } }),
        cleared: (gesture) => post({ cleared: { gesture } }),
        zoom: () => post({ zoomSelection: bandBox() }),
        view: () => scoreView(),
        grid: (geometry) => selectionGrid(geometry),
    });
}

/** The higher of two tops of marks over a band, either of which may be none. */
function highestOf(a: number | null, b: number | null): number | null {
    return a === null ? b : b === null ? a : Math.min(a, b);
}

/** Whether the place rule takes the presses (v0.6.2, once in Settings · Experimental; `input.places`): in Select, not in focus. */
function placesOn(): boolean {
    return state.input?.places === true && state.mode === "select" && !state.focus;
}

/** The bars' starts in time order: the lines a band is cut at while Swift has sent no beats. */
function barStarts(): number[] | null {
    return state.geometry?.bars.map((bar) => bar.tick) ?? null;
}

/**
 * Reads the tuplet groups of the engraved pages (Verovio's `g.tuplet` round its notes and rests), each one block from its
 * first start to its last end in the page's ticks (v0.6.4, Andrei 19:54Z: a tuplet is never split by a selection's edge),
 * and keeps them for the selection's grid, which is worked out again. Answers them.
 */
export function keepTuplets(): GridSpan[] {
    tuplets = readTuplets();
    gridMemo.geometry = null;
    return tuplets;
}

function readTuplets(): GridSpan[] {
    const timeline = state.timeline;
    if (timeline === null) {
        return [];
    }
    const byId = new Map([...timeline.spans, ...timeline.rests].map((span) => [span.id, span]));
    const groups = [...document.querySelectorAll("#pages g.tuplet")].map((group) =>
        [...group.querySelectorAll("g.note, g.rest")].map((member) => byId.get(member.id)).filter((span) => span !== undefined).map((span) => ({ start: span.onset, end: span.end })));
    return tupletBlocks(groups);
}

/** The selection's grid for the layout shown (selection-grid.ts): sixteenths from each bar's start, a tuplet one block. */
const gridMemo: { geometry: ScoreGeometry | null; lines: number[] } = { geometry: null, lines: [] };
export function selectionGrid(geometry: ScoreGeometry): number[] {
    if (gridMemo.geometry !== geometry) {
        gridMemo.geometry = geometry;
        gridMemo.lines = gridLines(geometry.bars.map((bar) => ({ start: bar.tick, end: bar.end })), tuplets);
    }
    return gridMemo.lines;
}

/**
 * The part of the window the score shows, in window pixels: below the floating bar and above the pill (the body's padding,
 * score.css --pad-top and --pad-bottom, the bottom grown by a drawer's inset), where the place layer's autoscroll starts.
 */
export function scoreView(): { top: number; bottom: number } {
    const style = getComputedStyle(document.body);
    return { top: parseFloat(style.paddingTop) || 0, bottom: window.innerHeight - (parseFloat(style.paddingBottom) || 0) };
}

/** The note an engraved note head belongs to: a later piece of a tied note ("…-2") is the note's. */
export function noteIdOf(id: string): string {
    const base = id.replace(/-\d+$/, "");
    return base !== id && state.timeline?.spans.some((span) => span.id === base) ? base : id;
}

/** The box round the selection's band, in the document's pixels; null for none. */
function bandBox(): Rect | null {
    const bands = [...document.querySelectorAll("body > .sel-band")].map((band) => band.getBoundingClientRect());
    if (bands.length === 0) {
        return null;
    }
    const view = document.body.getBoundingClientRect();
    const left = Math.min(...bands.map((band) => band.left)) - view.left;
    const top = Math.min(...bands.map((band) => band.top)) - view.top;
    return { left, top, right: Math.max(...bands.map((band) => band.right)) - view.left, bottom: Math.max(...bands.map((band) => band.bottom)) - view.top };
}

/** The notes picked into the selection (v0.6.2), cobalt on the page. */
export function markPicked(): void {
    tint(document.getElementById("pages") as HTMLElement, state.timeline, new Set(chosen?.picked ?? []), "sel-picked");
}

/** Selection by beats on (`lines`, the beat lines in ticks from music-core's playback score) or off (none). */
export function beats({ lines }: Parameters<PageCommands["beats"]>[0]): void {
    state.beats = Array.isArray(lines) && lines.length > 0 ? lines : null;
    selection.place();
}

let handlesReported = "";

/**
 * Where the selection's handles are, in the document's pixels, whenever they move: a touch on one moves it, and the
 * Pencil does not write there (#479 item 18).
 */
function reportHandles(): void {
    // Hidden with the selection controls (#582 item 10), they take no touch and keep the Pencil from nothing.
    const handles = !selectionShown ? [] : [...document.querySelectorAll<HTMLElement>("body > .sel-handle")].map((handle) => ({ x: parseFloat(handle.style.left) || 0, y: parseFloat(handle.style.top) || 0 }));
    const key = JSON.stringify(handles);
    if (key !== handlesReported) {
        handlesReported = key;
        post({ handles });
    }
}

/** A drawer over the left of the page (the Contents): the selection layer keeps the menu to its right, as on the web. */
const clearing = document.createElement("div");
clearing.dataset.menuClear = "left";
clearing.setAttribute("aria-hidden", "true");
clearing.style.cssText = "position: fixed; left: 0; top: 0; width: 0; height: 0; visibility: hidden; pointer-events: none;";

/**
 * Gives Swift's menu to the selection layer, which reports its place, the place layer's decorations and the handles
 * whenever it places it (posting `menu` and `handles` now), and adds the drawer's stand-in on the left.
 */
export function installMenu(): void {
    const placeMenu = selection.placeMenu.bind(selection);
    selection.placeMenu = () => {
        placeMenu();
        places.decorate();
        reportMenu();
        reportHandles();
    };
    selection.setMenu(menu);
    document.body.append(clearing);
}

/** How far from the left of the screen a drawer covers the page, in pixels; 0 for none. */
export function clearLeft({ width }: Parameters<PageCommands["clearLeft"]>[0]): void {
    clearing.style.width = `${Math.max(0, width ?? 0)}px`;
    selection.placeMenu();
}

/** The selection's span in ticks, with what a screen reader says for each handle and the phrase it is; null for none. */
export function select(given: Parameters<PageCommands["select"]>[0]): number {
    chosen = given ?? null;
    markPicked();
    showSelectedPhrase(chosen?.phrase);
    places.settle();
    selection.showPlaced(null);
    selection.setSelection(given ?? null);
    return given ? document.querySelectorAll("body > .sel-band").length : 0;
}

/**
 * Swift's selection menu dragged aside by the player (#582 item 7, Scripts/score/menu-drag.ts): moved by (dx, dy) from where
 * it stands, kept inside the window, and there until it closes; the place is reported back as `menu`.
 */
export function menuMoved({ dx, dy }: Parameters<PageCommands["menuMoved"]>[0]): void {
    selection.moveMenuBy(Number(dx) || 0, Number(dy) || 0);
}

/** Swift's selection menu put away (Hide, the roll's drawer, Expand): the next one opens over its band again. */
export function menuClosed(): void {
    selection.menuClosed();
}

/** The size of Swift's selection menu, for the selection layer to place it. */
export function menuSize({ width, height }: Parameters<PageCommands["menuSize"]>[0]): void {
    menu.style.width = `${width}px`;
    menu.style.height = `${height}px`;
    selection.placeMenu();
}

/**
 * The selection controls (#582 item 10, PracticeView.swift): hidden, the selection's handles go with the menu (score.css),
 * whatever practice view does; the selection's band stays.
 */
export function selectionControls(shown: boolean): void {
    selectionShown = shown !== false;
    document.body.toggleAttribute("data-selection-hidden", !selectionShown);
    reportHandles();
}
