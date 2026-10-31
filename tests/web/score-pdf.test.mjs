// Tests for the score's A4 pages and PDF export (decision record 0017), run in Node on the compiled
// modules and the vendored Verovio build:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";
import { inflateSync } from "node:zlib";

import { hasText, num, parseColour, parseSvg, parseTransform, pathOperators, svgToPdfOperators, textOnlySvg } from "../../src/MotiffLab.Web/wwwroot/js/score/svg-to-pdf.js";
import { A4_HEIGHT, A4_WIDTH, buildPdf, crop, inkBounds, pdfText } from "../../src/MotiffLab.Web/wwwroot/js/score/pdf-document.js";
import { A4_LAYOUT, pdfFileName, withTitle } from "../../src/MotiffLab.Web/wwwroot/js/score/score-page.js";
import createVerovioModule from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/MotiffLab.Web/wwwroot/lib/verovio/verovio.mjs";

test("the SVG parser keeps elements and attributes, decodes entities and skips style text", () => {
    const document = parseSvg(`<?xml version="1.0"?><!-- a comment --><svg viewBox="0 0 10 20"><style>g > path { fill: red }</style><g id='a' class="x &amp; y"><path d="M0 0"/></g></svg>`);
    const svg = document.children[0];
    assert.equal(svg.name, "svg");
    assert.equal(svg.attrs.viewBox, "0 0 10 20");
    assert.deepEqual(svg.children.map((child) => child.name), ["style", "g"]);
    assert.equal(svg.children[0].children.length, 0);
    assert.equal(svg.children[1].attrs.class, "x & y");
    assert.equal(svg.children[1].children[0].attrs.d, "M0 0");
});

test("path data becomes absolute PDF path operators", () => {
    assert.equal(pathOperators("M10 20 L30 40"), "10 20 m\n30 40 l");
    // Pairs after a move are lines; relative commands add to the current point; Z returns to the start.
    assert.equal(pathOperators("m10 20 5 5 h10 v-5 z l1 1"), "10 20 m\n15 25 l\n25 25 l\n25 20 l\nh\n11 21 l");
    assert.equal(pathOperators("M0 0 c1 2 3 4 5 6 s1 1 2 2"), "0 0 m\n1 2 3 4 5 6 c\n7 8 6 7 7 8 c");
    // A quadratic curve becomes the cubic two thirds of the way to its control point.
    assert.equal(pathOperators("M0 0 Q3 3 6 0"), "0 0 m\n2 2 4 2 6 0 c");
    assert.equal(pathOperators("M1-2.5.5.5"), "1 -2.5 m\n0.5 0.5 l");
});

test("an arc becomes cubic curves that end where the arc does", () => {
    const operators = pathOperators("M0 0 A10 10 0 0 1 20 0").split("\n");
    assert.equal(operators[0], "0 0 m");
    assert.ok(operators.length >= 3);
    assert.match(operators.at(-1), / 20 0 c$/);
});

test("transforms compose into one matrix", () => {
    assert.equal(parseTransform(undefined), null);
    assert.deepEqual(parseTransform("translate(10, 20) scale(0.5)"), [0.5, 0, 0, 0.5, 10, 20]);
    assert.deepEqual(parseTransform("scale(1,-1)"), [1, 0, 0, -1, 0, 0]);
    const [a, b, c, d] = parseTransform("rotate(90)");
    assert.deepEqual([a, b, c, d].map(num), ["0", "1", "-1", "0"]);
});

test("colours read as PDF RGB", () => {
    assert.deepEqual(parseColour("#000", [1, 1, 1]), [0, 0, 0]);
    assert.deepEqual(parseColour("#ff0000", [0, 0, 0]), [1, 0, 0]);
    assert.deepEqual(parseColour("currentColor", [0, 0, 1]), [0, 0, 1]);
    assert.equal(parseColour("none", [0, 0, 0]), null);
    assert.equal(parseColour(undefined, [0, 0, 0]), undefined);
});

