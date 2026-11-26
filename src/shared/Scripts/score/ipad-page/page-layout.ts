// The pages of the iPad's score page: Verovio's A4 pages shown, where each bar sits read by the score geometry
// (Scripts/score/score-geometry.ts), every mark placed again whenever the pages move, and which pages are in view told to
// Swift (`pages`).
import { type PageGeometry, ScoreGeometry, readPage } from "../score-geometry.js";
import { ScoreTimeline } from "../score-timeline.js";
import { type BarFrame, framesByTick } from "../pencil-marks.js";
import type { LaidOut, PageCommands, PagesInView } from "../page-contract.js";
import { READING_LINE_PX, post, state } from "./page-state.js";
import { redraw } from "./page-ink.js";
import { placeBookmarks } from "./page-bookmarks.js";
import { placePhrases } from "./page-phrases.js";
import { placeNotes } from "./page-marks.js";
import { tintUnheard } from "./page-versions.js";
import { forgetRings, placeRings } from "./page-rings.js";
import { markPicked, keepTuplets, selection } from "./page-selection.js";
import { drawHeads, reportPlace } from "./page-playheads.js";
import { forgetSounding, heard } from "./page-sounding.js";
import { drawCardHeads, layoutCard, placeOssias, reportCardWidth } from "./page-card.js";
import { tellMarkMoved } from "./page-mark-place.js";

/** What each engraved page holds, read when it is shown. */
let engraved: PageGeometry[] = [];

/** Shows the pages and reads where their bars are; `timemap` is Verovio's, with measures and rests. */
export function show({ pages, timemap, dark }: Parameters<PageCommands["show"]>[0]): LaidOut {
    document.documentElement.toggleAttribute("data-theme", false);
    if (dark) {
        document.documentElement.setAttribute("data-theme", "dark");
    }
    reported = "";
    forgetRings();
    forgetSounding("page");
    const host = document.getElementById("pages") as HTMLElement;
    host.innerHTML = pages.map((svg, index) => `<div class="page" data-page="${index + 1}">${svg}</div>`).join("");
    state.svgs = pages;
    engraved = pages.map((svg, index) => readPage(svg, index + 1));
    state.timeline = ScoreTimeline.fromTimemap(timemap);
    // The tuplets as blocks for the selection's grid, here and on the roll (Swift passes them on, v0.6.4).
    post({ tuplets: keepTuplets() });
    tintUnheard();
    return layout();
}

/** Where every bar sits now, in the document's pixels: after showing the pages and whenever the width changes. */
export function layout(): LaidOut {
    const placements: { left: number; top: number; scale: number }[] = [];
    document.querySelectorAll("#pages > .page > svg").forEach((svg, index) => {
        const page = engraved[index];
        if (page === undefined) {
            return;
        }
        // The page's view box is fitted into the element and centred, as preserveAspectRatio has it.
        const box = svg.getBoundingClientRect();
        const scale = Math.min(box.width / page.width, box.height / page.height);
        placements.push({
            left: box.left + window.scrollX + (box.width - page.width * scale) / 2,
            top: box.top + window.scrollY + (box.height - page.height * scale) / 2,
            scale,
        });
    });
    const timeline = state.timeline;
    const geometry = new ScoreGeometry(engraved, placements, (id) => timeline?.measureOnset(id));
    state.geometry = geometry;
    state.frames = geometry.barFrames();
    state.byTick = framesByTick(state.frames);
    redraw();
    placePhrases();
    placeBookmarks();
    placeNotes();
    placeRings();
    markPicked();
    selection.place();
    // The playheads go where their places are on the pages now, as the band does (v0.6.3, #608 item 3).
    if (state.heads !== null) {
        reportPlace(drawHeads(), !heard.playing);
    }
    reportPages();
    return { bars: state.frames.length, space: state.frames[0]?.space ?? 0 };
}

/**
 * The pages under the reading line: { first, last, count }, "p. 2 of 4" or, with two to a row, "pp. 1–2 of 4". Between
 * two rows the next row down is the one being read.
 */
export function inView(): PagesInView {
    const pages = [...document.querySelectorAll<HTMLElement>("#pages > .page")];
    if (pages.length === 0) {
        return { first: 0, last: 0, count: 0 };
    }
    let shown = pages.filter((page) => {
        const box = page.getBoundingClientRect();
        return box.top <= READING_LINE_PX && box.bottom > READING_LINE_PX;
    });
    if (shown.length === 0) {
        const next = pages.find((page) => page.getBoundingClientRect().top > READING_LINE_PX) ?? pages[pages.length - 1];
        const top = next.getBoundingClientRect().top;
        shown = pages.filter((page) => Math.abs(page.getBoundingClientRect().top - top) < 2);
    }
    const numbers = shown.map((page) => Number(page.dataset.page));
    return { first: Math.min(...numbers), last: Math.max(...numbers), count: pages.length };
}

/** Where each bar is now, in the document's pixels: for the tests. */
export function frames(): BarFrame[] {
    return state.frames;
}

let reported = "";

/** Tells Swift which pages the bar names, when they change. */
function reportPages(): void {
    const pages = inView();
    const key = `${pages.first}-${pages.last}-${pages.count}`;
    if (key !== reported) {
        reported = key;
        post({ pages });
    }
}

/** Room under the last page for a drawer over the bottom of the screen, in pixels, so the end of the piece can be read. */
export function inset({ bottom }: Parameters<PageCommands["inset"]>[0]): LaidOut {
    document.body.style.paddingBottom = bottom > 0 ? `calc(var(--pad-bottom) + ${bottom}px)` : "";
    return layout();
}

/** Listens to the page's scroll: the pages in view go to Swift, the selection's menu follows its band and a card its mark. */
export function listenScroll(): void {
    let scrolling = 0;
    window.addEventListener("scroll", () => {
        cancelAnimationFrame(scrolling);
        scrolling = requestAnimationFrame(() => {
            reportPages();
            selection.placeMenu();
            tellMarkMoved();
        });
    }, { passive: true });
}

/** Listens to the window's size: the pages, the card in focus and its marks are laid out again, and a card follows its mark. */
export function listenResize(): void {
    let resizing = 0;
    window.addEventListener("resize", () => {
        cancelAnimationFrame(resizing);
        resizing = requestAnimationFrame(() => {
            layout();
            placeOssias();
            layoutCard();
            drawCardHeads();
            reportCardWidth();
            tellMarkMoved();
        });
    });
}
