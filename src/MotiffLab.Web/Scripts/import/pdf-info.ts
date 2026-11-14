// What the folder import reads from a PDF without drawing it (version 0.5, «Импорт папки»): its page count, the Title and
// Author of its own information, and whether it is locked with a password or is no PDF at all. Pure and small, so it runs in
// the folder worker and in Node's tests; pdf.js is only for drawing pages. Nothing here changes the file: it is only read.

/** What a PDF says about itself. Pages and the texts are null when the file does not say them where they can be read. */
export interface PdfInfo {
    pages: number | null;
    title: string | null;
    author: string | null;
    /** A password protects it: the app cannot show its pages. */
    locked: boolean;
    /** It does not start as a PDF does. */
    damaged: boolean;
}

/** How much of a large file's start and end is searched; a whole file up to WHOLE is searched. */
const EDGE = 2 * 1024 * 1024;
const WHOLE = 32 * 1024 * 1024;

/** The bytes as text one character a byte, so offsets match and nothing is decoded. */
export function latin1(bytes: Uint8Array): string {
    let text = "";
    for (let at = 0; at < bytes.length; at += 0x8000) {
        text += String.fromCharCode(...bytes.subarray(at, Math.min(bytes.length, at + 0x8000)));
    }
    return text;
}

/** The parts of the file searched: all of it when small, else its start and end. */
function searched(bytes: Uint8Array): string {
    return bytes.length <= WHOLE
        ? latin1(bytes)
        : `${latin1(bytes.subarray(0, EDGE))}\n${latin1(bytes.subarray(bytes.length - EDGE))}`;
}

/** The dictionary around an offset: from the nearest "<<" before it to its matching ">>". */
function dictionaryAround(text: string, at: number): string {
    const start = text.lastIndexOf("<<", at);
    if (start < 0) {
        return "";
    }
    let depth = 0;
    for (let index = start; index < text.length - 1; index++) {
        if (text.startsWith("<<", index)) {
            depth++;
            index++;
        } else if (text.startsWith(">>", index)) {
            depth--;
            index++;
            if (depth === 0) {
                return text.slice(start, index + 1);
            }
        }
    }
    return text.slice(start);
}

/** The largest /Count of a /Type /Pages dictionary in the text: the root of the page tree. */
export function pageCount(text: string): number | null {
    const linearized = /\/Linearized[^>]*?\/N\s+(\d+)/.exec(text.slice(0, 4096));
    if (linearized) {
        return Number(linearized[1]);
    }
    let most: number | null = null;
    for (const match of text.matchAll(/\/Count\s+(\d+)/g)) {
        const count = Number(match[1]);
        if (count > 0 && (most === null || count > most) && /\/Type\s*\/Pages\b/.test(dictionaryAround(text, match.index ?? 0))) {
            most = count;
        }
    }
    return most;
}

/** A PDF string as written in a dictionary, "(…)" with its escapes or "<…>" in hex, decoded: UTF-16 with its mark, else one byte a letter. */
export function pdfString(written: string): string {
    let bytes: number[];
    if (written.startsWith("<")) {
        const hex = written.slice(1, -1).replace(/\s+/g, "");
        bytes = [];
        for (let at = 0; at < hex.length; at += 2) {
            bytes.push(parseInt(hex.slice(at, at + 2).padEnd(2, "0"), 16));
        }
    } else {
        bytes = unescapeLiteral(written.slice(1, -1));
    }
    if (bytes[0] === 0xfe && bytes[1] === 0xff) {
        let text = "";
        for (let at = 2; at + 1 < bytes.length; at += 2) {
            text += String.fromCharCode((bytes[at] << 8) | bytes[at + 1]);
        }
        return text;
    }
    return String.fromCharCode(...bytes);
}

const ESCAPES: Record<string, number> = { n: 10, r: 13, t: 9, b: 8, f: 12, "(": 40, ")": 41, "\\": 92 };

function unescapeLiteral(text: string): number[] {
    const bytes: number[] = [];
    for (let at = 0; at < text.length; at++) {
        const letter = text[at];
        if (letter !== "\\") {
            bytes.push(letter.charCodeAt(0) & 0xff);
            continue;
        }
        const next = text[++at] ?? "";
        const octal = /^[0-7]{1,3}/.exec(text.slice(at, at + 3));
        if (octal) {
            bytes.push(parseInt(octal[0], 8) & 0xff);
            at += octal[0].length - 1;
        } else if (next in ESCAPES) {
            bytes.push(ESCAPES[next]);
        } else if (next === "\r" && text[at + 1] === "\n") {
            at++;
        }
    }
    return bytes;
}

