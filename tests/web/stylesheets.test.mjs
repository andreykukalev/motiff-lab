// The app's stylesheets close every brace they open. One rule left open (2026-11-01) made browsers with CSS nesting read
// the rest of app.css as nested inside it, so the header, the start tiles and Settings lost their layout.
//   node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../", import.meta.url);
const sheets = ["src/MotiffLab.Web/wwwroot/css/app.css", "design/kit/tokens.css", "design/kit/ds.css"];

for (const sheet of sheets) {
    test(`${sheet} closes every brace it opens`, () => {
        const text = readFileSync(new URL(sheet, root), "utf8")
            .replace(/\/\*[\s\S]*?\*\//g, "")
            .replace(/"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)*'/g, '""');
        let depth = 0;
        let line = 1;
        const open = [];
        for (const ch of text) {
            if (ch === "\n") line++;
            else if (ch === "{") { depth++; open.push(line); }
            else if (ch === "}") { depth--; open.pop(); assert.ok(depth >= 0, `an extra } on line ${line} (comments removed)`); }
        }
        assert.equal(depth, 0, `a { left open, opened on line ${open.at(-1)} (comments removed)`);
    });
}

// A :has() asked of the body or the root with a descendant in it walks the whole document each time the page changes until
// it finds one, and the whole of it when there is none: on a long score, tens of thousands of elements on every change (#419).
// Ask with child combinators from the top (body:has(> #app > .library)), or of a sibling (.library ~ .site-footer).
test("the app's stylesheet never asks the body or the root about any descendant", () => {
    const text = readFileSync(new URL(sheets[0], root), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
    const broad = [...text.matchAll(/(?:^|[\s,}])(?:html|body|:root)\s*:has\(\s*(?!>)[^)]*\)/g)].map((match) => match[0].trim());
    assert.deepEqual(broad, []);
});

// The engraved A4 sheets are laid out and painted only near the view (#419): a 70-page score is one document of tens of
// thousands of elements. The sheet's own box is what the score measures (score-view.ts, geometry), so it must keep its size.
test("the score's A4 sheets are rendered only near the view, at the size of a sheet", () => {
    const text = readFileSync(new URL(sheets[0], root), "utf8");
    assert.match(text, /\.score\.paged \.score-page:not\(\.pdf-page\)\s*\{\s*content-visibility:\s*auto;/);
    assert.match(text, /\.score\.paged \.score-page\s*\{\s*width:\s*var\(--page-w\);\s*aspect-ratio:\s*210 \/ 297;/);
});
