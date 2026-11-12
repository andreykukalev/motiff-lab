// A PDF read as it is (decision record 0023): its pages on the desk as sheets of paper, one continuously scrolling
// document like the score's A4 pages (0018 section 2), drawn by pdf.js only when they come near the view. pdf.js is
// loaded on first use from lib/pdfjs, which the service worker keeps once fetched, so a PDF opens offline after that.

import { PencilLayer, type PencilState } from "../score/pencil-layer.js";
import type { BarFrame } from "../score/pencil-marks.js";
import { saveCover } from "../storage/project-store.js";
import { openPdf, type PdfDocumentProxy, type PdfLoadingTask, type PdfRenderTask } from "./pdfjs.js";

interface DotNetRef {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** How far beyond the view a page is drawn before it scrolls in, as a share of the view's height. */
const DRAW_AHEAD = "150% 0px";

/** The line under the bar that counts as where the player is reading, as on the score. */
const READING_LINE_PX = 72;

/**
 * A page's own unit for the pencil (decision record 0023): a hundred and twentieth of its width, about a printed staff space,
 * so the pen and the highlighter are as thick on a page as on the score, and a stroke keeps its size when the page grows.
 */
const PAGE_SPACES = 120;

/** The cover's width in pixels: enough for a library card at twice the density. */
const COVER_WIDTH = 360;


/** One page: its sheet on the desk, its size at scale 1, and the width it was last drawn at (0 for not drawn). */
interface PageSheet {
    number: number;
    element: HTMLElement;
    width: number;
    height: number;
    drawnAt: number;
    task: PdfRenderTask | null;
}

export class PdfView {
    private document: PdfDocumentProxy | null = null;
    private loading: PdfLoadingTask | null = null;
    private pages: PageSheet[] = [];
    private readonly near = new Set<PageSheet>();
    private observer: IntersectionObserver | null = null;
    private resize: ResizeObserver | null = null;
    private scrollFrame = 0;
    private resizeTimer = 0;
    private shown = "";
    private bookmarked = new Set<number>();
    private readonly pencil: PencilLayer;

    constructor(private readonly host: HTMLElement, private readonly dotnet: DotNetRef) {
        host.addEventListener("scroll", this.onScroll, { passive: true });
        // The pencil writes on the pages as on the score: each page is one frame its strokes are kept against, by its number.
        this.pencil = new PencilLayer(host, {
            frames: () => this.pageFrames(),
            tap: () => {
                // Nothing on a PDF's page is picked.
            },
            changed: (state: PencilState) => void this.dotnet.invokeMethodAsync("OnPencilChanged", state),
            marksChanged: (text: string) => void this.dotnet.invokeMethodAsync("OnPencilMarksChanged", text),
        });
    }

    /** The pencil layer over the pages, for Blazor to set the marks and choose the tools. */
    pencilLayer(): PencilLayer {
        return this.pencil;
    }

    /** Marks the pages the player bookmarked with a ribbon in the corner. */
    setBookmarks(pages: number[]): void {
        this.bookmarked = new Set(pages);
        for (const page of this.pages) {
            page.element.classList.toggle("bookmarked", this.bookmarked.has(page.number));
        }
    }

