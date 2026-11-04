// The score view (S1-W7, decision record 0008): engraves MusicXML or generated MEI with Verovio,
// marks the notes being heard, scrolls with playback and moves playback to a clicked note, and
// highlights the places a chosen fact names (#25). A click on a bar selects it (0012). The player's
// pencil marks are drawn over it (pencil-layer.ts, 0015). The score is laid out as A4 pages or as one
// strip the width of the view, and exported as an A4 PDF (decision record 0017). In the redesign (0018)
// the A4 pages are one continuously scrolling document, two to a row in landscape, with the title set
// by the app on the first page, the pages under the top of the view reported for the bar, and the
// phrases drawn as pencil brackets above the top staff that select their phrase when tapped.
// Blazor imports this module through JS interop. Verovio loads on first use.

import type { BarFrame } from "./pencil-marks.js";
import { PencilLayer, type PencilState } from "./pencil-layer.js";
import { ScoreTimeline, highlightedIds, type HighlightPlace, type TimemapEntry } from "./score-timeline.js";
import { A4_HEIGHT, A4_WIDTH, buildPdf, crop, inkBounds, type InkLayer, type PdfPage } from "./pdf-document.js";
import { hasText, svgToPdfOperators, textOnlySvg } from "./svg-to-pdf.js";
import { A4_LAYOUT, pdfFileName, titleBlock, withTitle } from "./score-page.js";
import { ScoreGeometry, readPage, type PageGeometry, type PagePlacement } from "./score-geometry.js";
import { downloadBytes } from "../storage/project-store.js";

