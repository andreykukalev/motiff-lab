// Pencil marks on the score (decision record 0015): what the player writes over the engraving by hand,
// as on paper. The score reflows with the window's width, so a mark is kept against the bar it was
// written in, not against the page: where in the bar it starts (a share of the bar's width) and how far
// from the bar's top staff line, with its shape in staff spaces. Laid out again, each mark goes back to
// its bar at the same size as the staves. Pure functions only; the layer that draws them is pencil-layer.ts.
//
// Marks are the player's own writing. They are never read as music: nothing here turns them into notes,
// facts or analysis.

/** A point the pen passed through, in the score's content pixels; pressure is 0 to 1, 0.5 when the device has none. */
export interface InkPoint {
    x: number;
    y: number;
    pressure: number;
}

export type MarkTool = "pen" | "highlighter";

/** Pen colours and the highlighter's; the stylesheet gives each its colour on paper. */
export type MarkColour = "ink" | "red" | "blue" | "yellow";

/** One stroke, kept against the bar it starts in. */
export interface PencilMark {
    id: string;
    tool: MarkTool;
    colour: MarkColour;
    /** The stroke's thickness in staff spaces. */
    size: number;
    /** Where the bar it starts in begins, in ticks: stable across layouts and across the as-written and generated scores. */
    bar: number;
    /** Where the stroke starts: across the bar, 0 at its left edge and 1 at its right. */
    u: number;
    /** Where the stroke starts: staff spaces below the bar's top staff line (negative above it). */
    v: number;
    /** The stroke's points after the first, as dx, dy (staff spaces from the first point) and pressure, three numbers each. */
    points: number[];
    /** The first point's pressure. */
    pressure: number;
}

/** Where an engraved bar sits in the score's content pixels, measured after each layout. */
export interface BarFrame {
    /** Where the bar starts, in ticks. */
    tick: number;
    left: number;
    right: number;
    /** The top and bottom of everything engraved in the bar. */
    top: number;
    bottom: number;
    /** The bar's top staff line. */
    staffTop: number;
    /** Pixels between two staff lines. */
    space: number;
}

/** What a saved set of marks looks like; version 1. */
export interface MarksFile {
    version: 1;
    marks: PencilMark[];
}

/** Pen thickness in staff spaces: about a pencil's line at the score's size. */
export const PEN_SIZE = 0.16;

/** The highlighter is about two staff spaces wide, enough to cover a note and its neighbours' heads. */
export const HIGHLIGHTER_SIZE = 1.1;

/** Points closer than this (pixels) to the last one kept add nothing to the line. */
export const MIN_POINT_DISTANCE = 0.75;

/** A press that moves less than this (pixels) is a tap: it picks a note or a bar, as a click does. */
export const TAP_DISTANCE = 6;

const DECIMALS = 1000;

const round = (value: number): number => Math.round(value * DECIMALS) / DECIMALS;

/** Distance from a point to a box; 0 inside it. */
function distanceToBox(frame: BarFrame, x: number, y: number): number {
    const dx = x < frame.left ? frame.left - x : x > frame.right ? x - frame.right : 0;
    const dy = y < frame.top ? frame.top - y : y > frame.bottom ? y - frame.bottom : 0;
    return Math.hypot(dx, dy);
}

/** The bar a point belongs to: the one it lies in, or else the nearest; the first of equals. Undefined with no bars. */
export function nearestBar(frames: readonly BarFrame[], x: number, y: number): BarFrame | undefined {
    let best: BarFrame | undefined;
    let bestDistance = Infinity;
    for (const frame of frames) {
        const distance = distanceToBox(frame, x, y);
        if (distance < bestDistance) {
            best = frame;
            bestDistance = distance;
        }
    }
    return best;
}

