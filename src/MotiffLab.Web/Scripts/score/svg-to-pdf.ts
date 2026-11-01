// Turns a page of Verovio's SVG into PDF drawing operators, for the score's PDF export (decision
// record 0017). Verovio draws with a small part of SVG: groups and nested svg with transforms and
// view boxes, paths, polygons, polylines, ellipses and rects, and glyphs placed with <use>. Those
// become vector paths in the PDF. Text (titles, tempo words, bar and page numbers) is left to the
// browser, which knows the fonts: textOnlySvg gives the same page with only its text, for the
// export to draw as an image over the vectors. Kept free of the DOM so Node can test it.

/** An element of the parsed SVG. */
export interface SvgNode {
    name: string;
    attrs: Record<string, string>;
    children: SvgNode[];
}

const ENTITIES: Record<string, string> = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'" };

function decode(text: string): string {
    return text.replace(/&(#x[0-9a-fA-F]+|#\d+|[a-z]+);/g, (whole, entity: string) => {
        if (entity.startsWith("#x")) {
            return String.fromCodePoint(parseInt(entity.slice(2), 16));
        }
        if (entity.startsWith("#")) {
            return String.fromCodePoint(parseInt(entity.slice(1), 10));
        }
        return ENTITIES[entity] ?? whole;
    });
}

const ATTRIBUTE = /([^\s=/>]+)\s*=\s*("([^"]*)"|'([^']*)')/g;

/** Parses well-formed XML such as Verovio's SVG into elements; text, comments and declarations are dropped. */
export function parseSvg(xml: string): SvgNode {
    const root: SvgNode = { name: "#document", attrs: {}, children: [] };
    const stack: SvgNode[] = [root];
    let at = 0;
    while (at < xml.length) {
        const open = xml.indexOf("<", at);
        if (open < 0) {
            break;
        }
        if (xml.startsWith("<!--", open)) {
            at = end(xml, "-->", open) + 3;
            continue;
        }
        if (xml.startsWith("<![CDATA[", open)) {
            at = end(xml, "]]>", open) + 3;
            continue;
        }
        if (xml.startsWith("<?", open) || xml.startsWith("<!", open)) {
            at = end(xml, ">", open) + 1;
            continue;
        }
        const close = end(xml, ">", open);
        const tag = xml.slice(open + 1, close);
        at = close + 1;
        if (tag.startsWith("/")) {
            if (stack.length > 1) {
                stack.pop();
            }
            continue;
        }
        const selfClosing = tag.endsWith("/");
        const body = selfClosing ? tag.slice(0, -1) : tag;
        const name = /^[^\s/>]+/.exec(body)?.[0] ?? "";
        const attrs: Record<string, string> = {};
        for (const match of body.slice(name.length).matchAll(ATTRIBUTE)) {
            attrs[match[1]] = decode(match[3] ?? match[4] ?? "");
        }
        const node: SvgNode = { name, attrs, children: [] };
        stack[stack.length - 1].children.push(node);
        if (!selfClosing) {
            stack.push(node);
            // The contents of style and script are not markup.
            if (name === "style" || name === "script") {
                at = end(xml, `</${name}`, at);
            }
        }
    }
    return root;
}

function end(xml: string, token: string, from: number): number {
    const found = xml.indexOf(token, from);
    if (found < 0) {
        throw new Error("The SVG is not well formed.");
    }
    return found;
}

/** The view box of a page's root svg: [x, y, width, height]. */
export function viewBoxOf(svg: SvgNode): [number, number, number, number] {
    const root = svg.name === "svg" ? svg : svg.children.find((child) => child.name === "svg");
    const box = numbers(root?.attrs.viewBox ?? "");
    if (box.length !== 4 || box[2] <= 0 || box[3] <= 0) {
        throw new Error("The SVG page has no view box.");
    }
    return [box[0], box[1], box[2], box[3]];
}

