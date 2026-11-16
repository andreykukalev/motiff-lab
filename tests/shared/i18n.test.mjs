// Every interface text the iPad app names by a literal key is in English and in Russian (0018: both languages ship). The
// archived web app's keys: archive/web/tests/web/i18n-web.test.mjs.
//   node --test tests/shared

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

const root = new URL("../../src/shared/", import.meta.url);
const en = JSON.parse(readFileSync(new URL("wwwroot/i18n/en.json", root), "utf8"));
const ru = JSON.parse(readFileSync(new URL("wwwroot/i18n/ru.json", root), "utf8"));
// What only the iPad says (src/ipad/App/Words), read by the iPad app beside the web app's files.
const ipadWords = new URL("../../src/ipad/App/Words/", import.meta.url);
const ipadEn = JSON.parse(readFileSync(new URL("en.json", ipadWords), "utf8"));
const ipadRu = JSON.parse(readFileSync(new URL("ru.json", ipadWords), "utf8"));

/** A key-shaped literal; one followed by `+` starts a key built in place (a prefix). */
const KEY = /"([a-zA-Z]\w*(?:\.\w+)+)"(?!\s*\+)/g;
/** The start of a key built in place: `"edit.\($0)"` or `"recognition.stopped." + reason`. */
const PREFIX = /"([a-zA-Z]\w*(?:\.\w*)+)(?:\\\(|"\s*\+)/g;
/** Names in key holders that are no texts: what the app keeps on the device ("motiff.…", the roll drawer's heights). */
const NOT_TEXT = /^(?:motiff|roll\.drawerShare)\./;

/** What follows `open` (the index of "[", "(" or "{") up to its closing bracket; brackets inside strings are skipped. */
function bracketed(source, open, untilComma = false) {
    let depth = 0;
    let quoted = false;
    for (let at = open; at < source.length; at++) {
        const c = source[at];
        if (quoted) {
            if (c === "\\") at++;
            else if (c === '"') quoted = false;
        } else if (c === '"') quoted = true;
        else if ("[({".includes(c)) depth++;
        else if ("])}".includes(c) && --depth === 0) return source.slice(open + 1, at);
        else if (untilComma && c === "," && depth === 1) return source.slice(open + 1, at);
    }
    return source.slice(open + 1);
}

/**
 * The keys the iPad app names (UIText.swift): every literal in a lookup (`text[…]`, the key of `text.format(…)`
 * and of `text.count(…)` with its plural forms), ternaries included, and every literal in a declaration that holds a
 * key (`var … key`, `labelKey`, `hint`, an assistant state's `note`, …); `prefixes` are the starts of keys built in
 * either (`text["edit.\($0)"]`).
 */
function namedKeys() {
    const dir = new URL("../../src/ipad/App/MotiffLab/", import.meta.url);
    const keys = new Set();
    const prefixes = new Set();
    for (const name of readdirSync(dir).filter((file) => file.endsWith(".swift"))) {
        const swift = readFileSync(new URL(name, dir), "utf8").replace(/^\s*\/\/.*$/gm, "");
        for (const lookup of swift.matchAll(/\btext(\[|\.format\(|\.count\()/g)) {
            const inner = bracketed(swift, lookup.index + lookup[0].length - 1, lookup[1] !== "[");
            for (const match of inner.matchAll(KEY)) {
                for (const key of lookup[1] === ".count(" ? [`${match[1]}.one`, `${match[1]}.many`] : [match[1]]) keys.add(key);
            }
            for (const match of inner.matchAll(PREFIX)) prefixes.add(match[1]);
        }
        for (const holder of swift.matchAll(/\b(?:var|let|func)\s+(?:\w*(?:[kK]ey|[lL]abel|[hH]int)s?|note)\b[^{\n]*\{/g)) {
            const body = bracketed(swift, holder.index + holder[0].length - 1);
            for (const match of [...body.matchAll(KEY), ...body.matchAll(PREFIX)].filter((one) => !NOT_TEXT.test(one[1]))) {
                (match[0].endsWith('"') ? keys : prefixes).add(match[1]);
            }
        }
    }
    return { keys, prefixes };
}

const inBoth = (key) => (key in en && key in ru) || (key in ipadEn && key in ipadRu);
/** A counted text's base (`text.count` through a key holder): its plural forms are there. */
const counted = (base) => [`${base}.one`, `${base}.many`].every(inBoth);

test("every text the iPad app names is in English and in Russian, as it reads the web app's string files", () => {
    const { keys, prefixes } = namedKeys();
    assert.ok(keys.size > 900, `only ${keys.size} keys found: the patterns no longer match`);
    const missing = [...keys].filter((key) => !inBoth(key) && !counted(key));
    assert.deepEqual(missing, [], "add these keys to wwwroot/i18n/en.json and ru.json (or src/ipad/App/Words), or stop naming them in the iPad app");
    // A key built in a lookup still has its family: the prefix starts a text in both languages.
    assert.ok(prefixes.size > 10, `only ${prefixes.size} built keys found: the pattern no longer matches`);
    const gone = [...prefixes].filter((prefix) => ![...Object.keys(en), ...Object.keys(ipadEn)].some((key) => key.startsWith(prefix) && inBoth(key)));
    assert.deepEqual(gone, [], "a lookup builds keys with these starts, but no text starts with them in both languages");
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
