// «Очистить тестовую библиотеку» on the web test site (Scripts/storage/test-library.ts): what goes, what stays, and that it
// runs nowhere but on a test site.
//   node --test tests/web   (after a build: the compiled wwwroot/js)

import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";

import { LIBRARY_DATABASES, clearTestLibrary, isLibraryKey, isTestSiteHere, siteKind } from "../../src/MotiffLab.Web/wwwroot/js/storage/test-library.js";

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
