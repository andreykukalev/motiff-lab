// Finding places on the iPad's score page: the Contents drawer's Pages tab shows the pages as small pictures (`outline`),
// and a page or a bookmark chosen there is brought into view (`showPage`, `reveal`); PDF | Notes leads between a PDF's
// pages and the notes (0023: `firstTickOn`, `showTick`) and keeps the view's place across a new layout (#511 section 10:
// `placeAt`, `lineAt`).
import { anchorAt, anchorOf, lineOf, systemSpans, type ViewAnchor } from "../view-anchor.js";
import type { PageCommands, PageOutline } from "../page-contract.js";
import { READING_LINE_PX, state } from "./page-state.js";

/**
 * The pages as the Contents drawer's Pages tab shows them: each { page, start, end, image }, its stretch of the piece in
 * ticks and its picture as a PNG `width` pixels wide, drawn in the page's ink on its paper; a page whose picture cannot be
 * drawn comes without one.
 */
export async function outline({ width }: Parameters<PageCommands["outline"]>[0]): Promise<PageOutline[]> {
    const geometry = state.geometry;
    if (geometry === null) {
        return [];
    }
    const style = getComputedStyle(document.documentElement);
    const ink = style.getPropertyValue("--notation").trim() || "black";
    const paper = style.getPropertyValue("--paper").trim() || "white";
    const pages: PageOutline[] = [];
    for (const [index, svg] of state.svgs.entries()) {
        const bars = geometry.bars.filter((bar) => bar.page === index + 1);
        if (bars.length === 0) {
            continue;
        }
        const start = Math.min(...bars.map((bar) => bar.tick));
        const end = Math.max(...bars.map((bar) => (Number.isFinite(bar.end) ? bar.end : Number.MAX_SAFE_INTEGER)));
        pages.push({ page: index + 1, start, end, image: await picture(svg, width, ink, paper) });
    }
    return pages;
}

/** A page drawn as a PNG on its paper, as a data URL; "" when it cannot be drawn. */
async function picture(svg: string, width: number, ink: string, paper: string): Promise<string> {
    const height = Math.round((width * 297) / 210);
    // Verovio's notation is black on the page's own scale; the picture takes the stand's ink, as the page does.
    const inked = svg.replace(/ color="black"/, ` color="${ink}" fill="${ink}"`);
    const url = URL.createObjectURL(new Blob([inked], { type: "image/svg+xml" }));
    try {
        const image = new Image();
        image.src = url;
        await image.decode();
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d") as CanvasRenderingContext2D;
        context.fillStyle = paper;
        context.fillRect(0, 0, width, height);
        context.drawImage(image, 0, 0, width, height);
        return canvas.toDataURL("image/png");
    } catch {
        return "";
    } finally {
        URL.revokeObjectURL(url);
    }
}

/** Brings a page to the top of the view, under the bar; at once when `instant`, as the notes open there (#533 6). */
export function showPage({ page, instant = false }: Parameters<PageCommands["showPage"]>[0]): boolean {
    const element = document.querySelector(`#pages > .page[data-page="${page}"]`);
    if (element === null) {
        return false;
    }
    const top = element.getBoundingClientRect().top + window.scrollY - parseFloat(getComputedStyle(document.body).paddingTop);
    window.scrollTo({ top: Math.max(0, top), behavior: instant ? "instant" : "smooth" });
    return true;
}

/** Brings the bar that holds `start` into view, a third of the way down, unless it is in view already. */
export function reveal({ start }: Parameters<PageCommands["reveal"]>[0]): boolean {
    const bar = state.geometry?.barAt(start);
    if (bar === undefined) {
        return false;
    }
    const top = bar.top - window.scrollY;
    if (top < READING_LINE_PX || bar.bottom - window.scrollY > window.innerHeight) {
        window.scrollTo({ top: Math.max(0, bar.top - window.innerHeight / 3), behavior: "smooth" });
    }
    return true;
}

/** Where the notes on a page (from 1) start: its first bar's tick, or null. PDF | Notes leads from it to the PDF's page (0023). */
export function firstTickOn({ page }: Parameters<PageCommands["firstTickOn"]>[0]): number | null {
    const ticks = (state.geometry?.bars ?? []).filter((bar) => bar.page === page).map((bar) => bar.tick);
    return ticks.length > 0 ? Math.min(...ticks) : null;
}

/** Brings the page a place in the piece is on to the top, under the bar, as the PDF's pages lead to the notes (0023). */
export function showTick({ tick, instant = false }: Parameters<PageCommands["showTick"]>[0]): boolean {
    const bars = state.geometry?.bars ?? [];
    const before = bars.filter((bar) => bar.tick <= tick);
    const bar = before.length > 0 ? before.reduce((latest, candidate) => (candidate.tick > latest.tick ? candidate : latest)) : bars[0];
    return bar === undefined ? false : showPage({ page: bar.page, instant });
}

/**
 * Where the view is, as a place in the notes that survives a new layout (PDF | Notes, #511 section 10): the system a line of
 * the document crosses, by the tick of its first bar, and how far into it the line is. Swift reads the line from its scroll
 * view (`y`, in the document's pixels) and keeps the place while the PDF is shown; null before the pages are laid out.
 */
export function placeAt({ y }: Parameters<PageCommands["placeAt"]>[0]): ViewAnchor | null {
    const geometry = state.geometry;
    return geometry ? anchorAt(systemSpans(geometry.systems, geometry.bars), y) : null;
}

/** The line of the document a place kept by `placeAt` lies at now, in the document's pixels, for Swift to scroll to; or null. */
export function lineAt(place: unknown): number | null {
    const geometry = state.geometry;
    const anchor = anchorOf(place);
    return geometry && anchor ? lineOf(systemSpans(geometry.systems, geometry.bars), anchor) : null;
}