    /** Opens the PDF and lays out a sheet for each page, scrolled to the page given; the number of pages, or 0 when it cannot be read. */
    async open(data: Uint8Array, startPage: number): Promise<number> {
        this.close();
        let document: PdfDocumentProxy;
        try {
            const loading = await openPdf(data);
            this.loading = loading;
            document = await loading.promise;
        } catch (error) {
            console.warn("The PDF could not be read.", error);
            return 0;
        }

        this.document = document;
        // Every page's size first, so the document scrolls at its full length before any page is drawn.
        const sizes = await Promise.all(
            Array.from({ length: document.numPages }, async (_, index) => {
                const viewport = (await document.getPage(index + 1)).getViewport({ scale: 1 });
                return { width: viewport.width, height: viewport.height };
            }),
        );
        if (this.document !== document) {
            return 0;
        }

        this.host.classList.add("paged");
        this.pages = sizes.map((size, index) => {
            const element = this.host.ownerDocument.createElement("div");
            element.className = "score-page pdf-page";
            element.dataset.page = String(index + 1);
            element.style.setProperty("--ratio", `${size.width} / ${size.height}`);
            element.setAttribute("role", "img");
            element.classList.toggle("bookmarked", this.bookmarked.has(index + 1));
            this.host.append(element);
            return { number: index + 1, element, width: size.width, height: size.height, drawnAt: 0, task: null };
        });

        this.observer = new IntersectionObserver(this.onNear, { root: this.host, rootMargin: DRAW_AHEAD });
        for (const page of this.pages) {
            this.observer.observe(page.element);
        }
        this.resize = new ResizeObserver(this.onResize);
        this.resize.observe(this.host);

        this.pencil.relayout();
        this.showPage(startPage);
        this.reportPages();
        return document.numPages;
    }

    /** Names each page for a screen reader ("Page 2 of 5"), in the interface's language. */
    labelPages(label: string): void {
        for (const page of this.pages) {
            page.element.setAttribute("aria-label", label.replace("{0}", String(page.number)).replace("{1}", String(this.pages.length)));
        }
    }

    /** Scrolls a page's top to under the bar. */
    showPage(number: number): void {
        const page = this.pages[Math.min(Math.max(number, 1), this.pages.length) - 1];
        if (!page) {
            return;
        }
        const top = page.element.offsetTop - parseFloat(getComputedStyle(this.host).paddingTop || "0");
        this.host.scrollTo({ top: Math.max(0, top), behavior: "instant" });
    }

    /** Keeps page 1 as the piece's cover in the library, as the score keeps its first page. */
    async saveCover(id: string, title: string | null): Promise<boolean> {
        const first = this.pages[0];
        if (!this.document || !first) {
            return false;
        }
        try {
            const page = await this.document.getPage(1);
            const viewport = page.getViewport({ scale: COVER_WIDTH / first.width });
            const canvas = this.host.ownerDocument.createElement("canvas");
            canvas.width = Math.round(viewport.width);
            canvas.height = Math.round(viewport.height);
            await page.render({ canvas, viewport }).promise;
            const png = canvas.toDataURL("image/png");
            const svg =
                `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 ${canvas.width} ${canvas.height}">` +
                `<image width="${canvas.width}" height="${canvas.height}" href="${png}" xlink:href="${png}"/></svg>`;
            await saveCover({ id, svg, title, lines: [], pages: this.pages.length, pageStarts: [], phraseStarts: [], barStarts: [], firstBar: 1 });
            return true;
        } catch (error) {
            console.warn("The PDF's cover could not be kept.", error);
            return false;
        }
    }

    /** Lets go of the document and its sheets. */
    close(): void {
        this.observer?.disconnect();
        this.resize?.disconnect();
        this.observer = null;
        this.resize = null;
        window.clearTimeout(this.resizeTimer);
        for (const page of this.pages) {
            page.task?.cancel();
            page.element.remove();
        }
        this.pages = [];
        this.near.clear();
        this.shown = "";
        this.host.classList.remove("paged");
        this.loading?.destroy().catch(() => undefined);
        this.loading = null;
        this.document = null;
    }

    dispose(): void {
        this.pencil.dispose();
        this.close();
        this.host.removeEventListener("scroll", this.onScroll);
        cancelAnimationFrame(this.scrollFrame);
    }

    private readonly onNear = (entries: IntersectionObserverEntry[]): void => {
        for (const entry of entries) {
            const page = this.pages.find((sheet) => sheet.element === entry.target);
            if (!page) {
                continue;
            }
            if (entry.isIntersecting) {
                this.near.add(page);
                void this.draw(page);
            } else {
                this.near.delete(page);
                this.forget(page);
            }
        }
    };

