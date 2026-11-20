// The phrase marks on the iPad's score page (the phrases spec, v1.2, decision 1 and «Polish of option A»): at each phrase's
// start a breath mark right before the start note and the phrase's number, and a section of the form's boxed letter at its
// start, right above the notes and raised only over what is printed there (Scripts/score/phrase-marks.ts places them).
// Found marks are a tone lighter than the notes, the player's own amber, the selected phrase's number cobalt; while the
// piece plays, the number of the phrase playing is in full ink and the rest are dimmed; a found start the player moved keeps
// a faint comma where it was found. A tap on a number or a letter names it to Swift (page-taps.ts), a hold or a drag of a
// number is page-phrase-drag.ts's; in the Pen they take no touch. In focus the card has the same marks over its bars
// (page-card.ts hands them its surface, `placeCardPhrases`), only to read, and each motif of a phrase as a small comma
// without a number (decision 2), whose tap names it to Swift (page-taps.ts); a motif at its phrase's start is marked by the
// phrase's comma, which takes its tap. Nothing here changes the selection.
import type { Rect, ScoreGeometry } from "../score-geometry.js";
import type { PageCommands, PagePhraseMark, PageSectionMark } from "../page-contract.js";
import type { BarBeatPlace } from "../phrase-drag.js";
import { type MarkSpot, type MarkStart, type PlacedMark, commaOf, markStarts, phraseAt, placeMarks } from "../phrase-marks.js";
import { token } from "../../layout/tokens.js";
import { state } from "./page-state.js";

/**
 * What is printed on a system that a mark stands above where it covers it: the notes with their stems and beams, ledger
 * lines, rests, ties, slurs and tuplets, tempo, voltas, dynamics and words, ornaments and fingering, bar numbers, clefs and
 * signatures.
 */
const PRINTED = "g.note, g.chord, g.rest, g.mRest, g.beam, g.ledgerLines, g.tie, g.slur, g.tuplet, g.tempo, g.ending, g.dynam, g.dir, "
    + "g.hairpin, g.artic, g.fermata, g.trill, g.mordent, g.turn, g.octave, g.fing, g.harm, g.reh, g.mNum, g.clef, g.keySig, g.meterSig, g.breath";

/** The breath mark: a round head with a tail down to the left, as a pencil writes it over the notes, a unit wide. */
const COMMA = "M -1 0 a 1 1 0 1 1 2 0 c 0 1.538 -0.769 2.692 -2.077 3.231 c 0.692 -0.692 1 -1.385 1 -2 z";

/**
 * Where the marks are drawn: the pages, in the document's pixels, with the marks' touch targets; or the card in focus, in
 * its own pixels (it scrolls inside itself), its ticks counted from its first bar (`offset` in the piece), only to read.
 */
export interface PhraseSurface {
    root: HTMLElement;
    geometry: ScoreGeometry | null;
    offset: number;
    /** Where its notes end, in its ticks: the card's last bar holds no start after its notes. */
    end: number;
    onsetOf: (id: string) => number | undefined;
    /** A box on the screen, in the surface's pixels. */
    local: (box: DOMRect) => Rect;
    /** The engraved system that holds a bar, by its id. */
    system: (bar: string) => Element | null;
    /**
     * What takes a tap: the phrases' numbers and the sections' letters (the pages), or the motifs' small commas, which are
     * drawn only there (the card in focus, decision 2).
     */
    taps: "marks" | "motifs";
}

const pageSurface: PhraseSurface = {
    root: document.body,
    get geometry() {
        return state.geometry;
    },
    offset: 0,
    end: Infinity,
    onsetOf: (id) => state.timeline?.onsetOf(id),
    local: (box) => ({ left: box.left + window.scrollX, top: box.top + window.scrollY, right: box.right + window.scrollX, bottom: box.bottom + window.scrollY }),
    system: (bar) => document.querySelector(`#pages [id="${CSS.escape(bar)}"]`)?.closest("g.system") ?? null,
    taps: "marks",
};

/** The card in focus, while it has its bars; null out of focus. */
let cardSurface: PhraseSurface | null = null;

/** The marks' parts on both surfaces, for the selected and the playing phrase to show on each. */
const INK = "body > .phrase-ink, #focus .card-score > .phrase-ink";
const NUMBERS = "body > .phrase-ink.number, #focus .card-score > .phrase-ink.number";

let phrases: PagePhraseMark[] = [];
let sections: PageSectionMark[] = [];
/** The phrases' starts in time order, for the one playing. */
let starts: number[] = [];
/** The piece's beats with their bar and beat, and the words the drag's caption is written in («такт {0} · доля {1}»). */
let places: BarBeatPlace[] = [];
let caption = "";
/** The phrase the selection is (Swift's `select`), whose number is cobalt; null for none. */
let selected: number | null = null;
/** The phrase playing, by its place in `starts`; -1 while nothing plays. */
let playingIndex = -1;
/** Whether the marks are drawn again once the fonts they are set in have loaded. */
let awaitingFonts = false;

