// A fact chosen in the Insights stream (0018 section 6) rings its places on the iPad's score page (`highlight`), as the web
// app's score view did: the notes turn cobalt with a box round each notehead, and the first comes into view.
import { highlightedIds } from "../score-timeline.js";
import type { PageCommands } from "../page-contract.js";
import { state } from "./page-state.js";

/** The notes ringed for the fact chosen. */
let ringed = new Set<string>();

/**
 * The places of a fact chosen in the Insights stream, each { onset, end, noteIds }, as music-core gives them: their
 * engraved notes turn cobalt with a box round each notehead, and with `scroll` the first is brought into view. A place
 * without notes, such as a phrase or a silence, rings every note and rest in its stretch of time. Engraved ids are
 * music-core's, a tied note's later pieces with "-2", "-3", … (GeneratedNotation). Empty places clear the rings.
 * Returns how many notes are ringed.
 */
export function highlight({ places, scroll }: Parameters<PageCommands["highlight"]>[0]): number {
    for (const id of ringed) {
        document.getElementById(id)?.classList.remove("highlight");
    }
    const timeline = state.timeline;
    const chosen = places ?? [];
    const ringing = new Set<string>();
    if (timeline !== null && chosen.length > 0) {
        const ids = new Set(chosen.flatMap((place) => place.noteIds ?? []));
        const stretches = chosen.filter((place) => (place.noteIds ?? []).length === 0).map((place) => ({ start: place.onset, end: place.end, notes: [] }));
        for (const id of highlightedIds(timeline, stretches, () => undefined)) {
            ringing.add(id);
        }
        for (const span of timeline.spans) {
            if (ids.has(span.id) || ids.has(span.id.replace(/-\d+$/, ""))) {
                ringing.add(span.id);
            }
        }
    }
    ringed = ringing;
    for (const id of ringed) {
        document.getElementById(id)?.classList.add("highlight");
    }
    placeRings();
    if (scroll) {
        const first = document.querySelector("body > .ring");
        const box = first?.getBoundingClientRect();
        if (box && (box.top < 0 || box.bottom > window.innerHeight)) {
            window.scrollTo({ top: window.scrollY + box.top - window.innerHeight / 3, behavior: "smooth" });
        }
    }
    return ringed.size;
}

/** New pages ring nothing. */
export function forgetRings(): void {
    ringed = new Set();
}

/** The boxes round the ringed noteheads, in the document's pixels, in reading order; again whenever the pages move. */
export function placeRings(): void {
    for (const old of document.querySelectorAll("body > .ring")) {
        old.remove();
    }
    const boxes: DOMRect[] = [];
    for (const id of ringed) {
        const element = document.getElementById(id);
        if (element === null || element.closest("#focus")) {
            continue;
        }
        // The box goes round the notehead, not the stem, so neighbouring notes keep their own boxes.
        const box = (element.querySelector("g.notehead") ?? element).getBoundingClientRect();
        if (box.width > 0) {
            boxes.push(box);
        }
    }
    boxes.sort((a, b) => a.top - b.top || a.left - b.left);
    for (const box of boxes) {
        const ring = document.createElement("span");
        ring.className = "ring";
        ring.setAttribute("aria-hidden", "true");
        ring.style.left = `${box.left + window.scrollX - 3}px`;
        ring.style.top = `${box.top + window.scrollY - 3}px`;
        ring.style.width = `${box.width + 6}px`;
        ring.style.height = `${box.height + 6}px`;
        document.body.append(ring);
    }
}