    private readonly onResize = (): void => {
        window.clearTimeout(this.resizeTimer);
        this.resizeTimer = window.setTimeout(() => {
            for (const page of this.near) {
                void this.draw(page);
            }
            this.pencil.relayout();
            this.reportPages();
        }, 150);
    };

    private readonly onScroll = (): void => {
        cancelAnimationFrame(this.scrollFrame);
        this.scrollFrame = requestAnimationFrame(() => this.reportPages());
    };

    /** Draws a page at its sheet's width on this screen, unless it is drawn at that width already. */
    private async draw(page: PageSheet): Promise<void> {
        const document = this.document;
        const width = Math.round(page.element.clientWidth * (window.devicePixelRatio || 1));
        if (!document || width === 0 || page.drawnAt === width) {
            return;
        }
        page.task?.cancel();
        page.drawnAt = width;
        try {
            const pdfPage = await document.getPage(page.number);
            const viewport = pdfPage.getViewport({ scale: width / page.width });
            const canvas = this.host.ownerDocument.createElement("canvas");
            canvas.width = Math.round(viewport.width);
            canvas.height = Math.round(viewport.height);
            canvas.setAttribute("aria-hidden", "true");
            const task = pdfPage.render({ canvas, viewport });
            page.task = task;
            await task.promise;
            if (page.task !== task || this.document !== document) {
                return;
            }
            page.task = null;
            page.element.querySelector("canvas")?.remove();
            page.element.append(canvas);
        } catch (error) {
            if ((error as { name?: string })?.name !== "RenderingCancelledException") {
                console.warn(`Page ${page.number} of the PDF could not be drawn.`, error);
            }
            if (page.drawnAt === width) {
                page.drawnAt = 0;
            }
        }
    }

    /** A page far from the view gives up its drawing, so a long book does not hold every page in memory. */
    private forget(page: PageSheet): void {
        page.task?.cancel();
        page.task = null;
        page.drawnAt = 0;
        page.element.querySelector("canvas")?.remove();
    }

    /** Where each page sits in the view's content pixels, as the frame its pencil strokes are kept against. */
    private pageFrames(): BarFrame[] {
        const view = this.host.getBoundingClientRect();
        return this.pages.map((page) => {
            const box = page.element.getBoundingClientRect();
            const top = box.top - view.top + this.host.scrollTop;
            const left = box.left - view.left + this.host.scrollLeft;
            return { tick: page.number, left, right: left + box.width, top, bottom: top + box.height, staffTop: top, space: box.width / PAGE_SPACES };
        });
    }

    /** Tells the bar which pages are under the reading line ("p. 2 of 4", or "pp. 1–2 of 4" with two to a row). */
    private reportPages(): void {
        if (this.pages.length === 0) {
            return;
        }
        const line = this.host.getBoundingClientRect().top + READING_LINE_PX;
        let shown = this.pages.filter((page) => {
            const box = page.element.getBoundingClientRect();
            return box.top <= line && box.bottom > line;
        });
        if (shown.length === 0) {
            const next = this.pages.find((page) => page.element.getBoundingClientRect().top > line) ?? this.pages[this.pages.length - 1];
            const top = next.element.getBoundingClientRect().top;
            shown = this.pages.filter((page) => Math.abs(page.element.getBoundingClientRect().top - top) < 2);
        }
        const numbers = shown.map((page) => page.number);
        const first = Math.min(...numbers);
        const last = Math.max(...numbers);
        const key = `${first}-${last}-${this.pages.length}`;
        if (key !== this.shown) {
            this.shown = key;
            void this.dotnet.invokeMethodAsync("OnPagesShown", first, last, this.pages.length);
        }
    }
}

export function createPdfView(host: HTMLElement, dotnet: DotNetRef): PdfView {
    return new PdfView(host, dotnet);
}