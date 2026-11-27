// The player's marks over the bars on the iPad's score page: margin notes as numbered amber markers at the top right of
// their first bar, and highlights as translucent bands in an ink behind the staves (`notes`), cut where they start and end
// inside a bar; a tap on a marker gives the note to Swift (page-taps.ts). The bars a scan or a transcription is unsure of
// carry an amber "?" at their top right (`checks`), the notes on the same bar to its left. Each layer of marks can be
// hidden for a clean page (`layers`).
import type { Rect } from "../page-reader.js";
import type { Run, ScoreGeometry } from "../score-geometry.js";
import type { ScoreTimeline } from "../score-timeline.js";
import type { PageCheck, PageCommands, PageHighlight, PageNote } from "../page-contract.js";
import { state } from "./page-state.js";
import { selection } from "./page-selection.js";
import { type MarkFlag, placePerformance, placeTaken } from "./page-versions.js";

/** A margin note's marker sits this far right of the bar's top right corner and this far above it, as on the web. */
export const NOTE_OFFSET_X = 4;
const NOTE_OFFSET_Y = 22;

/**
 * The layers of marks that can be hidden: the pencil, notes and highlights, bookmarks, the analysis (rings), Check and the
 * phrases' marks (v1.2).
 */
const LAYERS = ["ink", "notes", "bookmarks", "analysis", "check", "phrases"];

let notesShown: PageNote[] = [];
let highlightsShown: PageHighlight[] = [];
let checksShown: PageCheck[] = [];

/**
 * The margin notes, each { id, tick, number, label }, as amber markers numbered in score order at the top right of the
 * bar they start in, side by side on one bar; and the highlights, each { id, start, end, ink }, as bands behind the
 * staves of their bars in "amber", "carmine", "forest" or "ochre".
 */
export function notes({ notes, highlights }: Parameters<PageCommands["notes"]>[0]): number {
    notesShown = notes ?? [];
    highlightsShown = highlights ?? [];
    placeNotes();
    return document.querySelectorAll("body > .marker").length + document.querySelectorAll("body > .hl").length;
}

/** The bars Check asks about, each { bar, tick, label }: an amber "?" above the bar's top right; none draws none. */
export function checks({ flags }: Parameters<PageCommands["checks"]>[0]): number {
    checksShown = flags ?? [];
    placeNotes();
    return document.querySelectorAll("body > .check-flag").length;
}

/** Gives the notes engraved in `root` with these ids, or a later piece of one tied over ("…-2"), the class; none lose it. */
export function tint(root: ParentNode, timeline: ScoreTimeline | null, ids: ReadonlySet<string>, name: string): void {
    for (const old of root.querySelectorAll(`.${name}`)) {
        old.classList.remove(name);
    }
    if (timeline === null || ids.size === 0) {
        return;
    }
    for (const span of timeline.spans) {
        if (ids.has(span.id) || ids.has(span.id.replace(/-\d+$/, ""))) {
            root.querySelector(`#${CSS.escape(span.id)}`)?.classList.add(name);
        }
    }
}

/**
 * A highlight's band: the staves of the bars it lies in, cut where it starts and ends inside a bar, as the selection's band
 * is (ScoreBeats.cutEdges), since the pen's colour bars colour beats and sixteenths too (finding 31).
 */
export function highlightRuns(geometry: ScoreGeometry, start: number, end: number): Run[] {
    const runs = geometry.band(start, end, 2);
    selection.beats.cutEdges(geometry, runs, start, end);
    return runs;
}

/** A band of a highlight over the page, in the classes given. */
export function highlightBand(run: Rect, className: string): HTMLElement {
    const band = document.createElement("div");
    band.className = className;
    band.setAttribute("aria-hidden", "true");
    band.style.left = `${run.left}px`;
    band.style.top = `${run.top}px`;
    band.style.width = `${run.right - run.left}px`;
    band.style.height = `${run.bottom - run.top}px`;
    document.body.append(band);
    return band;
}

