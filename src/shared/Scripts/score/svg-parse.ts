// Reads a page of Verovio's SVG into elements, view boxes and transform matrices: score-geometry.ts reads the score's
// pages with it. Well-formed XML only; text, comments and declarations are dropped. Kept free of the DOM so Node can
// test it.

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

export function numbers(text: string): number[] {
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
