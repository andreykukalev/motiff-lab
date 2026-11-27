// One engraved page read from Verovio's SVG text (decision record 0021; the audit's W3.13): its systems, bars, staves
// and noteheads in page units, the page's own view box with y downwards. Kept free of the DOM so Node can test it.
// ScoreGeometry (score-geometry.ts) places the pages read here on screen and puts their bars in time.

import { multiply, parseSvg, parseTransform, viewBoxOf, type Matrix, type SvgNode } from "./svg-parse.js";

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

/** SMuFL noteheads are about this many staff spaces wide; a whole note is wider. */
const NOTEHEAD_WIDTH = 1.18;
const WHOLE_NOTEHEAD_WIDTH = 1.69;
const WHOLE_NOTEHEADS = new Set(["E0A2", "E0A0"]);

const IDENTITY: Matrix = [1, 0, 0, 1, 0, 0];

/** A box that holds nothing yet: the first box it is joined with becomes it. */
const EMPTY: Rect = { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity };

function apply(m: Matrix, x: number, y: number): [number, number] {
    return [m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]];
}

function classes(node: SvgNode): string[] {
    return (node.attrs.class ?? "").split(/\s+/).filter((name) => name.length > 0);
}

function hasClass(node: SvgNode, name: string): boolean {
    return classes(node).includes(name);
}