test("a page maps its view box onto the PDF page, with y up, and draws glyphs placed with use", () => {
    const svg = `<svg viewBox="0 0 100 200"><defs><g id="dot"><path d="M0 0 L1 0 L1 1 Z"/></g></defs>
        <svg class="definition-scale" viewBox="0 0 1000 2000"><g transform="translate(10, 20)">
            <use xlink:href="#dot" transform="translate(5, 5) scale(2, 2)"/>
            <polyline points="0,0 10,10" fill="none" stroke-width="3"/>
            <text x="1" y="2">Title</text>
        </g></svg></svg>`;
    const operators = svgToPdfOperators(svg, 50, 100).split("\n");
    // 100 units across a 50 pt page, flipped.
    assert.equal(operators[0], "q");
    assert.equal(operators[1], "0.5 0 0 -0.5 0 100 cm");
    // The nested svg scales its 1000 units to the 100 of the page.
    assert.ok(operators.includes("0.1 0 0 0.1 0 0 cm"));
    assert.ok(operators.includes("1 0 0 1 10 20 cm"));
    assert.ok(operators.includes("2 0 0 2 5 5 cm"));
    // The glyph is filled (and given Verovio's hairline stroke); the polyline with no fill is only stroked.
    assert.ok(operators.includes("0 0 m") && operators.includes("B"));
    assert.ok(operators.includes("3 w") && operators.includes("S"));
    // Text is left to the browser's layer.
    assert.ok(!operators.some((operator) => operator.includes("Title")));
    assert.equal(operators.filter((operator) => operator === "q").length, operators.filter((operator) => operator === "Q").length);
});

test("the text layer hides the shapes and takes the size in pixels", () => {
    const svg = `<svg viewBox="0 0 2100 2970" width="10" version="1.1"><path d="M0 0"/><text><tspan>Title</tspan></text></svg>`;
    const layer = textOnlySvg(svg, 2480, 3508);
    assert.match(layer, /^<svg width="2480" height="3508" viewBox="0 0 2100 2970" version="1.1"><style>/);
    assert.match(layer, /path,polygon,polyline,ellipse,circle,rect,line,use,image\{display:none\}/);
    assert.ok(hasText(svg));
    assert.ok(!hasText(`<svg><text font-size="0px"><tspan class="text"> </tspan></text></svg>`));
});

test("ink bounds and crop keep only the part of a layer with ink", () => {
    const coverage = new Uint8Array(5 * 4);
    coverage[1 * 5 + 2] = 200;
    coverage[2 * 5 + 3] = 50;
    const box = inkBounds(coverage, 5, 4);
    assert.deepEqual(box, { left: 2, top: 1, right: 4, bottom: 3 });
    assert.deepEqual([...crop(coverage, 5, box)], [200, 0, 0, 50]);
    assert.equal(inkBounds(new Uint8Array(6), 3, 2), null);
});

test("text strings in the PDF are UTF-16 with a byte order mark", () => {
    assert.equal(pdfText("Ф"), "<FEFF0424>");
});

test("a page's title and the PDF's file name", () => {
    const untitled = `<?xml version="1.0"?><score-partwise version="4.0"><part-list/></score-partwise>`;
    assert.equal(withTitle(untitled, "Зимние истории & co"), `<?xml version="1.0"?><score-partwise version="4.0"><movement-title>Зимние истории &amp; co</movement-title><part-list/></score-partwise>`);
    const titled = `<score-partwise><work><work-title>Op. 1</work-title></work></score-partwise>`;
    assert.equal(withTitle(titled, "Other"), titled);
    assert.equal(withTitle(`<mei><meiHead/></mei>`, "Other"), `<mei><meiHead/></mei>`);
    assert.equal(withTitle(untitled, "  "), untitled);
    assert.equal(pdfFileName("Für Elise: a/b?"), "Für Elise- a-b-.pdf");
    assert.equal(pdfFileName(null), "score.pdf");
});

/** The objects of a PDF by number, checking the cross-reference table points at each. */
function objectsOf(pdf) {
    const text = Buffer.from(pdf).toString("latin1");
    const start = Number(/startxref\n(\d+)\n%%EOF\n$/.exec(text)[1]);
    assert.ok(text.startsWith("xref\n", start));
    const [, count] = /^xref\n0 (\d+)\n/.exec(text.slice(start));
    const entries = text.slice(start).split("\n").slice(3, 2 + Number(count));
    const objects = new Map();
    entries.forEach((entry, index) => {
        const offset = Number(entry.slice(0, 10));
        assert.ok(text.startsWith(`${index + 1} 0 obj\n`, offset), `object ${index + 1} is where the table says`);
        objects.set(index + 1, offset);
    });
    return { text, objects };
}

function streamOf(pdf, text, offset) {
    const begin = text.indexOf("stream\n", offset) + "stream\n".length;
    const length = Number(/\/Length (\d+)/.exec(text.slice(offset, begin))[1]);
    return Buffer.from(pdf).subarray(begin, begin + length);
}