interface DotNetObject {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** The part of the playback player (Scripts/playback/playback.ts) the view reads every frame. */
interface PlayerHandle {
    currentTick(): number;
    state(): string;
}

/** The part of Verovio's VerovioToolkit the view uses. */
interface VerovioToolkit {
    setOptions(options: Record<string, unknown>): void;
    /** 1 when the data was read, 0 when not. */
    loadData(data: string): number;
    getPageCount(): number;
    renderToSVG(page: number): string;
    renderToTimemap(options?: Record<string, unknown>): TimemapEntry[];
    redoLayout(options?: Record<string, unknown>): void;
    /** The MIDI values of an engraved note, its key among them; empty for a rest. */
    getMIDIValuesForElement(id: string): { pitch?: number };
    getLog(): string;
    /** Frees the toolkit's memory; it cannot be used after. */
    destroy?(): void;
}

interface VerovioWrapper {
    VerovioToolkit: new (module: unknown) => VerovioToolkit;
    enableLogToBuffer(value: boolean, module: unknown): void;
}

/** What Blazor gets back from a load. */
export interface ScoreLoadResult {
    pages: number;
    notes: number;
    /** The title the first page is headed with, for the bar; null in the strip or when the score has none. */
    title: string | null;
}

const VEROVIO_BASE = new URL("../../lib/verovio/", import.meta.url);

/** The resolution of the text drawn over a PDF page's vectors: print quality. */
const PDF_TEXT_DPI = 300;

/** Verovio units per pixel are 100 / SCALE; 40 gives staves about 9 px apart. */
const SCALE = 40;

const MIN_PAGE_WIDTH = 900;

const RELAYOUT_DELAY_MS = 200;

/** Only a change of width beyond this lays the score out again. */
const RELAYOUT_THRESHOLD_PX = 24;

const PLAYING_CLASS = "playing";

/** After the player scrolls the score by hand, playback leaves it where they put it for this long before following again. */
const FOLLOW_PAUSE_MS = 4000;

/** The keys that scroll the score when it has focus. */
const SCROLL_KEYS = new Set(["ArrowUp", "ArrowDown", "PageUp", "PageDown", "Home", "End"]);

/** Notes outside the range that plays for the selection, drawn faint as on the piano roll. */
const OUTSIDE_CLASS = "outside";

/** A phrase's pencil bracket above the top staff (0018 section 3, design/kit/ds.css), placed over the engraving. */
const BRACKET_CLASS = "bracket";

/** The line under the bar the page count reads at: the page under it is the page being read. */
const READING_LINE_PX = 72;

/** The notes and rests of the places a chosen fact names (#25): blue, with a box around each, so they differ from playing and outside by more than colour. */
const HIGHLIGHT_CLASS = "highlight";
const HIGHLIGHT_MARK_CLASS = "highlight-mark";

/** A phrase as Blazor sends it for its bracket: where it starts and ends, its number at the level shown, and how its start was found. */
export interface ScorePhrase {
    start: number;
    end: number;
    number: number;
    /** "start", "imported", "inferred" or "yours", as a class. */
    kind: string;
    /** The selected phrase: its bracket is drawn in the accent. */
    current: boolean;
    /** Whether the player kept ideas from it: a plum dot at the bracket's right end. */
    ideas: boolean;
    /** What a screen reader says for the bracket. */
    label: string;
}

let verovio: Promise<{ module: unknown; wrapper: VerovioWrapper }> | null = null;
let toolkit: Promise<VerovioToolkit> | null = null;

function loadVerovio(): Promise<{ module: unknown; wrapper: VerovioWrapper }> {
    verovio ??= (async () => {
        const [factory, wrapper] = await Promise.all([
            import(new URL("verovio-module.mjs", VEROVIO_BASE).href) as Promise<{ default: () => Promise<unknown> }>,
            import(new URL("verovio.mjs", VEROVIO_BASE).href) as Promise<VerovioWrapper>,
        ]);
        const module = await factory.default();
        wrapper.enableLogToBuffer(true, module);
        return { module, wrapper };
    })();
    // A failed download can be retried on the next load.
    verovio.catch(() => {
        verovio = null;
    });
    return verovio;
}

function loadToolkit(): Promise<VerovioToolkit> {
    toolkit ??= loadVerovio().then(({ module, wrapper }) => new wrapper.VerovioToolkit(module));
    toolkit.catch(() => {
        toolkit = null;
    });
    return toolkit;
}

export class ScoreView {
    private readonly host: HTMLElement;
    private readonly dotnet: DotNetObject;
    private readonly resizeObserver: ResizeObserver;
    private engraver: VerovioToolkit | null = null;
    private timeline: ScoreTimeline | null = null;
    private player: PlayerHandle | null = null;
    private marked = new Set<string>();
    private markedKey = "";
    /** The range that plays for the selection [start, end), or null when nothing is selected. */
    private range: { start: number; end: number } | null = null;
    private holding = false;
    private phrases: ScorePhrase[] = [];
    /** The places of the chosen fact, and the engraved notes and rests that stand for them. */
    private highlight: HighlightPlace[] = [];
    private highlighted = new Set<string>();
    /** The MIDI key of each engraved note asked about since the last load; undefined for a rest or when Verovio has none. */
    private pitches = new Map<string, number | undefined>();
    private laidOutWidth = 0;
    /** A4 pages are fitted to the view's height in landscape, so a change of height moves the marks over them too. */
    private laidOutHeight = 0;
    /** When the player last scrolled the score by hand, from performance.now(). */
    private scrolledByHand = -Infinity;
    private frame = 0;
    private relayoutTimer = 0;
    private disposed = false;
    private readonly pencil: PencilLayer;
    /** A4 pages (0017), or one strip the width of the view. */
    private paged = true;
    /** What was loaded, so a change of layout can engrave it again. */
    private data: string | null = null;
    /** The pages as engraved, and their geometry once read (score-geometry.ts): read again after every layout. */
    private pageSvgs: string[] = [];
    private pageGeometry: PageGeometry[] | null = null;
    /** The pages under the reading line as last reported, so the bar hears only of a change. */
    private pagesShown = "";
    private scrollFrame = 0;

