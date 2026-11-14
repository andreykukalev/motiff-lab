// The folder import's reading of a file (version 0.5, «Импорт папки»): a PDF's pages, Title and Author, a lock and a file
// that is no PDF, read without drawing; and the worker's fingerprint. Run in Node on the compiled modules:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { test } from "node:test";
import { deflateSync } from "node:zlib";

import { pageCount, pdfInfo, pdfString, stringValue } from "../../src/MotiffLab.Web/wwwroot/js/import/pdf-info.js";
import { readDetails } from "../../src/MotiffLab.Web/wwwroot/js/import/folder-worker.js";

const bytes = (text) => Uint8Array.from(text, (letter) => letter.charCodeAt(0) & 0xff);

/** A small PDF written plainly: a page tree of `pages` pages and an information dictionary. */
function plainPdf(pages, info, extra = "") {
    const kids = Array.from({ length: pages }, (_, index) => `${10 + index} 0 R`).join(" ");
    return bytes([
        "%PDF-1.4",
        "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
        `2 0 obj << /Type /Pages /Kids [${kids}] /Count ${pages} >> endobj`,
        ...Array.from({ length: pages }, (_, index) => `${10 + index} 0 obj << /Type /Page /Parent 2 0 R >> endobj`),
        `3 0 obj ${info} endobj`,
        `trailer << /Root 1 0 R /Info 3 0 R ${extra}>>`,
        "%%EOF",
    ].join("\n"));
}

test("a plain PDF gives its pages, Title and Author", async () => {
    const info = await pdfInfo(plainPdf(3, "<< /Title (Arabesque No. 1) /Author (Claude Debussy) >>"));
    assert.deepEqual(info, { pages: 3, title: "Arabesque No. 1", author: "Claude Debussy", locked: false, damaged: false });
});

test("a nested page tree counts its root, not a branch", () => {
    const text = "<< /Type /Pages /Kids [4 0 R] /Count 12 >>\n<< /Type /Pages /Parent 2 0 R /Count 5 >>\n<< /Type /Font /Count 40 >>";
    assert.equal(pageCount(text), 12);
});

test("a linearized PDF gives the page count it states first", () => {
    assert.equal(pageCount("%PDF-1.7\n1 0 obj << /Linearized 1 /L 4000 /N 7 /T 3000 >> endobj"), 7);
});

test("PDF strings decode their escapes, octal codes and UTF-16 in hex", () => {
    assert.equal(pdfString("(Ballade \\(No. 1\\)\\051)"), "Ballade (No. 1))");
    assert.equal(pdfString("(Caf\\351)"), "Café");
    assert.equal(pdfString("<FEFF0428043E043F0435043D>"), "Шопен");
    assert.equal(stringValue("<< /Title (a (nested) title) /Author <FEFF0042> >>", "Title"), "a (nested) title");
    assert.equal(stringValue("<< /Title (x) >>", "Author"), null);
});

test("a PDF with a password is locked; a file that is no PDF is damaged", async () => {
    const locked = await pdfInfo(plainPdf(2, "<< /Title (Etudes) >>", "/Encrypt 9 0 R "));
    assert.equal(locked.locked, true);
    const damaged = await pdfInfo(bytes("Not a PDF at all"));
    assert.deepEqual(damaged, { pages: null, title: null, author: null, locked: false, damaged: true });
});

for (const length of ["direct", "indirect"]) test(`a PDF 1.5 keeps its page tree and information in a compressed object stream (${length} /Length)`, async () => {
    const objects = ["<< /Type /Pages /Kids [] /Count 9 >>", "<< /Title <FEFF0411043004450020> /Author (J. S. Bach) >>"];
    const first = "2 0 3 40 ";
    const body = `${first}${objects[0].padEnd(40)}${objects[1]}`;
    const stream = deflateSync(Buffer.from(body, "latin1"));
    const head = bytes([
        "%PDF-1.5",
        `5 0 obj << /Type /ObjStm /N 2 /First ${first.length} /Filter /FlateDecode /Length ${length === "direct" ? stream.length : "12 0 R"} >>`,
        "stream\n",
    ].join("\n"));
    const tail = bytes("\nendstream\nendobj\n6 0 obj << /Type /XRef /Root 1 0 R /Info 3 0 R >> endobj\n%%EOF");
    const file = new Uint8Array(head.length + stream.length + tail.length);
    file.set(head, 0);
    file.set(stream, head.length);
    file.set(tail, head.length + stream.length);
    const info = await pdfInfo(file);
    assert.equal(info.pages, 9);
    assert.equal(info.title, "Бах");
    assert.equal(info.author, "J. S. Bach");
});

test("the worker answers a file's SHA-256 and a PDF's details, and only reads it", async () => {
    const data = plainPdf(1, "<< /Title (Gymnopédie) >>");
    const file = new File([data], "Gymnopedie.pdf", { type: "application/pdf" });
    const read = await readDetails({ id: 4, file, pdf: true });
    assert.equal(read.id, 4);
    assert.equal(read.sha256, createHash("sha256").update(data).digest("hex"));
    assert.equal(read.info.pages, 1);
    assert.equal(read.unreadable, false);
    const midi = await readDetails({ id: 5, file: new File([bytes("MThd")], "a.mid"), pdf: false });
    assert.equal(midi.info, null);
});

test("a file that cannot be read is unreadable, not a failure", async () => {
    const gone = { size: 10, arrayBuffer: () => Promise.reject(new Error("NotReadableError")) };
    const read = await readDetails({ id: 6, file: gone, pdf: true });
    assert.deepEqual(read, { id: 6, sha256: null, info: null, unreadable: true });
});
