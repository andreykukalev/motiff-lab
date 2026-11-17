// Tests for the score's A4 pages (decision record 0017) and the SVG parser that reads them (svg-parse.ts), run in Node
// on the compiled modules and the vendored Verovio build:
//   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";
import { num, parseSvg, parseTransform } from "../../src/shared/wwwroot/js/score/svg-parse.js";
import { A4_LAYOUT, A4_PDF_LAYOUT, EVEN_SPACING, NOTE_SPACING, pdfFileName, withTitle } from "../../src/shared/wwwroot/js/score/score-page.js";
import createVerovioModule from "../../src/shared/wwwroot/lib/verovio/verovio-module.mjs";
import { VerovioToolkit } from "../../src/shared/wwwroot/lib/verovio/verovio.mjs";

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

test("transforms compose into one matrix", () => {
    assert.equal(parseTransform(undefined), null);
    assert.deepEqual(parseTransform("translate(10, 20) scale(0.5)"), [0.5, 0, 0, 0.5, 10, 20]);
    assert.deepEqual(parseTransform("scale(1,-1)"), [1, 0, 0, -1, 0, 0]);
    const [a, b, c, d] = parseTransform("rotate(90)");
    assert.deepEqual([a, b, c, d].map(num), ["0", "1", "-1", "0"]);
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

test("a page's systems spread down to a bottom margin as deep as the top one, one more system to a page (#521 section 14)", async () => {
    const bars = Array.from({ length: 100 }, (_, bar) =>
        `<measure n="${bar + 1}"><staff n="1"><layer n="1"><note pname="c" oct="5" dur="4"/><note pname="e" oct="5" dur="4"/><note pname="g" oct="5" dur="2"/></layer></staff><staff n="2"><layer n="1"><chord dur="1"><note pname="c" oct="3"/><note pname="g" oct="3"/></chord></layer></staff></measure>`,
    ).join("");
    const mei = `<?xml version="1.0" encoding="UTF-8"?><mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.1"><meiHead><fileDesc><titleStmt><title>Santiago</title></titleStmt><pubStmt/></fileDesc></meiHead><music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp symbol="brace" bar.thru="true"><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef><section>${bars}</section></score></mdiv></body></music></mei>`;
    const engrave = async (options) => {
        const engraver = new VerovioToolkit(await createVerovioModule());
        engraver.setOptions({ ...options });
        assert.ok(engraver.loadData(mei));
        return Array.from({ length: engraver.getPageCount() }, (_, index) => engraver.renderToSVG(index + 1));
    };
    // In the page's own units (tenths of a millimetre, ten to a unit of the SVG's inner view box): where each system's
    // top staff line starts, and the lowest staff line of the page.
    const systems = (svg) => {
        const top = Number(svg.match(/class="page-margin" transform="translate\(\d+, (\d+)\)"/)[1]);
        const parts = svg.split('class="system"').slice(1);
        const tops = parts.map((part) => (top + Number(part.match(/<path d="M\d+ (\d+)/)[1])) / 10);
        // Staff lines are horizontal paths: the same height at both ends.
        const lowest = Math.max(...[...svg.matchAll(/<path d="M\d+ (\d+) L\d+ \1"/g)].map((line) => (top + Number(line[1])) / 10));
        return { tops, lowest };
    };
    const pages = await engrave(A4_LAYOUT);
    assert.ok(pages.length >= 3, `100 bars of two staves fill pages (${pages.length})`);
    const full = systems(pages[1]);
    // The systems are spread evenly down the page...
    const gaps = full.tops.slice(1).map((at, index) => at - full.tops[index]);
    assert.ok(Math.max(...gaps) - Math.min(...gaps) <= 2, `even gaps ${gaps}`);
    // ...to the bottom margin, as deep as the top one (20 mm): the bottom of the page is not left empty.
    const bottom = 2970 - full.lowest;
    assert.ok(bottom >= 190 && bottom <= 260, `the page ends ${bottom} below its lowest staff line`);
    // The last page's systems, fewer, keep about the same distance: they are not spread over the whole page.
    const last = systems(pages[pages.length - 1]);
    const lastGaps = last.tops.slice(1).map((at, index) => at - last.tops[index]);
    assert.ok(last.tops.length < full.tops.length);
    assert.ok(lastGaps.every((gap) => gap <= Math.min(...gaps) * 1.1), `the last page's gaps ${lastGaps}, a full page's ${gaps}`);
    // The page the iPad had (20 mm at the top, 46 mm at the bottom, systems at least 20 units apart, not spread) held fewer.
    const before = await engrave({ ...A4_LAYOUT, pageMarginBottom: 460, spacingSystem: 20, justifyVertically: false, bottomMarginHeader: 2 });
    assert.ok(systems(before[1]).tops.length < full.tops.length, `${systems(before[1]).tops.length} systems a page before, ${full.tops.length} now`);
    // The PDF is the screen's page, so a page named on screen is the same page on paper.
    assert.equal(A4_PDF_LAYOUT, A4_LAYOUT);
});

test("the iPad app engraves its A4 pages with the web app's Verovio options", async () => {
    const { readFile } = await import("node:fs/promises");
    const swift = await readFile(new URL("../../src/ipad/App/MotiffLab/Engraver.swift", import.meta.url), "utf8");
    const literal = swift.match(/static let a4Leland = """\n([\s\S]*?)\n\s*"""/);
    assert.ok(literal, "Engraver.a4Leland is a multi-line string literal");
    // The iPad, the web's screen and the PDF lay a piece out on the same pages (#521 section 14).
    assert.deepEqual(JSON.parse(literal[1]), { ...A4_LAYOUT });
    const even = swift.match(/static let evenSpacing = """\n([\s\S]*?)\n\s*"""/);
    assert.ok(even, "Engraver.evenSpacing is a multi-line string literal");
    assert.deepEqual(JSON.parse(even[1]), { ...EVEN_SPACING });
    const byNotes = swift.match(/static let noteSpacing = """\n([\s\S]*?)\n\s*"""/);
    assert.ok(byNotes, "Engraver.noteSpacing is a multi-line string literal");
    assert.deepEqual(JSON.parse(byNotes[1]), { ...NOTE_SPACING });
});

test("with even bars, a bar of long notes is about as wide as a bar of runs, so the playhead keeps one pace (testing item 35)", async () => {
    const engraver = new VerovioToolkit(await createVerovioModule());
    const run = Array.from({ length: 16 }, (_, index) => `<note pname="${"cdefgab"[index % 7]}" oct="5" dur="16"/>`).join("");
    const bars = Array.from({ length: 24 }, (_, bar) =>
        `<measure n="${bar + 1}"><staff n="1"><layer n="1">${bar % 2 === 0 ? `<note pname="c" oct="5" dur="1"/>` : run}</layer></staff><staff n="2"><layer n="1"><note pname="c" oct="3" dur="1"/></layer></staff></measure>`,
    ).join("");
    const mei = `<?xml version="1.0" encoding="UTF-8"?><mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.1"><music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp symbol="brace" bar.thru="true"><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef><section>${bars}</section></score></mdiv></body></music></mei>`;
    const widest = (options) => {
        engraver.setOptions({ ...options });
        assert.ok(engraver.loadData(mei));
        let worst = 1;
        for (let page = 1; page <= engraver.getPageCount(); page++) {
            for (const system of engraver.renderToSVG(page).split('class="system"').slice(1)) {
                // A bar's width from its top staff line; the first bar of a system also holds the clef and is left out.
                const widths = system.split('class="measure"').slice(2)
                    .map((measure) => measure.match(/<path d="M(\d+) \d+ L(\d+) \d+"/))
                    .filter((line) => line !== null)
                    .map((line) => Number(line[2]) - Number(line[1]));
                if (widths.length > 1) {
                    worst = Math.max(worst, Math.max(...widths) / Math.min(...widths));
                }
            }
        }
        return worst;
    };
    const before = widest({ ...A4_LAYOUT, ...NOTE_SPACING });
    const now = widest({ ...A4_LAYOUT, ...EVEN_SPACING });
    // The toolkit keeps the options it was last set: Verovio's own spacing given outright brings its layout back.
    assert.equal(widest({ ...A4_LAYOUT, ...NOTE_SPACING }), before);
    assert.ok(before > 1.8, `Verovio's own spacing makes the bars of runs much wider (${before.toFixed(2)})`);
    assert.ok(now < 1.3, `the bars are nearly as wide as each other (${now.toFixed(2)})`);
});