/**
 * The phrases' marks, each { number, tick, own, label, found }, and the sections' letters, each { id, tick, letter, label },
 * drawn where their starts are; none draws none. With them the beats (`places`) and the words (`caption`) a dragged mark's
 * caption is written with. Answers how many marks take a tap.
 */
export function phraseMarks({ phrases: given, sections: lettered, places: beats, caption: words }: Parameters<PageCommands["phraseMarks"]>[0]): number {
    phrases = [...(given ?? [])].sort((a, b) => a.tick - b.tick);
    sections = lettered ?? [];
    starts = phrases.map((phrase) => phrase.tick);
    places = beats ?? [];
    caption = words ?? "";
    placePhrases();
    return document.querySelectorAll("body > .phrase-mark").length;
}

/**
 * Draws the marks again where their starts are now, on the pages and on the card in focus, and once more when the fonts
 * they are set in have loaded: a font starts loading when a mark first uses it, and the marks' widths place them.
 */
export function placePhrases(): void {
    drawOn(pageSurface);
    if (cardSurface !== null) {
        drawOn(cardSurface);
    }
    drawAgainWithFonts();
}

/**
 * Once the fonts the marks are set in have loaded, all of them drawn again: their widths place them. That drawing waits
 * for no fonts again, so a `ready` WebKit has already resolved while the fonts still say "loading" (the card's engraving
 * in focus) draws them once more, never round and round without end.
 */
function drawAgainWithFonts(): void {
    if (document.fonts.status === "loading" && !awaitingFonts) {
        awaitingFonts = true;
        void document.fonts.ready.then(() => {
            placePhrases();
            awaitingFonts = false;
        });
    }
}

/**
 * The card in focus laid out with its bars (page-card.ts `layoutCard`): the same marks over them, the page's left as they
 * are; null (out of focus) takes the card's away.
 */
export function placeCardPhrases(surface: PhraseSurface | null): void {
    if (surface === null && cardSurface !== null) {
        clear(cardSurface);
    }
    cardSurface = surface;
    if (surface !== null) {
        drawOn(surface);
        drawAgainWithFonts();
    }
}

function clear(surface: PhraseSurface): void {
    for (const old of surface.root.querySelectorAll(":scope > .phrase-mark, :scope > .phrase-ink")) {
        old.remove();
    }
}

/** The marks over the bars of one surface, each start that falls on them. */
function drawOn(surface: PhraseSurface): void {
    clear(surface);
    const geometry = surface.geometry;
    if (geometry === null) {
        return;
    }
    const drawn = markStarts(phrases, sections, surface.taps === "motifs").flatMap((start) => {
        const tick = start.tick - surface.offset;
        const bar = tick < surface.end ? geometry.barAt(tick) : undefined;
        if (bar === undefined) {
            return [];
        }
        const index = geometry.bars.indexOf(bar);
        const heads = geometry.notes.filter((note) => note.bar === index && surface.onsetOf(note.id) === tick).map((note) => note.left);
        const noteLeft = heads.length > 0 ? Math.min(...heads) : geometry.playhead(tick, surface.onsetOf)?.x ?? bar.left;
        return [{ start, bar, noteLeft, ink: inkOf(start, surface.root) }];
    });
    const spots: MarkSpot[] = drawn.map(({ start, bar, noteLeft, ink }) => ({
        system: bar.system, noteLeft, staffTop: bar.staves[0]?.top ?? bar.top,
        comma: ink.comma?.offsetWidth ?? 0, letter: ink.letter?.offsetWidth ?? 0, number: ink.number?.offsetWidth ?? 0,
        tap: tapOf(start, surface),
    }));
    const height = Math.max(0, ...drawn.map(({ ink }) => (ink.number ?? ink.letter)?.offsetHeight ?? 0));
    const printed = printedOn(drawn.map(({ bar }) => bar), surface);
    const sizes = { gap: Number.parseFloat(token("s-1")) || 0, height, target: Number.parseFloat(token("h-touch")) || 0 };
    placeMarks(spots, (system) => printed.get(system) ?? [], sizes).forEach((place, index) => {
        const { start, ink } = drawn[index];
        moveTo(ink.comma, place.comma, place.bottom - (ink.comma?.offsetHeight ?? 0));
        moveTo(ink.letter, place.letter, place.top);
        moveTo(ink.number, place.number, place.top);
        targetsOf(start, place, surface.root);
    });
}

/** What of a start's mark takes a tap on a surface: on the pages its letter and number, on the card a motif's comma. */
function tapOf(start: MarkStart, surface: PhraseSurface): MarkSpot["tap"] {
    if (surface.taps === "marks") {
        return "parts";
    }
    return start.motif === undefined ? "none" : "comma";
}

/** The touch targets of a start's mark placed (`placeMarks` makes only those its surface taps), each naming what it marks. */
function targetsOf(start: MarkStart, place: PlacedMark, root: HTMLElement): void {
    if (start.motif !== undefined && place.commaTarget !== null) {
        const button = target(place.commaTarget, start.motif.label, root);
        button.dataset.motif = String(start.motif.number);
        button.dataset.motifOf = String(start.motif.of);
    }
    if (start.section !== undefined && place.letterTarget !== null) {
        target(place.letterTarget, start.section.label, root).dataset.section = start.section.id;
    }
    if (start.phrase !== undefined && place.numberTarget !== null) {
        target(place.numberTarget, start.phrase.label, root).dataset.phrase = String(start.phrase.number);
    }
}

