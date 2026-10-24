// The score view (S1-W7, decision record 0008): engraves MusicXML or generated MEI with Verovio,
// marks the notes being heard, scrolls with playback and moves playback to a clicked note.
// Blazor imports this module through JS interop. Verovio loads on first use.

import { ScoreTimeline, type TimemapEntry } from "./score-timeline.js";

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
    getLog(): string;
}

interface VerovioWrapper {
    VerovioToolkit: new (module: unknown) => VerovioToolkit;
    enableLogToBuffer(value: boolean, module: unknown): void;
}

/** What Blazor gets back from a load. */
export interface ScoreLoadResult {
    pages: number;
    notes: number;
}

const VEROVIO_BASE = new URL("../../lib/verovio/", import.meta.url);

/** Verovio units per pixel are 100 / SCALE; 40 gives staves about 9 px apart. */
const SCALE = 40;

const MIN_PAGE_WIDTH = 900;

const RELAYOUT_DELAY_MS = 200;

/** Only a change of width beyond this lays the score out again. */
const RELAYOUT_THRESHOLD_PX = 24;

const PLAYING_CLASS = "playing";

/** Notes outside the range that plays for the selection, drawn faint as on the piano roll. */
const OUTSIDE_CLASS = "outside";

let toolkit: Promise<VerovioToolkit> | null = null;

function loadToolkit(): Promise<VerovioToolkit> {
    toolkit ??= (async () => {
        const [factory, wrapper] = await Promise.all([
            import(new URL("verovio-module.mjs", VEROVIO_BASE).href) as Promise<{ default: () => Promise<unknown> }>,
            import(new URL("verovio.mjs", VEROVIO_BASE).href) as Promise<VerovioWrapper>,
        ]);
        const module = await factory.default();
        wrapper.enableLogToBuffer(true, module);
        return new wrapper.VerovioToolkit(module);
    })();
    // A failed download can be retried on the next load.
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
    private laidOutWidth = 0;
    private frame = 0;
    private relayoutTimer = 0;
    private disposed = false;

    constructor(host: HTMLElement, dotnet: DotNetObject) {
        this.host = host;
        this.dotnet = dotnet;
        this.host.addEventListener("click", this.onClick);
        this.resizeObserver = new ResizeObserver(() => this.scheduleRelayout());
        this.resizeObserver.observe(host);
    }

    /** Engraves a MusicXML or MEI document, replacing what was shown. Throws if Verovio cannot read it. */
    async load(data: string): Promise<ScoreLoadResult> {
        const engraver = await loadToolkit();
        if (this.disposed) {
            return { pages: 0, notes: 0 };
        }

        engraver.setOptions(this.layoutOptions());
        if (!engraver.loadData(data)) {
            throw new Error(`The score could not be engraved. ${engraver.getLog()}`.trim());
        }

        this.engraver = engraver;
        this.timeline = ScoreTimeline.fromTimemap(engraver.renderToTimemap());
        this.render();
        return { pages: engraver.getPageCount(), notes: this.timeline.spans.length };
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

    dispose(): void {
        this.disposed = true;
        cancelAnimationFrame(this.frame);
        clearTimeout(this.relayoutTimer);
        this.resizeObserver.disconnect();
        this.host.removeEventListener("click", this.onClick);
        this.host.replaceChildren();
    }

    private layoutOptions(): Record<string, unknown> {
        this.laidOutWidth = this.host.clientWidth;
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
            svgViewBox: true,
        };
    }

    private render(): void {
        const engraver = this.engraver;
        if (engraver === null) {
            return;
        }

        const pages: Element[] = [];
        for (let page = 1; page <= engraver.getPageCount(); page++) {
            const container = document.createElement("div");
            container.className = "score-page";
            container.innerHTML = engraver.renderToSVG(page);
            pages.push(container);
        }
        this.host.replaceChildren(...pages);

        this.applyRange(false);

        // The new SVG has no marks; put back the ones that were showing.
        const marked = [...this.marked];
        this.marked = new Set();
        this.markedKey = "";
        this.mark(marked, false);
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

    private scheduleRelayout(): void {
        if (this.engraver === null || Math.abs(this.host.clientWidth - this.laidOutWidth) < RELAYOUT_THRESHOLD_PX) {
            return;
        }
        clearTimeout(this.relayoutTimer);
        this.relayoutTimer = window.setTimeout(() => {
            if (this.engraver !== null && !this.disposed) {
                this.engraver.setOptions(this.layoutOptions());
                this.engraver.redoLayout();
                this.render();
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

        if (scroll && ids.length > 0) {
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
        if (box.top < view.top || box.bottom > view.bottom) {
            this.host.scrollTo({ top: this.host.scrollTop + box.top - view.top - 8, behavior: "smooth" });
        }
    }

    private element(id: string): Element | null {
        return this.host.querySelector(`#${CSS.escape(id)}`);
    }

    private readonly onClick = (event: MouseEvent): void => {
        const timeline = this.timeline;
        if (timeline === null || !(event.target instanceof Element)) {
            return;
        }
        // A click on a chord's stem finds the chord; its notes start where it starts.
        const hit = event.target.closest("g.note, g.chord");
        const note = hit?.matches("g.chord") ? hit.querySelector("g.note") : hit;
        const onset = note?.id ? timeline.onsetOf(note.id) : undefined;
        if (onset !== undefined) {
            void this.dotnet.invokeMethodAsync("OnScoreNoteClicked", onset);
        }
    };
}

/** Entry point for Blazor. */
export function createScoreView(host: HTMLElement, dotnet: DotNetObject): ScoreView {
    return new ScoreView(host, dotnet);
}
