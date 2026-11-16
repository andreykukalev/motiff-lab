// Every interface text the archived web app names by a literal key is in English and in Russian (0018: both languages
// ship); the iPad's half and the words themselves: tests/shared/i18n.test.mjs. Not run while the web is archived.
//   node --test archive/web/tests/web

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../src/MotiffLab.Web/", import.meta.url);
const words = new URL("../../../../src/shared/wwwroot/i18n/", import.meta.url);
const en = JSON.parse(readFileSync(new URL("en.json", words), "utf8"));
const ru = JSON.parse(readFileSync(new URL("ru.json", words), "utf8"));

/** The keys named as Text["…"], Text.Format("…", …) and Text.Count("…", …) in the app's Razor and C# files; a Count key has its plural forms. */
function namedKeys() {
    const keys = new Set();
    const walk = (dir) => {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            if (entry.isDirectory()) {
                if (!["bin", "obj", "wwwroot", "node_modules", "Scripts"].includes(entry.name)) walk(new URL(`${entry.name}/`, dir));
            } else if (/\.(razor|cs)$/.test(entry.name)) {
                // Documentation comments quote keys as examples.
                const text = readFileSync(new URL(entry.name, dir), "utf8").replace(/^\s*\/\/\/.*$/gm, "");
                for (const match of text.matchAll(/\bText\[\s*"([a-zA-Z][\w.]*)"\s*\]/g)) keys.add(match[1]);
                for (const match of text.matchAll(/\bText\.Format\(\s*"([a-zA-Z][\w.]*)"/g)) keys.add(match[1]);
                for (const match of text.matchAll(/\bText\.Count\(\s*"([a-zA-Z][\w.]*)"/g)) {
                    for (const form of ["one", "many"]) keys.add(`${match[1]}.${form}`);
                }
            }
        }
    };
    walk(root);
    return keys;
}

test("every text the app names is in English and in Russian", () => {
    const keys = namedKeys();
    assert.ok(keys.size > 200, `only ${keys.size} keys found: the patterns no longer match`);
    const missing = [...keys].filter((key) => !(key in en) || !(key in ru));
    assert.deepEqual(missing, [], "add these keys to wwwroot/i18n/en.json and ru.json");
});