    constructor(host: HTMLElement, dotnet: DotNetObject) {
        this.host = host;
        this.dotnet = dotnet;
        this.pencil = new PencilLayer(host, {
            frames: () => this.barFrames(),
            tap: (target, extend) => this.pick(target, extend),
            changed: (state: PencilState) => void this.dotnet.invokeMethodAsync("OnPencilChanged", state),
            marksChanged: (text: string) => void this.dotnet.invokeMethodAsync("OnPencilMarksChanged", text),
        });
        this.host.classList.toggle("paged", this.paged);
        this.host.addEventListener("click", this.onClick);
        // Passive: they only note that the player is scrolling, and never hold the scroll up.
        this.host.addEventListener("wheel", this.onScrollByHand, { passive: true });
        this.host.addEventListener("touchmove", this.onScrollByHand, { passive: true });
        this.host.addEventListener("keydown", this.onScrollByHand);
        this.host.addEventListener("keydown", this.onKeyDown);
        this.host.addEventListener("scroll", this.onScroll, { passive: true });
        this.resizeObserver = new ResizeObserver(() => this.scheduleRelayout());
        this.resizeObserver.observe(host);
    }

    /**
     * Engraves a MusicXML or MEI document, replacing what was shown; a MusicXML file without a title
     * is headed by the one given. Throws if Verovio cannot read it.
     */
    async load(data: string, title?: string | null): Promise<ScoreLoadResult> {
        const engraver = await loadToolkit();
        if (this.disposed) {
            return { pages: 0, notes: 0, title: null };
        }

        const titled = withTitle(data, title);
        // The page header is made when the data is read, so a change of layout reads it again.
        engraver.setOptions(this.layoutOptions());
        if (!engraver.loadData(titled)) {
            throw new Error(`The score could not be engraved. ${engraver.getLog()}`.trim());
        }

        this.data = titled;
        this.engraver = engraver;
        this.pitches = new Map();
        this.highlighted = new Set();
        this.timeline = ScoreTimeline.fromTimemap(engraver.renderToTimemap({ includeRests: true, includeMeasures: true }));
        this.render();
        const heading = this.pageSvgs.length > 0 ? titleBlock(this.pageSvgs[0]) : null;
        return { pages: engraver.getPageCount(), notes: this.timeline.spans.length, title: heading?.title || null };
    }

    /**
     * Lays the score out as A4 pages, as it prints, or as one strip the width of the view. What is
     * showing is engraved again, keeping the player's place in it.
     */
    async setPaged(paged: boolean): Promise<void> {
        if (paged === this.paged) {
            return;
        }
        this.paged = paged;
        this.host.classList.toggle("paged", paged);
        const engraver = this.engraver;
        const data = this.data;
        if (engraver === null || data === null || this.disposed) {
            return;
        }
        const host = this.host;
        const place = host.scrollHeight > 0 ? host.scrollTop / host.scrollHeight : 0;
        engraver.setOptions(this.layoutOptions());
        engraver.loadData(data);
        this.pitches = new Map();
        this.highlighted = new Set();
        this.timeline = ScoreTimeline.fromTimemap(engraver.renderToTimemap({ includeRests: true, includeMeasures: true }));
        this.render();
        host.scrollTop = place * host.scrollHeight;
    }

    /**
     * Engraves what is showing on A4 pages and hands the player a PDF of them (0017): the notation as
     * vectors, its text drawn by the browser over it. Returns the number of pages.
     */
    async exportPdf(title?: string | null): Promise<number> {
        const data = this.data;
        if (data === null) {
            throw new Error("There is no score to export.");
        }
        const { module, wrapper } = await loadVerovio();
        // A toolkit of its own, so the score on screen keeps its layout and ids.
        const engraver = new wrapper.VerovioToolkit(module);
        const svgs: string[] = [];
        try {
            engraver.setOptions({ ...A4_LAYOUT });
            if (!engraver.loadData(data)) {
                throw new Error(`The score could not be engraved. ${engraver.getLog()}`.trim());
            }
            for (let page = 1; page <= engraver.getPageCount(); page++) {
                svgs.push(engraver.renderToSVG(page));
            }
        } finally {
            engraver.destroy?.();
        }

        const pages: PdfPage[] = [];
        for (const svg of svgs) {
            pages.push({ operators: svgToPdfOperators(svg, A4_WIDTH, A4_HEIGHT), ink: hasText(svg) ? await textInk(svg) : null });
        }
        const pdf = await buildPdf(pages, { title: title?.trim() || undefined, creator: "Motiff Lab" });
        downloadBytes(pdfFileName(title), pdf, "application/pdf");
        return pages.length;
    }

