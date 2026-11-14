// The pages of a PDF piece drawn one at a time as images for the scan server (version 0.5, the library's batch recognition):
// at 300 dpi, as the scan server draws a PDF it is sent (services/omr/app/pages.py), so the notes read come page by page
// and are joined in music-core (ScanJoin). Only the bytes the player's piece already has are read; nothing is written.

import { openPdf, type PdfDocumentProxy, type PdfLoadingTask } from "../pdf/pdfjs.js";

/** As the scan server draws a PDF's pages: 300 dpi, what homr was tested at (decision record 0011). */
const DPI = 300;

/** A PDF's own unit is 1/72 inch. */
const POINTS_PER_INCH = 72;

/** No side longer than the scan server keeps a photo's (MAX_LONG_SIDE there): a poster-sized page is drawn smaller. */
const MAX_LONG_SIDE = 4200;

let loading: PdfLoadingTask | null = null;
let document: PdfDocumentProxy | null = null;

/** Opens the PDF to draw its pages; the number of pages, or 0 when it cannot be read. */
export async function openPages(data: Uint8Array): Promise<number> {
    await closePages();
    try {
        loading = await openPdf(data);
        document = await loading.promise;
        return document.numPages;
    } catch (error) {
        console.warn("The PDF's pages could not be read.", error);
        await closePages();
        return 0;
    }
}

/** One page, from 1, as a PNG image; null when it cannot be drawn. */
export async function pageImage(number: number): Promise<Uint8Array | null> {
    if (!document || number < 1 || number > document.numPages) {
        return null;
    }
    try {
        const page = await document.getPage(number);
        const unit = page.getViewport({ scale: 1 });
        const scale = Math.min(DPI / POINTS_PER_INCH, MAX_LONG_SIDE / Math.max(unit.width, unit.height));
        const viewport = page.getViewport({ scale });
        const canvas = globalThis.document.createElement("canvas");
        canvas.width = Math.ceil(viewport.width);
        canvas.height = Math.ceil(viewport.height);
        await page.render({ canvas, viewport }).promise;
        const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
        // The canvas's memory goes at once, not when it is collected: a page at 300 dpi is tens of megabytes.
        canvas.width = 0;
        canvas.height = 0;
        return blob ? new Uint8Array(await blob.arrayBuffer()) : null;
    } catch (error) {
        console.warn("A page could not be drawn.", error);
        return null;
    }
}

/** Lets pdf.js drop the PDF. */
export async function closePages(): Promise<void> {
    const task = loading;
    loading = null;
    document = null;
    await task?.destroy().catch(() => undefined);
}
