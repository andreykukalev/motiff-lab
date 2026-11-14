// The strips of the iPad's roll (v0.5.2, #479 item 25, approved mockup; the playback model's two playheads, #495 UX-10):
// the black playhead, the position, as an ink line down the notes with its dot on the whole-piece strip; with a selection
// the blue playhead, the position held to it, as a cobalt line with its handle in the loop strip; under the bar numbers
// the loop strip, the selection's bounds with a handle at each end, tinted while it loops (no loop sign inside it, #521 6:
// it read as a refresh sign and as the playhead's dot), where a tap moves the position; and over everything the whole piece
// in one line, always shown: the position written as bar.beat, a frame round the bars on screen, the selection in cobalt
// and the black dot. On that strip the dot and the frame are two things to drag (#521 1): the dot moves the position, the
// frame moves the view. Pure drawing and arithmetic on what the roll gives; nothing here touches the DOM.
import { drawPositionLine } from "./roll-modes.js";

/** The loop strip's height under the bar numbers, and the whole-piece strip's height over the roll, in pixels. */
export const LOOP_LANE = 28;
export const OVERVIEW_HEIGHT = 30;
/** The loop strip's inset in its lane, its handles' radius, the position's dot; the whole-piece strip's ends. */
const LANE_INSET = 6;
const HANDLE_RADIUS = 6;
const DOT_RADIUS = 6;
const LABEL_WIDTH = 48;
const TRAILING = 40;
const TRACK = 2;
const VIEW_RADIUS = 4;

/** The colours the strips are drawn in, read from the kit's tokens by the roll. */
export interface StripInks {
    ink: string;
    accent: string;
    /** The selection's tint (`--accent-tint`). */
    tint: string;
    surface: string;
    /** The whole-piece track (`--hairline-strong`). */
    line: string;
    muted: string;
}

/** A stretch of time in ticks, [start, end). */
export interface TickSpan {
    start: number;
    end: number;
}

/** Where the loop lane lies across the roll: its top, the notes' left and right, and where a tick lies. */
export interface LaneFrame {
    top: number;
    left: number;
    right: number;
    height: number;
    xOf(tick: number): number;
}

/** Whether a tick lies inside the selection, [start, end). */
export function insideOf(selection: TickSpan | null, tick: number): boolean {
    return selection !== null && tick >= selection.start && tick < selection.end;
}

/**
 * The loop strip: the selection's bounds as a rounded strip outlined in cobalt, tinted while it loops, and a handle at each
 * end; nothing without a selection.
 */
export function drawLoopLane(context: CanvasRenderingContext2D, frame: LaneFrame, selection: TickSpan | null, looping: boolean, inks: StripInks): void {
    if (selection === null) {
        return;
    }
    const start = frame.xOf(selection.start);
    const end = frame.xOf(selection.end);
    const left = Math.max(frame.left, start);
    const right = Math.min(frame.right, end);
    if (right <= left) {
        return;
    }
    const top = frame.top + LANE_INSET;
    const height = LOOP_LANE - 2 * LANE_INSET;
    const middle = frame.top + LOOP_LANE / 2;
    context.beginPath();
    context.roundRect(left, top, right - left, height, height / 2);
    context.fillStyle = looping ? inks.tint : inks.surface;
    context.fill();
    context.strokeStyle = inks.accent;
    context.lineWidth = 1.5;
    context.stroke();
    for (const x of [start, end]) {
        if (x >= frame.left && x <= frame.right) {
            context.beginPath();
            context.arc(x, middle, HANDLE_RADIUS, 0, 2 * Math.PI);
            context.fillStyle = inks.surface;
            context.fill();
            context.lineWidth = 2;
            context.strokeStyle = inks.accent;
            context.stroke();
        }
    }
}

/** Where the two playheads are, from the playback model's view (playback-model.ts, viewOf). */
export interface PositionPlaces {
    black: number;
    blue: number | null;
    playing: boolean;
}

/**
 * The two playheads down the roll: the black one an ink line under the loop strip (its dot is on the whole-piece strip),
 * and with a selection the blue one a cobalt line over it with its handle in the loop strip, hollow when it stands. Where
 * they meet the cobalt line covers the ink one, and both handles show, each in its strip.
 */
