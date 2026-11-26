// The mark whose card Swift shows, followed (r-1): a card opens at a mark the page named with where it is (a label, a
// draft's приём number, seam diamond or anchor mark, a phrase's number held), under it or, with no room there, above it
// (its top, v1.4 a-1); when the page scrolls or is laid out again (a rotation, Split View) the mark's new place goes to
// Swift (`markMoved`), so the card stays at it, and once the mark has left the visible part of the page (playback turning
// the page) Swift is told (`markAway`), which closes its card, and the mark is forgotten. It is forgotten too when Swift
// says no card shows there any more (`markForgotten`: its card closed by its own cover, which the page never hears), when
// a press reaches the page (the card covers it while it shows), and when other pages are drawn; a mark drawn anew is
// found again, one gone is forgotten.
import type { MarkPlace } from "../page-contract.js";
import { post } from "./page-state.js";

/** The mark followed as last found, and how to find it again once it is drawn anew; null while none is followed. */
let followed: { mark: Element; find: () => Element | null } | null = null;
/** Where it was last told to Swift, in window points. */
let told = "";

/**
 * Where a box on the page is in the web view's points, as Swift lays its cards: its left, top and bottom from the visual
 * viewport's top left, at its zoom (CSS pixels at a zoom other than 1 are not points).
 */
export function windowPoint(box: DOMRect): MarkPlace {
    const view = window.visualViewport;
    const scale = view?.scale ?? 1;
    const left = view?.offsetLeft ?? 0;
    const top = view?.offsetTop ?? 0;
    return { left: (box.left - left) * scale, top: (box.top - top) * scale, bottom: (box.bottom - top) * scale };
}

/** Whether any of a box on the page is in the visible part of the page (the visual viewport, at its zoom). */
function inSight(box: DOMRect): boolean {
    const view = window.visualViewport;
    const left = view?.offsetLeft ?? 0;
    const top = view?.offsetTop ?? 0;
    const width = view?.width ?? window.innerWidth;
    const height = view?.height ?? window.innerHeight;
    return box.bottom > top && box.top < top + height && box.right > left && box.left < left + width;
}

function placeKey({ left, top, bottom }: MarkPlace): string {
    return `${left} ${top} ${bottom}`;
}

/** A mark named to Swift with where it is (`told`, `windowPoint`'s): from now on its place is followed. */
export function followMark(mark: Element, find: () => Element | null, place: MarkPlace): void {
    followed = { mark, find };
    told = placeKey(place);
}

/**
 * The mark followed moved (a scroll, a new layout): where it is now goes to Swift, nothing when it stayed or is gone; out
 * of sight, Swift is told so (its card closes) and the mark is forgotten.
 */
export function tellMarkMoved(): void {
    if (followed === null) {
        return;
    }
    if (!followed.mark.isConnected) {
        // Drawn anew: found once, then kept, not looked for again each frame.
        const found = followed.find();
        if (found === null) {
            forgetMark();
            return;
        }
        followed.mark = found;
    }
    const box = followed.mark.getBoundingClientRect();
    if (!inSight(box)) {
        forgetMark();
        post({ markAway: true });
        return;
    }
    const place = windowPoint(box);
    if (placeKey(place) === told) {
        return;
    }
    told = placeKey(place);
    post({ markMoved: place });
}

/** No mark is followed: Swift's `markForgotten`, a press on the page, other pages. */
export function forgetMark(): void {
    followed = null;
}

/** Listens for every press first: one that reaches the page means no card is shown at a mark any more. */
export function listenMarkPress(): void {
    document.addEventListener("pointerdown", forgetMark, true);
}