    /** Marks the notes the player is sounding, every animation frame, until called with null. */
    follow(player: PlayerHandle | null): void {
        this.player = player;
        cancelAnimationFrame(this.frame);
        if (player !== null) {
            this.frame = requestAnimationFrame(this.tick);
        } else {
            this.mark([], false);
        }
    }

    /**
     * Shows the range that plays for the selection: notes that start outside [start, end) go faint,
     * and the first note in it is scrolled into view. Null shows every note at full strength.
     */
    setRange(start: number | null, end: number | null): void {
        this.range = start === null || end === null ? null : { start, end };
        this.applyRange(true);
    }

    /**
     * While held, the score never gets shorter: each engraving keeps at least the tallest height so far,
     * so the page below it does not jump when two versions take turns. Released, it fits its content again.
     */
    holdHeight(hold: boolean): void {
        this.holding = hold;
        this.host.style.minHeight = "";
        if (hold) {
            this.keepHeight();
        }
    }

    /** Draws the phrases' brackets above the top staff; an empty list draws none. */
    setPhrases(phrases: ScorePhrase[]): void {
        this.phrases = phrases;
        this.placePhrases();
    }

    /**
     * Scrolls exactly one page (one row of pages in landscape) on, or back with -1, within the same
     * continuous document (0018 section 2); in the strip, by most of the view's height.
     */
    stepPage(direction: number): void {
        const host = this.host;
        const step = Math.sign(direction) || 1;
        if (!this.paged) {
            host.scrollBy({ top: step * host.clientHeight * 0.85, behavior: "smooth" });
            return;
        }
        const tops = [...new Set(this.pageElements().map((page) => Math.round(page.offsetTop)))].sort((a, b) => a - b);
        const now = host.scrollTop;
        const gap = tops.length > 0 ? Math.max(0, tops[0]) : 0;
        const current = tops.filter((top) => top - gap <= now + 2).length - 1;
        const target = tops[Math.min(tops.length - 1, Math.max(0, current + step))];
        if (target !== undefined) {
            this.scrolledByHand = performance.now();
            host.scrollTo({ top: Math.max(0, target - gap), behavior: "smooth" });
        }
    }

    /**
     * Highlights the places a chosen fact names: their engraved notes and rests turn blue with a box
     * around each, and the first is scrolled into view. An empty list clears the highlight.
     */
    setHighlight(places: HighlightPlace[]): void {
        this.highlight = places;
        this.applyHighlight(true);
    }

    /**
     * Where every page, system, bar and note sits now, in the score's content pixels, and the boxes overlays
     * are drawn from (score-geometry.ts, 0018 preparation item 11); null before a score is engraved.
     */
    geometry(): ScoreGeometry | null {
        const timeline = this.timeline;
        if (timeline === null || this.pageSvgs.length === 0) {
            return null;
        }
        this.pageGeometry ??= this.pageSvgs.map((svg, index) => readPage(svg, index + 1));
        const pages = this.pageGeometry;
        const view = this.host.getBoundingClientRect();
        const placements: PagePlacement[] = [];
        this.host.querySelectorAll(":scope > .score-page > svg").forEach((svg, index) => {
            const page = pages[index];
            if (page === undefined) {
                return;
            }
            // The page's view box is fitted into the element and centred, as preserveAspectRatio has it.
            const box = svg.getBoundingClientRect();
            const scale = Math.min(box.width / page.width, box.height / page.height);
            placements.push({
                left: box.left - view.left + this.host.scrollLeft + (box.width - page.width * scale) / 2,
                top: box.top - view.top + this.host.scrollTop + (box.height - page.height * scale) / 2,
                scale,
            });
        });
        return new ScoreGeometry(pages, placements, (id) => timeline.measureOnset(id));
    }

    /** The pencil marks over the score, for Blazor to choose the piece and the tool (0015). */
    pencilLayer(): PencilLayer {
        return this.pencil;
    }

