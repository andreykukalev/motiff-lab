// pdf.js, loaded on first use from lib/pdfjs, which the service worker keeps once fetched, so a PDF opens offline after
// that: the PDF reader draws pages with it, and the library's batch recognition draws each page to send (version 0.5).

/** The part of pdf.js the reader uses. */
export interface PdfViewport {
    width: number;
    height: number;
}

export interface PdfRenderTask {
    promise: Promise<void>;
    cancel(): void;
}

export interface PdfPage {
    getViewport(options: { scale: number }): PdfViewport;
    render(options: { canvas: HTMLCanvasElement; viewport: PdfViewport }): PdfRenderTask;
}

export interface PdfDocumentProxy {
    numPages: number;
    getPage(number: number): Promise<PdfPage>;
}

/** What getDocument returns: the document to come, and the way to let go of it (pdf.js 6 has no destroy on the document). */
export interface PdfLoadingTask {
    promise: Promise<PdfDocumentProxy>;
    destroy(): Promise<void>;
}

export interface PdfJs {
    GlobalWorkerOptions: { workerSrc: string };
    getDocument(options: Record<string, unknown>): PdfLoadingTask;
}

const PDFJS_BASE = new URL("../../lib/pdfjs/", import.meta.url);

let pdfjs: Promise<PdfJs> | null = null;

export function loadPdfJs(): Promise<PdfJs> {
    pdfjs ??= (async () => {
        const module = (await import(new URL("pdf.min.mjs", PDFJS_BASE).href)) as PdfJs;
        module.GlobalWorkerOptions.workerSrc = new URL("pdf.worker.min.mjs", PDFJS_BASE).href;
        return module;
    })();
    // A failed download can be retried on the next open.
    pdfjs.catch(() => {
        pdfjs = null;
    });
    return pdfjs;
}

/** Starts reading a PDF's bytes with the options both uses share. */
export async function openPdf(data: Uint8Array): Promise<PdfLoadingTask> {
    const pdf = await loadPdfJs();
    return pdf.getDocument({
        data,
        wasmUrl: new URL("wasm/", PDFJS_BASE).href,
        standardFontDataUrl: new URL("standard_fonts/", PDFJS_BASE).href,
        isEvalSupported: false,
        enableXfa: false,
    });
}
