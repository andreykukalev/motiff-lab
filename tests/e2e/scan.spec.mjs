// Scanning printed music (decision record 0011): Settings takes the scan server's address, Import sends
// a photo there and shows the bars to check, and the workspace opens the piece on its Scan tab. The scan
// server is played by the test: it answers with what homr read from a page of Für Elise.
// Runs in desktop Chromium only (playwright.config.mjs), with the WCAG checks on the new screens.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const photo = fileURLToPath(new URL("../../src/MotiffLab.Web/wwwroot/icon-192.png", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

async function violations(page) {
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
    return results.violations.map(v => `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.slice(0, 5).map(n => n.target.join(" ")).join("\n  ")}`);
}

/** Plays the scan server; returns the uploads it received. */
async function scanServer(page) {
    const uploads = [];
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages" };
    await page.route(`${server}/health`, route => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, route => {
        uploads.push(route.request().postDataBuffer()?.length ?? 0);
        return route.fulfill({
            body: musicXml,
            contentType: "application/vnd.recordare.musicxml+xml",
            headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "1" },
        });
    });
    return uploads;
}

test("scanning is off until Settings has a scan server", async ({ page }) => {
    await page.goto("/import");
    await expect(page.getByText("Set it up in Settings")).toBeVisible();
    await expect(page.getByLabel("Choose a PDF or photo of printed music to scan")).toHaveCount(0);
});

test("a scanned page opens with its doubtful bars to check, and checked bars stay checked", async ({ page }) => {
    const uploads = await scanServer(page);
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();
    expect(await violations(page)).toEqual([]);

    await page.goto("/import");
    await page.getByLabel("Choose a PDF or photo of printed music to scan").setInputFiles(photo);
    const check = page.locator(".scan-check");
    await expect(check).toContainText("7 bars of 32 may have been misread");
    await expect(page.getByText("Scanned by homr 0.7.0")).toBeVisible();
    expect(uploads).toHaveLength(1);
    expect(await violations(page)).toEqual([]);

    await page.getByText("Open the piece").click();
    await expect(page.getByRole("tab", { name: "Tools · Scan" })).toHaveAttribute("aria-selected", "true");
    await expect(check).toContainText("0 bars of 32 checked");
    await check.getByRole("button", { name: "Show" }).first().click();
    await expect(page.locator(".inspector .selection-text")).toContainText(/bars? 8/);
    await check.getByRole("button", { name: "Looks right" }).first().click();
    await expect(check).toContainText("1 bar of 32 checked");
    expect(await violations(page)).toEqual([]);

    await page.reload();
    await expect(page.locator(".scan-check li.confirmed")).toHaveCount(1);
    await expect(page.locator(".scan-check")).toContainText("1 bar of 32 checked");
});
