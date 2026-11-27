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
// the cross that closes, in both apps; the pen's tools are different drawings, the same on the iPad, the colour bars the
// highlight the selection menu had (v0.10, finding 31).
test("Clear selection and the pen's tools have icons of their own on the iPad", () => {
    const icons = readIcons();
    for (const name of ["deselect", "pencil", "marker", "highlight", "eraser"]) assert.ok(name in icons, name);
    assert.equal(new Set(["pencil", "marker", "eraser", "pen", "highlight"].map((name) => icons[name])).size, 5, "each tool its own drawing");
    const tools = read("src/ipad/App/MotiffLab/Ink/PenState.swift").toString();
    assert.match(tools, /case \.pencil: "pencil"\s*case \.marker: "marker"\s*case \.colour: "highlight"\s*case \.eraser: "eraser"/);
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
    const workspace = "src/ipad/App/MotiffLab/Workspace/";
    const file = read(workspace + "SelectionMenu.swift").toString();
    const menu = file.slice(file.indexOf("struct SelectionMenu"), file.indexOf("struct ClearSelectionButton"));
    assert.match(menu, /iconItem\("memo", text\["menu\.note"\]/);
    assert.doesNotMatch(menu, /Item\("note"/);
    assert.match(menu, /iconItem\("memo", text\["menu\.note"\][\s\S]*?iconItem\("bookmark", text\["menu\.bookmark"\]/, "Note before Bookmark, as the kit's menu");
    assert.match(read(workspace + "Pill.swift").toString(), /iconItem\("note", text\["pill\.assistant"\]/, "the bubble is the assistant's");
});

// Andrei's 0.8 test, finding 7 and answer 1: the piano roll's button is the pill's alone, not the top bar's or the
// selection menu's.
test("the piano roll's button is in the pill only", () => {
    const view = (name) => read(`src/ipad/App/MotiffLab/Workspace/${name}.swift`).toString();
    const menu = view("SelectionMenu");
    assert.match(view("Pill"), /iconItem\("roll", text\["bar\.roll"\]/);
    const rollButton = /"roll"|bar\.roll|rollOpen|\(\.roll\)|\[\.roll\]/;
    assert.doesNotMatch(menu.slice(menu.indexOf("struct SelectionMenu"), menu.indexOf("struct ClearSelectionButton")), rollButton);
    assert.doesNotMatch(view("ScoreBar"), rollButton);
    // Nor what the screen hangs under the bar (ScoreScreenBars' `bars`); its transport hands the pill the drawer.
    const bars = view("ScoreScreenBars");
    assert.doesNotMatch(bars.slice(bars.indexOf("func bars("), bars.indexOf("func askAboutPiece(")), rollButton);
});

// Andrei's answer 15 of 1.4 (2026-11-26 10:16 UTC, built in 1.5): a − or + that steps a number repeats while held, faster
// the longer it is held, as the platform's steppers do (SwiftUI's `buttonRepeatBehavior`), so a place of a 300-bar piece is
// not 300 taps; each is off at its bound, which ends a hold there. Every button whose label is a − or + on the iPad, and
// every `stepButton` that draws one: the counters of a draft's place's length and of the PDF's pages (`PageStepper`), the
// tempo's pace and the metronome's beats a minute. The roll's zoom steps are icons, no number.
test("every − and + that steps a number on the iPad steps on while held, off at its bound", () => {
    const app = new URL("src/ipad/App/MotiffLab/", root);
    // Each step of a note's value in Check is a correction that engraves the score again: a hold would queue many and
    // could overshoot the bound music-core answers after, so these step once a press.
    const once = new Set(["Marks/Check.swift:−", "Marks/Check.swift:+"]);
    const found = [];
    for (const name of readdirSync(app, { recursive: true }).filter((file) => file.endsWith(".swift")).sort()) {
        const source = readFileSync(new URL(name, app)).toString();
        for (const site of source.matchAll(/Button\("[−+]"\)|Text\("[−+]"\)|func stepButton\(/g)) {
            const after = site.index + site[0].length;
            const closes = source.indexOf("\n    }\n", after);
            const nextButton = source.slice(after).search(/\bButton\b/);
            const end = site[0].startsWith("func") || nextButton < 0 ? closes : Math.min(closes, after + nextButton);
            const button = source.slice(site.index, end);
            const line = source.slice(0, site.index).split("\n").length;
            const id = `${name}:${site[0].startsWith("func") ? "stepButton" : site[0].slice(-3, -2)}`;
            found.push(id);
            if (once.has(id)) assert.doesNotMatch(button, /\.buttonRepeatBehavior/, `${name}:${line} steps once a press`);
            else assert.match(button, /\.buttonRepeatBehavior\(\.enabled\)/, `${name}:${line} repeats while held`);
            assert.match(button, /\.disabled\(/, `${name}:${line} is off at its bound`);
        }
    }
    assert.deepEqual(found, [
        "Marks/Check.swift:−", "Marks/Check.swift:+",
        "Pdf/PiecePartsSheet.swift:stepButton",
        "Playback/Metronome.swift:−", "Playback/Metronome.swift:+",
        "Playback/Tempo.swift:stepButton",
    ], "the scan finds every stepper; a new one is listed here");
});
