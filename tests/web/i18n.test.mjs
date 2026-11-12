// Every interface text the web app names by a literal key is in English and in Russian (0018: both languages ship).
//   node --test tests/web

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../src/MotiffLab.Web/", import.meta.url);
const en = JSON.parse(readFileSync(new URL("wwwroot/i18n/en.json", root), "utf8"));
const ru = JSON.parse(readFileSync(new URL("wwwroot/i18n/ru.json", root), "utf8"));
// What only the iPad says (ipad/App/Words), read by the iPad app beside the web app's files.
const ipadWords = new URL("../../ipad/App/Words/", import.meta.url);
const ipadEn = JSON.parse(readFileSync(new URL("en.json", ipadWords), "utf8"));
const ipadRu = JSON.parse(readFileSync(new URL("ru.json", ipadWords), "utf8"));

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

test("every text the iPad app names is in English and in Russian, as it reads the web app's string files", () => {
    const dir = new URL("../../ipad/App/MotiffLab/", import.meta.url);
    const keys = new Set();
    for (const name of readdirSync(dir).filter((file) => file.endsWith(".swift"))) {
        const swift = readFileSync(new URL(name, dir), "utf8").replace(/^\s*\/\/\/.*$/gm, "");
        for (const match of swift.matchAll(/\btext\[\s*"([a-zA-Z][\w.]*)"\s*\]/g)) keys.add(match[1]);
        for (const match of swift.matchAll(/\btext\.format\(\s*"([a-zA-Z][\w.]*)"/g)) keys.add(match[1]);
        for (const match of swift.matchAll(/\btext\.count\(\s*"([a-zA-Z][\w.]*)"/g)) {
            for (const form of ["one", "many"]) keys.add(`${match[1]}.${form}`);
        }
    }
    assert.ok(keys.size > 100, `only ${keys.size} keys found: the patterns no longer match`);
    const missing = [...keys].filter((key) => !((key in en && key in ru) || (key in ipadEn && key in ipadRu)));
    assert.deepEqual(missing, [], "add these keys to wwwroot/i18n/en.json and ru.json (or ipad/App/Words), or stop naming them in the iPad app");
});

test("the iPad's own words are in English and in Russian, Russian with its three plural forms", () => {
    assert.deepEqual(Object.keys(ipadEn).sort(), Object.keys(ipadRu).filter((key) => !/\.few$/.test(key)).sort());
    const counted = Object.keys(ipadEn).filter((key) => key.endsWith(".many")).map((key) => key.slice(0, -5));
    assert.deepEqual(counted.filter((base) => !(`${base}.few` in ipadRu)), []);
});

test("Russian has the three plural forms wherever English has a counted text", () => {
    const counted = Object.keys(en).filter((key) => key.endsWith(".many")).map((key) => key.slice(0, -5));
    const missing = counted.filter((base) => ["one", "few", "many"].some((form) => !(`${base}.${form}` in ru)));
    assert.deepEqual(missing, []);
});