/** What a mark's drag reads: the phrases and their starts in time order, the beats and the caption's words. */
export function phraseDragContext(): { phrases: readonly PagePhraseMark[]; starts: readonly number[]; places: readonly BarBeatPlace[]; caption: string } {
    return { phrases, starts, places, caption };
}

/** The parts of a start's mark, on the page to be measured: the comma and the number for a phrase, the letter for a section. */
function inkOf(start: MarkStart, root: HTMLElement): { comma?: HTMLElement; letter?: HTMLElement; number?: HTMLElement } {
    const part = (kind: string): HTMLElement => {
        const element = document.createElement("span");
        element.className = `phrase-ink ${kind}`;
        element.setAttribute("aria-hidden", "true");
        if (start.phrase !== undefined) {
            element.dataset.phrase = String(start.phrase.number);
            element.classList.toggle("own", start.phrase.own === true);
            element.classList.toggle("current", start.phrase.number === phrases[playingIndex]?.number);
        }
        root.append(element);
        return element;
    };
    const ink: { comma?: HTMLElement; letter?: HTMLElement; number?: HTMLElement } = {};
    const comma = commaOf(start);
    if (comma !== null) {
        ink.comma = part(comma);
        ink.comma.innerHTML = `<svg viewBox="-1.2 -1.2 2.4 4.6" aria-hidden="true"><path d="${COMMA}"/></svg>`;
    }
    if (start.section !== undefined) {
        ink.letter = part("letter");
        ink.letter.textContent = start.section.letter;
    }
    if (start.phrase !== undefined) {
        ink.number = part("number");
        ink.number.textContent = String(start.phrase.number);
        ink.number.classList.toggle("selected", start.phrase.number === selected);
    }
    return ink;
}

/** The boxes of what is printed on the systems of these bars, in the surface's pixels, by system. */
function printedOn(bars: readonly { id: string; system: number }[], surface: PhraseSurface): Map<number, Rect[]> {
    const bySystem = new Map<number, Rect[]>();
    for (const bar of bars) {
        if (bySystem.has(bar.system)) {
            continue;
        }
        const boxes = [...(surface.system(bar.id)?.querySelectorAll(PRINTED) ?? [])].map((element) => element.getBoundingClientRect())
            .filter((box) => box.width > 0 || box.height > 0)
            .map(surface.local);
        bySystem.set(bar.system, boxes);
    }
    return bySystem;
}

function moveTo(element: HTMLElement | undefined, left: number | null, top: number): void {
    if (element !== undefined && left !== null) {
        element.style.left = `${left}px`;
        element.style.top = `${top}px`;
    }
}

/** A mark's touch target over its letter, number or a motif's comma on a surface, what a screen reader says of it. */
function target(box: Rect, label: string, root: HTMLElement): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "phrase-mark";
    button.setAttribute("aria-label", label);
    button.style.left = `${box.left}px`;
    button.style.top = `${box.top}px`;
    button.style.width = `${box.right - box.left}px`;
    button.style.height = `${box.bottom - box.top}px`;
    root.append(button);
    return button;
}

/** The phrase the selection is (`select`): its number turns cobalt, the one before goes back; none for no phrase. */
export function showSelectedPhrase(phrase: number | undefined): void {
    selected = phrase ?? null;
    for (const number of document.querySelectorAll<HTMLElement>(NUMBERS)) {
        number.classList.toggle("selected", Number(number.dataset.phrase) === selected);
    }
}

/**
 * Where the player is (`playing`): while the piece plays, the marks of the phrase at `tick` are the current ones, in full
 * ink, and the rest are dimmed; stopped, all are as before.
 */
export function showPhrasePlaying(tick: number, playing: boolean): void {
    const index = playing ? phraseAt(starts, tick) : -1;
    document.body.toggleAttribute("data-phrases-playing", playing && starts.length > 0);
    if (index === playingIndex) {
        return;
    }
    playingIndex = index;
    const number = index >= 0 ? phrases[index].number : null;
    for (const ink of document.querySelectorAll<HTMLElement>(INK)) {
        ink.classList.toggle("current", ink.dataset.phrase !== undefined && Number(ink.dataset.phrase) === number);
    }
}

/**
 * The top of the marks over a band's first run, in window pixels, for the selection menu to clear; null when none is there.
 * A mark stands on its system's top staff, at most two touch targets above it when printed things raise it; one further up
 * belongs to the system above.
 */
export function phraseMarksTop(band: { left: number; right: number; top: number }): number | null {
    const reach = 2 * (Number.parseFloat(token("h-touch")) || 0);
    let top: number | null = null;
    for (const mark of document.querySelectorAll("body > .phrase-ink")) {
        const box = mark.getBoundingClientRect();
        if (box.width > 0 && box.right > band.left && box.left < band.right && box.top < band.top && band.top - box.bottom <= reach) {
            top = Math.min(top ?? box.top, box.top);
        }
    }
    return top;
}
