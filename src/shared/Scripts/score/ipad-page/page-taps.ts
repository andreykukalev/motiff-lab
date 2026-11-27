// Taps on the iPad's score page, and who may make them. Who selects, taps or writes with a finger or the Pencil is
// Swift's (`input`, InputRules.swift, #479 item 18), under the bar's mode (`mode`) and its Pen (`pen`). A tap on a mark over
// the bars names it to Swift (a Check flag, a version's label, a margin note, a bookmark, a phrase's number or a section's
// letter, a motif's comma on the card in focus); a tap on the music names its bar, or under the modes puts the playhead on
// the beat under it; a tap never plays (#338).
import { beatUnder, modeOf } from "../score-modes.js";
import { ScoreBeats } from "../selection-beats.js";
import type { ScoreBar } from "../score-geometry.js";
import type { PageCommands } from "../page-contract.js";
import { documentPoint, pointerRules, post, state } from "./page-state.js";
import { places, selection } from "./page-selection.js";
import { colouring } from "./page-colouring.js";
import { press } from "./page-playheads.js";
import { cardTapped } from "./page-card.js";
import { phraseMarkSwallowsClick } from "./page-phrase-drag.js";
import { followMark, windowPoint } from "./page-mark-place.js";

/** How far above or below a bar's staves a tap still lands on that bar, in pixels. */
const BAR_REACH = 24;

/** The kind of pointer that pressed last ("touch" for a finger, "pen" for the Pencil), for the tap that follows. */
let lastPointer = "touch";

/** The piece's beats as the selection reads them, for a tap to find the beat under it (version 0.5). */
const scoreBeats = new ScoreBeats({ beats: () => state.beats, tickX: (tick) => state.geometry?.playhead(tick, (id) => state.timeline?.onsetOf(id))?.x });

/** Listens for every press first, to know which kind of pointer the tap that follows comes from. */
export function listenPointerKind(): void {
    document.addEventListener("pointerdown", (event) => { lastPointer = event.pointerType; }, true);
}

/**
 * The mode the bar chose (version 0.5, once the experiment Two playheads and modes): "navigate", "select" or "pen";
 * none keeps the score as before.
 */
export function mode({ name }: Parameters<PageCommands["mode"]>[0]): void {
    state.mode = modeOf(name);
    if (state.mode === null) {
        delete document.body.dataset.mode;
    } else {
        document.body.dataset.mode = state.mode;
    }
    press.cancel();
    selection.place();
}

/**
 * Who does what on the score (#479 item 18, InputRules.swift): for "touch" (a finger) and "pen" (the Pencil), how a press
 * starts a selection ("now", "longPress" or "none") and whether a tap acts on the page. Before Swift sends them, the
 * rules before: the Pen on, nothing but writing.
 */
export function input(rules: Parameters<PageCommands["input"]>[0]): void {
    state.input = rules ?? null;
    press.cancel();
    selection.place();
}

/** While the bar's Pen is on, a finger writes: it neither selects nor taps. */
export function pen(on: boolean): void {
    state.pen = Boolean(on);
}

/** Whether the tap just made acts on the page: not by a pointer that writes. */
function tapActs(): boolean {
    const rule = pointerRules(lastPointer);
    return rule === undefined ? !state.pen : rule.tap;
}

/**
 * A tap under the modes never changes the selection: it places the playhead on the beat under it, or puts the sheet away
 * off the bars. Notes are edited only in the roll (#479 item 23). The Pen only writes.
 */
function tapUnderModes(event: MouseEvent): void {
    const geometry = state.geometry;
    // In the Pen a tap by a finger that does not write places the playhead, as in Navigate (#479 item 18).
    if (geometry === null || (state.mode === "pen" && state.input === null)) {
        return;
    }
    const { x, y } = documentPoint(event);
    const beat = beatUnder(geometry, scoreBeats, x, y);
    post(beat === undefined ? { blank: true } : { place: { start: beat.start, end: beat.end } });
}

/**
 * The bar under a tap, found from where the bars are engraved rather than from the SVG element under the finger, so a tap on
 * the white between the staff lines lands on its bar as a tap on a note does (#338).
 */
