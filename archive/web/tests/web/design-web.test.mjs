// The archived web app's half of the redesign's shared pieces (0018 sections 13 and 14; the active half is
// tests/shared/design.test.mjs): the kit's tokens and fonts as the web app serves them, its icon sprite and the icons it
// names, Clear selection and the pen's tools in its Razor. Not run while the web is archived (docs/web-archive.md).
//   node --test archive/web/tests/web   (after a build of the web app)

import assert from "node:assert/strict";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

import { SPRITE, readIcons, sprite } from "../../../../tools/icons/build.mjs";

const root = new URL("../../../../", import.meta.url);
const read = (path) => readFileSync(new URL(path, root));

test("the web app serves the kit's tokens unchanged, with the fonts beside them", () => {
    assert.deepEqual(read("archive/web/src/MotiffLab.Web/wwwroot/css/tokens.css"), read("design/kit/tokens.css"), "copy design/kit/tokens.css to wwwroot/css");
    const fonts = readdirSync(new URL("design/kit/fonts/", root));
    assert.ok(fonts.length >= 6);
    for (const font of fonts) {
        assert.deepEqual(read(`archive/web/src/MotiffLab.Web/wwwroot/css/fonts/${font}`), read(`design/kit/fonts/${font}`), font);
    }
});

test("the web app loads the tokens before its own styles", () => {
    const index = read("archive/web/src/MotiffLab.Web/wwwroot/index.html").toString();
    assert.ok(index.indexOf("css/tokens.css") > 0 && index.indexOf("css/tokens.css") < index.indexOf("css/app.css"));
});

test("the icon sprite is what the kit's icons.js gives (node tools/icons/build.mjs)", () => {
    assert.equal(read(SPRITE).toString(), sprite(readIcons()));
});

test("every icon the web app names is in the kit", () => {
    const icons = readIcons();
    const named = new Set();
    const walk = (dir) => {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            if (entry.isDirectory() && !["bin", "obj", "wwwroot", "node_modules"].includes(entry.name)) {
                walk(new URL(`${entry.name}/`, dir));
            } else if (entry.name.endsWith(".razor")) {
                const text = readFileSync(new URL(entry.name, dir), "utf8");
                for (const match of text.matchAll(/<Icon\b[^>]*\bName="([^"]*(?:"[^"]*"[^"]*)*?)"\s/g)) {
                    for (const literal of match[1].matchAll(/"([a-z]+)"/g)) named.add(literal[1]);
                    if (/^[a-z]+$/.test(match[1])) named.add(match[1]);
                }
            }
        }
    };
    walk(new URL("archive/web/src/MotiffLab.Web/", root));
    assert.ok(named.size > 0, "no icons found: the pattern no longer matches");
    for (const name of named) {
        assert.ok(name in icons, `<Icon Name="${name}"> is not in design/kit/icons.js`);
    }
});

test("Clear selection and the pen's tools have icons of their own in the web app", () => {
    const lab = read("archive/web/src/MotiffLab.Web/Pages/PlaybackLab.razor").toString();
    assert.match(lab, /aria-label="@Text\["menu\.clearSelection"\]"[^>]*><Icon Name="deselect" \/>/);
    const palette = read("archive/web/src/MotiffLab.Web/Score/PenPalette.razor").toString();
    assert.match(palette, /\(PenTool, "pencil", "pencil\.pen"\),\s*\(HighlighterTool, "marker", "pencil\.highlighter"\),\s*\(EraserTool, "eraser", "pencil\.eraser"\)/);
});

test("no stylesheet of the web app keeps the old lime or charcoal", () => {
    const css = read("archive/web/src/MotiffLab.Web/wwwroot/css/app.css").toString();
    assert.equal(/#C8F169|#15191F|--ml-lime|--ml-charcoal/i.test(css), false);
    assert.ok(existsSync(new URL("archive/web/src/MotiffLab.Web/wwwroot/img/icons.svg", root)));
});
