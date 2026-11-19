// Every interface text the iPad app names is in English and in Russian (0018: both languages ship): a key missing in
// both shows as the key itself, one missing only in Russian falls back to English (UIText). Every key-shaped literal of
// the app counts, so a new way of passing a key around is covered too; the ids that are no texts are listed. The texts
// are in one shared pair of files and one pair per feature folder (blueprint v0.7, 5.5), each key in exactly one: a
// folder's pair holds what only that folder's Swift names. The archived web app's keys:
// archive/web/tests/web/i18n-web.test.mjs.
//   node --test tests/shared

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

const swiftDir = new URL("../../src/ipad/App/MotiffLab/", import.meta.url);
const read = (url) => JSON.parse(readFileSync(url, "utf8"));
/**
 * The text files as UIText reads them, the shared pair first, then each folder's (`Roll/Words/Roll.en.json`) by name;
 * `folder` is null for the shared pair. Every `*.en.json` or `*.ru.json` below the app's Swift counts, since UIText
 * reads every one at the app's top.
 */
const wordFiles = readdirSync(swiftDir, { recursive: true }).filter((file) => /\.(?:en|ru)\.json$/.test(file)).sort();
const shared = new URL("../../src/shared/wwwroot/i18n/", import.meta.url);
const pairs = [
    { name: "src/shared/wwwroot/i18n/{en,ru}.json", folder: null, url: (language) => new URL(`${language}.json`, shared) },
    ...[...new Set(wordFiles.map((file) => file.replace(/\.(?:en|ru)\.json$/, "")))].map((stem) => ({
        name: `${stem}.{en,ru}.json`,
        folder: stem.split("/")[0],
        stem,
        url: (language) => new URL(`${stem}.${language}.json`, swiftDir),
    })),
].map((pair) => ({ ...pair, en: read(pair.url("en")), ru: read(pair.url("ru")) }));
/** The files that define each key, in either language. */
const files = new Map();
for (const pair of pairs) {
    for (const key of Object.keys({ ...pair.en, ...pair.ru })) files.set(key, [...(files.get(key) ?? []), pair.name]);
}
/** All texts of a language, merged as UIText merges them (each key is in one file). */
const en = Object.assign({}, ...pairs.map((pair) => pair.en));
const ru = Object.assign({}, ...pairs.map((pair) => pair.ru));

const inBoth = (key) => key in en && key in ru;
/** A text, or the base of a counted one (`text.count`, `SourceLayers.counted`) with its plural forms. */
const isText = (key) => inBoth(key) || [`${key}.one`, `${key}.many`].every(inBoth);

/** The app's Swift files, every feature folder below it, as paths relative to it (`Platform/UIText.swift`). */
const swiftFiles = readdirSync(swiftDir, { recursive: true }).filter((file) => file.endsWith(".swift")).sort();
/** A Swift file of the app with its comment lines removed. */
const swift = (name) => readFileSync(new URL(name, swiftDir), "utf8").replace(/^\s*\/\/.*$/gm, "");
const core = new URL("../../src/backend/MotiffLab.MusicCore/", import.meta.url);
const catalogue = readFileSync(new URL("Settings/SettingsCatalogue.cs", core), "utf8");

/** A key-shaped literal; one followed by `+` starts a key built in place (a prefix). */
const KEY = /"([a-zA-Z]\w*(?:\.\w+)+)"(?!\s*\+)/g;
/** The start of a key built in place: `"edit.\($0)"` or `"recognition.stopped." + reason`. */
const PREFIX = /"([a-zA-Z]\w*(?:\.\w*)+)(?:\\\(|"\s*\+)/g;

