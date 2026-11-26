// Another version of the piece on the iPad's score page (0022): a plum label above the top left of each bar it has
// otherwise, over the bookmarks' level and before any Check flag or note on the bar, and its missing notes tinted
// (`performance`); a tap on a label gives its bar to Swift with where the label is (page-taps.ts). These show only with
// Extra information on (testing item 33), with a thin plum line under each bar taken from a version. A draft's view (v1.1,
// ADR 0034) draws its places' labels, its приёмы's numbers and its seams' diamonds in the same row, and (v1.2) the amber
// marks of the anchors the player confirmed, beside the numbers.
import { BRACKET_LIFT, type ScoreBar, type ScoreGeometry } from "../score-geometry.js";
import type { PageCommands, PageMarker } from "../page-contract.js";
import { state } from "./page-state.js";
import { NOTE_OFFSET_X, placeNotes, tint } from "./page-marks.js";

/** How far under a taken bar's lowest staff its plum line runs, in pixels, as the web app's score view has it. */
const TAKEN_GAP = 4;
/** Another version's label: 20 px of plum in a 24 px target, at least wide enough for a count and at most this wide. */
const PERFORMANCE_HEIGHT = 20;
const PERFORMANCE_TARGET = 24;
const PERFORMANCE_MIN_WIDTH = 36;
const PERFORMANCE_MAX_WIDTH = 180;
/** A draft's place label (v1.1) says where the place came from, so it may run on longer than a version's. */
const PLACE_MAX_WIDTH = 360;

/** Where a Check flag or a margin note stands over the bars, which a label stops short of. */
export interface MarkFlag {
    left: number;
    top: number;
    system: number;
}

/** A label with the bar it stands over. */
interface Placed {
    marker: PageMarker;
    bar: ScoreBar;
}

let markers: PageMarker[] = [];
let unheard = new Set<string>();
let artefacts = new Set<string>();
let taken: number[] = [];

/**
 * The bars another version has otherwise, each { bar, tick, text, label }, as plum labels above the bars (in a draft's
 * view, each place's label above its start, with `place`, in the ink, and the приёмы's plum numbers, with `technique`, the
 * key a tap posts, and `state`: v1.1, ADR 0034), and the ids of
 * the printed notes it does not have, tinted in place (0022, section 4), and the ticks of the bars `taken` from versions,
 * a thin plum line under each; empty lists draw none. Given to Swift as `performance`; named otherwise here, where
 * `performance` is the clock.
 */
export function showVersion(version: Parameters<PageCommands["performance"]>[0]): number {
    markers = version.markers ?? [];
    unheard = new Set(version.unheard ?? []);
    artefacts = new Set(version.artefacts ?? []);
    taken = version.taken ?? [];
    placeNotes();
    tintUnheard();
    return document.querySelectorAll("body > .perf-marker").length;
}

/**
 * The printed notes the other version does not have in the warning colour, and its likely artefacts in grey, wherever the
 * page draws them.
 */
export function tintUnheard(): void {
    const host = document.getElementById("pages") as HTMLElement;
    tint(host, state.timeline, unheard, "not-heard");
    tint(host, state.timeline, artefacts, "version-artefact");
}

/** The thin plum line under each bar taken from a version. */
export function placeTaken(geometry: ScoreGeometry): void {
    for (const tick of taken) {
        const bar = geometry.barAt(tick);
        if (bar === undefined) {
            continue;
        }
        const line = document.createElement("div");
        line.className = "taken-bar";
        line.setAttribute("aria-hidden", "true");
        line.style.left = `${bar.left}px`;
        line.style.top = `${bar.bottom + TAKEN_GAP}px`;
        line.style.width = `${bar.right - bar.left}px`;
        document.body.append(line);
    }
}

/** A приём's number, or an anchor's mark (v1.2), which stands beside the numbers of its bar. */
const isNumber = (marker: PageMarker): boolean => marker.technique !== undefined || marker.anchor !== undefined;

/**
 * Another version's labels above the bars' top left, under the bookmarks: one may run on over the next bar, up to the
 * next label, Check flag or note on the same system, and goes up above the flags when a narrow bar leaves it too little room.
 */