/** Draws the marks over the bars again where the bars are now: a version's taken bars, highlights, Check flags, notes, labels. */
export function placeNotes(): void {
    for (const old of document.querySelectorAll("body > .marker, body > .hl, body > .check-flag, body > .perf-marker, body > .taken-bar")) {
        old.remove();
    }
    const geometry = state.geometry;
    if (geometry === null) {
        return;
    }
    placeTaken(geometry);
    state.painted = highlightsShown.map((highlight) => ({ start: highlight.start, end: highlight.end, runs: highlightRuns(geometry, highlight.start, highlight.end) }));
    highlightsShown.forEach((highlight, index) => {
        for (const run of state.painted[index].runs) {
            highlightBand(run, `hl ink-${highlight.ink}`).dataset.highlight = highlight.id;
        }
    });
    // A Check flag sits at the bar's top right; notes on the same bar sit side by side to its left.
    const onBar = placeChecks(geometry);
    const flags: MarkFlag[] = [];
    for (const check of checksShown) {
        const place = geometry.flag(check.tick);
        if (place !== undefined) {
            flags.push({ left: place.x - NOTE_OFFSET_X - 24, top: place.y - NOTE_OFFSET_Y, system: geometry.barAt(check.tick)?.system ?? -1 });
        }
    }
    placeMarkers(geometry, onBar, flags);
    placePerformance(geometry, flags);
}

/** The Check flags at their bars' top right; answers how many marks stand on each bar (by its tick) so far. */
function placeChecks(geometry: ScoreGeometry): Map<number, number> {
    const onBar = new Map<number, number>();
    for (const check of checksShown) {
        const place = geometry.flag(check.tick);
        if (place === undefined) {
            continue;
        }
        onBar.set(geometry.barAt(check.tick)?.tick ?? check.tick, 1);
        const flag = document.createElement("button");
        flag.type = "button";
        flag.className = "check-flag";
        flag.dataset.bar = String(check.bar);
        flag.setAttribute("aria-label", check.label);
        const square = document.createElement("span");
        square.setAttribute("aria-hidden", "true");
        square.textContent = "?";
        flag.append(square);
        flag.style.left = `${place.x - NOTE_OFFSET_X - 24}px`;
        flag.style.top = `${place.y - NOTE_OFFSET_Y}px`;
        document.body.append(flag);
    }
    return onBar;
}

/** The margin notes' markers, side by side left of what stands on their bar already; each joins the flags. */
function placeMarkers(geometry: ScoreGeometry, onBar: Map<number, number>, flags: MarkFlag[]): void {
    for (const note of notesShown) {
        const place = geometry.flag(note.tick);
        if (place === undefined) {
            continue;
        }
        const bar = geometry.barAt(note.tick)?.tick ?? note.tick;
        const index = onBar.get(bar) ?? 0;
        onBar.set(bar, index + 1);
        const marker = document.createElement("button");
        marker.type = "button";
        marker.className = "marker";
        marker.dataset.note = note.id;
        marker.textContent = String(note.number);
        marker.setAttribute("aria-label", note.label);
        marker.style.left = `${place.x - NOTE_OFFSET_X - 24 - index * 28}px`;
        marker.style.top = `${place.y - NOTE_OFFSET_Y}px`;
        document.body.append(marker);
        flags.push({ left: place.x - NOTE_OFFSET_X - 24 - index * 28, top: place.y - NOTE_OFFSET_Y, system: geometry.barAt(note.tick)?.system ?? -1 });
    }
}

/**
 * Hides the layers named, for a clean page; the others show. With `slate`, Night stand shows light notes on a dark page. Returns what is hidden.
 */
export function layers({ hidden, slate }: Parameters<PageCommands["layers"]>[0]): string[] {
    const names = hidden ?? [];
    for (const layer of LAYERS) {
        document.body.classList.toggle(`hide-${layer}`, names.includes(layer));
    }
    document.documentElement.toggleAttribute("data-page", false);
    if (slate) {
        document.documentElement.setAttribute("data-page", "slate");
    }
    return LAYERS.filter((layer) => names.includes(layer));
}