    dispose(): void {
        this.disposed = true;
        this.pencil.dispose();
        cancelAnimationFrame(this.frame);
        clearTimeout(this.relayoutTimer);
        this.resizeObserver.disconnect();
        this.host.removeEventListener("click", this.onClick);
        this.host.removeEventListener("wheel", this.onScrollByHand);
        this.host.removeEventListener("touchmove", this.onScrollByHand);
        this.host.removeEventListener("keydown", this.onScrollByHand);
        this.host.removeEventListener("keydown", this.onKeyDown);
        this.host.removeEventListener("scroll", this.onScroll);
        cancelAnimationFrame(this.scrollFrame);
        this.host.replaceChildren();
    }

    private layoutOptions(): Record<string, unknown> {
        this.laidOutWidth = this.host.clientWidth;
        this.laidOutHeight = this.host.clientHeight;
        if (this.paged) {
            return { ...A4_LAYOUT };
        }
        return {
            scale: SCALE,
            pageWidth: Math.max(MIN_PAGE_WIDTH, Math.round((this.laidOutWidth * 100) / SCALE)),
            pageMarginLeft: 40,
            pageMarginRight: 40,
            pageMarginTop: 40,
            pageMarginBottom: 40,
            adjustPageHeight: true,
            breaks: "auto",
            header: "none",
            footer: "none",
            justifyVertically: false,
            svgViewBox: true,
        };
    }

    private render(): void {
        const engraver = this.engraver;
        if (engraver === null) {
            return;
        }

        const pages: Element[] = [];
        this.pageSvgs = [];
        this.pageGeometry = null;
        for (let page = 1; page <= engraver.getPageCount(); page++) {
            const container = document.createElement("div");
            container.className = "score-page";
            const svg = engraver.renderToSVG(page);
            this.pageSvgs.push(svg);
            container.innerHTML = svg;
            if (this.paged) {
                container.dataset.page = String(page);
                const heading = page === 1 ? titleBlock(svg) : null;
                if (heading !== null) {
                    container.append(titleElement(heading.title, heading.lines));
                }
            }
            pages.push(container);
        }
        this.host.replaceChildren(...pages);
        this.pagesShown = "";
        if (this.holding) {
            this.keepHeight();
        }

        this.applyRange(false);
        this.placePhrases();
        this.applyHighlight(false);

        // The new SVG has no marks; put back the ones that were showing.
        const marked = [...this.marked];
        this.marked = new Set();
        this.markedKey = "";
        this.mark(marked, false);

        this.pencil.relayout();
        this.reportPages();
    }

    private pageElements(): HTMLElement[] {
        return [...this.host.querySelectorAll<HTMLElement>(":scope > .score-page")];
    }

    /** Tells the bar which pages are under the reading line ("p. 2 of 4", or "pp. 1–2 of 4" with two to a row). */
    private reportPages(): void {
        if (!this.paged) {
            if (this.pagesShown !== "strip") {
                this.pagesShown = "strip";
                void this.dotnet.invokeMethodAsync("OnPagesShown", 0, 0, 0);
            }
            return;
        }
        const pages = this.pageElements();
        if (pages.length === 0) {
            return;
        }
        const line = this.host.getBoundingClientRect().top + READING_LINE_PX;
        let shown = pages.filter((page) => {
            const box = page.getBoundingClientRect();
            return box.top <= line && box.bottom > line;
        });
        if (shown.length === 0) {
            // Between two rows: the next row down is the one being read.
            const next = pages.find((page) => page.getBoundingClientRect().top > line) ?? pages[pages.length - 1];
            const top = next.getBoundingClientRect().top;
            shown = pages.filter((page) => Math.abs(page.getBoundingClientRect().top - top) < 2);
        }
        const numbers = shown.map((page) => Number(page.dataset.page)).filter((number) => number > 0);
        const key = `${Math.min(...numbers)}-${Math.max(...numbers)}-${pages.length}`;
        if (numbers.length > 0 && key !== this.pagesShown) {
            this.pagesShown = key;
            void this.dotnet.invokeMethodAsync("OnPagesShown", Math.min(...numbers), Math.max(...numbers), pages.length);
        }
    }

    private readonly onScroll = (): void => {
        cancelAnimationFrame(this.scrollFrame);
        this.scrollFrame = requestAnimationFrame(() => this.reportPages());
    };

