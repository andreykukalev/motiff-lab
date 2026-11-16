// The kit's stylesheets close every brace they open. One rule left open in the web app's app.css (2026-11-01) made
// browsers with CSS nesting read the rest as nested inside it (archived: archive/web/tests/web/stylesheets-web.test.mjs).
//   node --test tests/shared

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../", import.meta.url);
const sheets = ["design/kit/tokens.css", "design/kit/ds.css"];

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

