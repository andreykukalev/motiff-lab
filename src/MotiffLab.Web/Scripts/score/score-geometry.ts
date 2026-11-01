// The score's geometry as one module (decision record 0018, sections 2 and 9; preparation item 11):
// where every page, system, bar, staff and note sits, read from Verovio's SVG, and the boxes every
// overlay is drawn from (phrase brackets, the selection band, rings round notes, Check flags,
// bookmarks, the pencil's bars) and the bar-to-page map. A4 pages and the strip are the same to it:
// a strip is one long page. Kept free of the DOM so Node can test it and the iPad app, which shows
// the same SVG, can do the same: geometry is read in page units from the SVG text, and the view only
// says where each page sits on screen and at what size.

import type { BarFrame } from "./pencil-marks.js";
import { multiply, parseSvg, parseTransform, viewBoxOf, type Matrix, type SvgNode } from "./svg-to-pdf.js";

/** A box: left < right, top < bottom (y grows downwards). */
export interface Rect {
    left: number;
    top: number;
    right: number;
    bottom: number;
}

/** One staff of a bar: its top and bottom lines, and the distance between two lines. */
export interface StaffBox extends Rect {
    /** The staff's MEI number, counted from 1 at the top of the system. */
    n: number;
    space: number;
}

/** A bar as engraved on a page: the box of its staves, and everything engraved in it. */
export interface PageBar extends Rect {
    /** The id of Verovio's measure, which the timeline maps to a tick. */
    id: string;
    /** The index of its system on the page, from 0. */
    system: number;
    staves: StaffBox[];
    /** The staves and the noteheads in the bar together. */
    ink: Rect;
}

/** An engraved note: its notehead's box and centre. */
export interface PageNote extends Rect {
    id: string;
    /** The index of its bar in the page's bars. */
    bar: number;
    /** The MEI number of its staff. */
    staff: number;
    cx: number;
    cy: number;
}

/** A system (line of staves) on a page: the box of its bars' staves. */
export interface PageSystem extends Rect {
    /** The indexes of its bars in the page's bars, in order. */
    bars: number[];
}

/** One page of the score in page units: Verovio's view box, with y downwards. */
export interface PageGeometry {
    /** From 1. */
    number: number;
    width: number;
    height: number;
    systems: PageSystem[];
    bars: PageBar[];
    notes: PageNote[];
}

/** Where a page sits on screen: the offset of its top left corner, and screen pixels per page unit. */
export interface PagePlacement {
    left: number;
    top: number;
    scale: number;
}

/** A bar placed on screen with its place in time. */
export interface ScoreBar extends Rect {
    id: string;
    /** Where the bar starts and ends, in ticks. */
    tick: number;
    end: number;
    page: number;
    /** The system across the whole score, from 0. */
    system: number;
    staves: StaffBox[];
    ink: Rect;
}

/** A note placed on screen. */
export interface ScoreNote extends Rect {
    id: string;
    page: number;
    bar: number;
    staff: number;
    cx: number;
    cy: number;
}

/** A system placed on screen. */
export interface ScoreSystem extends Rect {
    page: number;
    /** The indexes of its bars in the score's bars. */
    bars: number[];
}

/**
 * The part of a passage on one system: what a bracket, a band or a bookmark draws there. A passage
 * across a system break or a page edge has a run on each system it touches.
 */
export interface Run extends Rect {
    page: number;
    system: number;
    /** The run where the passage starts, and the one where it ends: a bracket's number goes on the first, a band's handles on both ends. */
    first: boolean;
    last: boolean;
}

/** A ring round a notehead, as the design kit draws fact highlights: a circle larger than the notehead. */
export interface Ring {
    cx: number;
    cy: number;
    diameter: number;
}

/** SMuFL noteheads are about this many staff spaces wide; a whole note is wider. */
const NOTEHEAD_WIDTH = 1.18;
const WHOLE_NOTEHEAD_WIDTH = 1.69;
const WHOLE_NOTEHEADS = new Set(["E0A2", "E0A0"]);

/** The kit's ring is 8 px wider than the notehead. */
export const RING_GROWTH = 8;

/** A bracket sits this far above the top staff of its bars, in screen pixels (design/kit/score.js). */
export const BRACKET_LIFT = 14;

/** A band reaches this far above and below the staves, in screen pixels (design/kit/score.js). */
export const BAND_PADDING = 8;

const IDENTITY: Matrix = [1, 0, 0, 1, 0, 0];

function apply(m: Matrix, x: number, y: number): [number, number] {
    return [m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]];
}

function classes(node: SvgNode): string[] {
    return (node.attrs.class ?? "").split(/\s+/).filter((name) => name.length > 0);
}

