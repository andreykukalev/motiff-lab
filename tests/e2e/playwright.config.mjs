// Browser tests for the published app (issue #66): the core journey in each engine players use,
// on desktop and tablet, the journey offline, and a WCAG 2.2 AA scan. Run after
// `dotnet publish src/MotiffLab.Web --configuration Release --output publish` from the repository root;
// PUBLISH_DIR points elsewhere. Firefox needs a sound device, real or virtual (PulseAudio in CI),
// or its audio stays suspended.
import { defineConfig, devices } from "@playwright/test";
import { resolve } from "node:path";

const publish = resolve(import.meta.dirname, "../..", process.env.PUBLISH_DIR ?? "publish", "wwwroot");
const port = 5180;
const chromiumOnly = /(accessibility|analytics)\.spec/;

export default defineConfig({
    testDir: ".",
    timeout: 60_000,
    expect: { timeout: 15_000 },
    fullyParallel: true,
    forbidOnly: !!process.env.CI,
    reporter: process.env.CI ? [["list"], ["github"]] : "list",
    use: {
        baseURL: `http://localhost:${port}`,
        trace: "retain-on-failure",
    },
    webServer: {
        command: `node serve.mjs "${publish}" ${port}`,
        url: `http://localhost:${port}/`,
        reuseExistingServer: !process.env.CI,
    },
    // Chrome and Edge share Chromium; Safari is WebKit. Tablets are emulated in the engine they ship
    // with. The accessibility and analytics checks read markup and app logic, the same in every engine, so run once.
    projects: [
        { name: "chromium", use: { ...devices["Desktop Chrome"] } },
        { name: "firefox", use: { ...devices["Desktop Firefox"] }, testIgnore: chromiumOnly },
        { name: "webkit", use: { ...devices["Desktop Safari"] }, testIgnore: chromiumOnly },
        { name: "tablet-ipad", use: { ...devices["iPad (gen 11) landscape"] }, testIgnore: chromiumOnly },
        { name: "tablet-android", use: { ...devices["Galaxy Tab S9 landscape"] }, testIgnore: chromiumOnly },
    ],
});
