// The score page's contract (decision record 0031, steps 2 and 11): the functions the iPad app calls on the score page
// (`window.motiff`, Swift's PageCommand) with what each takes and answers, and the messages the page posts to the app
// (`webkit.messageHandlers.score`, Swift's PageEvent.Kind) with what each carries. The page (score/ipad-page) is typed
// against it. The names are tests/contracts/score-page.json, which tests/shared/score-page-contract.test.mjs checks
// against this file and the page, and ScorePageContractTests against Swift; Swift reads the payloads by hand.
import type { Handle } from "../playback/playback-model.js";
import type { DragMessage } from "../playback/playhead-drag.js";
import type { PracticeShown } from "../layout/practice-view.js";
import type { CardOssia } from "./phrase-card.js";
import type { BarBeatPlace } from "./phrase-drag.js";
import type { BarFrame, InkPoint, MarkColour, MarkTool, PencilMark } from "./pencil-marks.js";
import type { Rect } from "./score-geometry.js";
import type { TimemapEntry } from "./score-timeline.js";
import type { SelectionChange } from "./selection-gesture.js";
import type { GridSpan } from "./selection-grid.js";
import type { ScoreSelection } from "./selection-layer.js";
import type { BandHand } from "./selection-boxes.js";
import type { ViewAnchor } from "./view-anchor.js";

/** A stretch of the piece in ticks, [start, end). */
export interface TickRange {
    start: number;
    end: number;
}

/** A point in the document's pixels. */
export interface PagePoint {
    x: number;
    y: number;
}

/** What `show`, `layout` and `inset` answer: how many bars the pages have and the first bar's staff space. */
export interface LaidOut {
    bars: number;
    space: number;
}

/** The pages under the reading line, from 1, and how many there are; 0 for none. */
export interface PagesInView {
    first: number;
    last: number;
    count: number;
}

/** Who does what with one pointer (InputRules.swift, #479 item 18). */
export interface PointerRules {
    select?: "now" | "longPress" | "none";
    tap: boolean;
    handles?: boolean;
    hold?: boolean;
    colour?: boolean;
}

/** The rules for a finger ("touch") and the Pencil ("pen"), the pen's colour while it colours bars, and the place rule. */
export interface InputRules {
    touch?: PointerRules;
    pen?: PointerRules;
    colour?: string;
    places?: boolean;
}

/**
 * The selection Swift shows: its span, what a screen reader says for each handle, its hand, the notes picked, and the
 * phrase it is exactly (its selectable span), whose number is cobalt (the phrases spec, decision 1).
 */
export interface PageSelection extends ScoreSelection {
    picked?: string[];
    phrase?: number;
}

/** The playback model under the modes: the selection the blue playhead keeps to, and Цикл. */
export interface PagePlayheads {
    selection: (TickRange & { hand?: BandHand }) | null;
    loop?: boolean;
}

/** A bookmark: its passage, title, category's ink and what a screen reader says. */
export interface PageBookmark extends TickRange {
    id: string;
    title: string;
    ink: string;
    label: string;
}

/** A margin note at the bar it starts in, numbered in score order. */
export interface PageNote {
    id: string;
    tick: number;
    number: number;
    label: string;
}

/** A highlight band in an ink. */
export interface PageHighlight extends TickRange {
    id: string;
    ink: string;
}

/** A bar Check asks about. */
export interface PageCheck {
    bar: number;
    tick: number;
    label: string;
}

/**
 * A label over a bar: another version's, a draft's place (`place`), a приём's number (`technique`, its key) or a seam's
 * diamond (`seam`, its key), with its `state` (v1.1, ADR 0034).
 */
export interface PageMarker {
    bar: number;
    tick: number;
    text: string;
    label: string;
    place?: boolean;
    technique?: string;
    seam?: string;
    state?: string;
}

/**
 * A phrase's mark at its start (the phrases spec, v1.2): its number, its start note's onset, the player's own (amber) or
 * found, what a screen reader says, and for a found start the player moved, where it was found (its faint ghost).
 */
export interface PagePhraseMark {
    number: number;
    tick: number;
    own?: boolean;
    label: string;
    found?: number;
}

/** A section of the form where it starts: its id (what a tap on its letter posts), its boxed letter and what a screen reader says. */
export interface PageSectionMark {
    id: string;
    tick: number;
    letter: string;
    label: string;
}

/** An ossia on the card in focus, with the ids of the notes it adds. */
export interface PageOssia extends Pick<CardOssia, "staves" | "label"> {
    added?: string[];
}

/** A place of a fact chosen in Insights: its stretch of time and its notes, none for a phrase or a silence. */
export interface FactPlace {
    onset: number;
    end: number;
    noteIds?: string[];
}

/** A page as Contents' Pages tab shows it: its stretch of the piece and its picture as a PNG data URL ("" for none). */
export interface PageOutline extends TickRange {
    page: number;
    image: string;
}

/** What the eraser did: the marks it cut and the pieces left of them. */
export interface Rubbed<Removed> {
    removed: readonly Removed[];
    added: readonly PencilMark[];
}