    private readonly onKeyDown = (event: KeyboardEvent): void => {
        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.target !== this.host) {
            return;
        }
        // The arrow keys move exactly one page (0018 section 2); Page Up and Page Down step phrases (phrase-keys.ts).
        const step = event.key === "ArrowDown" ? 1 : event.key === "ArrowUp" ? -1 : 0;
        if (step !== 0) {
            event.preventDefault();
            this.stepPage(step);
        }
    };

    /**
     * Where each engraved bar sits, in the score's content pixels, for the pencil marks: its box, and its
     * top staff line and the space between lines, so marks keep their size against the staves.
     */
    private barFrames(): BarFrame[] {
        return this.geometry()?.barFrames() ?? [];
    }

    private keepHeight(): void {
        this.host.style.minHeight = `${this.host.getBoundingClientRect().height}px`;
    }

    private applyRange(scroll: boolean): void {
        const timeline = this.timeline;
        if (timeline === null) {
            return;
        }
        const range = this.range;
        let first: Element | null = null;
        for (const span of timeline.spans) {
            const element = this.element(span.id);
            const inside = range === null || (span.onset >= range.start && span.onset < range.end);
            element?.classList.toggle(OUTSIDE_CLASS, !inside);
            if (inside && range !== null && first === null) {
                first = element;
            }
        }
        if (scroll && first !== null && this.player?.state() !== "playing") {
            this.scrollIntoView(first);
        }
    }

    private placePhrases(): void {
        for (const old of this.host.querySelectorAll(`:scope > .${BRACKET_CLASS}`)) {
            old.remove();
        }
        if (this.phrases.length === 0) {
            return;
        }
        const geometry = this.geometry();
        if (geometry === null) {
            return;
        }
        for (const phrase of this.phrases) {
            for (const run of geometry.bracket(phrase.start, phrase.end)) {
                const bracket = document.createElement("button");
                bracket.type = "button";
                bracket.className = `${BRACKET_CLASS} ${phrase.kind}`;
                bracket.classList.toggle("on", phrase.current);
                bracket.classList.toggle("from", !run.first);
                bracket.classList.toggle("on-to", !run.last);
                bracket.dataset.phrase = String(phrase.number);
                bracket.style.left = `${run.left}px`;
                bracket.style.top = `${run.top}px`;
                bracket.style.width = `${Math.max(4, run.right - run.left)}px`;
                if (run.first) {
                    bracket.setAttribute("aria-label", phrase.label);
                    bracket.setAttribute("aria-pressed", phrase.current ? "true" : "false");
                    const number = document.createElement("span");
                    number.className = "n";
                    number.setAttribute("aria-hidden", "true");
                    number.textContent = String(phrase.number);
                    bracket.append(number);
                } else {
                    // The rest of a phrase that runs on to the next system is the same target, said once.
                    bracket.tabIndex = -1;
                    bracket.setAttribute("aria-hidden", "true");
                }
                if (run.last && phrase.ideas) {
                    const dot = document.createElement("span");
                    dot.className = "dot";
                    bracket.append(dot);
                }
                this.host.append(bracket);
            }
        }
    }

    private applyHighlight(scroll: boolean): void {
        for (const old of this.host.querySelectorAll(`.${HIGHLIGHT_MARK_CLASS}`)) {
            old.remove();
        }
        for (const id of this.highlighted) {
            this.element(id)?.classList.remove(HIGHLIGHT_CLASS);
        }
        const timeline = this.timeline;
        this.highlighted = timeline === null || this.highlight.length === 0 ? new Set() : highlightedIds(timeline, this.highlight, (id) => this.pitchOf(id));

        const view = this.host.getBoundingClientRect();
        let first: Element | null = null;
        for (const id of this.highlighted) {
            const element = this.element(id);
            if (element === null) {
                continue;
            }
            element.classList.add(HIGHLIGHT_CLASS);
            first ??= element;
            // The box goes round the notehead, not the stem, so neighbouring notes keep their own boxes.
            const box = (element.querySelector("g.notehead") ?? element).getBoundingClientRect();
            const mark = document.createElement("span");
            mark.className = HIGHLIGHT_MARK_CLASS;
            mark.setAttribute("aria-hidden", "true");
            mark.style.left = `${box.left - view.left + this.host.scrollLeft - 3}px`;
            mark.style.top = `${box.top - view.top + this.host.scrollTop - 3}px`;
            mark.style.width = `${box.width + 6}px`;
            mark.style.height = `${box.height + 6}px`;
            this.host.append(mark);
        }
        if (scroll && first !== null && this.player?.state() !== "playing") {
            this.scrollIntoView(first);
        }
    }

