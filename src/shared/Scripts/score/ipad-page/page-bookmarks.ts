// The player's bookmarks on the iPad's score page (0018 section 5): brackets in their category's ink above the top staff,
// titled in italic at the left end and stacked where they overlap, as the web app's score view drew them; a tap gives the
// bookmark to Swift (page-taps.ts).
import { bookmarkLevels } from "../score-geometry.js";
import type { PageBookmark, PageCommands } from "../page-contract.js";
import { state } from "./page-state.js";
import { selection } from "./page-selection.js";

/** Bookmarks sit this far above the top staff, clear of a version's labels over the bars, and overlapping ones stack by the next amount, as on the web. */
const BOOKMARK_LIFT = 40;
const BOOKMARK_STACK = 24;

/** A bookmark's title rises this far above its bracket; marks further above a band than the reach belong to the system above. */
const BOOKMARK_TITLE = 17;
const BOOKMARK_REACH = 120;

let shown: PageBookmark[] = [];

/**
 * The bookmarks, each { id, start, end, title, ink, label }: brackets in the category's ink ("graphite", "carmine",
 * "forest" or "ochre") above the top staff, the title in italic at the left end of every run, overlapping ones
 * stacked.
 */
export function bookmarks({ items }: Parameters<PageCommands["bookmarks"]>[0]): number {
    shown = items ?? [];
    placeBookmarks();
    selection.placeMenu();
    return document.querySelectorAll("body > .bmb").length;
}

/** Draws the bookmarks again where their bars are now. */
export function placeBookmarks(): void {
    for (const old of document.querySelectorAll("body > .bmb")) {
        old.remove();
    }
    const geometry = state.geometry;
    if (geometry === null || shown.length === 0) {
        return;
    }
    const levels = bookmarkLevels(shown);
    shown.forEach((bookmark, index) => {
        for (const run of geometry.bracket(bookmark.start, bookmark.end, BOOKMARK_LIFT + levels[index] * BOOKMARK_STACK)) {
            const mark = document.createElement("button");
            mark.type = "button";
            mark.className = `bmb ink-${bookmark.ink}`;
            mark.classList.toggle("from", !run.first);
            mark.classList.toggle("on-to", !run.last);
            mark.dataset.bookmark = bookmark.id;
            mark.style.left = `${run.left}px`;
            mark.style.top = `${run.top}px`;
            mark.style.width = `${Math.max(4, run.right - run.left)}px`;
            // Every run has its hooks and its title, as a printed edition marks a passage across a system break (the
            // web app's score view): the continuation's title follows "…", so it does not read as a rule under the
            // system above.
            const title = document.createElement("span");
            title.className = "w";
            title.setAttribute("aria-hidden", "true");
            title.textContent = run.first ? bookmark.title : `…${bookmark.title}`;
            mark.append(title);
            if (run.first) {
                mark.setAttribute("aria-label", bookmark.label);
            } else {
                mark.tabIndex = -1;
                mark.setAttribute("aria-hidden", "true");
            }
            document.body.append(mark);
        }
    });
}

/** The top of the bookmark titles over a band's first run, in window pixels, for the menu to clear; null when none is there. */
export function bookmarksTop(band: { left: number; right: number; top: number }): number | null {
    let top: number | null = null;
    for (const mark of document.querySelectorAll("body > .bmb")) {
        const box = mark.getBoundingClientRect();
        if (box.right > band.left && box.left < band.right && box.bottom <= band.top + 1 && band.top - box.top <= BOOKMARK_REACH) {
            top = Math.min(top ?? box.top, box.top - BOOKMARK_TITLE);
        }
    }
    return top;
}
