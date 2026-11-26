// The roll's viewport (W3.12): the player's zoom (the time as pixels per quarter, the rows as a share of their height at
// 100 %) and the view (the left edge in ticks, the highest row shown), where a tick or a pitch is on the roll and back,
// how far the view and the zoom may go, the glide of Fit and the easing of following the playhead, and the passage
// Expand binds the roll to (#521 19). It owns that state: the roll's input and its painter read it and move it through
// these methods. The rules themselves are roll-view.ts's and roll-zoom.ts's; the DOM is the roll's (ViewportHost).

import { MAX_ROW, MIN_ROW, ROW_AT_100, type RollModel, rowFor, rowWindow } from "./roll-model.js";
import { measures } from "./roll-palette.js";
import type { Span } from "./roll-playhead.js";
import { barShareInView, barsInView, octavesInView, rowForOctaves, stepBars, stepOctaves, type AxisZoom } from "./roll-zoom.js";
import { chase, fitFrame, fitZoom, followPlayhead, glideFrame, paddedPitches, scrollLimits, type ViewFrame } from "./roll-view.js";

export const TICKS_PER_QUARTER = 480;
/** The keys and the octave band down the left, in pixels: the time starts right of it. */
export const GUTTER = 62;
/** The bar numbers over the notes: tall enough to press and drag on for a bar selection (WCAG 2.2 target size, 2.5.8). */
export const BAR_ROW = 24;
/** Horizontal zoom, in pixels per quarter note. */
export const DEFAULT_ZOOM = 48;
const MIN_ZOOM = 6;
export const MAX_ZOOM = 480;
/** How near the least zoom counts as at it, so a zoom laid there by Fit offers no further step out. */
const ZOOM_SLACK = 1e-6;
/** How near one bar in view counts as one, so a step of the time zoom that laid one bar there offers no further step in. */
const SHARE_SLACK = 1e-6;
/**
 * In touch reading Fit may make the rows this thin, so a selection's two hands fit the drawer (four octaves in the iPad
 * Pro 11" drawer, whose roll page shows no keyboard).
 */
export const READING_MIN_ROW = 5;
/** After the player moves or zooms the roll by hand, playback leaves the view there this long (ms). */
const HAND_PAUSE_MS = 4000;
/** A note or a bar brought into view by the keyboard or a pointer pages on when it passes this share of the width. */
const FOLLOW_RIGHT = 0.85;
const FOLLOW_LEFT = 0.15;

/** The rows in view: the lowest and highest pitch shown, a row's height, and whether rows go on above or below. */
export interface PitchRange {
    low: number;
    high: number;
    row: number;
    above: boolean;
    below: boolean;
}

/** What the viewport reads from the roll it belongs to. */
export interface ViewportHost {
    /** The roll's width, in pixels. */
    width(): number;
    /** The notes area's height, in pixels. */
    height(): number;
    /** Where the notes start: under the bar numbers, and under the loop strip with the lanes. */
    header(): number;
    /** The notes shown; null before a piece. */
    model(): RollModel | null;
    /** Whether the roll is read by touch (#338), whose rows may be thinner. */
    touchReading(): boolean;
    /** The playhead's tick, which a zoom keeps in place. */
    playhead(): number;
    /** The zoom changed: what writes it (the floating zoom's percentage) writes it again. */
    zoomed(): void;
    /** What the roll shows changed: it draws again. */
    changed(): void;
}