function hasClass(node: SvgNode, name: string): boolean {
    return classes(node).includes(name);
}

const LINE = /^\s*M\s*([-+\d.eE]+)[\s,]+([-+\d.eE]+)\s*L\s*([-+\d.eE]+)[\s,]+([-+\d.eE]+)\s*$/;

/** A path "M x y L x y" as its two ends, or null for any other path. */
function straightLine(d: string | undefined): [number, number, number, number] | null {
    const match = d === undefined ? null : LINE.exec(d);
    return match === null ? null : [Number(match[1]), Number(match[2]), Number(match[3]), Number(match[4])];
}

/**
 * The matrix a nested svg with a view box adds: it fills its parent's viewport, scaled to fit and centred
 * (preserveAspectRatio xMidYMid meet, the default), as Verovio's "definition-scale" svg does.
 */
function nestedViewBox(node: SvgNode, parentWidth: number, parentHeight: number): Matrix {
    const box = (node.attrs.viewBox ?? "").trim().split(/[\s,]+/).map(Number);
    if (box.length !== 4 || !(box[2] > 0) || !(box[3] > 0)) {
        return IDENTITY;
    }
    const scale = Math.min(parentWidth / box[2], parentHeight / box[3]);
    return [scale, 0, 0, scale, (parentWidth - box[2] * scale) / 2 - box[0] * scale, (parentHeight - box[3] * scale) / 2 - box[1] * scale];
}

function union(a: Rect, b: Rect): Rect {
    return { left: Math.min(a.left, b.left), top: Math.min(a.top, b.top), right: Math.max(a.right, b.right), bottom: Math.max(a.bottom, b.bottom) };
}

/**
 * Reads one engraved page (Verovio's SVG for it) in page units: the outer view box, y downwards. A bar's
 * box is its staves, from the top line of the first to the bottom line of the last; its ink adds the
 * noteheads. A note's box is its notehead's, from the glyph's origin and the staff space.
 */
