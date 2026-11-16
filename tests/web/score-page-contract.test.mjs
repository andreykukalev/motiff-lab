// The score page's contract (decision record 0031, step 2): the functions ipad/App/Web/score.js offers the iPad app
// (window.motiff) and the keys of the messages it posts are exactly the lists in tests/contracts/score-page.json, which
// the app's PageCommand and PageEvent.Kind are checked against by ScorePageContractTests on macOS.
//   node --test tests/web   (needs no build: it reads the page's source)

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../", import.meta.url);
const contract = JSON.parse(readFileSync(new URL("tests/contracts/score-page.json", root), "utf8"));
const page = readFileSync(new URL("ipad/App/Web/score.js", root), "utf8");

/** The names window.motiff offers, from `window.motiff = { show, eraseAlong: eraseAlongPath, … };`. */
function offered(source) {
    const body = /window\.motiff\s*=\s*\{([^}]*)\}/.exec(source)?.[1] ?? "";
    return body.split(",").map((entry) => entry.split(":")[0].trim()).filter(Boolean);
}

/** The first key of each object literal at the top of a `post(…)` or `postMessage(…)` argument. */
function posted(source) {
    const keys = new Set();
    for (const call of source.matchAll(/\bpost(?:Message)?\(/g)) {
        let depth = 0;
        for (let i = call.index + call[0].length; i < source.length; i++) {
            const c = source[i];
            if (c === "(" || c === "[") depth += 1;
            else if (c === ")" || c === "]") {
                if (depth === 0) break;
                depth -= 1;
            } else if (c === "{") {
                if (depth === 0) {
                    const key = /^\s*(\w+)/.exec(source.slice(i + 1))?.[1];
                    if (key) keys.add(key);
                }
                depth += 1;
            } else if (c === "}") depth -= 1;
        }
    }
    return [...keys];
}

test("the page offers exactly the functions the app's PageCommand names", () => {
    assert.deepEqual(offered(page).sort(), [...contract.commands].sort());
});

test("the page posts only the message kinds the app's PageEvent decodes, and every one of them", () => {
    assert.deepEqual(posted(page).sort(), [...contract.events].sort());
});

test("the readers see through renamed functions, shorthand keys and nested objects", () => {
    assert.deepEqual(offered("window.motiff = { show, card: showCard };"), ["show", "card"]);
    const source = 'post({ chosen: { start, end } }); post(x ? { blank: true } : { place: { start } }); post({ handles });'
        + ' post({ drag: "tick" in m ? { ...m } : m }); postMessage("ready");';
    assert.deepEqual(posted(source).sort(), ["blank", "chosen", "drag", "handles", "place"]);
});
