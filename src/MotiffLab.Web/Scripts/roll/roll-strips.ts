// The strips of the iPad's roll (v0.5.2, #479 item 25, approved mockup): one playback position, drawn in ink outside the
// selection and in cobalt inside it; under the bar numbers a loop strip, the selection's bounds with a handle at each end
// and the loop sign, where a tap moves the position; and over everything the whole piece in one line, always shown: the
// position written as bar.beat, a frame round the bars on screen, the selection in cobalt and the position's dot, where a
// tap moves the position and the roll. Pure drawing on a context the roll gives; nothing here touches the DOM.
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

/** Whether a tick lies inside the selection, [start, end): the position is cobalt there and ink elsewhere. */
export function insideOf(selection: TickSpan | null, tick: number): boolean {
    return selection !== null && tick >= selection.start && tick < selection.end;
}

/**
 * The loop strip: the selection's bounds as a rounded strip outlined in cobalt, tinted while it loops, with the loop sign at
 * its start and a handle at each end; nothing without a selection.
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
    if (looping && right - left > 4 * HANDLE_RADIUS) {
        drawLoopSign(context, left + 3 * HANDLE_RADIUS + 2, middle, inks.accent);
    }
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

/** The loop sign: a ring almost closed with an arrow at its end, as the kit's loop icon reads. */
function drawLoopSign(context: CanvasRenderingContext2D, x: number, y: number, colour: string): void {
    const radius = 4;
    context.beginPath();
    context.arc(x, y, radius, 0.25 * Math.PI, 1.85 * Math.PI);
    context.strokeStyle = colour;
    context.lineWidth = 1.5;
    context.stroke();
    const tipX = x + radius * Math.cos(1.85 * Math.PI);
    const tipY = y + radius * Math.sin(1.85 * Math.PI);
    context.beginPath();
    context.moveTo(tipX + 3, tipY);
    context.lineTo(tipX - 1, tipY - 3);
    context.lineTo(tipX - 1, tipY + 3);
    context.closePath();
    context.fillStyle = colour;
    context.fill();
}

/** The one playback position down the roll: cobalt inside the selection, ink outside, its dot in the loop lane, hollow when it stands. */
export function drawPosition(context: CanvasRenderingContext2D, frame: LaneFrame, tick: number, selection: TickSpan | null, playing: boolean, inks: StripInks): void {
    const colour = insideOf(selection, tick) ? inks.accent : inks.ink;
    const lines = { xOf: frame.xOf, header: frame.top, left: frame.left, right: frame.right, height: frame.height };
    drawPositionLine(context, lines, frame.xOf(tick), { colour, knobY: frame.top + LOOP_LANE / 2, radius: DOT_RADIUS, solid: playing }, { ink: inks.ink, accent: inks.accent, surface: inks.surface, paper: inks.surface });
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

/** The whole-piece strip: label, track, the frame round the bars on screen, the selection in cobalt, the position's dot. */
export function drawOverview(context: CanvasRenderingContext2D, width: number, state: OverviewState, inks: StripInks): void {
    const track = overviewTrack(width);
    const middle = OVERVIEW_HEIGHT / 2;
    const xOf = (tick: number): number => track.left + (track.right - track.left) * (state.end > 0 ? Math.min(1, Math.max(0, tick / state.end)) : 0);
    const inside = insideOf(state.selection, state.tick);
    context.fillStyle = inks.surface;
    context.fillRect(0, 0, width, OVERVIEW_HEIGHT);
    context.font = "600 12px \"Source Sans 3\", system-ui, sans-serif";
    context.textBaseline = "middle";
    context.textAlign = "left";
    context.fillStyle = inside ? inks.accent : inks.ink;
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
    context.fillStyle = inside ? inks.accent : inks.ink;
    context.fill();
    if (!state.playing) {
        context.beginPath();
        context.arc(dot, middle, DOT_RADIUS - 2, 0, 2 * Math.PI);
        context.fillStyle = inks.surface;
        context.fill();
    }
}
