// The phrase marks on the iPad's score page (the phrases spec, v1.2, decision 1 and «Polish of option A»): at each phrase's
// start a breath mark right before the start note and the phrase's number, and a section of the form's boxed letter at its
// start, right above the notes and raised only over what is printed there (Scripts/score/phrase-marks.ts places them).
// Found marks are a tone lighter than the notes, the player's own amber, the selected phrase's number cobalt; while the
// piece plays, the number of the phrase playing is in full ink and the rest are dimmed. A tap on a number or a letter
// names it to Swift (page-taps.ts); in the Pen they take no touch. Nothing here changes the selection.
import type { Rect } from "../score-geometry.js";
import type { PageCommands, PagePhraseMark, PageSectionMark } from "../page-contract.js";
import { type MarkSpot, phraseAt, placeMarks } from "../phrase-marks.js";
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

/** A start on the score: a phrase's, a section's, or both. */
interface Start {
    tick: number;
    phrase?: PagePhraseMark;
    section?: PageSectionMark;
}

let phrases: PagePhraseMark[] = [];
let sections: PageSectionMark[] = [];
/** The phrases' starts in time order, for the one playing. */
let starts: number[] = [];
/** The phrase the selection is (Swift's `select`), whose number is cobalt; null for none. */
let selected: number | null = null;
/** The phrase playing, by its place in `starts`; -1 while nothing plays. */
let playingIndex = -1;
/** Whether the marks are drawn again once the fonts they are set in have loaded. */
let awaitingFonts = false;

/**
 * The phrases' marks, each { number, tick, own, label }, and the sections' letters, each { id, tick, letter, label }, drawn
 * where their starts are; none draws none. Answers how many marks take a tap.
 */
export function phraseMarks({ phrases: given, sections: lettered }: Parameters<PageCommands["phraseMarks"]>[0]): number {
    phrases = [...(given ?? [])].sort((a, b) => a.tick - b.tick);
    sections = lettered ?? [];
    starts = phrases.map((phrase) => phrase.tick);
    placePhrases();
    return document.querySelectorAll("body > .phrase-mark").length;
}

/**
 * Draws the marks again where their starts are now, and once more when the fonts they are set in have loaded: a font
 * starts loading when a mark first uses it, and the marks' widths place them.
 */
export function placePhrases(): void {
    for (const old of document.querySelectorAll("body > .phrase-mark, body > .phrase-ink")) {
        old.remove();
    }
    const geometry = state.geometry;
    if (geometry === null) {
        return;
    }
    const onsetOf = (id: string): number | undefined => state.timeline?.onsetOf(id);
    const drawn = startsOf().flatMap((start) => {
        const bar = geometry.barAt(start.tick);
        if (bar === undefined) {
            return [];
        }
        const index = geometry.bars.indexOf(bar);
        const heads = geometry.notes.filter((note) => note.bar === index && onsetOf(note.id) === start.tick).map((note) => note.left);
        const noteLeft = heads.length > 0 ? Math.min(...heads) : geometry.playhead(start.tick, onsetOf)?.x ?? bar.left;
        return [{ start, bar, noteLeft, ink: inkOf(start) }];
    });
    const spots: MarkSpot[] = drawn.map(({ bar, noteLeft, ink }) => ({
        system: bar.system, noteLeft, staffTop: bar.staves[0]?.top ?? bar.top,
        comma: ink.comma?.offsetWidth ?? 0, letter: ink.letter?.offsetWidth ?? 0, number: ink.number?.offsetWidth ?? 0,
    }));
    const height = Math.max(0, ...drawn.map(({ ink }) => (ink.number ?? ink.letter)?.offsetHeight ?? 0));
    const printed = printedOn(drawn.map(({ bar }) => bar));
    const sizes = { gap: Number.parseFloat(token("s-1")) || 0, height, target: Number.parseFloat(token("h-touch")) || 0 };
    placeMarks(spots, (system) => printed.get(system) ?? [], sizes).forEach((place, index) => {
        const { start, ink } = drawn[index];
        moveTo(ink.comma, place.comma, place.bottom - (ink.comma?.offsetHeight ?? 0));
        moveTo(ink.letter, place.letter, place.top);
        moveTo(ink.number, place.number, place.top);
        if (start.section !== undefined && place.letterTarget !== null) {
            target(place.letterTarget, start.section.label).dataset.section = start.section.id;
        }
        if (start.phrase !== undefined && place.numberTarget !== null) {
            target(place.numberTarget, start.phrase.label).dataset.phrase = String(start.phrase.number);
        }
    });
    if (document.fonts.status === "loading" && !awaitingFonts) {
        awaitingFonts = true;
        void document.fonts.ready.then(() => {
            awaitingFonts = false;
            placePhrases();
        });
    }
}

