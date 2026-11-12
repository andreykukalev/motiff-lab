// Scanning printed music (decision record 0011): Settings takes the scan server's address, Import sends
// a photo there and shows the bars to check, and the workspace opens the piece with its Check flags and drawer. The scan
// server is played by the test: it answers with what homr read from a page of Für Elise.
// Runs in desktop Chromium only (playwright.config.mjs), with the WCAG checks on the new screens.
import { expect, test } from "@playwright/test";
import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { openRoll, violations, librarySessions } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const photo = fileURLToPath(new URL("../../src/MotiffLab.Web/wwwroot/icon-192.png", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

/** Plays the scan server, with its page report when one is given (version 0.4); returns the uploads it received. */
async function scanServer(page, report = null) {
    const uploads = [];
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Page-Report" };
    const reported = report === null ? {} : { "X-Page-Report": JSON.stringify(report) };
    await page.route(`${server}/health`, route => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, route => {
        uploads.push(route.request().postDataBuffer()?.length ?? 0);
        return route.fulfill({
            body: musicXml,
            contentType: "application/vnd.recordare.musicxml+xml",
            headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "1", ...reported },
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
    await librarySessions(page);
    await expect(page.locator(".card.piece .tag.warn")).toHaveText("Check · 7 bars to review");
    expect(await violations(page)).toEqual([]);
});

test("a page the scan server calls poor flags each of its bars, and the summary by page says how many are in question", async ({ page }) => {
    // Faint print: contrast 50, below the 70 a poor page has (PageReport.PoorContrast).
    await scanServer(page, [{ page: 1, rotation: 0, sharpness: 1.1, contrast: 50, staves: 4, noteheads: 180, barLines: 32, spacePx: 15.5, measures: 32 }]);
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();

    await page.goto("/import/scan");
    await page.getByLabel("Choose a PDF or photo of printed music to scan").setInputFiles(photo);
    const pages = page.locator(".scan-check .scan-pages");
    await expect(pages.locator("li")).toHaveText(["Page 1: 32 of 32 bars in question poor page"]);
    expect(await violations(page)).toEqual([]);

    // In the workspace every bar of the page is to review, with the reason, and the Check sheet repeats the summary.
    await page.getByText("Open the piece").click();
    const drawer = page.locator(".sheet.check-sheet");
    await expect(drawer.getByRole("heading", { level: 2 })).toHaveText("32 bars to review");
    await expect(drawer.locator(".scan-pages li")).toHaveText(["Page 1: 32 of 32 bars in question poor page"]);
    await expect(drawer.locator(".check-entry").first()).toContainText("Poor page");
    expect(await violations(page)).toEqual([]);

    // A bar confirmed is no longer in question; the report is kept with the piece, so this holds after a reload.
    await drawer.locator(".check-entry").first().getByRole("button", { name: "Looks right" }).click();
    await expect(drawer.locator(".scan-pages li")).toHaveText(["Page 1: 31 of 32 bars in question poor page"]);
    await page.reload();
    await expect(drawer.locator(".scan-pages li")).toHaveText(["Page 1: 31 of 32 bars in question poor page"], { timeout: 30_000 });
});

test("a file larger than the scan server reads is refused before it is sent, in plain words", async ({ page }, testInfo) => {
    const uploads = await scanServer(page);
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();

    // One byte over the 50 MB the scan server reads (services/omr MAX_UPLOAD_MB).
    const big = testInfo.outputPath("big.pdf");
    await writeFile(big, Buffer.alloc(50 * 1024 * 1024 + 1));
    await page.goto("/import/scan");
    await page.getByLabel("Choose a PDF or photo of printed music to scan").setInputFiles(big);
    await expect(page.getByText("This file is larger than 50 MB, the most the scan server reads.")).toBeVisible();
    await expect(page.getByText("Scan fewer pages at a time, or save the photo at a lower resolution.")).toBeVisible();
    expect(uploads).toHaveLength(0);
    expect(await violations(page)).toEqual([]);
});

test("photos of pages: several photos are read one at a time into one piece, and a page not read is left out and named", async ({ page }) => {
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Page-Report" };
    const sent = [];
    await page.route(`${server}/health`, route => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, route => {
        sent.push(route.request().postDataBuffer().toString("latin1").match(/filename="([^"]+)"/)[1]);
        if (sent.length === 2) {
            return route.fulfill({ status: 422, json: { message: "No staves were found on this page." }, headers: cors });
        }
        const report = JSON.stringify([{ page: 1, rotation: 0, sharpness: 1.1, contrast: 110, staves: 4, noteheads: 60, barLines: 12, spacePx: 15, measures: 32 }]);
        return route.fulfill({ body: musicXml, contentType: "application/vnd.recordare.musicxml+xml", headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "1", "X-Page-Report": report } });
    });
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();

    // Off by default: one file at a time.
    await page.goto("/import/scan");
    await expect(page.getByLabel("Choose a PDF or photo of printed music to scan")).not.toHaveAttribute("multiple");
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    await sheet.getByRole("switch", { name: "Photos of pages" }).check();
    await sheet.press("Escape");

    // Picked in any order, read in the order of their names, the numbers counted as numbers.
    const png = await readFile(photo);
    await page.goto("/import/scan");
    const field = page.getByLabel("Choose a PDF or photo of printed music, or several photos of one piece's pages");
    await field.setInputFiles([
        { name: "page 10.png", mimeType: "image/png", buffer: png },
        { name: "page 2.png", mimeType: "image/png", buffer: png },
        { name: "page 1.png", mimeType: "image/png", buffer: png },
    ]);
    await expect(page.locator(".scan-check")).toContainText("of 64 may have been misread");
    expect(sent).toEqual(["page 1.png", "page 2.png", "page 10.png"]);
    await expect(page.getByText("2 pages read in")).toBeVisible();
    await expect(page.locator(".scan-check .scan-pages li")).toHaveCount(2);
    await expect(page.locator(".left-out li")).toHaveText(["Page 2 (page 2.png) could not be read and is left out: No staves were found on this page."]);
    expect(await violations(page)).toEqual([]);

    // A PDF among the photos is read on its own, and nothing is sent.
    await field.setInputFiles([
        { name: "page 1.png", mimeType: "image/png", buffer: png },
        { name: "score.pdf", mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.4") },
    ]);
    await expect(page.getByText("Several files are read as photos of one piece's pages: choose PNG or JPEG photos only.")).toBeVisible();
    expect(sent).toHaveLength(3);
});