/** The ids the app names that are no texts: music-core's calls and settings, read where they are declared. */
const facadeCalls = JSON.parse(readFileSync(new URL("../contracts/facade-calls.json", import.meta.url), "utf8"));
const CALLS = new Set(facadeCalls.calls.map((call) => call.name));
const SETTINGS = new Set([...catalogue.matchAll(/\b(?:Choice|Switch)\("([\w.]+)"/g)].map((match) => match[1]));
/** The other key-shaped literals that are no texts, each with why. */
const NOT_TEXT = [
    [/^motiff\./, "a defaults key: what the app keeps on the device"],
    [/^roll\.drawerShare\./, "a defaults key: the roll drawer's heights"],
    [/^(?:library\.tab|roll\.pickShape|score\.(?:playback|selection)ControlsShown)$/, "a defaults key"],
    [/^msauth\.$/, "the start of the sign-in redirect scheme"],
    [/^pen\.$/, "the start of a kept pen code (PenMemory)"],
    [/\.(?:json|musicxml)$/, "a file name"],
    [/^com\./, "a bundle or file provider id"],
    [/^harmony\.(?:chords|motion)$/, "a music-core fact id"],
    [/^pdf\.screen$/, "a SwiftUI coordinate space"],
];
/** An id listed as no text, unless the app also looks it up as a text (`lookedUp`), whatever else it coincides with. */
const notText = (key) =>
    !lookedUp.has(key) && (CALLS.has(key) || SETTINGS.has(key) || NOT_TEXT.some(([pattern]) => pattern.test(key)));

/** What follows `open` (the index of "[", "(" or "{") up to its closing bracket; brackets in strings are skipped. */
function bracketed(source, open) {
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
    }
    return source.slice(open + 1);
}

