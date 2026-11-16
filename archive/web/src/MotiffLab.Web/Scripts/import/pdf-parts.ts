// A PDF longer than the scan server reads in one request, cut into consecutive parts (version 0.5, issue #479): each part
// is a PDF of its own with those pages exactly as they are (pdf.js extractPages), sent as the whole PDF would be, and the
// notes of the parts are joined in music-core (ScanParts, ScanJoin). Only the bytes the player's piece already has are read;
// nothing is written. One PDF at a time, apart from the library's batch (pdf-pages.ts).

import { openPdf, type PdfDocumentProxy, type PdfLoadingTask } from "../pdf/pdfjs.js";

/** pdf.js 6: a new PDF of some of the document's pages; `null` as the document means this one, pages counted from 0. */
interface PdfWithParts extends PdfDocumentProxy {
    extractPages(pageInfos: { document: null; includePages: [number, number][] }[]): Promise<Uint8Array | null>;
}

let loading: PdfLoadingTask | null = null;
let document: PdfWithParts | null = null;

/** Opens the PDF to cut it; the number of pages, or 0 when it cannot be read. */
export async function openParts(data: Uint8Array): Promise<number> {
    await closeParts();
    try {
        // The bytes are the app's own copy, passed over from .NET: pdf.js may hand them to its worker.
        loading = await openPdf(data);
        document = (await loading.promise) as PdfWithParts;
        return document.numPages;
    } catch (error) {
        console.warn("The PDF's pages could not be read.", error);
        await closeParts();
        return 0;
    }
}

/** Pages first to last, from 1, as a PDF of their own; null when they cannot be cut out. */
export async function partOf(first: number, last: number): Promise<Uint8Array | null> {
    if (!document || first < 1 || last < first || last > document.numPages || typeof document.extractPages !== "function") {
        return null;
    }
    try {
        return await document.extractPages([{ document: null, includePages: [[first - 1, last - 1]] }]);
    } catch (error) {
        console.warn("Pages could not be cut out of the PDF.", error);
        return null;
    }
}

/** Lets pdf.js drop the PDF. */
export async function closeParts(): Promise<void> {
    const task = loading;
    loading = null;
    document = null;
    await task?.destroy().catch(() => undefined);
}