export function drawPosition(context: CanvasRenderingContext2D, frame: LaneFrame, places: PositionPlaces, inks: StripInks): void {
    const black = Math.round(frame.xOf(places.black));
    if (black >= frame.left && black <= frame.right) {
        context.fillStyle = inks.ink;
        context.fillRect(black - 1, frame.top + LOOP_LANE, 2, frame.height - frame.top - LOOP_LANE);
    }
    if (places.blue === null) {
        return;
    }
    const lines = { xOf: frame.xOf, header: frame.top, left: frame.left, right: frame.right, height: frame.height };
    const mark = { colour: inks.accent, knobY: frame.top + LOOP_LANE / 2, radius: DOT_RADIUS, solid: places.playing };
    drawPositionLine(context, lines, frame.xOf(places.blue), mark, { ink: inks.ink, accent: inks.accent, surface: inks.surface, paper: inks.surface });
}

/** What the whole-piece strip shows: the position and its label, the piece's end and last bar, the bars on screen, the selection. */
export interface OverviewState {
    tick: number;
    label: string;
    end: number;
    lastBar: string;
    view: TickSpan;
    selection: TickSpan | null;
    playing: boolean;
}

/** The whole-piece track across the strip: after the position's label and before the last bar's number. */
export function overviewTrack(width: number): { left: number; right: number } {
    return { left: LABEL_WIDTH, right: Math.max(LABEL_WIDTH + 1, width - TRAILING) };
}

/** The tick under a point across the whole-piece strip, on the track, as a whole tick. */
export function overviewTick(x: number, width: number, end: number): number {
    const track = overviewTrack(width);
    const share = Math.min(1, Math.max(0, (x - track.left) / (track.right - track.left)));
    return Math.round(share * end);
}

/** Where a tick lies across the whole-piece strip, on its track, clamped to the piece. */
export function overviewX(tick: number, width: number, end: number): number {
    const track = overviewTrack(width);
    return track.left + (track.right - track.left) * (end > 0 ? Math.min(1, Math.max(0, tick / end)) : 0);
}

/**
 * What a press on the whole-piece strip takes (#521 1): the black dot, the position, within `reach` pixels of it; anywhere
 * else the frame round the bars on screen, the view. They are never the same target.
 */
export function overviewGrab(x: number, width: number, tick: number, end: number, reach: number): "position" | "view" {
    return Math.abs(x - overviewX(tick, width, end)) <= reach ? "position" : "view";
}

/**
 * Where the pointer holds the frame on the whole-piece strip, in ticks from the view's left edge (#521 1): where it was
 * pressed inside the frame, so the frame moves with the pointer without a jump; a press outside it takes the frame by its
 * middle, so the frame comes to the pointer.
 */
export function viewGrabOffset(pressTick: number, view: TickSpan): number {
    return pressTick >= view.start && pressTick <= view.end ? pressTick - view.start : (view.end - view.start) / 2;
}

/** The whole-piece strip: label, track, the frame round the bars on screen, the selection in cobalt, the black playhead's dot. */
export function drawOverview(context: CanvasRenderingContext2D, width: number, state: OverviewState, inks: StripInks): void {
    const track = overviewTrack(width);
    const middle = OVERVIEW_HEIGHT / 2;
    const xOf = (tick: number): number => overviewX(tick, width, state.end);
    context.fillStyle = inks.surface;
    context.fillRect(0, 0, width, OVERVIEW_HEIGHT);
    context.font = "600 12px \"Source Sans 3\", system-ui, sans-serif";
    context.textBaseline = "middle";
    context.textAlign = "left";
    context.fillStyle = inks.ink;
    context.fillText(state.label, 8, middle);
    context.font = "11px \"Source Sans 3\", system-ui, sans-serif";
    context.textAlign = "right";
    context.fillStyle = inks.muted;
    context.fillText(state.lastBar, width - 8, middle);
    context.fillStyle = inks.line;
    context.fillRect(track.left, middle - TRACK / 2, track.right - track.left, TRACK);
    const viewLeft = xOf(state.view.start);
    const viewRight = Math.max(viewLeft + 2 * VIEW_RADIUS, xOf(state.view.end));
    context.beginPath();
    context.roundRect(viewLeft, middle - 7, viewRight - viewLeft, 14, VIEW_RADIUS);
    context.strokeStyle = inks.muted;
    context.lineWidth = 1;
    context.stroke();
    if (state.selection !== null) {
        const left = xOf(state.selection.start);
        context.fillStyle = inks.accent;
        context.fillRect(left, middle - 2, Math.max(2, xOf(state.selection.end) - left), 4);
    }
    const dot = xOf(state.tick);
    context.beginPath();
    context.arc(dot, middle, DOT_RADIUS, 0, 2 * Math.PI);
    context.fillStyle = inks.ink;
    context.fill();
    if (!state.playing) {
        context.beginPath();
        context.arc(dot, middle, DOT_RADIUS - 2, 0, 2 * Math.PI);
        context.fillStyle = inks.surface;
        context.fill();
    }
}
