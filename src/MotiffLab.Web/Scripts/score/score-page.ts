// The score's A4 page (decision record 0017): Verovio's layout options for it, the title that heads
// it, and the PDF's file name. Kept free of the DOM so Node can test it.

/**
 * An A4 page as printed piano music has it (0017): Verovio's page units at scale 100 are tenths of a
 * millimetre, so the page is 210 × 297 mm, with margins of 15 mm at the sides, 12 mm at the top and
 * 15 mm at the bottom, and staves 7.2 mm high (Verovio's default unit). The title heads the first page
 * and the page number the others; the systems spread to fill each page but the last.
 */
export const A4_LAYOUT: Readonly<Record<string, unknown>> = {
    scale: 100,
    pageWidth: 2100,
    pageHeight: 2970,
    pageMarginLeft: 150,
    pageMarginRight: 150,
    pageMarginTop: 120,
    pageMarginBottom: 150,
    adjustPageHeight: false,
    justifyVertically: true,
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
