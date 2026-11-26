// The mark whose card Swift shows, followed (r-1): a card opens under a mark the page named with where it is (a label, a
// draft's приём number, seam diamond or anchor mark, a phrase's number held); when the page scrolls or is laid out again
// (a rotation, Split View) the mark's new place goes to Swift (`markMoved`), so the card stays under it. The mark is
// forgotten when Swift says no card shows there any more (`markForgotten`: its card closed by its own cover, which the
// page never hears), when a press reaches the page (the card covers it while it shows), and when other pages are drawn;
// a mark drawn anew is found again, one gone is forgotten.
import { post } from "./page-state.js";

/** The mark followed as last found, and how to find it again once it is drawn anew; null while none is followed. */
let followed: { mark: Element; find: () => Element | null } | null = null;
/** Where it was last told to Swift, in window points. */
let told = "";

/**
 * Where a box on the page is in the web view's points, as Swift lays its cards: its bottom left from the visual viewport's
 * top left, at its zoom (CSS pixels at a zoom other than 1 are not points).
 */
export function windowPoint(box: DOMRect): { left: number; bottom: number } {
    const view = window.visualViewport;
    const scale = view?.scale ?? 1;
    return { left: (box.left - (view?.offsetLeft ?? 0)) * scale, bottom: (box.bottom - (view?.offsetTop ?? 0)) * scale };
}

function placeKey({ left, bottom }: { left: number; bottom: number }): string {
    return `${left} ${bottom}`;
}

/** A mark named to Swift with where it is (`told`, `windowPoint`'s): from now on its place is followed. */
export function followMark(mark: Element, find: () => Element | null, place: { left: number; bottom: number }): void {
    followed = { mark, find };
    told = placeKey(place);
}

/** The mark followed moved (a scroll, a new layout): where it is now goes to Swift; nothing when it stayed or is gone. */
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
    const place = windowPoint(followed.mark.getBoundingClientRect());
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