const NUMBER = /[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?/g;

function numbers(text: string): number[] {
    return (text.match(NUMBER) ?? []).map(Number);
}

/** A number for the PDF: at most three decimals, no exponent. */
export function num(value: number): string {
    if (!Number.isFinite(value)) {
        return "0";
    }
    const rounded = Math.round(value * 1000) / 1000;
    return Object.is(rounded, -0) ? "0" : String(rounded);
}

export type Matrix = [number, number, number, number, number, number];

/** The matrix that applies n, then m. */
export function multiply(m: Matrix, n: Matrix): Matrix {
    return [
        m[0] * n[0] + m[2] * n[1],
        m[1] * n[0] + m[3] * n[1],
        m[0] * n[2] + m[2] * n[3],
        m[1] * n[2] + m[3] * n[3],
        m[0] * n[4] + m[2] * n[5] + m[4],
        m[1] * n[4] + m[3] * n[5] + m[5],
    ];
}

/** An SVG transform list as one matrix; null when it is the identity or absent. */
export function parseTransform(text: string | undefined): Matrix | null {
    if (text === undefined) {
        return null;
    }
    let matrix: Matrix = [1, 0, 0, 1, 0, 0];
    let any = false;
    for (const match of text.matchAll(/(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)/g)) {
        const v = numbers(match[2]);
        let step: Matrix;
        switch (match[1]) {
            case "matrix":
                step = v.length === 6 ? [v[0], v[1], v[2], v[3], v[4], v[5]] : [1, 0, 0, 1, 0, 0];
                break;
            case "translate":
                step = [1, 0, 0, 1, v[0] ?? 0, v[1] ?? 0];
                break;
            case "scale":
                step = [v[0] ?? 1, 0, 0, v[1] ?? v[0] ?? 1, 0, 0];
                break;
            case "rotate": {
                const angle = ((v[0] ?? 0) * Math.PI) / 180;
                const [cx, cy] = [v[1] ?? 0, v[2] ?? 0];
                const [cos, sin] = [Math.cos(angle), Math.sin(angle)];
                step = multiply(multiply([1, 0, 0, 1, cx, cy], [cos, sin, -sin, cos, 0, 0]), [1, 0, 0, 1, -cx, -cy]);
                break;
            }
            case "skewX":
                step = [1, 0, Math.tan(((v[0] ?? 0) * Math.PI) / 180), 1, 0, 0];
                break;
            default:
                step = [1, Math.tan(((v[0] ?? 0) * Math.PI) / 180), 0, 1, 0, 0];
                break;
        }
        matrix = multiply(matrix, step);
        any = true;
    }
    return any ? matrix : null;
}

/** An SVG path's data as PDF path construction operators (m, l, c, h), in the same coordinates. */
export function pathOperators(d: string): string {
    const tokens = d.match(/[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?/g) ?? [];
    const out: string[] = [];
    let i = 0;
    let command = "";
    let [x, y] = [0, 0];
    let [startX, startY] = [0, 0];
    // The last control point, for S and T.
    let [cx, cy] = [0, 0];
    let last = "";
    const next = (): number => Number(tokens[i++]);
    const hasNumber = (): boolean => i < tokens.length && !/^[A-Za-z]$/.test(tokens[i]);
    while (i < tokens.length) {
        if (/^[A-Za-z]$/.test(tokens[i])) {
            command = tokens[i++];
        } else if (command === "") {
            break;
        }
        const relative = command === command.toLowerCase();
        const dx = relative ? x : 0;
        const dy = relative ? y : 0;
        switch (command.toUpperCase()) {
            case "M": {
                if (!hasNumber()) {
                    break;
                }
                x = next() + dx;
                y = next() + dy;
                [startX, startY] = [x, y];
                out.push(`${num(x)} ${num(y)} m`);
                // Further pairs after a move are lines.
                command = relative ? "l" : "L";
                break;
            }
            case "L":
                if (!hasNumber()) {
                    break;
                }
                x = next() + dx;
                y = next() + dy;
                out.push(`${num(x)} ${num(y)} l`);
                break;
            case "H":
                if (!hasNumber()) {
                    break;
                }
                x = next() + dx;
                out.push(`${num(x)} ${num(y)} l`);
                break;
            case "V":
                if (!hasNumber()) {
                    break;
                }
                y = next() + dy;
                out.push(`${num(x)} ${num(y)} l`);
                break;
            case "C": {
                if (!hasNumber()) {
                    break;
                }
                const x1 = next() + dx;
                const y1 = next() + dy;
                const x2 = next() + dx;
                const y2 = next() + dy;
                x = next() + dx;
                y = next() + dy;
                out.push(`${num(x1)} ${num(y1)} ${num(x2)} ${num(y2)} ${num(x)} ${num(y)} c`);
                [cx, cy] = [x2, y2];
                break;
            }
            case "S": {
                if (!hasNumber()) {
                    break;
                }
                const reflect = last === "C" || last === "S";
                const x1 = reflect ? 2 * x - cx : x;
                const y1 = reflect ? 2 * y - cy : y;
                const x2 = next() + dx;
                const y2 = next() + dy;
                x = next() + dx;
                y = next() + dy;
                out.push(`${num(x1)} ${num(y1)} ${num(x2)} ${num(y2)} ${num(x)} ${num(y)} c`);
                [cx, cy] = [x2, y2];
                break;
            }
            case "Q":
            case "T": {
                if (!hasNumber()) {
                    break;
                }
                let qx: number;
                let qy: number;
                if (command.toUpperCase() === "Q") {
                    qx = next() + dx;
                    qy = next() + dy;
                } else {
                    const reflect = last === "Q" || last === "T";
                    qx = reflect ? 2 * x - cx : x;
                    qy = reflect ? 2 * y - cy : y;
                }
                const ex = next() + dx;
                const ey = next() + dy;
                // A quadratic curve is the cubic with its control points two thirds of the way to the quadratic's.
                out.push(
                    `${num(x + (2 / 3) * (qx - x))} ${num(y + (2 / 3) * (qy - y))} ${num(ex + (2 / 3) * (qx - ex))} ${num(ey + (2 / 3) * (qy - ey))} ${num(ex)} ${num(ey)} c`,
                );
                [cx, cy] = [qx, qy];
                [x, y] = [ex, ey];
                break;
            }
            case "A": {
                if (!hasNumber()) {
                    break;
                }
                const rx = next();
                const ry = next();
                const rotation = next();
                const large = next() !== 0;
                const sweep = next() !== 0;
                const ex = next() + dx;
                const ey = next() + dy;
                out.push(...arcOperators(x, y, rx, ry, rotation, large, sweep, ex, ey));
                [x, y] = [ex, ey];
                break;
            }
            case "Z":
                out.push("h");
                [x, y] = [startX, startY];
                break;
            default:
                // An unknown command: skip its numbers.
                while (hasNumber()) {
                    i++;
                }
                break;
        }
        last = command.toUpperCase();
        if (last === "Z") {
            // Z takes no numbers; a number after it starts nothing.
            command = "";
            while (hasNumber()) {
                i++;
            }
        }
    }
    return out.join("\n");
}

/** An elliptical arc as cubic curves (SVG implementation notes, F.6). */
function arcOperators(x1: number, y1: number, rx: number, ry: number, rotation: number, large: boolean, sweep: boolean, x2: number, y2: number): string[] {
    if (rx === 0 || ry === 0) {
        return [`${num(x2)} ${num(y2)} l`];
    }
    rx = Math.abs(rx);
    ry = Math.abs(ry);
    const phi = (rotation * Math.PI) / 180;
    const [cos, sin] = [Math.cos(phi), Math.sin(phi)];
    const xp = (cos * (x1 - x2)) / 2 + (sin * (y1 - y2)) / 2;
    const yp = (-sin * (x1 - x2)) / 2 + (cos * (y1 - y2)) / 2;
    const lambda = (xp * xp) / (rx * rx) + (yp * yp) / (ry * ry);
    if (lambda > 1) {
        rx *= Math.sqrt(lambda);
        ry *= Math.sqrt(lambda);
    }
    const sign = large === sweep ? -1 : 1;
    const numerator = rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp;
    const factor = sign * Math.sqrt(Math.max(0, numerator / (rx * rx * yp * yp + ry * ry * xp * xp)));
    const cxp = (factor * rx * yp) / ry;
    const cyp = (-factor * ry * xp) / rx;
    const centreX = cos * cxp - sin * cyp + (x1 + x2) / 2;
    const centreY = sin * cxp + cos * cyp + (y1 + y2) / 2;
    const angle = (ux: number, uy: number, vx: number, vy: number): number => Math.atan2(ux * vy - uy * vx, ux * vx + uy * vy);
    const start = angle(1, 0, (xp - cxp) / rx, (yp - cyp) / ry);
    let delta = angle((xp - cxp) / rx, (yp - cyp) / ry, (-xp - cxp) / rx, (-yp - cyp) / ry);
    if (!sweep && delta > 0) {
        delta -= 2 * Math.PI;
    } else if (sweep && delta < 0) {
        delta += 2 * Math.PI;
    }
    const segments = Math.max(1, Math.ceil(Math.abs(delta) / (Math.PI / 2)));
    const step = delta / segments;
    const k = (4 / 3) * Math.tan(step / 4);
    const point = (t: number): [number, number] => {
        const [ex, ey] = [rx * Math.cos(t), ry * Math.sin(t)];
        return [cos * ex - sin * ey + centreX, sin * ex + cos * ey + centreY];
    };
    const derivative = (t: number): [number, number] => {
        const [ex, ey] = [-rx * Math.sin(t), ry * Math.cos(t)];
        return [cos * ex - sin * ey, sin * ex + cos * ey];
    };
    const out: string[] = [];
    for (let s = 0; s < segments; s++) {
        const t1 = start + s * step;
        const t2 = t1 + step;
        const [p1x, p1y] = point(t1);
        const [d1x, d1y] = derivative(t1);
        const [p2x, p2y] = s === segments - 1 ? [x2, y2] : point(t2);
        const [d2x, d2y] = derivative(t2);
        out.push(`${num(p1x + k * d1x)} ${num(p1y + k * d1y)} ${num(p2x - k * d2x)} ${num(p2y - k * d2y)} ${num(p2x)} ${num(p2y)} c`);
    }
    return out;
}

/** Four cubic curves round an ellipse, the usual approximation. */
function ellipseOperators(cx: number, cy: number, rx: number, ry: number): string {
    const k = 0.5522847498;
    const [ox, oy] = [rx * k, ry * k];
    return [
        `${num(cx + rx)} ${num(cy)} m`,
        `${num(cx + rx)} ${num(cy + oy)} ${num(cx + ox)} ${num(cy + ry)} ${num(cx)} ${num(cy + ry)} c`,
        `${num(cx - ox)} ${num(cy + ry)} ${num(cx - rx)} ${num(cy + oy)} ${num(cx - rx)} ${num(cy)} c`,
        `${num(cx - rx)} ${num(cy - oy)} ${num(cx - ox)} ${num(cy - ry)} ${num(cx)} ${num(cy - ry)} c`,
        `${num(cx + ox)} ${num(cy - ry)} ${num(cx + rx)} ${num(cy - oy)} ${num(cx + rx)} ${num(cy)} c`,
        "h",
    ].join("\n");
}

function pointsOperators(points: string, close: boolean): string {
    const v = numbers(points);
    const out: string[] = [];
    for (let i = 0; i + 1 < v.length; i += 2) {
        out.push(`${num(v[i])} ${num(v[i + 1])} ${i === 0 ? "m" : "l"}`);
    }
    if (close && out.length > 0) {
        out.push("h");
    }
    return out.join("\n");
}

/** Colour as PDF RGB components from 0 to 1; null for none. */
type Rgb = [number, number, number];

const NAMED: Record<string, Rgb> = { black: [0, 0, 0], white: [1, 1, 1], red: [1, 0, 0], green: [0, 0.5, 0], blue: [0, 0, 1], gray: [0.5, 0.5, 0.5], grey: [0.5, 0.5, 0.5] };

export function parseColour(value: string | undefined, current: Rgb): Rgb | null | undefined {
    if (value === undefined || value === "inherit") {
        return undefined;
    }
    const text = value.trim().toLowerCase();
    if (text === "none" || text === "transparent") {
        return null;
    }
    if (text === "currentcolor") {
        return current;
    }
    const hex = /^#([0-9a-f]{3}|[0-9a-f]{6})$/.exec(text);
    if (hex !== null) {
        const digits = hex[1].length === 3 ? [...hex[1]].map((digit) => digit + digit).join("") : hex[1];
        return [0, 2, 4].map((at) => parseInt(digits.slice(at, at + 2), 16) / 255) as Rgb;
    }
    const rgb = /^rgba?\(([^)]*)\)$/.exec(text);
    if (rgb !== null) {
        const v = numbers(rgb[1]);
        return [(v[0] ?? 0) / 255, (v[1] ?? 0) / 255, (v[2] ?? 0) / 255];
    }
    return NAMED[text] ?? current;
}

/** What a shape inherits: the presentation attributes Verovio uses. */
interface Paint {
    colour: Rgb;
    fill: Rgb | null;
    /** Verovio's own style gives every shape a stroke in the current colour. */
    stroke: Rgb | null;
    strokeWidth: number;
    cap: string;
    join: string;
}

const CAPS: Record<string, number> = { butt: 0, round: 1, square: 2 };
const JOINS: Record<string, number> = { miter: 0, round: 1, bevel: 2 };

/** Elements that draw nothing themselves, or whose content the PDF does not take. */
const SKIPPED = new Set(["defs", "style", "title", "desc", "metadata", "text", "symbol", "clipPath", "mask", "marker", "script", "foreignObject", "pattern", "linearGradient", "radialGradient"]);

const SHAPES = new Set(["path", "polygon", "polyline", "ellipse", "circle", "rect", "line"]);

/**
 * The vector content of one SVG page as PDF operators, drawn on a page of the given size in points.
 * The page's view box fills the PDF page; text is not drawn (see textOnlySvg).
 */
export function svgToPdfOperators(svg: string | SvgNode, pageWidth: number, pageHeight: number): string {
    const document = typeof svg === "string" ? parseSvg(svg) : svg;
    const root = document.name === "svg" ? document : document.children.find((child) => child.name === "svg");
    if (root === undefined) {
        throw new Error("The SVG has no svg element.");
    }
    const [bx, by, bw, bh] = viewBoxOf(root);
    const definitions = new Map<string, SvgNode>();
    collectIds(root, definitions);

    const out: string[] = [];
    // SVG's y runs down the page, PDF's up: flip, and fit the view box to the page.
    const sx = pageWidth / bw;
    const sy = pageHeight / bh;
    out.push("q", `${num(sx)} 0 0 ${num(-sy)} ${num(-bx * sx)} ${num(pageHeight + by * sy)} cm`);
    const paint: Paint = { colour: [0, 0, 0], fill: [0, 0, 0], stroke: [0, 0, 0], strokeWidth: 1, cap: "butt", join: "miter" };
    const viewport = { width: bw, height: bh };
    for (const child of root.children) {
        draw(child, paint, viewport, definitions, out, 0);
    }
    out.push("Q");
    return out.join("\n");
}

function collectIds(node: SvgNode, into: Map<string, SvgNode>): void {
    const id = node.attrs.id;
    if (id !== undefined && !into.has(id)) {
        into.set(id, node);
    }
    for (const child of node.children) {
        collectIds(child, into);
    }
}

function inherit(node: SvgNode, paint: Paint): Paint {
    const a = node.attrs;
    const colour = parseColour(a.color, paint.colour) ?? paint.colour;
    // currentColor in an inherited fill follows this element's colour.
    const fill = parseColour(a.fill, colour);
    const stroke = parseColour(a.stroke, colour);
    const width = a["stroke-width"] === undefined ? undefined : numbers(a["stroke-width"])[0];
    return {
        colour,
        fill: fill === undefined ? (paint.fill === paint.colour ? colour : paint.fill) : fill,
        stroke: stroke === undefined ? (paint.stroke === paint.colour ? colour : paint.stroke) : stroke,
        strokeWidth: width ?? paint.strokeWidth,
        cap: a["stroke-linecap"] ?? paint.cap,
        join: a["stroke-linejoin"] ?? paint.join,
    };
}

function hidden(node: SvgNode): boolean {
    const a = node.attrs;
    return a.display === "none" || a.visibility === "hidden" || a.opacity === "0";
}

function draw(node: SvgNode, parent: Paint, viewport: { width: number; height: number }, definitions: Map<string, SvgNode>, out: string[], depth: number): void {
    if (SKIPPED.has(node.name) || hidden(node) || depth > 64) {
        return;
    }
    const paint = inherit(node, parent);
    const transform = parseTransform(node.attrs.transform);
    const a = node.attrs;

    if (node.name === "svg") {
        // A nested svg maps its view box onto its own box, which defaults to the whole viewport.
        const x = Number(a.x ?? 0) || 0;
        const y = Number(a.y ?? 0) || 0;
        const width = length(a.width, viewport.width);
        const height = length(a.height, viewport.height);
        const box = numbers(a.viewBox ?? "");
        out.push("q");
        let inner = { width, height };
        if (box.length === 4 && box[2] > 0 && box[3] > 0) {
            // preserveAspectRatio's default, xMidYMid meet.
            const scale = Math.min(width / box[2], height / box[3]);
            const offsetX = x + (width - box[2] * scale) / 2 - box[0] * scale;
            const offsetY = y + (height - box[3] * scale) / 2 - box[1] * scale;
            out.push(`${num(scale)} 0 0 ${num(scale)} ${num(offsetX)} ${num(offsetY)} cm`);
            inner = { width: box[2], height: box[3] };
        } else if (x !== 0 || y !== 0) {
            out.push(`1 0 0 1 ${num(x)} ${num(y)} cm`);
        }
        for (const child of node.children) {
            draw(child, paint, inner, definitions, out, depth + 1);
        }
        out.push("Q");
        return;
    }

    if (node.name === "use") {
        const href = a["xlink:href"] ?? a.href ?? "";
        const target = href.startsWith("#") ? definitions.get(href.slice(1)) : undefined;
        if (target === undefined || target === node) {
            return;
        }
        out.push("q");
        if (transform !== null) {
            out.push(`${transform.map(num).join(" ")} cm`);
        }
        const x = Number(a.x ?? 0) || 0;
        const y = Number(a.y ?? 0) || 0;
        if (x !== 0 || y !== 0) {
            out.push(`1 0 0 1 ${num(x)} ${num(y)} cm`);
        }
        if (target.name === "symbol") {
            for (const child of target.children) {
                draw(child, paint, viewport, definitions, out, depth + 1);
            }
        } else {
            draw(target.name === "defs" ? { ...target, name: "g" } : target, paint, viewport, definitions, out, depth + 1);
        }
        out.push("Q");
        return;
    }

    if (SHAPES.has(node.name)) {
        const geometry = shapeOperators(node, viewport);
        if (geometry === "") {
            return;
        }
        const fill = node.name === "line" ? null : paint.fill;
        const stroke = paint.strokeWidth > 0 ? paint.stroke : null;
        if (fill === null && stroke === null) {
            return;
        }
        out.push("q");
        if (transform !== null) {
            out.push(`${transform.map(num).join(" ")} cm`);
        }
        if (fill !== null) {
            out.push(`${fill.map(num).join(" ")} rg`);
        }
        if (stroke !== null) {
            out.push(`${stroke.map(num).join(" ")} RG`, `${num(paint.strokeWidth)} w`);
            if (paint.cap !== "butt") {
                out.push(`${CAPS[paint.cap] ?? 0} J`);
            }
            if (paint.join !== "miter") {
                out.push(`${JOINS[paint.join] ?? 0} j`);
            }
        }
        out.push(geometry, fill !== null && stroke !== null ? "B" : fill !== null ? "f" : "S", "Q");
        return;
    }

    // A group, or anything else that may hold shapes.
    if (node.children.length === 0) {
        return;
    }
    if (transform !== null) {
        out.push("q", `${transform.map(num).join(" ")} cm`);
    }
    for (const child of node.children) {
        draw(child, paint, viewport, definitions, out, depth + 1);
    }
    if (transform !== null) {
        out.push("Q");
    }
}

function length(value: string | undefined, whole: number): number {
    if (value === undefined || value === "" || value === "auto") {
        return whole;
    }
    const n = parseFloat(value);
    if (!Number.isFinite(n)) {
        return whole;
    }
    return value.trim().endsWith("%") ? (whole * n) / 100 : n;
}

function shapeOperators(node: SvgNode, viewport: { width: number; height: number }): string {
    const a = node.attrs;
    const value = (key: string, whole: number): number => length(a[key], whole) || 0;
    switch (node.name) {
        case "path":
            return pathOperators(a.d ?? "");
        case "polygon":
            return pointsOperators(a.points ?? "", true);
        case "polyline":
            return pointsOperators(a.points ?? "", false);
        case "line":
            return `${num(value("x1", viewport.width))} ${num(value("y1", viewport.height))} m\n${num(value("x2", viewport.width))} ${num(value("y2", viewport.height))} l`;
        case "ellipse":
        case "circle": {
            const rx = node.name === "circle" ? value("r", viewport.width) : value("rx", viewport.width);
            const ry = node.name === "circle" ? rx : value("ry", viewport.height);
            return rx > 0 && ry > 0 ? ellipseOperators(value("cx", viewport.width), value("cy", viewport.height), rx, ry) : "";
        }
        case "rect": {
            const width = value("width", viewport.width);
            const height = value("height", viewport.height);
            return width > 0 && height > 0 ? `${num(value("x", viewport.width))} ${num(value("y", viewport.height))} ${num(width)} ${num(height)} re` : "";
        }
        default:
            return "";
    }
}

/**
 * The same SVG page with only its text showing, sized in pixels, for the browser to draw as the PDF's
 * text layer: the shapes are hidden, so the text lands exactly where it is on the page.
 */
export function textOnlySvg(svg: string, widthPx: number, heightPx: number): string {
    const style = "<style>path,polygon,polyline,ellipse,circle,rect,line,use,image{display:none}text,tspan{fill:#000}</style>";
    return svg.replace(/<svg\b([^>]*)>/, (_whole, attributes: string) => {
        const kept = attributes.replace(/\s(width|height)\s*=\s*("[^"]*"|'[^']*')/g, "");
        return `<svg width="${Math.round(widthPx)}" height="${Math.round(heightPx)}"${kept}>${style}`;
    });
}

/** Whether a page has any text to draw. */
export function hasText(svg: string): boolean {
    return /<tspan\b[^>]*>[^<\s]/.test(svg) || /<text\b[^>]*>[^<\s]/.test(svg);
}