    private pitchOf(id: string): number | undefined {
        const engraver = this.engraver;
        if (engraver === null) {
            return undefined;
        }
        if (!this.pitches.has(id)) {
            let pitch: number | undefined;
            try {
                const values = engraver.getMIDIValuesForElement(id);
                pitch = typeof values?.pitch === "number" ? values.pitch : undefined;
            } catch {
                pitch = undefined;
            }
            this.pitches.set(id, pitch);
        }
        return this.pitches.get(id);
    }

    private scheduleRelayout(): void {
        const wider = Math.abs(this.host.clientWidth - this.laidOutWidth) >= RELAYOUT_THRESHOLD_PX;
        const taller = this.paged && Math.abs(this.host.clientHeight - this.laidOutHeight) >= RELAYOUT_THRESHOLD_PX;
        if (this.engraver === null || (!wider && !taller)) {
            return;
        }
        clearTimeout(this.relayoutTimer);
        this.relayoutTimer = window.setTimeout(() => {
            if (this.paged && this.engraver !== null && !this.disposed) {
                // A4 pages keep their layout and only scale with the view: the marks over them move with it.
                this.laidOutWidth = this.host.clientWidth;
                this.laidOutHeight = this.host.clientHeight;
                this.placePhrases();
                this.applyHighlight(false);
                this.pencil.relayout();
                this.reportPages();
            } else if (this.engraver !== null && !this.disposed) {
                this.engraver.setOptions(this.layoutOptions());
                this.engraver.redoLayout();
                // Laid out again at a new width, the score keeps the player's place in it rather than jumping to the top.
                const host = this.host;
                const place = host.scrollHeight > 0 ? host.scrollTop / host.scrollHeight : 0;
                this.render();
                host.scrollTop = place * host.scrollHeight;
            }
        }, RELAYOUT_DELAY_MS);
    }

    private readonly tick = (): void => {
        const player = this.player;
        if (player === null || this.disposed) {
            return;
        }
        if (this.timeline !== null) {
            const playing = player.state() !== "stopped";
            this.mark(this.timeline.soundingAt(player.currentTick()), playing);
        }
        this.frame = requestAnimationFrame(this.tick);
    };

    private mark(ids: string[], scroll: boolean): void {
        const key = ids.join(" ");
        if (key === this.markedKey) {
            return;
        }
        this.markedKey = key;

        const next = new Set(ids);
        for (const id of this.marked) {
            if (!next.has(id)) {
                this.element(id)?.classList.remove(PLAYING_CLASS);
            }
        }
        for (const id of next) {
            if (!this.marked.has(id)) {
                this.element(id)?.classList.add(PLAYING_CLASS);
            }
        }
        this.marked = next;

        // Playback does not pull the score back while the player is reading elsewhere in it.
        if (scroll && ids.length > 0 && performance.now() - this.scrolledByHand >= FOLLOW_PAUSE_MS) {
            this.scrollIntoView(this.element(ids[0]));
        }
    }

    /** Keeps the system (line of staves) with the sounding notes in view. */
    private scrollIntoView(note: Element | null): void {
        const target = note?.closest("g.system") ?? note;
        if (target === null || target === undefined) {
            return;
        }
        const view = this.host.getBoundingClientRect();
        const box = target.getBoundingClientRect();
        // The bar floats over the top of the view, so the system goes below it.
        if (box.top < view.top + READING_LINE_PX - 16 || box.bottom > view.bottom) {
            this.host.scrollTo({ top: this.host.scrollTop + box.top - view.top - READING_LINE_PX, behavior: "smooth" });
        }
    }

    private readonly onScrollByHand = (event: Event): void => {
        if (event instanceof KeyboardEvent && !SCROLL_KEYS.has(event.key)) {
            return;
        }
        this.scrolledByHand = performance.now();
    };

