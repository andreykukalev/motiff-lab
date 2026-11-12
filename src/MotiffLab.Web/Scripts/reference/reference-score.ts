// The Reference's notation (version 0.5, «Справочник»): a chord, a scale or a progression of the harmony explorer that
// music-core wrote as MEI, engraved on its own. While it plays, the notes sounding light up and, for a progression, a
// playhead stands at them. The music may sound on a piece's timeline from a tick and at its own tempo: a played tick
// maps back to the written one by that start and the two tempos. Blazor imports this module through JS interop.

import { ScoreTimeline } from "../score/score-timeline.js";
import { newToolkit, type VerovioToolkit } from "../score/verovio.js";

/** The part of the playback player the notation reads every frame. */
interface PlayerHandle {
    currentTick(): number;
    state(): string;
}

/** Where the music sounds: from this tick, at the music's tempo over the timeline's (microseconds a quarter). */
export interface Placement {
    at: number;
    musicTempo: number;
    timelineTempo: number;
}

/** Verovio's scale: a chord or a scale large, as the mockup shows it; a progression of up to eight bars smaller. */
const SCALE = 64;
const PROGRESSION_SCALE = 48;

const MIN_PAGE_WIDTH = 900;

const PLAYING_CLASS = "playing";

/** Verovio's options for notation this many pixels wide; stretched to the width when it is a progression. */
export function referenceLayout(width: number, stretch: boolean): Record<string, unknown> {
    const scale = stretch ? PROGRESSION_SCALE : SCALE;
    return {
        font: "Leland",
        scale,
        pageWidth: Math.max(MIN_PAGE_WIDTH, Math.round((width * 100) / scale)),
        pageHeight: 60000,
        adjustPageHeight: true,
        adjustPageWidth: !stretch,
        pageMarginLeft: 20,
        pageMarginRight: 20,
        pageMarginTop: 40,
        pageMarginBottom: 20,
        breaks: "auto",
        minLastJustification: stretch ? 0 : 0.8,
        header: "none",
        footer: "none",
        svgViewBox: true,
    };
}

/** The written tick a played tick stands for, or -1 before the music starts. */
export function writtenTick(played: number, placement: Placement): number {
    const since = played - placement.at;
    return since < 0 ? -1 : (since * placement.timelineTempo) / placement.musicTempo;
}

export class ReferenceScore {
    private readonly host: HTMLElement;
    private readonly stretch: boolean;
    private readonly resizeObserver: ResizeObserver;
    private engraver: VerovioToolkit | null = null;
    private timeline: ScoreTimeline | null = null;
    private placement: Placement | null = null;
    private player: PlayerHandle | null = null;
    private marked: Element[] = [];
    private playhead: HTMLElement | null = null;
    private width = 0;
    private frame = 0;
    private disposed = false;

    constructor(host: HTMLElement, stretch: boolean) {
        this.host = host;
        this.stretch = stretch;
        this.resizeObserver = new ResizeObserver(() => this.relayout());
        this.resizeObserver.observe(host);
    }

    /** Engraves the music; returns how many notes it engraved. */
    async load(mei: string): Promise<number> {
        this.engraver ??= await newToolkit();
        if (this.disposed) {
            return 0;
        }
        this.width = this.host.clientWidth;
        this.engraver.setOptions(referenceLayout(this.width, this.stretch));
        if (!this.engraver.loadData(mei)) {
            throw new Error(`The music could not be engraved. ${this.engraver.getLog()}`.trim());
        }
        this.timeline = ScoreTimeline.fromTimemap(this.engraver.renderToTimemap({ includeRests: false }));
        this.render();
        return this.timeline.spans.length;
    }

    /** Lights the notes sounding while the player plays the music from where it was placed; null stops following. */
    follow(player: PlayerHandle | null, placement: Placement | null): void {
        this.player = player;
        this.placement = placement;
        cancelAnimationFrame(this.frame);
        if (player !== null && placement !== null) {
            this.frame = requestAnimationFrame(this.tick);
        } else {
            this.mark([]);
        }
    }

    dispose(): void {
        this.disposed = true;
        cancelAnimationFrame(this.frame);
        this.resizeObserver.disconnect();
        this.host.replaceChildren();
        this.engraver?.destroy?.();
        this.engraver = null;
    }

    private render(): void {
        if (this.engraver === null) {
            return;
        }
        const page = document.createElement("div");
        page.className = "ref-score-page";
        page.innerHTML = this.engraver.renderToSVG(1);
        // Verovio gives the size only as the view box, in pixels at its scale: the music keeps that size unless stretched.
        const svg = page.querySelector("svg");
        if (svg !== null) {
            svg.setAttribute("width", this.stretch ? "100%" : String(svg.viewBox.baseVal.width));
        }
        this.playhead = document.createElement("div");
        this.playhead.className = "ref-playhead";
        this.playhead.hidden = true;
        this.playhead.setAttribute("aria-hidden", "true");
        this.host.replaceChildren(page, this.playhead);
        this.marked = [];
    }

    private relayout(): void {
        if (this.engraver === null || this.disposed || Math.abs(this.host.clientWidth - this.width) < 24) {
            return;
        }
        this.width = this.host.clientWidth;
        this.engraver.setOptions(referenceLayout(this.width, this.stretch));
        this.engraver.redoLayout();
        this.render();
    }

    private readonly tick = (): void => {
        const { player, placement, timeline } = this;
        if (player === null || placement === null || this.disposed) {
            return;
        }
        const written = player.state() === "stopped" ? -1 : writtenTick(player.currentTick(), placement);
        this.mark(written < 0 || timeline === null ? [] : timeline.soundingAt(written));
        this.frame = requestAnimationFrame(this.tick);
    };

    private mark(ids: string[]): void {
        const next = ids.map((id) => this.host.querySelector(`#${CSS.escape(id)}`)).filter((element): element is Element => element !== null);
        if (next.length === this.marked.length && next.every((element, index) => element === this.marked[index])) {
            return;
        }
        this.marked.forEach((element) => element.classList.remove(PLAYING_CLASS));
        next.forEach((element) => element.classList.add(PLAYING_CLASS));
        this.marked = next;
        this.placePlayhead();
    }

    /** A progression's playhead stands at the notes sounding; none when nothing sounds. */
    private placePlayhead(): void {
        const playhead = this.playhead;
        if (playhead === null || !this.stretch) {
            return;
        }
        // The latest note to start is the rightmost of those sounding: a held bass starts earlier, further left.
        const boxes = this.marked.map((element) => element.getBoundingClientRect());
        playhead.hidden = boxes.length === 0;
        if (boxes.length === 0) {
            return;
        }
        const note = boxes.reduce((right, box) => (box.left > right.left ? box : right));
        playhead.style.left = `${note.left + note.width / 2 - this.host.getBoundingClientRect().left}px`;
    }
}

/** Entry point for Blazor. */
export function createReferenceScore(host: HTMLElement, stretch: boolean): ReferenceScore {
    return new ReferenceScore(host, stretch);
}