test("a PDF of A4 pages with an ink layer is well formed", async () => {
    const coverage = new Uint8Array([0, 128, 255, 64]);
    const pdf = await buildPdf(
        [{ operators: "0 0 m 10 10 l S" }, { operators: "", ink: { width: 2, height: 2, coverage, x: 10, y: 20, widthPt: 30, heightPt: 40 } }],
        { title: "Für Elise" },
    );
    const { text, objects } = objectsOf(pdf);
    assert.ok(text.startsWith("%PDF-1.4\n"));
    assert.match(text, /\/Type \/Pages \/Kids \[\d+ 0 R \d+ 0 R\] \/Count 2/);
    assert.equal((text.match(/\/MediaBox \[0 0 595\.276 841\.89\]/g) ?? []).length, 2);
    assert.match(text, /\/Title <FEFF004600FC0072/);
    assert.match(text, /\/XObject << \/Ink \d+ 0 R >>/);
    assert.match(text, /\/SMask \d+ 0 R/);
    // Streams are compressed, and the page draws the layer where it belongs.
    const head = (offset) => {
        const stream = text.indexOf("stream\n", offset);
        const end = text.indexOf("\nendobj", offset);
        return text.slice(offset, stream >= 0 && stream < end ? stream : end);
    };
    const offsets = [...objects.values()];
    const contents = offsets.filter((offset) => head(offset).includes("/FlateDecode") && !head(offset).includes("/Image"));
    assert.equal(contents.length, 2);
    assert.equal(inflateSync(streamOf(pdf, text, contents[0])).toString("latin1"), "0 0 m 10 10 l S");
    assert.equal(inflateSync(streamOf(pdf, text, contents[1])).toString("latin1"), "\nq 30 0 0 40 10 20 cm /Ink Do Q");
    const mask = offsets.find((offset) => head(offset).includes("/Subtype /Image") && !head(offset).includes("/SMask"));
    assert.deepEqual([...inflateSync(streamOf(pdf, text, mask))], [0, 128, 255, 64]);
});

test("Verovio's A4 pages become PDF pages of vector notation", async () => {
    const engraver = new VerovioToolkit(await createVerovioModule());
    const bars = Array.from({ length: 40 }, (_, bar) =>
        `<measure n="${bar + 1}"><staff n="1"><layer n="1"><note xml:id="n_${bar}" pname="c" oct="5" dur="4"/><note pname="e" oct="5" dur="8"/><note pname="g" oct="5" dur="8"/><beam><note pname="a" oct="5" dur="16"/><note pname="b" oct="5" dur="16"/><note pname="c" oct="6" dur="16"/><note pname="d" oct="6" dur="16"/></beam><rest dur="4"/></layer></staff><staff n="2"><layer n="1"><chord dur="1"><note pname="c" oct="3"/><note pname="g" oct="3"/></chord></layer></staff></measure>`,
    ).join("");
    const mei = `<?xml version="1.0" encoding="UTF-8"?><mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.1"><meiHead><fileDesc><titleStmt><title>Зимние истории</title></titleStmt><pubStmt/></fileDesc></meiHead><music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp symbol="brace" bar.thru="true"><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef><section>${bars}</section></score></mdiv></body></music></mei>`;
    engraver.setOptions({ ...A4_LAYOUT });
    assert.ok(engraver.loadData(mei));
    const count = engraver.getPageCount();
    assert.ok(count >= 2, `40 bars of two staves fill more than a page (${count})`);
    const svgs = Array.from({ length: count }, (_, index) => engraver.renderToSVG(index + 1));
    // The title heads the first page, the page number the next.
    assert.match(svgs[0], /^<svg viewBox="0 0 2100 2970"/);
    assert.match(svgs[0], /class="pgHead[^"]*"[\s\S]*Зимние истории/);
    assert.match(svgs[1], /class="pgHead[^"]*"[\s\S]*<title class="labelAttr">page<\/title>[\s\S]*>2<\/tspan>/);
    assert.ok(svgs.every(hasText));

    const pages = svgs.map((svg) => ({ operators: svgToPdfOperators(svg, A4_WIDTH, A4_HEIGHT) }));
    for (const page of pages) {
        assert.ok(!/NaN|Infinity|undefined/.test(page.operators));
        // Staff lines are stroked, noteheads filled.
        assert.match(page.operators, /\n13 w\n/);
        assert.ok(page.operators.split("\n").filter((operator) => operator === "B").length > 100);
    }
    const pdf = await buildPdf(pages, { title: "Зимние истории" });
    const { text } = objectsOf(pdf);
    assert.match(text, new RegExp(`/Count ${count} `));
});