    private element(id: string): Element | null {
        return this.host.querySelector(`#${CSS.escape(id)}`);
    }

    private readonly onClick = (event: MouseEvent): void => {
        const bracket = event.target instanceof Element ? event.target.closest<HTMLElement>(`.${BRACKET_CLASS}`) : null;
        if (bracket !== null) {
            void this.dotnet.invokeMethodAsync("OnBracketClicked", Number(bracket.dataset.phrase));
            return;
        }
        // A press the pencil layer took is handled there: a tap comes back through pick.
        if (event.target instanceof Element && !this.pencil.swallowsClick()) {
            this.pick(event.target, event.shiftKey);
        }
    };

    /** A click or a pen tap on the score: a note moves playback to it, a bar away from its notes selects it. */
    private pick(target: Element, extend: boolean): void {
        const timeline = this.timeline;
        if (timeline === null) {
            return;
        }
        // A click on a chord's stem finds the chord; its notes start where it starts.
        const hit = target.closest("g.note, g.chord");
        const note = hit?.matches("g.chord") ? hit.querySelector("g.note") : hit;
        const onset = note?.id ? timeline.onsetOf(note.id) : undefined;
        if (onset !== undefined) {
            void this.dotnet.invokeMethodAsync("OnScoreNoteClicked", onset);
            return;
        }
        // A click on a bar away from its notes selects that bar, as on the roll's bar numbers (0012); Shift extends to it.
        const measure = target.closest("g.measure");
        const start = measure?.id ? timeline.measureOnset(measure.id) : undefined;
        if (start !== undefined) {
            void this.dotnet.invokeMethodAsync("OnScoreBarClicked", start, extend);
        }
    }
}

/** The title block on the first page, in the app's serif rather than Verovio's header (0018 section 2). */
function titleElement(title: string, lines: string[]): HTMLElement {
    const block = document.createElement("div");
    block.className = "title-block";
    block.setAttribute("aria-hidden", "true");
    if (title !== "") {
        const name = document.createElement("p");
        name.className = "name";
        name.textContent = title;
        block.append(name);
    }
    if (lines.length > 0) {
        const more = document.createElement("p");
        more.className = "more";
        more.textContent = lines.join(" · ");
        block.append(more);
    }
    return block;
}

/**
 * A page's text drawn by the browser at print resolution, as coverage for the PDF's ink layer: the
 * browser knows the fonts (the title in any script, Verovio's music font for tempo marks), and the
 * layer covers only the part of the page that has text.
 */
async function textInk(svg: string): Promise<InkLayer | null> {
    const width = Math.round((210 / 25.4) * PDF_TEXT_DPI);
    const height = Math.round((297 / 25.4) * PDF_TEXT_DPI);
    const url = URL.createObjectURL(new Blob([textOnlySvg(svg, width, height)], { type: "image/svg+xml" }));
    const canvas = document.createElement("canvas");
    try {
        const image = new Image();
        image.src = url;
        await image.decode();
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d", { willReadFrequently: true });
        if (context === null) {
            return null;
        }
        context.drawImage(image, 0, 0, width, height);
        const pixels = context.getImageData(0, 0, width, height).data;
        const coverage = new Uint8Array(width * height);
        for (let i = 0; i < coverage.length; i++) {
            coverage[i] = pixels[i * 4 + 3];
        }
        const box = inkBounds(coverage, width, height);
        if (box === null) {
            return null;
        }
        return {
            width: box.right - box.left,
            height: box.bottom - box.top,
            coverage: crop(coverage, width, box),
            x: (box.left / width) * A4_WIDTH,
            y: A4_HEIGHT - (box.bottom / height) * A4_HEIGHT,
            widthPt: ((box.right - box.left) / width) * A4_WIDTH,
            heightPt: ((box.bottom - box.top) / height) * A4_HEIGHT,
        };
    } finally {
        URL.revokeObjectURL(url);
        // Safari keeps a canvas's memory until its size is zero.
        canvas.width = 0;
        canvas.height = 0;
    }
}

/** Entry point for Blazor. */
export function createScoreView(host: HTMLElement, dotnet: DotNetObject): ScoreView {
    return new ScoreView(host, dotnet);
}
