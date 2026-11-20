// The score page's contract (decision record 0031, steps 2 and 11): the functions the score page offers the iPad app
// (window.motiff, src/shared/Scripts/score/ipad-page/page.ts) and the keys of the messages its modules post are exactly
// the lists in tests/contracts/score-page.json, and so are the commands and the messages Scripts/score/page-contract.ts
// types the page with; the app's PageCommand and PageEvent.Kind are checked against the same lists by
// ScorePageContractTests on macOS. Web/score.js is only the entry score.html loads.
//   node --test tests/shared   (needs no build: it reads the page's sources)

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";
import { offered, posted } from "./page-contract.mjs";

const root = new URL("../../", import.meta.url);
const contract = JSON.parse(readFileSync(new URL("tests/contracts/score-page.json", root), "utf8"));
const folder = new URL("src/shared/Scripts/score/ipad-page/", root);
const modules = readdirSync(folder).filter((name) => name.endsWith(".ts")).sort();
const page = modules.map((name) => readFileSync(new URL(name, folder), "utf8")).join("\n");
const entry = readFileSync(new URL("page.ts", folder), "utf8");
const types = readFileSync(new URL("src/shared/Scripts/score/page-contract.ts", root), "utf8");

/** The members an interface of page-contract.ts declares, at its top level: `name(` or `name:` four spaces in. */
function members(source, name) {
    const start = source.indexOf(`export interface ${name} {`);
    assert.ok(start >= 0, `page-contract.ts declares ${name}`);
    const body = source.slice(start, source.indexOf("\n}", start));
    return [...body.matchAll(/^ {4}(\w+)\??[(:]/gm)].map((match) => match[1]);
}

test("the page offers exactly the functions the app's PageCommand names", () => {
    assert.deepEqual(offered(entry, "motiff").sort(), [...contract.commands].sort());
});

test("the page posts only the message kinds the app's PageEvent decodes, and every one of them", () => {
    assert.deepEqual(posted(page).sort(), [...contract.events].sort());
});

test("page-contract.ts types exactly the commands and the messages of the contract", () => {
    assert.deepEqual(members(types, "PageCommands").sort(), [...contract.commands].sort());
    assert.deepEqual(members(types, "PageEvents").sort(), [...contract.events].sort());
});

test("Web/score.js is only the entry of the page, with no page code of its own", () => {
    const script = readFileSync(new URL("src/ipad/App/Web/score.js", root), "utf8");
    const code = script.split("\n").filter((line) => line.trim() !== "" && !line.trim().startsWith("//"));
    assert.deepEqual(code, ['import "../score/ipad-page/page.js";']);
});

test("the readers see through renamed functions, shorthand keys and nested objects", () => {
    assert.deepEqual(offered("window.motiff = { show, card: showCard };", "motiff"), ["show", "card"]);
    const source = 'post({ chosen: { start, end } }); post(x ? { blank: true } : { place: { start } }); post({ handles });'
        + ' post({ drag: "tick" in m ? { ...m } : m }); postMessage("ready");';
    assert.deepEqual(posted(source).sort(), ["blank", "chosen", "drag", "handles", "place"]);
});