/** The functions of `window.motiff`, the app's PageCommand. */
export interface PageCommands {
    show(shown: { pages: string[]; timemap: TimemapEntry[]; dark?: boolean }): LaidOut;
    layout(): LaidOut;
    anchor(stroke: { points: InkPoint[]; tool: MarkTool; colour: MarkColour; width?: number }): PencilMark | Record<string, never>;
    erase(path: { points: PagePoint[] }): string[];
    eraseAlong(path: { points?: PagePoint[] }): Rubbed<string>;
    rub(path: { points?: PagePoint[] }): number;
    rubEnd(): Rubbed<PencilMark> & { uncoloured: TickRange[] };
    input(rules: InputRules | null | undefined): void;
    draw(drawn: { marks?: PencilMark[] }): number;
    frames(): BarFrame[];
    inView(): PagesInView;
    select(chosen: PageSelection | null | undefined): number;
    menuSize(size: { width: number; height: number }): void;
    menuMoved(by: { dx?: number; dy?: number }): void;
    menuClosed(): void;
    pen(on: boolean): void;
    beats(given: { lines?: number[] | null }): void;
    focus(shown: { open?: boolean; label?: string; back?: string }): { options?: Record<string, unknown> };
    card(engraved: { pages?: string[]; timemap?: TimemapEntry[]; start: number; ossias?: PageOssia[] }): number;
    inset(room: { bottom: number }): LaidOut;
    highlight(fact: { places?: FactPlace[]; scroll?: boolean }): number;
    bookmarks(given: { items?: PageBookmark[] }): number;
    clearLeft(drawer: { width?: number }): void;
    outline(size: { width: number }): Promise<PageOutline[]>;
    showPage(shown: { page: number; instant?: boolean }): boolean;
    showTick(shown: { tick: number; instant?: boolean }): boolean;
    firstTickOn(on: { page: number }): number | null;
    placeAt(line: { y: number }): ViewAnchor | null;
    lineAt(place: unknown): number | null;
    reveal(shown: { start: number }): boolean;
    notes(given: { notes?: PageNote[]; highlights?: PageHighlight[] }): number;
    checks(given: { flags?: PageCheck[] }): number;
    phraseMarks(given: { phrases?: PagePhraseMark[]; sections?: PageSectionMark[]; places?: BarBeatPlace[]; caption?: string }): number;
    performance(version: { markers?: PageMarker[]; unheard?: string[]; artefacts?: string[]; taken?: number[] }): number;
    layers(given: { hidden?: string[]; slate?: boolean }): string[];
    playing(position: { tick: number; playing: boolean; follow?: boolean; auto?: boolean }): number | null;
    returnToPlayhead(): boolean;
    mode(chosen: { name: string | null | undefined }): void;
    playheads(model: PagePlayheads | null | undefined): void;
    practice(shown: PracticeShown): void;
    selectionControls(shown: boolean): void;
    writing(stroke: { on: boolean }): void;
}

/** Each message the page posts, by its kind (its one key), and what that key carries: the app's PageEvent.Kind. */
export interface PageEvents {
    pages: PagesInView;
    /** Where Swift's selection menu goes, in window pixels; null for none. */
    menu: { left: number; top: number; below: boolean } | null;
    /** A span chosen: by bars, by beats (`beats`) or by places on the notes (`places`, with the hand). */
    chosen: TickRange & { how: SelectionChange; gesture: string; beats?: true; places?: true; hand?: BandHand };
    cleared: { gesture: string };
    gestureCancelled: string;
    holdHint: true;
    picked: { id: string; how: SelectionChange; gesture: string };
    /** The selection's band, in the document's pixels; null for none. */
    zoomSelection: Rect | null;
    tuplets: GridSpan[];
    bar: TickRange;
    bookmark: string | undefined;
    noteMarker: string | undefined;
    handles: PagePoint[];
    /** The bar the playhead is in while it is out of sight, -1 when it is back. */
    playheadAway: number;
    checkFlag: number;
    /** A label over a bar tapped: its bar, with where it is (`MarkerTap`). */
    performanceBar: number;
    /** A phrase's number tapped: the phrase's number. */
    phraseMark: number;
    /** A section's letter tapped: the section's id. */
    sectionMark: string;
    /** A phrase's number held still and let go: the phrase's number, with where it is (`MarkerTap`), for its menu. */
    phraseHeld: number;
    /** A phrase's mark dragged and let go on a note: the tick its phrase starts at and the onset of that note. */
    phraseMoved: { from: number; to: number };
    place: TickRange;
    heads: { handle: Handle; x: number; y: number }[];
    drag: DragMessage;
    blank: true;
    leave: true;
    cardLayout: Record<string, unknown>;
    coloured: TickRange;
}

/** Where a tapped label over a bar is, in window pixels, and a draft's приём number's or seam's key. */
export interface MarkerTap {
    left: number;
    bottom: number;
    technique?: string;
    seam?: string;
}

/** A message the page posts: one kind with what it carries; `performanceBar` and `phraseHeld` with where the mark is. */
export type PageMessage = {
    [Kind in keyof PageEvents]: { [Key in Kind]: PageEvents[Kind] } & (Kind extends "performanceBar" | "phraseHeld" ? MarkerTap : unknown);
}[keyof PageEvents];

declare global {
    interface Window {
        motiff: PageCommands;
        webkit?: { messageHandlers?: { score?: { postMessage(message: PageMessage | "ready"): void } } };
    }
}
