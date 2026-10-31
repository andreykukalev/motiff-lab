// A small PDF writer for the score's PDF export (decision record 0017): A4 pages of vector drawing
// operators, each with an optional image over it (the page's text, drawn by the browser) that is
// black ink through a soft mask of the text's coverage. Streams are compressed with the platform's
// CompressionStream where there is one, and stored as they are where not. Kept free of the DOM so
// Node can test it.

import { num } from "./svg-to-pdf.js";

/** A4 in points: 210 × 297 mm. */
export const A4_WIDTH = (210 / 25.4) * 72;
export const A4_HEIGHT = (297 / 25.4) * 72;

/** Coverage of the page's ink, one byte a pixel, row by row from the top: 0 none, 255 full. */
export interface InkLayer {
    width: number;
    height: number;
    coverage: Uint8Array;
    /** Where the layer sits on the page, in points from the bottom left. */
    x: number;
    y: number;
    widthPt: number;
    heightPt: number;
}

export interface PdfPage {
    /** Drawing operators in points, origin bottom left. */
    operators: string;
    ink?: InkLayer | null;
}

export interface PdfInfo {
    title?: string;
    creator?: string;
}

const encoder = new TextEncoder();

function latin1(text: string): Uint8Array {
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i++) {
        bytes[i] = text.charCodeAt(i) & 0xff;
    }
    return bytes;
}

/** A text string for the PDF: UTF-16BE with its byte order mark, as hex, so any script survives. */
export function pdfText(text: string): string {
    let hex = "FEFF";
    for (let i = 0; i < text.length; i++) {
        hex += text.charCodeAt(i).toString(16).padStart(4, "0").toUpperCase();
    }
    return `<${hex}>`;
}

/** zlib-compressed bytes, or null where the platform cannot compress. */
export async function deflate(data: Uint8Array): Promise<Uint8Array | null> {
    if (typeof CompressionStream === "undefined") {
        return null;
    }
    try {
        const stream = new Blob([data as BlobPart]).stream().pipeThrough(new CompressionStream("deflate"));
        return new Uint8Array(await new Response(stream).arrayBuffer());
    } catch {
        return null;
    }
}

/** Builds a PDF file from A4 pages. */
export async function buildPdf(pages: readonly PdfPage[], info: PdfInfo = {}): Promise<Uint8Array> {
    const objects: (Uint8Array | null)[] = [];
    const reserve = (): number => {
        objects.push(null);
        return objects.length;
    };
    const set = (id: number, body: Uint8Array | string): void => {
        objects[id - 1] = typeof body === "string" ? latin1(body) : body;
    };
    const stream = async (id: number, dictionary: string, data: Uint8Array): Promise<void> => {
        const packed = await deflate(data);
        const content = packed ?? data;
        const filter = packed === null ? "" : " /Filter /FlateDecode";
        set(id, concat([latin1(`<< ${dictionary}${filter} /Length ${content.length} >>\nstream\n`), content, latin1("\nendstream")]));
    };

    const catalog = reserve();
    const tree = reserve();
    const infoId = reserve();
    const kids: number[] = [];
    for (const page of pages) {
        const pageId = reserve();
        const contentsId = reserve();
        kids.push(pageId);
        let resources = "<< >>";
        let operators = page.operators;
        const ink = page.ink;
        if (ink && ink.width > 0 && ink.height > 0) {
            const imageId = reserve();
            const maskId = reserve();
            const pixels = `/Width ${ink.width} /Height ${ink.height} /ColorSpace /DeviceGray /BitsPerComponent 8`;
            await stream(maskId, `/Type /XObject /Subtype /Image ${pixels}`, ink.coverage);
            // The ink is black; the mask says where and how much.
            await stream(imageId, `/Type /XObject /Subtype /Image ${pixels} /SMask ${maskId} 0 R`, new Uint8Array(ink.width * ink.height));
            resources = `<< /XObject << /Ink ${imageId} 0 R >> >>`;
            operators += `\nq ${num(ink.widthPt)} 0 0 ${num(ink.heightPt)} ${num(ink.x)} ${num(ink.y)} cm /Ink Do Q`;
        }
        await stream(contentsId, "", encoder.encode(operators));
        set(pageId, `<< /Type /Page /Parent ${tree} 0 R /MediaBox [0 0 ${num(A4_WIDTH)} ${num(A4_HEIGHT)}] /Resources ${resources} /Contents ${contentsId} 0 R >>`);
    }
    set(catalog, `<< /Type /Catalog /Pages ${tree} 0 R >>`);
    set(tree, `<< /Type /Pages /Kids [${kids.map((kid) => `${kid} 0 R`).join(" ")}] /Count ${kids.length} >>`);
    const fields = [`/Producer ${pdfText("Motiff Lab")}`];
    if (info.title) {
        fields.push(`/Title ${pdfText(info.title)}`);
    }
    if (info.creator) {
        fields.push(`/Creator ${pdfText(info.creator)}`);
    }
    set(infoId, `<< ${fields.join(" ")} >>`);

    // Header, the objects, the cross-reference table and the trailer.
    const chunks: Uint8Array[] = [latin1("%PDF-1.4\n%âãÏÓ\n")];
    let offset = chunks[0].length;
    const offsets: number[] = [];
    objects.forEach((body, index) => {
        const object = concat([latin1(`${index + 1} 0 obj\n`), body ?? latin1("null"), latin1("\nendobj\n")]);
        offsets.push(offset);
        chunks.push(object);
        offset += object.length;
    });
    const xref = [`xref\n0 ${objects.length + 1}\n`, "0000000000 65535 f \n", ...offsets.map((at) => `${String(at).padStart(10, "0")} 00000 n \n`)].join("");
    chunks.push(latin1(`${xref}trailer\n<< /Size ${objects.length + 1} /Root ${catalog} 0 R /Info ${infoId} 0 R >>\nstartxref\n${offset}\n%%EOF\n`));
    return concat(chunks);
}

function concat(parts: readonly Uint8Array[]): Uint8Array {
    const total = parts.reduce((sum, part) => sum + part.length, 0);
    const out = new Uint8Array(total);
    let at = 0;
    for (const part of parts) {
        out.set(part, at);
        at += part.length;
    }
    return out;
}

/**
 * The smallest box holding all the ink in a layer of coverage, so a page's text image covers only
 * the part of the page with text; null when there is none.
 */
export function inkBounds(coverage: Uint8Array, width: number, height: number): { left: number; top: number; right: number; bottom: number } | null {
    let [left, top, right, bottom] = [width, height, -1, -1];
    for (let y = 0; y < height; y++) {
        const row = y * width;
        for (let x = 0; x < width; x++) {
            if (coverage[row + x] !== 0) {
                left = Math.min(left, x);
                right = Math.max(right, x);
                top = Math.min(top, y);
                bottom = y;
            }
        }
    }
    return right < 0 ? null : { left, top, right: right + 1, bottom: bottom + 1 };
}

/** A box cut from a layer of coverage. */
export function crop(coverage: Uint8Array, width: number, box: { left: number; top: number; right: number; bottom: number }): Uint8Array {
    const w = box.right - box.left;
    const out = new Uint8Array(w * (box.bottom - box.top));
    for (let y = box.top; y < box.bottom; y++) {
        out.set(coverage.subarray(y * width + box.left, y * width + box.right), (y - box.top) * w);
    }
    return out;
}
