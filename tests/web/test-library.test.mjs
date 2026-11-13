// «Очистить всё» on the web test site (Scripts/storage/test-library.ts): what goes, what stays, that it runs nowhere but
// on a test site, and how a reset made on another device reaches this browser.
//   node --test tests/web   (after a build: the compiled wwwroot/js)

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

import {
    LIBRARY_DATABASES, RESET_KEY, appliedReset, applyReset, clearTestLibrary, isLibraryKey, isResetDue, isTestSiteHere, parseStamp, recordReset, siteKind,
} from "../../src/MotiffLab.Web/wwwroot/js/storage/test-library.js";

test("every IndexedDB database the app opens is emptied", () => {
    const scripts = new URL("../../src/MotiffLab.Web/Scripts/", import.meta.url);
    const opened = new Set();
    const walk = (dir) => {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            if (entry.isDirectory()) {
                walk(new URL(`${entry.name}/`, dir));
            } else if (entry.name.endsWith(".ts")) {
                const source = readFileSync(new URL(entry.name, dir), "utf8");
                for (const match of source.matchAll(/const DATABASE = "([^"]+)"/g)) opened.add(match[1]);
            }
        }
    };
    walk(scripts);
    assert.ok(opened.size >= 5, `only ${[...opened]} found: the pattern no longer matches`);
    assert.deepEqual([...opened].sort(), [...LIBRARY_DATABASES].sort());
});

test("the library's entries go and the device's stay", () => {
    for (const key of ["motiff-lab:ml-sync", "motiff-lab:originals-here", "motiff-lab:edits.off.p_1", "motiff-lab:pdf.page.p_1", "motiff-lab:pdf.metronome.p_1"]) {
        assert.equal(isLibraryKey(key), true, key);
    }
    for (const key of [
        "motiff-lab:ml-language", "motiff-lab:ml-online", "motiff-lab:ml-test-plan", "motiff-lab:ml-score-layout", "motiff-lab:library.tab",
        "motiff-lab:motifflab.scan.address", "motiff-lab:motifflab.analytics", "motiff-lab:settings.presetChosen", "motiff-lab:guidance.brackets",
        "motiff-lab:scan.hostedPages.2026-11-13.player", "motiff-lab:persist-asked", "ml-roll-height", "msal.token.keys.client", "ml-sync",
    ]) {
        assert.equal(isLibraryKey(key), false, key);
    }
});

test("only a test site is one; the main address and a local run are not", () => {
    assert.equal(siteKind("delightful-ground-046924d0f-develop.eastus2.4.azurestaticapps.net"), "test");
    assert.equal(siteKind("delightful-ground-046924d0f.4.azurestaticapps.net"), "main");
    assert.equal(siteKind("localhost"), "local");
    assert.equal(siteKind("127.0.0.1"), "local");
});

test("the clear refuses to run anywhere but on a test site", async () => {
    // Node has no location: not a test site.
    assert.equal(isTestSiteHere(), false);
    await assert.rejects(clearTestLibrary(), /only on the test site/);
});

// «Очистить всё»: a reset made on another device (src/MotiffLab.Api/TestReset.cs) reaches this browser at its next list.
async function withStorage(entries, run) {
    const store = new Map(Object.entries(entries));
    const storage = {
        getItem: (key) => (store.has(key) ? store.get(key) : null),
        setItem: (key, value) => store.set(key, String(value)),
        removeItem: (key) => store.delete(key),
        key: (index) => [...store.keys()][index] ?? null,
        get length() { return store.size; },
    };
    const before = Object.getOwnPropertyDescriptor(globalThis, "localStorage");
    Object.defineProperty(globalThis, "localStorage", { value: storage, configurable: true, writable: true });
    try {
        return await run(store);
    } finally {
        if (before) Object.defineProperty(globalThis, "localStorage", before);
        else delete globalThis.localStorage;
    }
}

test("the applied stamp is the device's, so clearing the library never takes it", () => {
    assert.equal(RESET_KEY, "motiff-lab:ml-test-reset");
    assert.equal(isLibraryKey(RESET_KEY), false);
});

test("a stamp is a whole number; anything else is no stamp", () => {
    assert.equal(parseStamp("1763043540123"), 1763043540123);
    assert.equal(parseStamp(" 42 "), 42);
    for (const value of [null, undefined, "", "-1", "4.2", "1e9", "abc", "9".repeat(16)]) {
        assert.equal(parseStamp(value), null, String(value));
    }
});

test("only a newer stamp than the one applied clears this browser", () => {
    assert.equal(isResetDue(null, 0), false);
    assert.equal(isResetDue("0", 0), false);
    assert.equal(isResetDue("100", 0), true);
    assert.equal(isResetDue("100", 100), false);
    assert.equal(isResetDue("101", 100), true);
    assert.equal(isResetDue("99", 100), false);
});

test("the stamp applied is kept, and an older one never replaces it", async () => {
    await withStorage({}, (store) => {
        assert.equal(appliedReset(), 0);
        recordReset("200");
        assert.equal(appliedReset(), 200);
        assert.equal(isResetDue("200"), false);
        recordReset(150);
        recordReset("not a stamp");
        assert.equal(store.get(RESET_KEY), "200");
        assert.equal(isResetDue("201"), true);
    });
});

test("a reset is applied nowhere but on a test site", async () => {
    // Node has no location: not a test site, so nothing is cleared and nothing is kept.
    await withStorage({ "motiff-lab:ml-sync": "{}" }, async (store) => {
        assert.equal(await applyReset("300"), false);
        assert.equal(store.get("motiff-lab:ml-sync"), "{}");
        assert.equal(store.has(RESET_KEY), false);
    });
});