/** Whether a node is Verovio's group of a kind: `g.system`, `g.measure`, `g.staff`, `g.note`. */
function isGroup(node: SvgNode, name: string): boolean {
    return node.name === "g" && hasClass(node, name);
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

/** A staff's lines: its paths that are straight and level, each as its left, y, right and y on the page. */
function staffLines(node: SvgNode, matrix: Matrix): [number, number, number, number][] {
    const lines: [number, number, number, number][] = [];
    for (const child of node.children) {
        const line = child.name === "path" ? straightLine(child.attrs.d) : null;
        if (line === null || line[1] !== line[3]) {
            continue;
        }
        const [x1, y1] = apply(matrix, line[0], line[1]);
        const [x2, y2] = apply(matrix, line[2], line[3]);
        lines.push([Math.min(x1, x2), y1, Math.max(x1, x2), y2]);
    }
    return lines;
}

/** The staff numbered `n` from its lines (two or more): from its top line to its bottom one. */
function staffOf(lines: readonly [number, number, number, number][], n: number): StaffBox {
    const top = Math.min(...lines.map((line) => line[1]));
    const bottom = Math.max(...lines.map((line) => line[1]));
    return {
        n,
        left: Math.min(...lines.map((line) => line[0])),
        right: Math.max(...lines.map((line) => line[2])),
        top,
        bottom,
        space: (bottom - top) / (lines.length - 1),
    };
}

/** A note's notehead on its staff, from the glyph's origin and the staff space; none for a note without one. */
function noteOf(node: SvgNode, id: string, bar: number, staff: StaffBox, matrix: Matrix): PageNote | undefined {
    const head = node.children.find((child) => isGroup(child, "notehead"));
    const glyph = head?.children.find((child) => child.name === "use");
    if (glyph === undefined) {
        return undefined;
    }
    const placed = multiply(matrix, multiply(parseTransform(head?.attrs.transform) ?? IDENTITY, parseTransform(glyph.attrs.transform) ?? IDENTITY));
    const [left, middle] = apply(placed, 0, 0);
    const href = glyph.attrs["xlink:href"] ?? glyph.attrs.href ?? "";
    const wide = WHOLE_NOTEHEADS.has(href.replace(/^#/, "").split("-")[0]);
    const headWidth = staff.space * (wide ? WHOLE_NOTEHEAD_WIDTH : NOTEHEAD_WIDTH);
    return { id, bar, staff: staff.n, left, right: left + headWidth, top: middle - staff.space / 2, bottom: middle + staff.space / 2, cx: left + headWidth / 2, cy: middle };
}

/** Walks one page's SVG tree once, keeping the system, the bar and the staff it is inside. */
class PageReader {
    readonly page: PageGeometry;
    private system: PageSystem | null = null;
    private bar: PageBar | null = null;
    private staff: StaffBox | null = null;

    constructor(private readonly outer: SvgNode, number: number) {
        const [x, y, width, height] = viewBoxOf(outer);
        this.page = { number, width, height, systems: [], bars: [], notes: [] };
        this.visit(outer, [1, 0, 0, 1, -x, -y]);
    }

    private visit(node: SvgNode, matrix: Matrix): void {
        const current = this.placed(node, matrix);
        if (isGroup(node, "system")) {
            this.readSystem(node, current);
            return;
        }
        if (isGroup(node, "measure") && node.attrs.id !== undefined && this.system !== null) {
            this.readBar(node, node.attrs.id, this.system, current);
            return;
        }
        if (isGroup(node, "staff") && this.bar !== null) {
            this.readStaff(node, this.bar, current);
            return;
        }
        if (isGroup(node, "note") && node.attrs.id !== undefined && this.bar !== null && this.staff !== null) {
            const note = noteOf(node, node.attrs.id, this.page.bars.length, this.staff, current);
            if (note !== undefined) {
                this.page.notes.push(note);
                this.bar.ink = union(this.bar.ink, note);
            }
        }
        this.visitChildren(node, current);
    }

    private visitChildren(node: SvgNode, matrix: Matrix): void {
        for (const child of node.children) {
            this.visit(child, matrix);
        }
    }

    /** The matrix a node draws with: its parent's, then its own transform, then a nested svg's view box. */
    private placed(node: SvgNode, matrix: Matrix): Matrix {
        let current = matrix;
        const transform = parseTransform(node.attrs.transform);
        if (transform !== null) {
            current = multiply(current, transform);
        }
        if (node.name === "svg" && node !== this.outer) {
            current = multiply(current, nestedViewBox(node, this.page.width, this.page.height));
        }
        return current;
    }

    /** A system and its bars; one with no bar read is left out. */
    private readSystem(node: SvgNode, matrix: Matrix): void {
        const system: PageSystem = { ...EMPTY, bars: [] };
        this.system = system;
        this.visitChildren(node, matrix);
        if (system.bars.length > 0) {
            this.page.systems.push(system);
        }
        this.system = null;
    }

    /** A bar: the box of its staves, its ink with the noteheads; one with no staff read is left out. */
    private readBar(node: SvgNode, id: string, system: PageSystem, matrix: Matrix): void {
        const bar: PageBar = { id, system: this.page.systems.length, ...EMPTY, staves: [], ink: { ...EMPTY } };
        this.bar = bar;
        this.visitChildren(node, matrix);
        this.bar = null;
        if (bar.staves.length > 0) {
            Object.assign(bar, bar.staves.reduce<Rect>((box, line) => union(box, line), bar.staves[0]));
            bar.ink = union(bar.ink, bar);
            system.bars.push(this.page.bars.length);
            Object.assign(system, union(system, bar));
            this.page.bars.push(bar);
        }
    }

    /** A staff of the bar, from its first paths, straight and level; the notes inside it are on it. */
    private readStaff(node: SvgNode, bar: PageBar, matrix: Matrix): void {
        const lines = staffLines(node, matrix);
        if (lines.length >= 2) {
            this.staff = staffOf(lines, bar.staves.length + 1);
            bar.staves.push(this.staff);
        }
        this.visitChildren(node, matrix);
        this.staff = null;
    }
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
    return new PageReader(outer, number).page;
}

/**
 * For each system on a page, the box round the given staves of its bars, such as the ossia's above the original in
 * phrase focus (0018 section 4); a system without them gives none. Staves are numbered as in the document, from 1.
 */
export function staffFrames(page: PageGeometry, staves: readonly number[]): Rect[] {
    const wanted = new Set(staves);
    const frames: Rect[] = [];
    for (const system of page.systems) {
        const boxes = system.bars.flatMap((index) => page.bars[index].staves.filter((staff) => wanted.has(staff.n)));
        if (boxes.length > 0) {
            frames.push(boxes.reduce<Rect>((frame, staff) => union(frame, staff), EMPTY));
        }
    }
    return frames;
}