const words = (list) => [...list.matchAll(/"(\w+)"/g)].map((match) => match[1]);
/** The words of a Swift list `let name = [...]` in a file: strings, or an enum's cases (`[.navigate, .select]`). */
function swiftList(file, name) {
    const source = swift(file);
    const declared = new RegExp(String.raw`\b(?:let|var)\s+${name}\b[^=\n]*=\s*\[`).exec(source);
    assert.ok(declared, `no list ${name} in ${file}`);
    const list = bracketed(source, declared.index + declared[0].length - 1);
    return [...list.matchAll(/"(\w+)"|\.(\w+)/g)].map((match) => match[1] ?? match[2]);
}
/** The cases of a Swift `enum Name: String`, by raw value. */
function swiftEnum(name) {
    for (const file of swiftFiles) {
        const source = swift(file);
        const at = source.search(new RegExp(String.raw`\benum\s+${name}\s*:\s*String\b`));
        if (at < 0) continue;
        const body = bracketed(source, source.indexOf("{", at));
        const cases = [...body.matchAll(/^\s*case\s+([a-z][^.:\n(]*)$/gm)].flatMap((match) => match[1].split(","));
        return cases.map((one) => {
            const [name, raw] = one.split("=").map((part) => part.trim());
            return raw ? raw.replaceAll('"', "") : name;
        });
    }
    return assert.fail(`no enum ${name} in the app`);
}
/** The members of a C# enum in music-core, as the facade writes them (camel case). */
function csharpEnum(file, name) {
    const source = readFileSync(new URL(file, core), "utf8").replace(/\/\/\/.*$/gm, "");
    const at = source.search(new RegExp(String.raw`\benum\s+${name}\b`));
    assert.ok(at >= 0, `no enum ${name} in ${file}`);
    const members = [...bracketed(source, source.indexOf("{", at)).matchAll(/\b([A-Z]\w*)\s*(?:=[^,]*)?,?/g)];
    return members.map((match) => match[1][0].toLowerCase() + match[1].slice(1));
}
/** The choices of a setting in music-core's catalogue. */
function choices(id) {
    const match = new RegExp(String.raw`Choice\("${id.replace(".", "\\.")}",[^[]*(\[[^\]]*\])`).exec(catalogue);
    assert.ok(match, `no choice ${id} in SettingsCatalogue.cs`);
    return words(match[1]);
}

/**
 * The families whose members the code lists, as [start, end, members]: a Swift list mapped in place
 * (`["move", "add", "delete"].map { … "edit.\($0)" … }`), a `String` enum building its own key
 * (`"mode.\(rawValue)Hint"`), and the families fed by named lists, enums of music-core and its settings' choices.
 */
function listedFamilies() {
    const families = [
        ["term.lens.", "", csharpEnum("Analysis/MotifFact.cs", "FactLens")],
        ["term.certainty.", "", csharpEnum("Analysis/MotifFact.cs", "FactCertainty")],
        // "none" names no function: the chord's name leaves it out (DnaHarmony.swift).
        ["dna.fnName.", "", csharpEnum("Analysis/Harmony.Motion.cs", "HarmonicFunction").filter((one) => one !== "none")],
        ["settings.noteNaming.", "", choices("notes.naming")],
        ["settings.rollColour.", "", choices("roll.colour")],
        ["roll.colour.", "", choices("roll.colour")],
        ["settings.theme.", "", choices("appearance.theme")],
        ["settings.language.", "", swiftList("Platform/UIText.swift", "languages")],
        ["roll.js.", "", swiftList("Roll/RollController.swift", "names")],
        ["settings.preset.", "", swiftList("Account/SettingsView.swift", "presets")],
        ["settings.presetHint.", "", swiftList("Account/SettingsView.swift", "presets")],
        ["layers.", "", swiftList("Marks/MarksModel.swift", "layers")],
        ["extra.", "", swiftList("Marks/MarksModel.swift", "extra")],
        ["modePad.", "Hint", swiftList("Ink/InteractionMode.swift", "inBar")],
    ];
    for (const file of swiftFiles) {
        const source = swift(file);
        const mapped = /\[((?:\s*"\w+",?)+)\s*\]\.map\s*\{[^\n]*?"([a-zA-Z]\w*(?:\.\w*)+)\\\(\$0\)(\w*)"/g;
        for (const match of source.matchAll(mapped)) {
            families.push([match[2], match[3], words(match[1])]);
        }
        for (const match of source.matchAll(/\benum\s+(\w+)\s*:\s*String\b[^{]*\{/g)) {
            const body = bracketed(source, match.index + match[0].length - 1);
            for (const own of body.matchAll(/"([a-zA-Z]\w*(?:\.\w*)+)\\\(rawValue\)(\w*)"/g)) {
                families.push([own[1], own[2], swiftEnum(match[1])]);
            }
        }
    }
    return families;
}

/**
 * Every key-shaped literal of the app, with a file that names it, every start of a key it builds, and every literal it
 * looks up as a text: inside `text[…]`, `UIText.current[…]`, `.format(…)` or `.count(…)`.
 */
const named = new Map();
const built = new Set();
const lookedUp = new Set();
for (const file of swiftFiles) {
    const source = swift(file);
    for (const match of source.matchAll(KEY)) named.set(match[1], file);
    for (const match of source.matchAll(PREFIX)) built.add(match[1]);
    for (const lookup of source.matchAll(/(?:\btext|\bUIText\.current)\[|\.(?:format|count)\(/g)) {
        for (const match of bracketed(source, lookup.index + lookup[0].length - 1).matchAll(KEY)) lookedUp.add(match[1]);
    }
}

test("every key-shaped literal of the iPad app is a text in English and in Russian, or an id listed as no text", () => {
    assert.ok(named.size > 1000, `only ${named.size} literals found: the pattern no longer matches`);
    assert.ok(lookedUp.size > 900, `only ${lookedUp.size} literals looked up as texts found: the pattern no longer matches`);
    const missing = [...named].filter(([key]) => !isText(key) && !notText(key)).map(([key, file]) => `${key} (${file})`);
    assert.deepEqual(missing, [], "add these texts to <Folder>/Words/<Folder>.{en,ru}.json or wwwroot/i18n, or list the id in NOT_TEXT");
    // Every exception still names something, so the list does not keep ids the app no longer has.
    const names = [...named.keys(), ...built];
    const unused = NOT_TEXT.filter(([pattern]) => !names.some((key) => pattern.test(key) && !isText(key)));
    assert.deepEqual(unused.map(([pattern]) => String(pattern)), []);
});

test("every key the iPad app builds still has its family, and every listed member its text", () => {
    const prefixes = [...built].filter((prefix) => !notText(prefix));
    assert.ok(prefixes.length > 30, `only ${prefixes.length} built keys found: the pattern no longer matches`);
    const gone = prefixes.filter((prefix) => !Object.keys(en).some((key) => key.startsWith(prefix) && inBoth(key)));
    assert.deepEqual(gone, [], "the app builds keys with these starts, but no text starts with them in both languages");
    const families = listedFamilies();
    assert.ok(families.length > 20, `only ${families.length} listed families found: the patterns no longer match`);
    const members = families.flatMap(([start, end, listed]) => listed.map((member) => `${start}${member}${end}`));
    const missing = members.filter((key) => !inBoth(key));
    assert.deepEqual([...new Set(missing)], [], "a family's member has no text in both languages");
});

test("every pair of text files holds strings, the same keys in English and in Russian, Russian with three plural forms", () => {
    for (const pair of pairs) {
        // UIText reads a file as [String: String]: one value of another type and the whole file is dropped.
        for (const language of ["en", "ru"]) {
            const notString = Object.entries(pair[language]).filter(([, text]) => typeof text !== "string");
            assert.deepEqual(notString.map(([key]) => key), [], `${pair.name} (${language}): texts that are no strings`);
        }
        // English never asks for the form "few", so a text of it there is spare, not a key of its own.
        const keys = (texts) => Object.keys(texts).filter((key) => !key.endsWith(".few")).sort();
        const english = keys(pair.en);
        assert.deepEqual(english, keys(pair.ru), pair.name);
        const counted = english.filter((key) => key.endsWith(".many")).map((key) => key.slice(0, -5));
        const missing = counted.filter((base) => ["one", "few", "many"].some((form) => !(`${base}.${form}` in pair.ru)));
        assert.deepEqual(missing, [], pair.name);
    }
});

test("a folder's texts are its Words/<Folder>.en.json and .ru.json, read by UIText from the app's top", () => {
    const misplaced = pairs.filter((pair) => pair.folder && pair.stem !== `${pair.folder}/Words/${pair.folder}`);
    // The app's top holds them under their names alone, so the folder's name keeps them apart.
    assert.deepEqual(misplaced.map((pair) => pair.name), [], "name a folder's texts <Folder>/Words/<Folder>.{en,ru}.json");
});

test("a key lives in exactly one file", () => {
    const twice = [...files].filter(([, names]) => names.length > 1).map(([key, names]) => `${key}: ${names.join(", ")}`);
    assert.deepEqual(twice, [], "keep each of these keys in one file");
});

/**
 * Which feature folders' Swift name a key: as a literal, as the base of its sub-keys (`library.ideas` for
 * `library.ideas.one`), or by a start it builds (`"edit.\($0)"`, `"recognition.stopped." + reason`) or keeps
 * (`static let key = "folderImport.filter."`). Only literals count: a key reached through a value of another folder, an
 * answer of music-core or saved data counts for the folder that names it, or for none; an id that is also a text
 * (`layers.ink`) counts wherever it is named.
 */
const users = new Map();
for (const file of swiftFiles) {
    const folder = file.split("/")[0];
    const source = swift(file);
    const literals = new Set([...source.matchAll(KEY)].map((match) => match[1]));
    const starts = [...source.matchAll(PREFIX), ...source.matchAll(/"([a-zA-Z]\w*(?:\.\w+)*\.)"/g)]
        .map((match) => match[1])
        .filter((start) => !notText(start));
    for (const key of files.keys()) {
        const names = literals.has(key) || [...literals].some((literal) => key.startsWith(`${literal}.`))
            || starts.some((start) => key.startsWith(start));
        if (names) users.set(key, new Set([...(users.get(key) ?? []), folder]));
    }
}
/** What the pages say, which stays in the shared files though one folder passes it: the roll's labels (RollController). */
const PAGES = [/^roll\.js\./, /^roll\.ipad\.rollAria$/];

test("a folder's pair holds the keys only that folder names, the shared pair all the others", () => {
    const wrong = [];
    for (const pair of pairs) {
        for (const key of Object.keys({ ...pair.en, ...pair.ru })) {
            const by = [...(users.get(key) ?? [])].sort();
            const owner = by.length === 1 && !PAGES.some((pattern) => pattern.test(key)) ? by[0] : null;
            if (owner === pair.folder) continue;
            const home = owner ? `${owner}/Words/${owner}.{en,ru}.json` : pairs[0].name;
            wrong.push(`${key}: in ${pair.name}, named by ${by.join(", ") || "no folder"}, belongs in ${home}`);
        }
    }
    assert.deepEqual(wrong, [], "move these keys, with their texts unchanged, to the file named");
});
