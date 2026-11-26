// The mark whose card Swift shows, followed (r-1): a card opens under a mark the page named with where it is (a label, a
// draft's приём number, seam diamond or anchor mark, a phrase's number held); when the page scrolls or is laid out again
// (a rotation, Split View) the mark's new place goes to Swift (`markMoved`), so the card stays under its mark. Swift's
// card covers the page while it shows, so a press that reaches the page means it is gone: the mark is forgotten.
import { post } from "./page-state.js";

/** Finds the mark followed again (it may have been drawn anew); null while none is followed. */
let followed: (() => Element | null) | null = null;
/** Where it was last told to Swift, in window pixels. */
let told = "";

function placeOf(box: DOMRect): string {
    return `${box.left} ${box.bottom}`;
}

/** A mark named to Swift with where it is: from now on its place is followed. */
export function followMark(mark: Element, find: () => Element | null): void {
    followed = () => (mark.isConnected ? mark : find());
    told = placeOf(mark.getBoundingClientRect());
}

/** The mark followed moved (a scroll, a new layout): where it is now goes to Swift; nothing when it stayed or is gone. */
export function tellMarkMoved(): void {
    const box = followed?.()?.getBoundingClientRect();
    if (box === undefined || placeOf(box) === told) {
        return;
    }
    told = placeOf(box);
    post({ markMoved: { left: box.left, bottom: box.bottom } });
}

/** No mark is followed. */
export function forgetMark(): void {
    followed = null;
}

/** Listens for every press first: one that reaches the page means no card is shown at a mark any more. */
export function listenMarkPress(): void {
    document.addEventListener("pointerdown", forgetMark, true);
}