export function readPage(svg: string, number: number): PageGeometry {
    const root = parseSvg(svg);
    const outer = root.name === "svg" ? root : root.children.find((child) => child.name === "svg");
    if (outer === undefined) {
        throw new Error("The page is not an SVG.");
    }
    const [x, y, width, height] = viewBoxOf(outer);
    const page: PageGeometry = { number, width, height, systems: [], bars: [], notes: [] };

    let system: PageSystem | null = null;
    let bar: PageBar | null = null;
    let staff: StaffBox | null = null;

    const visit = (node: SvgNode, matrix: Matrix): void => {
        let current = matrix;
        const transform = parseTransform(node.attrs.transform);
        if (transform !== null) {
            current = multiply(current, transform);
        }
        if (node.name === "svg" && node !== outer) {
            current = multiply(current, nestedViewBox(node, width, height));
        }

        if (node.name === "g" && hasClass(node, "system")) {
            system = { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity, bars: [] };
            for (const child of node.children) {
                visit(child, current);
            }
            if (system.bars.length > 0) {
                page.systems.push(system);
            }
            system = null;
            return;
        }

        if (node.name === "g" && hasClass(node, "measure") && node.attrs.id !== undefined && system !== null) {
            bar = { id: node.attrs.id, system: page.systems.length, left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity, staves: [], ink: { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity } };
            for (const child of node.children) {
                visit(child, current);
            }
            const done: PageBar = bar;
            bar = null;
            if (done.staves.length > 0) {
                Object.assign(done, done.staves.reduce<Rect>((box, line) => union(box, line), done.staves[0]));
                done.ink = union(done.ink, done);
                const owner: PageSystem = system;
                owner.bars.push(page.bars.length);
                Object.assign(owner, union(owner, done));
                page.bars.push(done);
            }
            return;
        }

        if (node.name === "g" && hasClass(node, "staff") && bar !== null) {
            // A staff's lines are its first paths: straight and level.
            const lines: [number, number, number, number][] = [];
            for (const child of node.children) {
                if (child.name !== "path") {
                    continue;
                }
                const line = straightLine(child.attrs.d);
                if (line === null || line[1] !== line[3]) {
                    continue;
                }
                const [x1, y1] = apply(current, line[0], line[1]);
                const [x2, y2] = apply(current, line[2], line[3]);
                lines.push([Math.min(x1, x2), y1, Math.max(x1, x2), y2]);
            }
            const owner: PageBar = bar;
            if (lines.length >= 2) {
                const top = Math.min(...lines.map((line) => line[1]));
                const bottom = Math.max(...lines.map((line) => line[1]));
                staff = {
                    n: owner.staves.length + 1,
                    left: Math.min(...lines.map((line) => line[0])),
                    right: Math.max(...lines.map((line) => line[2])),
                    top,
                    bottom,
                    space: (bottom - top) / (lines.length - 1),
                };
                owner.staves.push(staff);
            }
            for (const child of node.children) {
                visit(child, current);
            }
            staff = null;
            return;
        }

        if (node.name === "g" && hasClass(node, "note") && node.attrs.id !== undefined && bar !== null && staff !== null) {
            const head = node.children.find((child) => child.name === "g" && hasClass(child, "notehead"));
            const glyph = head?.children.find((child) => child.name === "use");
            if (glyph !== undefined) {
                const placed = multiply(current, multiply(parseTransform(head?.attrs.transform) ?? IDENTITY, parseTransform(glyph.attrs.transform) ?? IDENTITY));
                const [left, middle] = apply(placed, 0, 0);
                const owner: StaffBox = staff;
                const href = glyph.attrs["xlink:href"] ?? glyph.attrs.href ?? "";
                const wide = WHOLE_NOTEHEADS.has(href.replace(/^#/, "").split("-")[0]);
                const headWidth = owner.space * (wide ? WHOLE_NOTEHEAD_WIDTH : NOTEHEAD_WIDTH);
                const note: PageNote = {
                    id: node.attrs.id,
                    bar: page.bars.length,
                    staff: owner.n,
                    left,
                    right: left + headWidth,
                    top: middle - owner.space / 2,
                    bottom: middle + owner.space / 2,
                    cx: left + headWidth / 2,
                    cy: middle,
                };
                page.notes.push(note);
                const inside: PageBar = bar;
                inside.ink = union(inside.ink, note);
            }
        }

        for (const child of node.children) {
            visit(child, current);
        }
    };

    visit(outer, [1, 0, 0, 1, -x, -y]);
    return page;
}

function place(rect: Rect, at: PagePlacement): Rect {
    return {
        left: at.left + rect.left * at.scale,
        top: at.top + rect.top * at.scale,
        right: at.left + rect.right * at.scale,
        bottom: at.top + rect.bottom * at.scale,
    };
}

/**
 * The whole score on screen: its pages read once per layout, placed where the view shows them, and the
 * bars put in time by the timeline's measure onsets. Every overlay is drawn from here, so it sits the same
 * on A4 pages and on the strip.
 */
export class ScoreGeometry {
    readonly bars: readonly ScoreBar[];
    readonly systems: readonly ScoreSystem[];
    readonly notes: readonly ScoreNote[];
    private readonly noteIndex: Map<string, ScoreNote>;

    /**
     * @param pages each page's geometry, in order.
     * @param placements where each page sits on screen, in the same order; a page without one is left out.
     * @param measureOnset where a measure starts, from the timeline; a measure it does not know is left out.
     * @param end where the piece ends, for the last bar's end.
     */
    constructor(pages: readonly PageGeometry[], placements: readonly PagePlacement[], measureOnset: (id: string) => number | undefined, end?: number) {
        const bars: ScoreBar[] = [];
        const systems: ScoreSystem[] = [];
        const notes: ScoreNote[] = [];
        pages.forEach((page, pageIndex) => {
            const at = placements[pageIndex];
            if (at === undefined) {
                return;
            }
            const barIndex = new Map<number, number>();
            for (const pageSystem of page.systems) {
                const system: ScoreSystem = { page: page.number, bars: [], ...place(pageSystem, at) };
                for (const index of pageSystem.bars) {
                    const pageBar = page.bars[index];
                    const tick = measureOnset(pageBar.id);
                    if (tick === undefined) {
                        continue;
                    }
                    barIndex.set(index, bars.length);
                    system.bars.push(bars.length);
                    bars.push({
                        id: pageBar.id,
                        tick,
                        end: Infinity,
                        page: page.number,
                        system: systems.length,
                        staves: pageBar.staves.map((staff) => ({ n: staff.n, space: staff.space * at.scale, ...place(staff, at) })),
                        ink: place(pageBar.ink, at),
                        ...place(pageBar, at),
                    });
                }
                if (system.bars.length > 0) {
                    systems.push(system);
                }
            }
            for (const note of page.notes) {
                const bar = barIndex.get(note.bar);
                if (bar !== undefined) {
                    notes.push({ id: note.id, page: page.number, bar, staff: note.staff, cx: at.left + note.cx * at.scale, cy: at.top + note.cy * at.scale, ...place(note, at) });
                }
            }
        });

        // A bar ends where the next one in time starts; the last where the piece ends.
        const inTime = [...bars.keys()].sort((a, b) => bars[a].tick - bars[b].tick);
        inTime.forEach((index, order) => {
            const next = inTime[order + 1];
            bars[index].end = next !== undefined ? bars[next].tick : (end ?? Infinity);
        });

        this.bars = bars;
        this.systems = systems;
        this.notes = notes;
        this.noteIndex = new Map(notes.map((note) => [note.id, note]));
    }

    /** The page each bar is on, by the tick where it starts: the same map as the PDF's on A4 pages (0017). */
    barToPage(): Map<number, number> {
        const map = new Map<number, number>();
        for (const bar of this.bars) {
            if (!map.has(bar.tick)) {
                map.set(bar.tick, bar.page);
            }
        }
        return map;
    }

    /** The page that holds a tick, or undefined before the first bar. */
    pageOf(tick: number): number | undefined {
        return this.barAt(tick)?.page;
    }

    /** The bar that holds a tick: starts at or before it and ends after it. */
    barAt(tick: number): ScoreBar | undefined {
        return this.bars.find((bar) => bar.tick <= tick && tick < bar.end);
    }

    /** The bars a passage [start, end) touches, in order on the page. */
    barsIn(start: number, end: number): ScoreBar[] {
        return this.bars.filter((bar) => bar.tick < end && bar.end > start);
    }

    /**
     * A passage [start, end) system by system: on each system it touches, the box from its first bar's left
     * edge to its last bar's right edge, over the staves (or only the staves numbered in `staves`).
     */
    runs(start: number, end: number, staves?: readonly number[]): Run[] {
        const bySystem = new Map<number, ScoreBar[]>();
        for (const bar of this.barsIn(start, end)) {
            const list = bySystem.get(bar.system) ?? [];
            list.push(bar);
            bySystem.set(bar.system, list);
        }
        const runs: Run[] = [];
        for (const [system, bars] of [...bySystem.entries()].sort((a, b) => a[0] - b[0])) {
            const lines = bars.flatMap((bar) => bar.staves.filter((staff) => staves === undefined || staves.includes(staff.n)));
            const from = lines.length > 0 ? lines : bars;
            runs.push({
                page: bars[0].page,
                system,
                left: Math.min(...bars.map((bar) => bar.left)),
                right: Math.max(...bars.map((bar) => bar.right)),
                top: Math.min(...from.map((box) => box.top)),
                bottom: Math.max(...from.map((box) => box.bottom)),
                first: false,
                last: false,
            });
        }
        if (runs.length > 0) {
            runs[0].first = true;
            runs[runs.length - 1].last = true;
        }
        return runs;
    }

    /** A phrase bracket over a passage: a line `lift` pixels above the top staff on each system, inset 2 px at each end as the kit draws it. */
    bracket(start: number, end: number, lift = BRACKET_LIFT): Run[] {
        return this.runs(start, end).map((run) => ({ ...run, left: run.left + 2, right: run.right - 2, top: run.top - lift, bottom: run.top - lift }));
    }

    /** The selection band over a passage: the staves of each system it touches, `padding` pixels above and below. */
    band(start: number, end: number, padding = BAND_PADDING, staves?: readonly number[]): Run[] {
        return this.runs(start, end, staves).map((run) => ({ ...run, top: run.top - padding, bottom: run.bottom + padding }));
    }

    /** The engraved note with an element id, if it is on a page. */
    note(id: string): ScoreNote | undefined {
        return this.noteIndex.get(id);
    }

    /** A ring round a note: centred on its notehead, `growth` pixels wider than it. */
    ring(id: string, growth = RING_GROWTH): Ring | undefined {
        const note = this.noteIndex.get(id);
        if (note === undefined) {
            return undefined;
        }
        return { cx: note.cx, cy: note.cy, diameter: Math.max(note.right - note.left, note.bottom - note.top) + growth };
    }

    /** Where a mark on a bar goes, such as a Check flag or a margin note's marker: the top right corner of its top staff. */
    flag(tick: number): { x: number; y: number } | undefined {
        const bar = this.barAt(tick);
        return bar === undefined ? undefined : { x: bar.right, y: bar.staves[0]?.top ?? bar.top };
    }

    /** The bars as the pencil layer anchors marks to them (pencil-marks.ts, 0015). */
    barFrames(): BarFrame[] {
        return this.bars.map((bar) => ({
            tick: bar.tick,
            left: bar.left,
            right: bar.right,
            top: bar.ink.top,
            bottom: bar.ink.bottom,
            staffTop: bar.staves[0]?.top ?? bar.top,
            space: bar.staves[0]?.space ?? (bar.bottom - bar.top) / 8,
        }));
    }
}
