// The hold and the drag of a phrase's number on the iPad's score page (the phrases spec, «Polish of option A» 2 and
// «Player's boundaries» 3, v1.2; the rule is Scripts/score/phrase-drag.ts): held still and let go, the page names the
// phrase to Swift with where its number is (`phraseHeld`), which opens the mark's menu there; held and moved, the comma and
// the number lift and follow the finger, a ghost stays where they were, an amber line stands at the note the boundary
// would go to with «такт 5 · доля 1» above the finger, and the two phrases it would make are lit; let go on a note, the page
// asks Swift to move the boundary there (`phraseMoved`). The piece's first phrase only opens its menu. Nothing here
// changes the selection.
import type { PressPoint } from "../../playback/playhead-press.js";
import { MarkPress, barBeatCaption, nearestOnset, neighbourSpans } from "../phrase-drag.js";
import { token } from "../../layout/tokens.js";
import { documentPoint, post, state } from "./page-state.js";
import { phraseDragContext } from "./page-phrases.js";

/** The mark held: its phrase's number, the tick its phrase starts at, and where the press came down. */
let held: { number: number; from: number; at: PressPoint } | null = null;
/** The note the dragged boundary would go to now; undefined off the notes. */
let target: number | undefined;

const markPress = new MarkPress({ lifted: liftMark, dragged: dragMark, menu: openMenu, dropped: dropMark });

/** Whether the click just made ends a hold of a mark, which is no tap on it (page-taps.ts). */
export function phraseMarkSwallowsClick(): boolean {
    return markPress.swallowsClick();
}

/** Listens for presses on the phrases' numbers, and keeps the page still while one holds its mark. */
export function listenPhraseDrag(): void {
    document.body.addEventListener("pointerdown", (event) => {
        const button = event.target instanceof Element ? event.target.closest<HTMLElement>("button.phrase-mark[data-phrase]") : null;
        const { phrases } = phraseDragContext();
        const phrase = phrases.find((mark) => String(mark.number) === button?.dataset.phrase);
        if (phrase === undefined || (event.pointerType === "mouse" && event.button !== 0)) {
            return;
        }
        // A press left without its end (a lost pointerup) ends first, so it puts its own mark back before this one is held.
        const at = documentPoint(event);
        markPress.down(event.pointerId, at, phrase !== phrases[0]);
        held = { number: phrase.number, from: phrase.tick, at };
    });
    window.addEventListener("pointermove", (event) => markPress.move(event.pointerId, documentPoint(event)));
    window.addEventListener("pointerup", (event) => markPress.up(event.pointerId, documentPoint(event)));
    window.addEventListener("pointercancel", (event) => markPress.cancel(event.pointerId));
    // Not passive: once a mark is held, the finger moves the mark, not the page.
    document.addEventListener("touchmove", (event) => {
        if (markPress.holding && event.cancelable) {
            event.preventDefault();
        }
    }, { passive: false });
    // A long press on a touch screen brings no browser menu while it may hold a mark.
    document.addEventListener("contextmenu", (event) => {
        if (markPress.holding || markPress.pending) {
            event.preventDefault();
        }
    });
}

/** The held phrase's comma and number on the page. */
function heldInk(): HTMLElement[] {
    return held === null ? [] : [...document.querySelectorAll<HTMLElement>(`body > .phrase-ink:is(.comma, .number)[data-phrase="${held.number}"]:not(.ghost)`)];
}

/** The hold: the mark lifts, and its ghost stays where it was. */
function liftMark(): void {
    for (const ink of heldInk()) {
        const ghost = ink.cloneNode(true) as HTMLElement;
        ghost.classList.add("ghost", "phrase-drag");
        ghost.classList.remove("selected", "lifted");
        ink.after(ghost);
        ink.classList.add("lifted");
    }
}

/** The drag: the mark follows the point; the line, the caption and the two phrases it would make, at the note under it. */
function dragMark(point: PressPoint): void {
    clearDrawn();
    const geometry = state.geometry;
    if (held === null || geometry === null) {
        return;
    }
    for (const ink of heldInk()) {
        ink.style.translate = `${point.x - held.at.x}px ${point.y - held.at.y}px`;
    }
    const onsetOf = (id: string): number | undefined => state.timeline?.onsetOf(id);
    const bar = geometry.barNear(point.x, point.y);
    target = bar === undefined ? undefined : nearestOnset(geometry.onsets(bar, onsetOf), geometry.tickAt(bar, point.x, onsetOf));
    const line = target === undefined ? undefined : geometry.playhead(target, onsetOf);
    if (target === undefined || line === undefined) {
        return;
    }
    const { starts, places, caption } = phraseDragContext();
    const end = geometry.bars.at(-1)?.end ?? target;
    for (const span of neighbourSpans(starts, held.from, target, end)) {
        for (const run of geometry.band(span.start, span.end)) {
            drawn("phrase-neighbour", run.left, run.top, run.right - run.left, run.bottom - run.top);
        }
    }
    drawn("phrase-snap", line.x, line.top, 0, line.bottom - line.top);
    const label = drawn("phrase-caption", point.x, 0, 0, 0);
    label.textContent = barBeatCaption(places, target, caption);
    label.style.left = `${point.x - label.offsetWidth / 2}px`;
    label.style.top = `${point.y - (Number.parseFloat(token("h-touch")) || 0) - label.offsetHeight}px`;
}

/** Held still and let go: the mark goes back, and Swift opens its menu under its number. */
function openMenu(): void {
    const number = held === null ? null : document.querySelector(`body > .phrase-ink.number[data-phrase="${held.number}"]:not(.ghost)`);
    const box = number?.getBoundingClientRect();
    if (held !== null && box !== undefined) {
        post({ phraseHeld: held.number, left: box.left, bottom: box.bottom });
    }
    putBack();
}

/** Dragged and let go on a note (`point`), Swift moves the boundary there; cancelled (null) or off the notes, it goes back. */
function dropMark(point: PressPoint | null): void {
    if (point !== null && held !== null && target !== undefined && target !== held.from) {
        post({ phraseMoved: { from: held.from, to: target } });
    }
    putBack();
}

/** The mark back where it stands, its ghost and the drag's line, caption and lit phrases gone. */
function putBack(): void {
    for (const ink of heldInk()) {
        ink.classList.remove("lifted");
        ink.style.translate = "";
    }
    clearDrawn();
    for (const ghost of document.querySelectorAll("body > .phrase-ink.phrase-drag")) {
        ghost.remove();
    }
    held = null;
    target = undefined;
}

function clearDrawn(): void {
    for (const old of document.querySelectorAll("body > div.phrase-drag")) {
        old.remove();
    }
}

/** One part of the drag drawn on the page, in the document's pixels. */
function drawn(kind: string, left: number, top: number, width: number, height: number): HTMLElement {
    const element = document.createElement("div");
    element.className = `phrase-drag ${kind}`;
    element.setAttribute("aria-hidden", "true");
    element.style.left = `${left}px`;
    element.style.top = `${top}px`;
    if (width > 0) {
        element.style.width = `${width}px`;
    }
    if (height > 0) {
        element.style.height = `${height}px`;
    }
    document.body.append(element);
    return element;
}