/** Keeps a stroke drawn in content pixels against the bar its first point is nearest. Null with no points or no bars. */
export function anchorMark(
    frames: readonly BarFrame[],
    points: readonly InkPoint[],
    style: { id: string; tool: MarkTool; colour: MarkColour; size: number },
): PencilMark | null {
    const first = points[0];
    const frame = first === undefined ? undefined : nearestBar(frames, first.x, first.y);
    if (first === undefined || frame === undefined || frame.space <= 0) {
        return null;
    }
    const width = frame.right - frame.left;
    const rest: number[] = [];
    for (const point of points.slice(1)) {
        rest.push(round((point.x - first.x) / frame.space), round((point.y - first.y) / frame.space), round(point.pressure));
    }
    return {
        id: style.id,
        tool: style.tool,
        colour: style.colour,
        size: style.size,
        bar: frame.tick,
        u: width > 0 ? round((first.x - frame.left) / width) : 0,
        v: round((first.y - frame.staffTop) / frame.space),
        points: rest,
        pressure: round(first.pressure),
    };
}

/** Where a mark goes in this layout, in content pixels, and its thickness there; null when its bar is not engraved. */
export function placeMark(mark: PencilMark, frames: ReadonlyMap<number, BarFrame>): { points: InkPoint[]; radius: number } | null {
    const frame = frames.get(mark.bar);
    if (frame === undefined) {
        return null;
    }
    const x0 = frame.left + mark.u * (frame.right - frame.left);
    const y0 = frame.staffTop + mark.v * frame.space;
    const points: InkPoint[] = [{ x: x0, y: y0, pressure: mark.pressure }];
    for (let i = 0; i + 2 < mark.points.length; i += 3) {
        points.push({ x: x0 + mark.points[i] * frame.space, y: y0 + mark.points[i + 1] * frame.space, pressure: mark.points[i + 2] });
    }
    return { points, radius: (mark.size * frame.space) / 2 };
}

/** The bars by where they start, for placeMark. */
export function framesByTick(frames: readonly BarFrame[]): Map<number, BarFrame> {
    const byTick = new Map<number, BarFrame>();
    for (const frame of frames) {
        if (!byTick.has(frame.tick)) {
            byTick.set(frame.tick, frame);
        }
    }
    return byTick;
}

/** A point's half-thickness: pressure 0.5 draws the stroke's own size, a light touch thinner, a firm one thicker. */
export function radiusAt(radius: number, pressure: number, followsPressure: boolean): number {
    return followsPressure ? radius * (0.5 + Math.min(1, Math.max(0, pressure))) : radius;
}

const f = (value: number): string => (Math.round(value * 100) / 100).toString();

/**
 * The outline of a stroke as an SVG path to fill: the line's two edges, each the stroke's half-thickness
 * from the centre line, joined by round ends. A single point is a dot.
 */
export function outlinePath(points: readonly InkPoint[], radius: number, followsPressure: boolean): string {
    if (points.length === 0) {
        return "";
    }
    // Each point's half-thickness, eased with its neighbours' so a pressure change does not show as a step.
    const raw = points.map((point) => radiusAt(radius, point.pressure, followsPressure));
    const radii = raw.map((r, i) => (raw[Math.max(0, i - 1)] + 2 * r + raw[Math.min(raw.length - 1, i + 1)]) / 4);

    if (points.length === 1 || points.every((point) => point.x === points[0].x && point.y === points[0].y)) {
        const { x, y } = points[0];
        const r = radii[0];
        return `M${f(x - r)} ${f(y)}A${f(r)} ${f(r)} 0 1 0 ${f(x + r)} ${f(y)}A${f(r)} ${f(r)} 0 1 0 ${f(x - r)} ${f(y)}Z`;
    }

    const left: Array<{ x: number; y: number }> = [];
    const right: Array<{ x: number; y: number }> = [];
    for (let i = 0; i < points.length; i++) {
        const before = points[Math.max(0, i - 1)];
        const after = points[Math.min(points.length - 1, i + 1)];
        let dx = after.x - before.x;
        let dy = after.y - before.y;
        const length = Math.hypot(dx, dy);
        if (length === 0) {
            dx = 1;
            dy = 0;
        } else {
            dx /= length;
            dy /= length;
        }
        const r = radii[i];
        left.push({ x: points[i].x - dy * r, y: points[i].y + dx * r });
        right.push({ x: points[i].x + dy * r, y: points[i].y - dx * r });
    }

    const end = radii[radii.length - 1];
    const start = radii[0];
    // The round ends bulge outward: sweep 0, as y points down.
    const back = [...right].reverse();
    const last = back[0];
    const first = left[0];
    return `M${f(first.x)} ${f(first.y)}${smooth(left)}`
        + `A${f(end)} ${f(end)} 0 0 0 ${f(last.x)} ${f(last.y)}${smooth(back)}`
        + `A${f(start)} ${f(start)} 0 0 0 ${f(first.x)} ${f(first.y)}Z`;
}

