// The iPad's score page (decision record 0031, step 11; docs/code-map/score-page.md): what more than one of its modules reads,
// and the one way it speaks to the app. Each field is written by the command that owns it: the pages by `show` and
// `layout` (page-layout.ts), the pen, the mode and the input rules by page-taps.ts, focus by page-card.ts, the playback
// model by page-playheads.ts, the beats by page-selection.ts and the highlights' bands by page-marks.ts;
// the others read them.
import type { InteractionMode } from "../score-modes.js";
import type { BarFrame } from "../pencil-marks.js";
import type { ScoreGeometry } from "../score-geometry.js";
import type { ScoreTimeline } from "../score-timeline.js";
import type { PaintedBand } from "../colour-bars.js";
import type { InputRules, PageMessage, PagePlayheads, PagePoint, PointerRules } from "../page-contract.js";

/** The reading line, this far below the top of the view: the pages it crosses are the ones the bar names, as on the web. */
export const READING_LINE_PX = 72;

export interface PageState {
    /** The engraved pages as SVG. */
    svgs: string[];
    timeline: ScoreTimeline | null;
    /** Where every bar sits now, in the document's pixels. */
    geometry: ScoreGeometry | null;
    frames: BarFrame[];
    byTick: Map<number, BarFrame>;
    /** The highlights' bands as drawn, where the eraser takes the colour. */
    painted: PaintedBand[];
    /** The bar's Pen on: before Swift's input rules, a finger writes. */
    pen: boolean;
    /** Phrase focus (Expand): the page under the veil. */
    focus: boolean;
    /** The piece's beat lines while selecting by beats; null for none. */
    beats: number[] | null;
    mode: InteractionMode | null;
    /** The playback model under the modes; null draws the playhead of before the modes. */
    heads: PagePlayheads | null;
    input: InputRules | null;
}

export const state: PageState = { svgs: [], timeline: null, geometry: null, frames: [], byTick: new Map(), painted: [], pen: false, focus: false, beats: null, mode: null, heads: null, input: null };

/** Posts a message to the app (`webkit.messageHandlers.score`); nothing outside the app. */
export function post(message: PageMessage): void {
    window.webkit?.messageHandlers?.score?.postMessage(message);
}

/** The rules Swift gave this pointer type ("touch" or "pen"); none for another, or before Swift's first `input`. */
export function pointerRules(pointerType: string): PointerRules | undefined {
    return pointerType === "touch" || pointerType === "pen" ? state.input?.[pointerType] : undefined;
}

/** A pointer's place in the document's pixels, as the geometry places the bars. */
export function documentPoint(event: MouseEvent): PagePoint {
    const view = document.body.getBoundingClientRect();
    return { x: event.clientX - view.left + document.body.scrollLeft, y: event.clientY - view.top + document.body.scrollTop };
}