/** The starts of the phrases and the sections, one for each tick, in time order. */
function startsOf(): Start[] {
    const byTick = new Map<number, Start>();
    for (const phrase of phrases) {
        byTick.set(phrase.tick, { tick: phrase.tick, phrase });
    }
    for (const section of sections) {
        byTick.set(section.tick, { ...(byTick.get(section.tick) ?? { tick: section.tick }), section });
    }
    return [...byTick.values()].sort((a, b) => a.tick - b.tick);
}

/** The parts of a start's mark, on the page to be measured: the comma and the number for a phrase, the letter for a section. */
function inkOf(start: Start): { comma?: HTMLElement; letter?: HTMLElement; number?: HTMLElement } {
    const part = (kind: string): HTMLElement => {
        const element = document.createElement("span");
        element.className = `phrase-ink ${kind}`;
        element.setAttribute("aria-hidden", "true");
        if (start.phrase !== undefined) {
            element.dataset.phrase = String(start.phrase.number);
            element.classList.toggle("own", start.phrase.own === true);
            element.classList.toggle("current", start.phrase.number === phrases[playingIndex]?.number);
        }
        document.body.append(element);
        return element;
    };
    const ink: { comma?: HTMLElement; letter?: HTMLElement; number?: HTMLElement } = {};
    if (start.phrase !== undefined) {
        ink.comma = part("comma");
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

/** The boxes of what is printed on the systems of these bars, in the document's pixels, by system. */
function printedOn(bars: readonly { id: string; system: number }[]): Map<number, Rect[]> {
    const bySystem = new Map<number, Rect[]>();
    for (const bar of bars) {
        if (bySystem.has(bar.system)) {
            continue;
        }
        const system = document.querySelector(`#pages [id="${CSS.escape(bar.id)}"]`)?.closest("g.system");
        const boxes = [...(system?.querySelectorAll(PRINTED) ?? [])].map((element) => element.getBoundingClientRect())
            .filter((box) => box.width > 0 || box.height > 0)
            .map((box) => ({ left: box.left + window.scrollX, top: box.top + window.scrollY, right: box.right + window.scrollX, bottom: box.bottom + window.scrollY }));
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

/** A mark's touch target over its letter or number, what a screen reader says of it. */
function target(box: Rect, label: string): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "phrase-mark";
    button.setAttribute("aria-label", label);
    button.style.left = `${box.left}px`;
    button.style.top = `${box.top}px`;
    button.style.width = `${box.right - box.left}px`;
    button.style.height = `${box.bottom - box.top}px`;
    document.body.append(button);
    return button;
}

/** The phrase the selection is (`select`): its number turns cobalt, the one before goes back; none for no phrase. */
export function showSelectedPhrase(phrase: number | undefined): void {
    selected = phrase ?? null;
    for (const number of document.querySelectorAll<HTMLElement>("body > .phrase-ink.number")) {
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
    for (const ink of document.querySelectorAll<HTMLElement>("body > .phrase-ink")) {
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
