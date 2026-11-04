// The phrase card in focus (decision record 0018 section 4, design/kit/ds.css): the focused phrase engraved on
// its own across the card's width, from the excerpt music-core writes with any ossia above the original bar for
// bar (0021). The ossia gets the kit's dashed plum frame and its name as an overline; the notes being heard
// light up, those of the ossia only while the variation sounds; a note clicked moves playback to it. A long
// phrase wraps to more systems inside the card. Verovio's own label at the ossia's left is left out: the
// overline names it. Blazor imports this module through JS interop.

import { readPage, staffFrames, type PageGeometry, type Rect } from "./score-geometry.js";
import { ScoreTimeline } from "./score-timeline.js";
import { newToolkit, type VerovioToolkit } from "./verovio.js";

interface DotNetObject {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** The part of the playback player the card reads every frame. */
interface PlayerHandle {
    currentTick(): number;
    state(): string;
}

/** An ossia above the original, as Blazor sends it: its staves in the document, its notes' id prefix and its name. */
export interface CardOssia {
    staves: number[];
    prefix: string;
    label: string;
}

/** What sounds now, so only its staves light up: the original, the variation (the ossia), or both. */
export type Sounding = "original" | "variation" | "both";

/** Verovio's scale on the card: about eight bars of a phrase to a line on an iPad in landscape, larger than on the page. */
export const CARD_SCALE = 42;

/** The narrowest layout, in Verovio's units, so a phone in portrait still gets two or three bars to a line. */
const MIN_PAGE_WIDTH = 1100;

/** Only a change of width beyond this lays the card out again. */
const RELAYOUT_THRESHOLD_PX = 24;

const RELAYOUT_DELAY_MS = 150;

/** Room round the ossia's staves inside its frame, in pixels. */
const FRAME_PAD_X = 10;
const FRAME_PAD_Y = 8;

const PLAYING_CLASS = "playing";

/** Verovio's options for a card this many pixels wide: one page as tall as the phrase needs, broken into systems at the width. */
export function cardLayout(width: number): Record<string, unknown> {
    return {
        font: "Leland",
        scale: CARD_SCALE,
        pageWidth: Math.max(MIN_PAGE_WIDTH, Math.round((width * 100) / CARD_SCALE)),
        pageHeight: 60000,
        adjustPageHeight: true,
        pageMarginLeft: 40,
        pageMarginRight: 40,
        pageMarginTop: 50,
        pageMarginBottom: 30,
        spacingSystem: 12,
        breaks: "auto",
        // A phrase of one line still spans the card, and so does its last line.
        minLastJustification: 0,
        header: "none",
        footer: "none",
        justifyVertically: false,
        svgViewBox: true,
    };
}

/** The ids of the notes sounding that belong to what is heard: the ossia's only while the variation sounds. */
export function soundingIds(ids: readonly string[], sounding: Sounding, prefixes: readonly string[]): string[] {
    if (sounding === "both" || prefixes.length === 0) {
        return [...ids];
    }
    const ossia = (id: string): boolean => prefixes.some((prefix) => id.startsWith(prefix));
    return ids.filter((id) => ossia(id) === (sounding === "variation"));
}

export class PhraseCard {
    private readonly host: HTMLElement;
    private readonly dotnet: DotNetObject;
    private readonly resizeObserver: ResizeObserver;
    private engraver: VerovioToolkit | null = null;
    private timeline: ScoreTimeline | null = null;
    /** Where the excerpt starts on the piece's timeline: the card's timemap starts at 0. */
    private offset = 0;
    private ossias: CardOssia[] = [];
    private sounding: Sounding = "original";
    private player: PlayerHandle | null = null;
    private marked = new Set<string>();
    private markedKey = "";
    private pages: string[] = [];
    private geometry: PageGeometry[] = [];
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

    /**
     * Engraves an excerpt (MEI from music-core) that starts at this tick of the piece, with these ossias above it.
     * Returns how many notes it engraved. Throws if Verovio cannot read it.
     */
    async load(mei: string, offset: number, ossias: CardOssia[]): Promise<number> {
        this.engraver ??= await newToolkit();
        if (this.disposed) {
            this.release();
            return 0;
        }
        const engraver = this.engraver;
        engraver.setOptions(this.layoutOptions());
        if (!engraver.loadData(mei)) {
            throw new Error(`The phrase could not be engraved. ${engraver.getLog()}`.trim());
        }
        this.offset = offset;
        this.ossias = ossias;
        this.timeline = ScoreTimeline.fromTimemap(engraver.renderToTimemap({ includeRests: false }));
        this.render();
        return this.timeline.spans.length;
    }

    /** Marks the notes the player is sounding, every animation frame, until called with null. */
    follow(player: PlayerHandle | null): void {
        this.player = player;
        cancelAnimationFrame(this.frame);
        if (player !== null) {
            this.frame = requestAnimationFrame(this.tick);
        } else {
            this.mark([]);
        }
    }