function barUnder(event: MouseEvent): ScoreBar | undefined {
    const geometry = state.geometry;
    if (!geometry) {
        return undefined;
    }
    const { x, y } = documentPoint(event);
    // The staves, widened to the notes above and below them (ledger lines) and a finger's reach round both.
    return geometry.bars.find((bar) => x >= bar.left && x < bar.right
        && y >= Math.min(bar.top, bar.ink.top) - BAR_REACH && y <= Math.max(bar.bottom, bar.ink.bottom) + BAR_REACH);
}

/**
 * A tap on a mark over the bars names it to Swift: a Check flag, a version's label, a margin note, a bookmark, or a phrase's
 * number or a section's letter (v1.2), which Swift selects.
 */
function tapOnMark(target: Element | null): boolean {
    const phrase = target?.closest<HTMLElement>(".phrase-mark");
    if (phrase) {
        // The release of a hold or a drag of a phrase's number is no tap (page-phrase-drag.ts).
        if (phraseMarkSwallowsClick()) {
            return true;
        }
        post(phrase.dataset.section === undefined ? { phraseMark: Number(phrase.dataset.phrase) } : { sectionMark: phrase.dataset.section });
        return true;
    }
    const flag = target?.closest<HTMLElement>(".check-flag");
    if (flag) {
        post({ checkFlag: Number(flag.dataset.bar) });
        return true;
    }
    const version = target?.closest<HTMLElement>(".perf-marker");
    if (version) {
        const place = windowPoint(version.getBoundingClientRect());
        // A draft's приём number or seam diamond (v1.1), or an anchor's mark (v1.2), says which it is.
        const technique = version.dataset.technique === undefined ? {} : { technique: version.dataset.technique };
        const seam = version.dataset.seam === undefined ? {} : { seam: version.dataset.seam };
        const anchor = version.dataset.anchor === undefined ? {} : { anchor: version.dataset.anchor };
        post({ performanceBar: Number(version.dataset.bar), ...place, ...technique, ...seam, ...anchor });
        // Its card opens under it, and stays there as the page scrolls or is laid out again (r-1).
        followMark(version, () => sameMarker(version.dataset), place);
        return true;
    }
    const marker = target?.closest<HTMLElement>(".marker");
    if (marker) {
        post({ noteMarker: marker.dataset.note });
        return true;
    }
    const bookmark = target?.closest<HTMLElement>(".bmb");
    if (bookmark) {
        post({ bookmark: bookmark.dataset.bookmark });
        return true;
    }
    return false;
}

/** The label, number, diamond or anchor mark over a bar with these keys, as the page draws it now; null when it is gone. */
function sameMarker({ bar, technique, seam, anchor }: DOMStringMap): Element | null {
    const key = (name: string, value: string | undefined) => (value === undefined ? `:not([data-${name}])` : `[data-${name}="${CSS.escape(value)}"]`);
    return document.querySelector(`.perf-marker${key("bar", bar)}${key("technique", technique)}${key("seam", seam)}${key("anchor", anchor)}`);
}

/**
 * Listens for a tap on the score: a tap anywhere in a bar, on a note or between them, names that bar to Swift, which
 * selects it when nothing is selected. A tap never plays (#338): the music starts only with Play or Loop.
 */
export function listenTaps(): void {
    document.body.addEventListener("click", (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest("#focus")) {
            // A motif's comma on the card names the motif to Swift, which selects it (the phrases spec, decision 2).
            const motif = target.closest<HTMLElement>("button.phrase-mark[data-motif]");
            if (motif !== null) {
                post({ motifMark: { phrase: Number(motif.dataset.motifOf), motif: Number(motif.dataset.motif) } });
            } else if (target.closest(".card-score") !== null) {
                cardTapped(event);
            }
            return;
        }
        if (tapOnMark(target)) {
            return;
        }
        // A press on a playhead's handle is the handle's, a tap on it included: it never places the playhead or selects a bar.
        // The release of a long press that put the playhead is no tap on a beat (#543 3.1).
        const pressed = press.swallowsClick();
        const placed = places.swallowsClick();
        const painted = colouring.swallowsClick();
        if (pressed || placed || painted || !tapActs() || target === null || target.closest(".sel-handle, .grip")) {
            return;
        }
        if (state.mode !== null) {
            tapUnderModes(event);
            return;
        }
        const bar = barUnder(event);
        post(bar ? { bar: { start: bar.tick, end: bar.end } } : { blank: true });
    });
}