/** The string value of a key in a dictionary's text, "(…)" with balanced parentheses or "<…>"; null when absent. */
export function stringValue(dictionary: string, key: string): string | null {
    const found = new RegExp(`/${key}\\s*([(<])`).exec(dictionary);
    if (!found || found.index === undefined) {
        return null;
    }
    const start = found.index + found[0].length - 1;
    if (found[1] === "<") {
        const end = dictionary.indexOf(">", start);
        return end < 0 ? null : pdfString(dictionary.slice(start, end + 1));
    }
    let depth = 0;
    for (let at = start; at < dictionary.length; at++) {
        const letter = dictionary[at];
        if (letter === "\\") {
            at++;
        } else if (letter === "(") {
            depth++;
        } else if (letter === ")" && --depth === 0) {
            return pdfString(dictionary.slice(start, at + 1));
        }
    }
    return null;
}

/** The trailer's /Info reference: the object number of the document's own information; null when the text has none. */
function infoReference(text: string): number | null {
    const last = [...text.matchAll(/\/Info\s+(\d+)\s+\d+\s+R/g)].at(-1);
    return last ? Number(last[1]) : null;
}

/** An object written plainly in the file, "N 0 obj …", as the text from its start; "" when it is not there. */
function plainObject(text: string, number: number): string {
    const object = new RegExp(`(?:^|[^0-9])${number}\\s+\\d+\\s+obj`).exec(text);
    return object?.index === undefined ? "" : text.slice(object.index, text.indexOf("endobj", object.index) + 1 || undefined);
}

/**
 * The objects kept in compressed object streams, by number, as text: where a PDF 1.5 file keeps its page tree and often
 * its information. Each stream lists its objects' numbers and offsets before the first one.
 */
async function compressedObjects(bytes: Uint8Array, text: string): Promise<Map<number, string>> {
    const objects = new Map<number, string>();
    for (const match of text.matchAll(/\/Type\s*\/ObjStm\b/g)) {
        const dictionary = dictionaryAround(text, match.index ?? 0);
        const begin = text.indexOf("stream", match.index ?? 0);
        const end = text.indexOf("endstream", begin);
        const first = Number(/\/First\s+(\d+)/.exec(dictionary)?.[1] ?? NaN);
        if (begin < 0 || end < 0 || !Number.isFinite(first) || !/\/FlateDecode/.test(dictionary)) {
            continue;
        }
        try {
            const start = begin + (text.startsWith("stream\r\n", begin) ? 8 : 7);
            const data = latin1(await inflate(bytes.subarray(start, streamEnd(text, dictionary, start, end))));
            const header = data.slice(0, first).trim().split(/\s+/).map(Number);
            for (let index = 0; index + 1 < header.length; index += 2) {
                const next = header[index + 3] ?? data.length - first;
                objects.set(header[index], data.slice(first + header[index + 1], first + next));
            }
        } catch {
            // A stream that does not inflate keeps its secrets; the page count may still come from elsewhere.
        }
    }
    return objects;
}

/**
 * Where a stream's data ends: its /Length when written as a number, else before the end of line that precedes "endstream",
 * which inflating would take for junk.
 */
function streamEnd(text: string, dictionary: string, start: number, endstream: number): number {
    const length = /\/Length\s+(\d+)\b(?!\s+\d+\s+R)/.exec(dictionary);
    if (length && start + Number(length[1]) <= endstream) {
        return start + Number(length[1]);
    }
    let end = endstream;
    while (end > start && (text[end - 1] === "\n" || text[end - 1] === "\r")) {
        end--;
    }
    return end;
}

async function inflate(data: Uint8Array): Promise<Uint8Array> {
    const stream = new Blob([data as BlobPart]).stream().pipeThrough(new DecompressionStream("deflate"));
    return new Uint8Array(await new Response(stream).arrayBuffer());
}

/** What the PDF says about itself: pages, Title, Author, and whether it is locked or not a PDF. */
export async function pdfInfo(bytes: Uint8Array): Promise<PdfInfo> {
    if (!latin1(bytes.subarray(0, 1024)).includes("%PDF-")) {
        return { pages: null, title: null, author: null, locked: false, damaged: true };
    }
    const text = searched(bytes);
    const locked = /\/Encrypt\s+(?:\d+\s+\d+\s+R|<<)/.test(text);
    const reference = infoReference(text);
    let pages = pageCount(text);
    let info = reference === null ? "" : plainObject(text, reference);
    if ((pages === null || (reference !== null && info === "")) && !locked && bytes.length <= WHOLE) {
        const objects = await compressedObjects(bytes, text);
        pages ??= pageCount([...objects.values()].join("\n"));
        info ||= reference === null ? "" : (objects.get(reference) ?? "");
    }
    const dictionary = info === "" ? "" : dictionaryAround(info, info.indexOf("<<") + 2);
    const clean = (value: string | null): string | null => value?.replaceAll("\u0000", "").trim() || null;
    return { pages, title: clean(stringValue(dictionary, "Title")), author: clean(stringValue(dictionary, "Author")), locked, damaged: false };
}