/** Whether the player asked for less motion: glides and following then go at once. */
function reducedMotion(): boolean {
    return typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export class RollViewport {
    /** The player's zoom (#511 section 5): only the zoom controls, a pinch, the wheel and Fit change it. */
    private zoomNow = DEFAULT_ZOOM;
    /** The rows' height as a share of 100 % (ROW_AT_100), zoomed with the time: one zoom for both (testing item 13). */
    private rowScaleNow = 1;
    /** The view's left edge, in ticks. */
    private scrollNow = 0;
    /** The highest row shown when the rows do not all fit; null shows the top of the notes. */
    private top: number | null = null;
    /** A glide of the zoom and the view to where Fit or a new selection takes them: from, to, and when it began (ms). */
    private glide: { from: ViewFrame; to: ViewFrame; began: number } | null = null;
    /** Where following the playhead is taking the view, eased to frame by frame; a reveal holds until it is there. */
    private following: { kind: "glue" | "reveal"; scroll: number } | null = null;
    /** Until when, after a move or zoom by hand, playback does not move the view. */
    private handUntil = 0;
    /**
     * The passage Expand is bound to (#521 19), or null: while it is, the view moves only within it and its margin, not at
     * all when it fits, and the zoom never shows more than it.
     */
    private bound: Span | null = null;
    /** The zoom and the view before the passage was bound, given back when it goes, so Collapse returns to them (#521 17). */
    private beforePassage: ViewFrame | null = null;
    /** Whether the passage is shown as fitted, with no zoom by hand since: a new size (Expand's card, a turn) fits it again. */
    private passageFitted = false;

    constructor(private readonly host: ViewportHost) {}

    get zoom(): number {
        return this.zoomNow;
    }

    get rowScale(): number {
        return this.rowScaleNow;
    }

    get scroll(): number {
        return this.scrollNow;
    }

    /** The highest row asked for, before the rows' window resolves it (pitchRange); null for the top of the notes. */
    get pitchTop(): number | null {
        return this.top;
    }

    /** The passage Expand binds the roll to, or null. */
    get passage(): Span | null {
        return this.bound;
    }

    /** Whether the bound passage still lies as fitted, so a new size fits it again. */
    get fitted(): boolean {
        return this.passageFitted;
    }

    /** The width the time has, right of the gutter. */
    timeWidth(): number {
        return Math.max(1, this.host.width() - GUTTER);
    }

    /** Where a tick is across the roll. */
    xOf(tick: number): number {
        return GUTTER + ((tick - this.scrollNow) * this.zoomNow) / TICKS_PER_QUARTER;
    }

    /** The tick at a point across the roll, never before the piece's start. */
    tickAtX(x: number): number {
        return Math.max(0, this.scrollNow + ((x - GUTTER) * TICKS_PER_QUARTER) / this.zoomNow);
    }

    /** Where a row's top is down the roll. */
    yOf(pitch: number, range: PitchRange = this.pitchRange()): number {
        return this.host.header() + (range.high - pitch) * range.row;
    }

    /** The row at a point down the roll, by its pitch. */
    pitchAt(y: number): number {
        const { high, row } = this.pitchRange();
        return high - Math.floor((y - this.host.header()) / row);
    }

    /** The pitch at a point down the roll as a real number, so a row's centre is its pitch. */
    exactPitchAt(y: number): number {
        const { high, row } = this.pitchRange();
        return high + 0.5 - (y - this.host.header()) / row;
    }

    /** How many ticks the view shows. */
    visibleTicks(): number {
        return (this.timeWidth() * TICKS_PER_QUARTER) / this.zoomNow;
    }

    /** How far the view's left edge goes: the whole piece, or only within the passage of Expand (roll-view.ts scrollLimits). */
    scrollLimits(): { min: number; max: number } {
        return scrollLimits({
            end: this.host.model()?.end ?? 0,
            visible: this.visibleTicks(),
            passage: this.bound,
            margin: (measures.fitMargin * TICKS_PER_QUARTER) / this.zoomNow,
        });
    }

    maxScroll(): number {
        return this.scrollLimits().max;
    }

    /** A left edge within the scroll's limits, in whole ticks. */
    private clampScroll(tick: number): number {
        const { min, max } = this.scrollLimits();
        return Math.round(Math.min(max, Math.max(min, tick)));
    }

    /** The least zoom: the kit's, or bound to a passage the one that lays it across the roll, so no more than it shows (#521 19.1). */
    minZoom(): number {
        return this.bound === null ? MIN_ZOOM : fitZoom(this.bound, this.timeWidth(), measures.fitMargin, [MIN_ZOOM, MAX_ZOOM]);
    }

    /** Whether the zoom is at its least, or at its greatest. */
    atLeastZoom(): boolean {
        return this.zoomNow <= this.minZoom() + ZOOM_SLACK;
    }

    atGreatestZoom(): boolean {
        return this.zoomNow >= MAX_ZOOM;
    }

    /** Moves the view to a left edge; a glide or a follow under way gives way to it. */
    scrollTo(tick: number): void {
        this.glide = null;
        this.following = null;
        this.scrollNow = this.clampScroll(tick);
        this.host.changed();
    }

    /** Brings a tick into view when it is left of the view or past FOLLOW_RIGHT of it, at FOLLOW_LEFT of the width. */
    keepInView(tick: number): void {
        const visible = this.visibleTicks();
        const offset = tick - this.scrollNow;
        if (offset < 0 || offset > visible * FOLLOW_RIGHT) {
            this.scrollTo(tick - visible * FOLLOW_LEFT);
        }
    }

    /**
     * Rows from one below the piece's lowest note to one above its highest, at least an octave: all of them where they fit,
     * else a window of the zoom's rows from the highest row shown (0018 section 4; #511 section 5.1).
     */
    pitchRange(): PitchRange {
        const model = this.host.model();
        // The rows reach the whole piece, so moving on through the music finds every note and a new selection never moves
        // them (#511 section 5.1); Fit puts the selection's rows in view.
        const { low, high } = paddedPitches(model?.lowest ?? 60, model?.highest ?? 72);
        // Rows of the zoom's height (testing item 11): as many as fit, widened round the notes when they take fewer, else a
        // window from the top of the notes (where the tune usually is) until scrolled.
        const row = this.rowHeight();
        const visible = Math.max(1, Math.floor(this.host.height() / row));
        return { ...rowWindow(low, high, visible, this.top), row };
    }

    /** The height of a row: the zoom's, down to thinner rows in touch reading. */
    rowHeight(): number {
        return this.host.touchReading() ? Math.min(MAX_ROW, Math.max(READING_MIN_ROW, Math.round(ROW_AT_100 * this.rowScaleNow))) : rowFor(this.rowScaleNow);
    }

    /** Puts the highest row shown at a pitch, as a move of the rows by hand does; null shows the top of the notes. */
    setPitchTop(pitch: number | null): void {
        this.top = pitch;
    }

    /** Puts the view back where a pinch began, as it was then, before the pinch's zoom and move now are laid on it. */
    restoreView(scroll: number, pitchTop: number): void {
        this.scrollNow = scroll;
        this.top = pitchTop;
    }

    /** Scrolls the rows by whole rows, up for a positive count, from a top row (the one shown), when they do not all fit. */
    scrollPitch(rows: number, from?: number): void {
        const range = this.pitchRange();
        if (!range.above && !range.below) {
            return;
        }
        this.top = (from ?? range.high) + rows;
        this.host.changed();
    }

    /** Sets the rows' height, keeping the pitch at a height on the roll where it is. */
    rowsAround(rowScale: number, y: number): void {
        const header = this.host.header();
        const before = this.pitchRange();
        const pitch = before.high - (y - header) / before.row;
        this.rowScaleNow = Math.min(MAX_ROW / ROW_AT_100, Math.max(READING_MIN_ROW / ROW_AT_100, rowScale));
        this.top = Math.round(pitch + (y - header) / this.rowHeight());
        this.host.changed();
    }

    /** Sets the zoom of the time and of the rows, keeping the playhead (or the anchor given) where it is. */
    zoomTo(zoom: number, rowScale: number, anchorX?: number): void {
        const width = this.timeWidth();
        const anchor = anchorX ?? Math.min(Math.max(this.xOf(this.host.playhead()) - GUTTER, 0), width);
        const anchorTick = this.scrollNow + (anchor * TICKS_PER_QUARTER) / this.zoomNow;
        this.zoomNow = Math.min(MAX_ZOOM, Math.max(this.minZoom(), zoom));
        this.rowScaleNow = Math.min(MAX_ROW / ROW_AT_100, Math.max((this.host.touchReading() ? READING_MIN_ROW : MIN_ROW) / ROW_AT_100, rowScale));
        this.scrollTo(anchorTick - (anchor * TICKS_PER_QUARTER) / this.zoomNow);
        this.host.zoomed();
    }

    /**
     * Zooms in (factor above 1) or out, the time and the rows together (testing item 13), keeping the playhead, or else the
     * left edge, where it is; read by touch, the time only, round the middle of the view unless an anchor is given.
     */
    zoomBy(factor: number, anchorX?: number): void {
        this.byHand();
        if (this.host.touchReading()) {
            this.zoomTo(this.zoomNow * factor, this.rowScaleNow, anchorX ?? this.timeWidth() / 2);
            return;
        }
        this.zoomTo(this.zoomNow * factor, this.rowScaleNow * factor, anchorX);
    }

    /** A step of the time zoom: + (1) shows fewer bars, − (−1) more, round the middle of the view; the rows stay. */
    zoomTime(model: RollModel, direction: 1 | -1): void {
        const share = barShareInView(model.bars, this.scrollNow, this.scrollNow + this.visibleTicks());
        if (share <= 0) {
            return;
        }
        this.byHand();
        this.zoomTo((this.zoomNow * share) / stepBars(share, direction), this.rowScaleNow, this.timeWidth() / 2);
    }

    /** A step of the pitch zoom: + (1) one octave fewer in view, − (−1) one more, round the middle row; the time stays. */
    zoomPitch(direction: 1 | -1): void {
        const height = this.host.height();
        const octaves = octavesInView(Math.floor(height / this.rowHeight()));
        this.byHand();
        this.rowsAround(rowForOctaves(stepOctaves(octaves, direction), height, READING_MIN_ROW, MAX_ROW) / ROW_AT_100, this.host.header() + height / 2);
    }

    /**
     * The bars and the octaves in view and which steps are left (the zoom on each axis); Zoom Out as the roll shows it. A
     * step of the time zoom is left only where zoomTime would take it: + while more than one bar is in view (it never shows
     * fewer than one), and neither without a bar in view.
     */
    axisZoom(model: RollModel, zoomOut: boolean): AxisZoom {
        const row = this.rowHeight();
        const octaves = octavesInView(Math.floor(this.host.height() / row));
        const share = barShareInView(model.bars, this.scrollNow, this.scrollNow + this.visibleTicks());
        return {
            bars: barsInView(model.bars, this.scrollNow, this.scrollNow + this.visibleTicks()),
            octaves,
            timeIn: !this.atGreatestZoom() && share > 1 + SHARE_SLACK,
            timeOut: !this.atLeastZoom() && share > 0,
            pitchIn: octaves > 1 && row < MAX_ROW,
            pitchOut: row > READING_MIN_ROW,
            zoomOut: zoomOut && this.bound === null,
        };
    }

    /** The player's zoom and the view as they are now, the highest row shown resolved. */
    currentFrame(): ViewFrame {
        return { zoom: this.zoomNow, rowScale: this.rowScaleNow, scroll: this.scrollNow, pitchTop: this.top ?? this.pitchRange().high };
    }

    /** Sets the zoom and the view to a frame, within the zoom's limits and the scroll's. */
    setFrame(frame: ViewFrame): void {
        this.zoomNow = Math.min(MAX_ZOOM, Math.max(this.minZoom(), frame.zoom));
        this.rowScaleNow = frame.rowScale;
        this.scrollNow = this.clampScroll(frame.scroll);
        this.top = Math.round(frame.pitchTop);
        this.host.zoomed();
        this.host.changed();
    }

    /**
     * A piece shown: the zoom given (the one kept from the roll before, as it was), or the player's as it is, and the view
     * given within the new notes' limits; a glide or a follow under way stops.
     */
    show(view: { scroll: number; pitchTop: number | null }, zoom: { zoom: number; rowScale: number } | null): void {
        if (zoom !== null) {
            this.zoomNow = zoom.zoom;
            this.rowScaleNow = zoom.rowScale;
        }
        this.glide = null;
        this.following = null;
        this.top = view.pitchTop;
        this.scrollNow = this.clampScroll(view.scroll);
    }

    /** The frame that lays a stretch across the roll as it is now, with its notes' rows in its height (fitFrame). */
    private stretchFrame(model: RollModel, span: Span): ViewFrame {
        let lowest = Number.POSITIVE_INFINITY;
        let highest = Number.NEGATIVE_INFINITY;
        for (const index of model.inRange(Math.max(0, span.start), Math.min(model.end, span.end))) {
            const pitch = model.notes[index].pitch;
            lowest = Math.min(lowest, pitch);
            highest = Math.max(highest, pitch);
        }
        return fitFrame({
            start: span.start,
            end: span.end,
            pitches: Number.isFinite(lowest) ? { lowest, highest } : null,
            width: this.timeWidth(),
            height: this.host.height(),
            margin: measures.fitMargin,
            zoomLimits: [MIN_ZOOM, MAX_ZOOM],
            rowLimits: [this.host.touchReading() ? READING_MIN_ROW : MIN_ROW, MAX_ROW],
            rowAt100: ROW_AT_100,
            before: this.currentFrame(),
        });
    }

    /** Glides to a stretch laid across the roll with its notes' rows in its height (Fit); playback may move the view at once. */
    glideToStretch(model: RollModel, span: Span): void {
        const from = this.currentFrame();
        const to = this.stretchFrame(model, span);
        this.handUntil = 0;
        this.glideTo(from, to);
    }

    /** Glides the zoom and the view from one frame to another in the kit's slow time, at once with less motion. */
    glideTo(from: ViewFrame, to: ViewFrame): void {
        this.following = null;
        if (reducedMotion() || measures.glideMs <= 0) {
            this.glide = null;
            this.setFrame(to);
            return;
        }
        this.glide = { from, to, began: performance.now() };
        this.host.changed();
    }

    /** One frame of a glide; true while it goes on. */
    stepGlide(now: number): boolean {
        const glide = this.glide;
        if (glide === null) {
            return false;
        }
        const share = Math.min(1, (now - glide.began) / measures.glideMs);
        this.setFrame(glideFrame(glide.from, glide.to, measures.ease(share)));
        if (share >= 1) {
            this.glide = null;
        }
        return true;
    }

    /**
     * Where the playhead takes the view (#511 section 6.1, roll-view.ts followPlayhead): along with it while playing, no
     * further than `end`, and back to it once after a jump out of view; not during a glide, for a while after the player
     * moved the view by hand, or before the roll has its width. A reveal under way holds until another jump.
     */
    follow(tick: number, jumped: boolean, playing: boolean, end: number): void {
        if (this.glide !== null || performance.now() < this.handUntil || this.host.width() === 0 || (this.following?.kind === "reveal" && !jumped)) {
            return;
        }
        const { min, max } = this.scrollLimits();
        this.following = followPlayhead({ scroll: this.scrollNow, visible: this.visibleTicks(), tick, playing, jumped, end, maxScroll: max, minScroll: min }) ?? this.following;
    }

    /** One frame of the view easing to where following takes it, in the kit's base time (at once with less motion). */
    stepFollow(elapsed: number): void {
        const following = this.following;
        if (following === null) {
            return;
        }
        const tau = reducedMotion() ? 0 : measures.followMs / 3;
        const next = chase(this.scrollNow, following.scroll, elapsed, tau);
        // Within half a pixel it is there.
        const there = Math.abs(next - following.scroll) * this.zoomNow < TICKS_PER_QUARTER / 2;
        this.scrollNow = there ? following.scroll : next;
        if (there) {
            this.following = null;
        }
        this.host.changed();
    }

    /** Following stops where it is; it starts again with the playhead's next move. */
    stopFollowing(): void {
        this.following = null;
    }

    /** Playback may move the view at once again, the pause after a move by hand over. */
    endHandPause(): void {
        this.handUntil = 0;
    }

    /** The player moved or zoomed the view by hand: a glide or a follow stops, and playback leaves the view for a while. */
    byHand(): void {
        this.passageFitted = false;
        this.glide = null;
        this.following = null;
        this.handUntil = performance.now() + HAND_PAUSE_MS;
    }

    /**
     * Binds the view to a passage (#521 19), keeping the frame before the first one, or lets it go (null), giving back the
     * frame before. Nothing is laid across the roll here: Fit does that (fitPassage). False when there was nothing to let go.
     */
    bind(span: Span | null): boolean {
        if (span === null) {
            if (this.bound === null) {
                return false;
            }
            this.bound = null;
            this.passageFitted = false;
            const before = this.beforePassage;
            this.beforePassage = null;
            this.glide = null;
            this.following = null;
            if (before !== null) {
                this.setFrame(before);
            }
            return true;
        }
        if (this.bound === null) {
            this.beforePassage = this.currentFrame();
        }
        this.bound = { start: span.start, end: Math.max(span.end, span.start + 1) };
        return true;
    }

    /** Lays the passage of Expand across the roll at once, its notes' rows in its height: what the passage shows by itself. */
    fitPassage(model: RollModel, passage: Span): void {
        const to = this.stretchFrame(model, passage);
        this.handUntil = 0;
        this.glide = null;
        this.following = null;
        this.passageFitted = true;
        this.setFrame(to);
    }
}