/** A line through the points after the first, eased with quadratic curves through their midpoints. */
function smooth(points: ReadonlyArray<{ x: number; y: number }>): string {
    let path = "";
    for (let i = 1; i < points.length - 1; i++) {
        const mx = (points[i].x + points[i + 1].x) / 2;
        const my = (points[i].y + points[i + 1].y) / 2;
        path += `Q${f(points[i].x)} ${f(points[i].y)} ${f(mx)} ${f(my)}`;
    }
    const last = points[points.length - 1];
    return points.length > 1 ? `${path}L${f(last.x)} ${f(last.y)}` : path;
}

/** Whether a point is within reach of a stroke's centre line, its own thickness added: what the eraser takes. */
export function touches(points: readonly InkPoint[], radius: number, x: number, y: number, reach: number): boolean {
    const limit = reach + radius;
    if (points.length === 1) {
        return Math.hypot(points[0].x - x, points[0].y - y) <= limit;
    }
    for (let i = 1; i < points.length; i++) {
        if (distanceToSegment(points[i - 1], points[i], x, y) <= limit) {
            return true;
        }
    }
    return false;
}

function distanceToSegment(a: InkPoint, b: InkPoint, x: number, y: number): number {
    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const lengthSquared = dx * dx + dy * dy;
    const t = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((x - a.x) * dx + (y - a.y) * dy) / lengthSquared));
    return Math.hypot(a.x + t * dx - x, a.y + t * dy - y);
}

const TOOLS: ReadonlySet<string> = new Set<MarkTool>(["pen", "highlighter"]);
const COLOURS: ReadonlySet<string> = new Set<MarkColour>(["ink", "red", "blue", "yellow"]);

const isNumber = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value);

/** The marks as saved. */
export function writeMarks(marks: readonly PencilMark[]): string {
    const file: MarksFile = { version: 1, marks: [...marks] };
    return JSON.stringify(file);
}

/** The marks in a saved file; marks that are not whole are left out, and anything unreadable gives none. */
export function readMarks(text: string | null | undefined): PencilMark[] {
    if (!text) {
        return [];
    }
    let file: unknown;
    try {
        file = JSON.parse(text);
    } catch {
        return [];
    }
    const marks = (file as Partial<MarksFile> | null)?.marks;
    if ((file as Partial<MarksFile> | null)?.version !== 1 || !Array.isArray(marks)) {
        return [];
    }
    return marks.filter((mark): mark is PencilMark =>
        typeof mark === "object" && mark !== null
        && typeof mark.id === "string"
        && TOOLS.has(mark.tool) && COLOURS.has(mark.colour)
        && isNumber(mark.size) && mark.size > 0
        && isNumber(mark.bar) && isNumber(mark.u) && isNumber(mark.v) && isNumber(mark.pressure)
        && Array.isArray(mark.points) && mark.points.length % 3 === 0 && mark.points.every(isNumber));
}
