// The redesign's shared pieces (0018 sections 13 and 14): the kit's tokens and fonts as the web app serves them, the one
// icon set written for both apps, the theme's choices and the colour helpers the piano roll draws with.
//   node --test tests/web   (after a build: the theme and token helpers are the compiled wwwroot/js)

import assert from "node:assert/strict";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

import { CATALOGUE, SPRITE, catalogue, readIcons, sprite } from "../../tools/icons/build.mjs";
import { attributesFor, readChoice } from "../../src/MotiffLab.Web/wwwroot/js/layout/theme.js";
import { mix, parseColour, withAlpha } from "../../src/MotiffLab.Web/wwwroot/js/layout/tokens.js";

const root = new URL("../../", import.meta.url);
const read = (path) => readFileSync(new URL(path, root));

test("the web app serves the kit's tokens unchanged, with the fonts beside them", () => {
    assert.deepEqual(read("src/MotiffLab.Web/wwwroot/css/tokens.css"), read("design/kit/tokens.css"), "copy design/kit/tokens.css to wwwroot/css");
    const fonts = readdirSync(new URL("design/kit/fonts/", root));
    assert.ok(fonts.length >= 6);
    for (const font of fonts) {
        assert.deepEqual(read(`src/MotiffLab.Web/wwwroot/css/fonts/${font}`), read(`design/kit/fonts/${font}`), font);
    }
});

test("the web app loads the tokens before its own styles", () => {
    const index = read("src/MotiffLab.Web/wwwroot/index.html").toString();
    assert.ok(index.indexOf("css/tokens.css") > 0 && index.indexOf("css/tokens.css") < index.indexOf("css/app.css"));
});

test("the icon sprite and the iPad's asset catalogue are what the kit's icons.js gives (node tools/icons/build.mjs)", () => {
    const icons = readIcons();
    assert.ok(Object.keys(icons).length >= 30);
    assert.equal(read(SPRITE).toString(), sprite(icons));
    const files = catalogue(icons);
    for (const [path, content] of Object.entries(files)) {
        assert.equal(read(`${CATALOGUE}/${path}`).toString(), content, path);
    }
    const sets = readdirSync(new URL(`${CATALOGUE}/`, root)).filter((name) => name.endsWith(".imageset"));
    assert.deepEqual(sets.sort(), Object.keys(icons).map((name) => `ic-${name}.imageset`).sort(), "an image set the kit has not got");
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
    walk(new URL("src/MotiffLab.Web/", root));
    assert.ok(named.size > 0, "no icons found: the pattern no longer matches");
    for (const name of named) {
        assert.ok(name in icons, `<Icon Name="${name}"> is not in design/kit/icons.js`);
    }
});

// #533 sections 5 and 9: an action has the icon that says it. Clear selection is the selection's frame with a cross in it, never
// the cross that closes, in both apps; the pen's three tools are three different drawings, the same on the iPad.
test("Clear selection and the pen's tools have icons of their own, in both apps", () => {
    const icons = readIcons();
    for (const name of ["deselect", "pencil", "marker", "eraser"]) assert.ok(name in icons, name);
    assert.equal(new Set(["pencil", "marker", "eraser", "pen", "highlight"].map((name) => icons[name])).size, 5, "each tool its own drawing");
    const lab = read("src/MotiffLab.Web/Pages/PlaybackLab.razor").toString();
    assert.match(lab, /aria-label="@Text\["menu\.clearSelection"\]"[^>]*><Icon Name="deselect" \/>/);
    const palette = read("src/MotiffLab.Web/Score/PenPalette.razor").toString();
    assert.match(palette, /\(PenTool, "pencil", "pencil\.pen"\),\s*\(HighlighterTool, "marker", "pencil\.highlighter"\),\s*\(EraserTool, "eraser", "pencil\.eraser"\)/);
    const tools = read("ipad/App/MotiffLab/PenState.swift").toString();
    assert.match(tools, /case \.pencil: "pencil"\s*case \.marker: "marker"\s*case \.eraser: "eraser"/);
    const selection = read("ipad/App/MotiffLab/SelectionTools.swift").toString();
    assert.match(selection, /case \.clearSelection: "deselect"/);
});

test("the theme's choices are the setting appearance.theme's, Auto first and the default", () => {
    const catalogue = read("src/MotiffLab.MusicCore/Settings/SettingsCatalogue.cs").toString();
    assert.match(catalogue, /Choice\("appearance\.theme", SettingSection\.Appearance, \["auto", "daylight", "night", "slate"\], "auto"/);
    assert.equal(readChoice(null), "auto");
    assert.equal(readChoice("sepia"), "auto");
    assert.equal(readChoice("slate"), "slate");
});

test("Auto follows the device; Night stand is dark; the Slate page is dark with the slate page", () => {
    assert.deepEqual(attributesFor("auto", false), { theme: "light", page: null });
    assert.deepEqual(attributesFor("auto", true), { theme: "dark", page: null });
    assert.deepEqual(attributesFor("daylight", true), { theme: "light", page: null });
    assert.deepEqual(attributesFor("night", false), { theme: "dark", page: null });
    assert.deepEqual(attributesFor("slate", false), { theme: "dark", page: "slate" });
});

test("colours from the tokens are read, made translucent and mixed for the canvas", () => {
    assert.deepEqual(parseColour("#2F5BD6"), [47, 91, 214, 1]);
    assert.deepEqual(parseColour("#fff"), [255, 255, 255, 1]);
    assert.deepEqual(parseColour("rgba(31,28,24,.12)"), [31, 28, 24, 0.12]);
    assert.deepEqual(parseColour("rgb(1 2 3 / 50%)"), [1, 2, 3, 0.5]);
    assert.equal(parseColour("color-mix(in srgb, red, blue)"), null);
    assert.equal(withAlpha("#2F5BD6", 0.45), "rgba(47, 91, 214, 0.45)");
    assert.equal(withAlpha("rgba(31,28,24,.5)", 0.5), "rgba(31, 28, 24, 0.25)");
    assert.equal(mix("#FFFFFF", "#000000", 0.25), "rgb(191, 191, 191)");
    assert.equal(mix("#FFFFFF", "rgba(0,0,0,.5)", 0.5), "rgb(191, 191, 191)");
    assert.equal(mix("nonsense", "#000", 0.5), "nonsense");
});

test("no stylesheet of the web app keeps the old lime or charcoal", () => {
    const css = read("src/MotiffLab.Web/wwwroot/css/app.css").toString();
    assert.equal(/#C8F169|#15191F|--ml-lime|--ml-charcoal/i.test(css), false);
    assert.ok(existsSync(new URL("src/MotiffLab.Web/wwwroot/img/icons.svg", root)));
});
