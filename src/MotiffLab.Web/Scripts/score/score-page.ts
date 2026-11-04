// The score's A4 page (decision record 0017): Verovio's layout options for it, the title that heads
// it, and the PDF's file name. Kept free of the DOM so Node can test it.

/**
 * An A4 page as the redesign engraves it (0018 section 2, design/kit/render.mjs; 0017 for the page itself): Verovio's
 * page units are tenths of a millimetre, so the page is 210 × 297 mm, with margins of 15 mm at the sides, 50 mm at the
 * top (the most Verovio takes: the app's bar floats over it and the app sets the title in it) and 16 mm at the bottom.
 * Scale 50 halves the SVG's units (the view box is 1050 × 1485, fifths of a millimetre) and leaves the layout as it is; Leland is the music font.
 * Verovio's header still heads the first page with the title and the others with their numbers: the screen hides it
 * (the app shows the title in its own type), the PDF keeps it. The screen and the PDF break the systems alike.
 */
export const A4_LAYOUT: Readonly<Record<string, unknown>> = {
    font: "Leland",
    scale: 50,
    pageWidth: 2100,
    pageHeight: 2970,
    pageMarginLeft: 150,
    pageMarginRight: 150,
    pageMarginTop: 500,
    pageMarginBottom: 160,
    spacingSystem: 20,
    spacingStaff: 10,
    adjustPageHeight: false,
    justifyVertically: false,
    breaks: "auto",
    header: "auto",
    footer: "none",
    svgViewBox: true,
};

/**
 * Gives a MusicXML file without a title one, so an A4 page is headed by the piece's name; a file
 * with a title, credits, or MEI (generated notation carries its title) is left as it is.
 */
export function withTitle(data: string, title: string | null | undefined): string {
    const name = title?.trim();
    if (!name || !/<score-(partwise|timewise)\b/.test(data) || /<(work-title|movement-title|credit)\b/.test(data)) {
        return data;
    }
    const escaped = name.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    return data.replace(/<score-(partwise|timewise)\b[^>]*>/, (open) => `${open}<movement-title>${escaped}</movement-title>`);
}

/** A file name for the PDF: the title without characters file systems refuse. */
export function pdfFileName(title: string | null | undefined): string {
    const name = (title ?? "").replace(/[\\/:*?"<>|\u0000-\u001f]/g, "-").trim().slice(0, 120);
    return `${name || "score"}.pdf`;
}

/** The first page's heading as the app sets it (0018 section 2): the title, and the other lines (subtitle, composer, lyricist) under it. */
export interface TitleBlock {
    title: string;
    lines: string[];
}

const ENTITIES: Record<string, string> = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'" };

/**
 * Reads the title block out of a page Verovio engraved with its header: each part of the header carries its
 * label ("title", "subtitle", "composer", …) and its text. Null when the page has no labelled header, as
 * every page but the first.
 */
/** The header's group, from its opening tag to the end that closes it: what follows on the page (the systems, a font's style) is not the header. */
function headGroup(svg: string, classAt: number): string {
    const open = svg.lastIndexOf("<g", classAt);
    const tags = /<g[\s>]|<\/g>/g;
    tags.lastIndex = open;
    let depth = 0;
    for (let tag = tags.exec(svg); tag !== null; tag = tags.exec(svg)) {
        depth += tag[0] === "</g>" ? -1 : 1;
        if (depth === 0) {
            return svg.slice(open, tag.index);
        }
    }
    return svg.slice(open);
}

export function titleBlock(svg: string): TitleBlock | null {
    const start = svg.indexOf('class="pgHead');
    if (start < 0) {
        return null;
    }
    const parts = headGroup(svg, start).split('<title class="labelAttr">').slice(1);
    let title = "";
    const lines: string[] = [];
    for (const part of parts) {
        const label = part.slice(0, part.indexOf("<")).trim();
        // The label's own text ends at </title>; what follows up to the next label is the part's text.
        const body = part.slice(part.indexOf("</title>") + "</title>".length);
        const text = [...body.matchAll(/>([^<]+)</g)]
            .map((match) => match[1])
            // Verovio's indentation between tags is not text.
            .filter((piece) => !/^\s*\n\s*$/.test(piece))
            .join("")
            .replace(/&(#x[0-9a-f]+|#\d+|\w+);/gi, (entity, name: string) =>
                name[0] === "#" ? String.fromCodePoint(Number.parseInt(name.slice(name[1] === "x" ? 2 : 1), name[1] === "x" ? 16 : 10)) : (ENTITIES[name] ?? entity))
            .replace(/\s+/g, " ")
            .trim();
        if (text === "") {
            continue;
        }
        if (label === "title" && title === "") {
            title = text;
        } else if (label !== "page") {
            lines.push(text);
        }
    }
    return title === "" && lines.length === 0 ? null : { title, lines };
}
