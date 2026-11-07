// Scanning printed music (decision record 0011): Settings takes the scan server's address, Import sends
// a photo there and shows the bars to check, and the workspace opens the piece with its Check flags and drawer. The scan
// server is played by the test: it answers with what homr read from a page of Für Elise.
// Runs in desktop Chromium only (playwright.config.mjs), with the WCAG checks on the new screens.
import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { openRoll, violations } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const photo = fileURLToPath(new URL("../../src/MotiffLab.Web/wwwroot/icon-192.png", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

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
    await page.goto("/import/scan");
    await expect(page.getByText("Set it up in Settings")).toBeVisible();
    await expect(page.getByLabel("Choose a PDF or photo of printed music to scan")).toHaveCount(0);
});

test("a scanned page opens with its doubtful bars to check, and checked bars stay checked", async ({ page }) => {
    const uploads = await scanServer(page);
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();
    expect(await violations(page)).toEqual([]);

    await page.goto("/import/scan");
    await page.getByLabel("Choose a PDF or photo of printed music to scan").setInputFiles(photo);
    const check = page.locator(".scan-check");
    await expect(check).toContainText("7 bars of 32 may have been misread");
    await expect(page.getByText("Scanned by homr 0.7.0")).toBeVisible();
    expect(uploads).toHaveLength(1);
    expect(await violations(page)).toEqual([]);

    // The workspace opens with the Check drawer beside the score and a flag on each doubtful bar (0018 section 9).
    await page.getByText("Open the piece").click();
    const drawer = page.locator(".sheet.check-sheet");
    await expect(drawer.getByRole("heading", { level: 2 })).toHaveText("7 bars to review");
    await expect(drawer).toContainText("Read from a scan by homr 0.7.0.");
    const flags = page.locator(".score button.check-flag");
    await expect(flags).toHaveCount(7);
    await expect(flags.first()).toHaveAttribute("aria-label", /^Check bar 8: /);
    const first = drawer.locator(".check-entry").first();
    await first.getByRole("button", { name: "Show" }).click();
    await expect(page.locator(".selection-status")).toContainText(/bars? 8/);
    await first.getByRole("button", { name: "Fix notes" }).click();
    await expect(first.locator(".notes-table")).toBeVisible();
    expect(await violations(page)).toEqual([]);
    await first.getByRole("button", { name: "Looks right" }).click();
    await expect(drawer.getByRole("heading", { level: 2 })).toHaveText("6 bars to review");
    await expect(flags).toHaveCount(6);
    await expect(drawer.locator(".resolved .check-entry")).toHaveCount(1);
    expect(await violations(page)).toEqual([]);

    // With the drawer closed the flags are extra information (testing item 33): shown with ⓘ, and a flag opens the drawer on its bar.
    await drawer.getByRole("button", { name: "Close Check" }).click();
    await expect(drawer).toHaveCount(0);
    await expect(flags.first()).toBeHidden();
    await page.locator(".bar").getByRole("button", { name: "Extra information", exact: true }).click();
    await flags.first().click();
    await expect(drawer.locator(".check-entry.on")).toHaveCount(1);

    // What was checked stays checked.
    await page.reload();
    await expect(drawer.getByRole("heading", { level: 2 })).toHaveText("6 bars to review", { timeout: 30_000 });
    await expect(drawer.locator(".resolved .check-entry")).toHaveCount(1);
    await drawer.locator(".resolved .check-entry").getByRole("button", { name: "Check again" }).click();
    await expect(flags).toHaveCount(7);

    // The piano roll carries the same flags on its bar numbers, and says so in its legend.
    await openRoll(page);
    await expect(page.locator(".roll .roll-legend")).toContainText("A bar to check: tap its ?");

    // The library's card counts the bars still to check.
    await page.goto("/");
    await expect(page.locator(".card.piece .tag.warn")).toHaveText("Check · 7 bars to review");
    expect(await violations(page)).toEqual([]);
});