export function placePerformance(geometry: ScoreGeometry, flags: readonly MarkFlag[]): void {
    const all = markers.map((marker) => ({ marker, bar: geometry.barAt(marker.tick) })).filter((entry): entry is Placed => entry.bar !== undefined);
    // A draft's seam diamonds (v1.1) stand apart, just before the bar line where the place after the seam starts.
    all.filter((entry) => entry.marker.seam !== undefined).forEach(placeSeam);
    const placed = all.filter((entry) => entry.marker.seam === undefined);
    const numbers = placed.filter((entry) => isNumber(entry.marker));
    // Where the next number of a bar starts: just after the one before it, as wide as it is drawn (v1.4, F7: a fixed step
    // let an anchor's mark run onto a number's frame).
    const inBar = new Map<string, number>();
    placed.forEach(({ marker, bar }, index) => {
        // A label runs on up to the next of its own kind: a draft's numbers (v1.1) neither cut a label nor are cut by one.
        const number = isNumber(marker);
        const next = placed.slice(index + 1).find((entry) => isNumber(entry.marker) === number);
        const widest = number ? PERFORMANCE_MIN_WIDTH : marker.place ? PLACE_MAX_WIDTH : PERFORMANCE_MAX_WIDTH;
        const { room, ahead, lifted } = labelRoom(bar, next, widest, flags);
        // A place's label goes up a row over the numbers in its room; the numbers of one bar stand side by side.
        const over = marker.place && numbers.some((entry) => entry.bar.system === bar.system && entry.bar.left >= bar.left && entry.bar.left - bar.left < room);
        const top = labelTop(bar) - (over ? PERFORMANCE_TARGET + 2 : 0);
        const key = `${bar.system}:${bar.left}`;
        const left = (number ? inBar.get(key) : undefined) ?? bar.left + NOTE_OFFSET_X;
        const label = labelOf(marker, number);
        label.style.left = `${left}px`;
        label.style.top = `${lifted ? Math.min(top, Math.min(...ahead.map((flag) => flag.top)) - PERFORMANCE_TARGET - 2) : top}px`;
        label.style.maxWidth = `${Math.max(PERFORMANCE_MIN_WIDTH, Math.min(widest, room))}px`;
        document.body.append(label);
        if (number) inBar.set(key, left + label.offsetWidth + NOTE_OFFSET_X);
    });
}

/**
 * How far a label may run on: up to the next label of its kind on the system, else as wide as it may be, and short of the
 * flags and notes ahead of it; lifted over them when that leaves it narrower than a count.
 */
function labelRoom(bar: ScoreBar, next: Placed | undefined, widest: number, flags: readonly MarkFlag[]): { room: number; ahead: MarkFlag[]; lifted: boolean } {
    let room = next !== undefined && next.bar.system === bar.system ? next.bar.left - bar.left - NOTE_OFFSET_X * 2 : widest;
    const ahead = flags.filter((flag) => flag.system === bar.system && flag.left > bar.left && flag.left - bar.left - NOTE_OFFSET_X * 2 < room);
    const flagRoom = Math.min(...ahead.map((flag) => flag.left - bar.left - NOTE_OFFSET_X * 2));
    const lifted = ahead.length > 0 && flagRoom < PERFORMANCE_MIN_WIDTH;
    if (ahead.length > 0 && !lifted) {
        room = flagRoom;
    }
    return { room, ahead, lifted };
}

/** A label's button: a version's, a draft's place (`place`), a приём's number (`technique`) or an anchor's mark (`anchor`). */
function labelOf(marker: PageMarker, number: boolean): HTMLButtonElement {
    const label = document.createElement("button");
    label.type = "button";
    label.className = marker.anchor !== undefined ? "perf-marker anchor" : number ? `perf-marker technique state-${marker.state}` : marker.place ? "perf-marker place" : "perf-marker";
    label.dataset.bar = String(marker.bar);
    if (marker.anchor !== undefined) label.dataset.anchor = marker.anchor;
    else if (number) label.dataset.technique = marker.technique;
    const text = document.createElement("span");
    text.textContent = marker.text;
    label.append(text);
    label.setAttribute("aria-label", marker.label);
    return label;
}

/**
 * A draft's seam (v1.1, ADR 0034 section 5): a diamond in a touch target just before the bar line where the place after
 * it starts, in the row of the labels; its key (`seam`) is what a tap posts, its `state` "open" or "left" (kept as it is).
 */
function placeSeam({ marker, bar }: Placed): void {
    const diamond = document.createElement("button");
    diamond.type = "button";
    diamond.className = `perf-marker seam state-${marker.state}`;
    diamond.dataset.bar = String(marker.bar);
    diamond.dataset.seam = marker.seam;
    diamond.append(document.createElement("span"));
    diamond.setAttribute("aria-label", marker.label);
    diamond.style.left = `${bar.left - PERFORMANCE_TARGET - NOTE_OFFSET_X / 2}px`;
    diamond.style.top = `${labelTop(bar)}px`;
    document.body.append(diamond);
}

/** The top of the row of labels over a bar: its touch target centred on a label's height, above the system's bracket. */
function labelTop(bar: ScoreBar): number {
    return (bar.staves[0]?.top ?? bar.top) - BRACKET_LIFT - PERFORMANCE_HEIGHT - NOTE_OFFSET_X - (PERFORMANCE_TARGET - PERFORMANCE_HEIGHT) / 2;
}
