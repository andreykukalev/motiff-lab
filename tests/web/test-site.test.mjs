// The web test site marks its browser tab (Scripts/layout/test-site.ts): which addresses are a test site, and the title.
//   node --test tests/web   (after a build: the compiled wwwroot/js)

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { isTestSite, testTitle } from "../../src/MotiffLab.Web/wwwroot/js/layout/test-site.js";

test("a Static Web App environment is a test site; the main address, other hosts and a local run are not", () => {
    assert.equal(isTestSite("delightful-ground-046924d0f-develop.eastus2.4.azurestaticapps.net"), true);
    assert.equal(isTestSite("Delightful-Ground-046924d0f-Develop.EastUS2.4.AzureStaticApps.net"), true);
    assert.equal(isTestSite("delightful-ground-046924d0f.4.azurestaticapps.net"), false);
    assert.equal(isTestSite("localhost"), false);
    assert.equal(isTestSite("127.0.0.1"), false);
    assert.equal(isTestSite("a.b.c.d.example.net"), false);
});

test("a test site's title starts with the word in the interface language, once", () => {
    assert.equal(testTitle("Motiff Lab", "ru"), "Тест · Motiff Lab");
    assert.equal(testTitle("Motiff Lab", "en"), "Test · Motiff Lab");
    assert.equal(testTitle("Тест · Motiff Lab", "ru"), "Тест · Motiff Lab");
    assert.equal(testTitle("Тест · Motiff Lab", "en"), "Test · Motiff Lab");
    assert.equal(testTitle("Test · Für Elise · Motiff Lab", "ru-RU"), "Тест · Für Elise · Motiff Lab");
});

test("index.html gives the tab an icon and loads the marker", () => {
    const index = readFileSync(new URL("../../src/MotiffLab.Web/wwwroot/index.html", import.meta.url), "utf8");
    assert.match(index, /<link rel="icon" type="image\/png" href="icon-192\.png" \/>/);
    assert.match(index, /<script type="module" src="js\/layout\/test-site\.js"><\/script>/);
});
