// A PDF piece recognised (0023; Andrei, 2026-11-04): Read the notes sends the kept PDF to the scan server, the notes open
// in a Notes view with their bars to check, and PDF | Notes in the bar switches between the pages and the notes at
// roughly the same place. The scan server is played by the test, answering with what homr read from Für Elise.
// Runs in desktop Chromium only (playwright.config.mjs).
import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { pdf } from "./pdf-fixture.mjs";
import { violations } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

test("a PDF's notes are read from its pages, and PDF | Notes switches between them at the same place", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    const file = pdf(3);
    const uploads = [];
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages" };
    await page.route(`${server}/health`, (route) => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, (route) => {
        uploads.push(route.request().postDataBuffer());
        return route.fulfill({ body: musicXml, contentType: "application/vnd.recordare.musicxml+xml", headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "3" } });
    });
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();

    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "Elise.pdf", mimeType: "application/pdf", buffer: file });
    const sheets = page.locator(".pdf-view .score-page.pdf-page");
    await expect(sheets.first().locator("canvas")).toHaveCount(1);
    const bar = page.locator(".bar");
    await expect(bar.getByRole("radiogroup", { name: "View" })).toHaveCount(0);

    // More offers to read the notes; the panel says where the PDF goes, and sends it, byte for byte.
    await bar.getByRole("button", { name: "More" }).click();
    await page.getByRole("dialog", { name: "More" }).getByRole("button", { name: "Read the notes" }).click();
    const panel = page.getByRole("dialog", { name: "Read the notes" });
    await expect(panel).toContainText(`goes to the scan server at ${server}/ on this computer`);
    expect(await violations(page)).toEqual([]);
    await panel.getByRole("button", { name: "Read the notes" }).click();

    // The Notes view opens with the bars to check, and the bar switches between the views.
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+&resume=r_[0-9a-z_]+&at=0$/);
    expect(uploads).toHaveLength(1);
    expect(uploads[0].includes(file)).toBe(true);
    await expect(page.locator(".sheet.check-sheet").getByRole("heading", { level: 2 })).toHaveText("7 bars to review");
    const views = bar.getByRole("radiogroup", { name: "View" });
    await expect(views.getByRole("radio", { name: "Notes" })).toHaveAttribute("aria-checked", "true");
    await expect(page.locator(".score .score-page svg").first()).toBeVisible();
    await expect(bar.locator(".ttl")).toHaveText("Elise");
    expect(await violations(page)).toEqual([]);

    // PDF goes back to the pages, which keep only the PDF's things: no Read the notes now, and Download still hands back the PDF.
    await views.getByRole("radio", { name: "PDF" }).click();
    await expect(page).toHaveURL(/\/pdf\?project=p_[0-9a-f]+&page=1$/);
    await expect(sheets.first().locator("canvas")).toHaveCount(1);
    await expect(views.getByRole("radio", { name: "PDF" })).toHaveAttribute("aria-checked", "true");
    await bar.getByRole("button", { name: "More" }).click();
    const more = page.getByRole("dialog", { name: "More" });
    await expect(more.getByRole("button", { name: "Read the notes" })).toHaveCount(0);
    const [download] = await Promise.all([page.waitForEvent("download"), more.getByRole("button", { name: "Download the PDF" }).click()]);
    expect(download.suggestedFilename()).toBe("Elise.pdf");
    expect(await violations(page)).toEqual([]);

    // From page 3 of the PDF, the notes open where the scan's page 3 starts: two thirds through the 32 bars, with no marks from the server.
    await page.locator(".pdf-view").evaluate((view) => view.scrollTo({ top: view.scrollHeight }));
    await expect(bar.locator(".pg")).toHaveText("p. 3 of 3");
    await views.getByRole("radio", { name: "Notes" }).click();
    await expect(page).toHaveURL(/&at=([1-9]\d*)$/);

    // A reload keeps each view: the PDF view stays the PDF's.
    await page.goto(page.url().replace(/\/playback\?project=([^&]+).*/, "/pdf?project=$1"));
    await expect(sheets.first().locator("canvas")).toHaveCount(1);
    await expect(views.getByRole("radio", { name: "PDF" })).toHaveAttribute("aria-checked", "true");
    // Leaving the PDF view lets go of the document without an error.
    await bar.getByRole("link", { name: "Library" }).click();
    await expect(page.locator(".lib-grid .card.piece", { hasText: "Elise" })).toHaveCount(1);
    await expect(page.locator("#blazor-error-ui")).toBeHidden();
    expect(errors).toEqual([]);
});
