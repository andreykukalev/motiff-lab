// The redesign's shared pieces (0018 sections 13 and 14): the one icon set as the iPad's asset catalogue, the theme's choices
// and the colour helpers the piano roll draws with. The archived web app's half: archive/web/tests/web/design-web.test.mjs.
//   node --test tests/shared   (after a build: the theme and token helpers are the compiled wwwroot/js)

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

import { CATALOGUE, catalogue, readIcons } from "../../tools/icons/build.mjs";
import { attributesFor, readChoice } from "../../src/shared/wwwroot/js/layout/theme.js";
import { mix, parseColour, withAlpha } from "../../src/shared/wwwroot/js/layout/tokens.js";

const root = new URL("../../", import.meta.url);
const read = (path) => readFileSync(new URL(path, root));



test("the iPad's asset catalogue is what the kit's icons.js gives (node tools/icons/build.mjs)", () => {
    const icons = readIcons();
    assert.ok(Object.keys(icons).length >= 30);
    const files = catalogue(icons);
    for (const [path, content] of Object.entries(files)) {
        assert.equal(read(`${CATALOGUE}/${path}`).toString(), content, path);
    }
    const sets = readdirSync(new URL(`${CATALOGUE}/`, root)).filter((name) => name.endsWith(".imageset"));
    assert.deepEqual(sets.sort(), Object.keys(icons).map((name) => `ic-${name}.imageset`).sort(), "an image set the kit has not got");
});


// #533 sections 5 and 9: an action has the icon that says it. Clear selection is the selection's frame with a cross in it, never
// the cross that closes, in both apps; the pen's three tools are three different drawings, the same on the iPad.
test("Clear selection and the pen's tools have icons of their own on the iPad", () => {
    const icons = readIcons();
    for (const name of ["deselect", "pencil", "marker", "eraser"]) assert.ok(name in icons, name);
    assert.equal(new Set(["pencil", "marker", "eraser", "pen", "highlight"].map((name) => icons[name])).size, 5, "each tool its own drawing");
    const tools = read("src/ipad/App/MotiffLab/Ink/PenState.swift").toString();
    assert.match(tools, /case \.pencil: "pencil"\s*case \.marker: "marker"\s*case \.eraser: "eraser"/);
    const selection = read("src/ipad/App/MotiffLab/Selection/SelectionTools.swift").toString();
    assert.match(selection, /case \.clearSelection: "deselect"/);
});

test("the theme's choices are the setting appearance.theme's, Auto first and the default", () => {
    const catalogue = read("src/backend/MotiffLab.MusicCore/Settings/SettingsCatalogue.cs").toString();
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


// Andrei, 2026-11-18: the selection menu's Note looked like a speech bubble and read as the assistant. It is a note sheet
// (`memo`), unlike the bubble, the assistant's (the pill's «Ассистент», v0.9 finding 8), and Insights' magnifier
// (`insight`, `search`) and the pages (`pages`, `file`).
test("the selection menu's Note has a note's icon, never the assistant's look", () => {
    const icons = readIcons();
    assert.ok("memo" in icons);
    assert.equal(new Set(["memo", "note", "insight", "search", "pages", "file"].map((name) => icons[name])).size, 6, "each its own drawing");
    const screen = read("src/ipad/App/MotiffLab/Workspace/ScoreScreen.swift").toString();
    const menu = screen.slice(screen.indexOf("struct SelectionMenu"), screen.indexOf("struct ClearSelectionButton"));
    assert.match(menu, /iconItem\("memo", text\["menu\.note"\]/);
    assert.doesNotMatch(menu, /Item\("note"/);
    assert.match(menu, /iconItem\("memo", text\["menu\.note"\][\s\S]*?iconItem\("bookmark", text\["menu\.bookmark"\]/, "Note before Bookmark, as the kit's menu");
    assert.match(screen, /iconItem\("note", text\["pill\.assistant"\]/, "the bubble is the assistant's");
});