    /** Which staves light up as they sound: the original's, the ossia's, or both. */
    setSounding(sounding: Sounding): void {
        this.sounding = sounding;
        this.markedKey = "";
    }

    dispose(): void {
        this.disposed = true;
        cancelAnimationFrame(this.frame);
        clearTimeout(this.relayoutTimer);
        this.resizeObserver.disconnect();
        this.host.removeEventListener("click", this.onClick);
        this.host.replaceChildren();
        this.release();
    }

    private release(): void {
        this.engraver?.destroy?.();
        this.engraver = null;
    }

    private layoutOptions(): Record<string, unknown> {
        this.laidOutWidth = this.host.clientWidth;
        return cardLayout(this.laidOutWidth);
    }

    private render(): void {
        const engraver = this.engraver;
        if (engraver === null) {
            return;
        }
        this.pages = [];
        const parts: Element[] = [];
        for (let page = 1; page <= engraver.getPageCount(); page++) {
            const svg = engraver.renderToSVG(page);
            this.pages.push(svg);
            const container = document.createElement("div");
            container.className = "card-page";
            container.innerHTML = svg;
            parts.push(container);
        }
        this.geometry = this.pages.map((svg, index) => readPage(svg, index + 1));
        this.host.replaceChildren(...parts);
        this.placeFrames();
        const marked = [...this.marked];
        this.marked = new Set();
        this.markedKey = "";
        this.mark(marked);
    }

    /** The ossia's frame round its staves on each system, and its name over the first. */
    private placeFrames(): void {
        for (const old of this.host.querySelectorAll(":scope > .ossia-frame")) {
            old.remove();
        }
        if (this.ossias.length === 0) {
            return;
        }
        const view = this.host.getBoundingClientRect();
        const svgs = [...this.host.querySelectorAll<SVGSVGElement>(":scope > .card-page > svg")];
        for (const ossia of this.ossias) {
            let first = true;
            svgs.forEach((svg, index) => {
                const page = this.geometry[index];
                if (page === undefined) {
                    return;
                }
                const box = svg.getBoundingClientRect();
                const scale = Math.min(box.width / page.width, box.height / page.height);
                const left = box.left - view.left + this.host.scrollLeft + (box.width - page.width * scale) / 2;
                const top = box.top - view.top + this.host.scrollTop + (box.height - page.height * scale) / 2;
                for (const frame of staffFrames(page, ossia.staves)) {
                    this.host.append(frameElement(frame, left, top, scale, first ? ossia.label : null));
                    first = false;
                }
            });
        }
    }

    private scheduleRelayout(): void {
        if (this.engraver === null || Math.abs(this.host.clientWidth - this.laidOutWidth) < RELAYOUT_THRESHOLD_PX) {
            this.placeFrames();
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
            const ids = player.state() === "stopped" ? [] : this.timeline.soundingAt(player.currentTick() - this.offset);
            this.mark(soundingIds(ids, this.sounding, this.ossias.map((ossia) => ossia.prefix)));
        }
        this.frame = requestAnimationFrame(this.tick);
    };

    private mark(ids: string[]): void {
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
    }

    private element(id: string): Element | null {
        return this.host.querySelector(`#${CSS.escape(id)}`);
    }

    private readonly onClick = (event: MouseEvent): void => {
        const timeline = this.timeline;
        if (timeline === null || !(event.target instanceof Element)) {
            return;
        }
        const hit = event.target.closest("g.note, g.chord");
        const note = hit?.matches("g.chord") ? hit.querySelector("g.note") : hit;
        const onset = note?.id ? timeline.onsetOf(note.id) : undefined;
        if (onset !== undefined) {
            void this.dotnet.invokeMethodAsync("OnCardNoteClicked", onset + this.offset);
        }
    };
}

/** A dashed plum frame round an ossia's staves on one system, in the card's pixels; the first carries the ossia's name. */
function frameElement(frame: Rect, left: number, top: number, scale: number, label: string | null): HTMLElement {
    const element = document.createElement("div");
    element.className = "ossia-frame";
    element.setAttribute("aria-hidden", "true");
    element.style.left = `${left + frame.left * scale - FRAME_PAD_X}px`;
    element.style.top = `${top + frame.top * scale - FRAME_PAD_Y}px`;
    element.style.width = `${(frame.right - frame.left) * scale + 2 * FRAME_PAD_X}px`;
    element.style.height = `${(frame.bottom - frame.top) * scale + 2 * FRAME_PAD_Y}px`;
    if (label !== null) {
        const name = document.createElement("span");
        name.className = "overline";
        name.textContent = label;
        element.append(name);
    }
    return element;
}

/** Entry point for Blazor. */
export function createPhraseCard(host: HTMLElement, dotnet: DotNetObject): PhraseCard {
    return new PhraseCard(host, dotnet);
}
