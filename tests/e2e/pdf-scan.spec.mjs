// A PDF piece recognised (0023; testing item 16, Andrei, 2026-11-06: "the PDF is available right inside the piece"):
// Recognise notes, in the PDF's bar (Andrei, 2026-11-11), sends the kept PDF to the scan server, and the notes go into the
// same piece with their bars to check, while the PDF and its marks stay exactly as they were; Original PDF | Notes switches
// between the two at the same place. Add a version on a
// PDF (0022) reads the notes first in the same way and opens Sources on the piece. The scan server is played by the
// test, answering with what homr read from Für Elise. Runs in desktop Chromium only (playwright.config.mjs).
import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { pdf } from "./pdf-fixture.mjs";
import { violations } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const cover = fileURLToPath(new URL("fixtures/fur-elise-cover.mid", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

/** Plays the scan server and points the app at it; returns the PDFs it was sent. */
async function scanServer(page) {
    const uploads = [];
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Page-Report" };
    // A report on each of the PDF's 3 pages (version 0.4), while the notes it sends are one page's: the reports do not
    // match the pages of the notes, so the notes are kept without them.
    const report = JSON.stringify([1, 2, 3].map((number) => ({ page: number, rotation: 0, sharpness: 1.1, contrast: 50, staves: 4, noteheads: 60, barLines: 12, spacePx: 15, measures: 11 })));
    await page.route(`${server}/health`, (route) => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, (route) => {
        uploads.push(route.request().postDataBuffer());
        return route.fulfill({ body: musicXml, contentType: "application/vnd.recordare.musicxml+xml", headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "3", "X-Page-Report": report } });
    });
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();
    return uploads;
}

async function addPdf(page, file) {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "Elise.pdf", mimeType: "application/pdf", buffer: file });
    await expect(page.locator(".pdf-view .score-page.pdf-page").first().locator("canvas")).toHaveCount(1);
    await expect(page).toHaveURL(/\/pdf\?project=p_[0-9a-f]+/);
    return new URL(page.url()).searchParams.get("project");
}

test("a PDF's notes are read into the same piece, the PDF stays as it was, and PDF | Notes switches between them", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    const file = pdf(3);
    const uploads = await scanServer(page);
    const pdfId = await addPdf(page, file);
    const bar = page.locator(".bar");
    await expect(bar.getByRole("radiogroup", { name: "View" })).toHaveCount(0);

    // The bar offers to recognise the notes, and More no longer does; the panel says where the PDF goes and that it stays as
    // it is, and sends it, byte for byte.
    await bar.getByRole("button", { name: "More" }).click();
    await expect(page.getByRole("dialog", { name: "More" }).getByRole("button", { name: "Recognise notes" })).toHaveCount(0);
    await page.locator("button.scrim").click();
    await bar.getByRole("button", { name: "Recognise notes" }).click();
    const panel = page.getByRole("dialog", { name: "Recognise notes" });
    await expect(panel).toContainText(`goes to the scan server at ${server}/ on this computer`);
    await expect(panel).toContainText("The notes go into this same piece");
    expect(await violations(page)).toEqual([]);
    await panel.getByRole("button", { name: "Recognise notes" }).click();

    // The notes open in the same piece with the bars to check, and Original PDF | Notes switches to its PDF.
    await expect(page).toHaveURL(new RegExp(`/playback\\?project=${pdfId}&resume=r_[0-9a-z_]+&at=0$`));
    expect(uploads).toHaveLength(1);
    expect(uploads[0].includes(file)).toBe(true);
    await expect(page.locator(".sheet.check-sheet").getByRole("heading", { level: 2 })).toHaveText("7 bars to review");
    await expect(page.locator(".sheet.check-sheet .scan-pages")).toHaveCount(0);
    await expect(page.locator(".score .score-page svg").first()).toBeVisible();
    await expect(bar.locator(".ttl")).toHaveText("Elise");
    const views = bar.getByRole("radiogroup", { name: "View" });
    await expect(views.getByRole("radio", { name: "Notes" })).toHaveAttribute("aria-checked", "true");
    expect(await violations(page)).toEqual([]);

    // Original PDF shows the PDF as it was: its pages, Download hands back the PDF, and no Recognise notes now.
    await views.getByRole("radio", { name: "Original PDF" }).click();
    await expect(page).toHaveURL(new RegExp(`/pdf\\?project=${pdfId}&page=1$`));
    await expect(page.locator(".pdf-view .score-page.pdf-page").first().locator("canvas")).toHaveCount(1);
    await expect(views.getByRole("radio", { name: "Original PDF" })).toHaveAttribute("aria-checked", "true");
    await expect(bar.getByRole("button", { name: "Recognise notes" })).toHaveCount(0);
    await bar.getByRole("button", { name: "More" }).click();
    const more = page.getByRole("dialog", { name: "More" });
    const [download] = await Promise.all([page.waitForEvent("download"), more.getByRole("button", { name: "Download the PDF" }).click()]);
    expect(download.suggestedFilename()).toBe("Elise.pdf");
    expect(await violations(page)).toEqual([]);

    // From page 3 of the PDF, the notes open where the scan's page 3 starts: two thirds through the bars.
    await page.locator(".pdf-view").evaluate((view) => view.scrollTo({ top: view.scrollHeight }));
    await expect(bar.locator(".pg")).toHaveText("p. 3 of 3");
    await views.getByRole("radio", { name: "Notes" }).click();
    await expect(page).toHaveURL(new RegExp(`/playback\\?project=${pdfId}&.*at=([1-9]\\d*)$`));

    // The library holds one piece.
    await page.goto("/");
    await expect(page.locator(".lib-table tbody tr", { hasText: "Elise" })).toHaveCount(1);
    await expect(page.locator("#blazor-error-ui")).toBeHidden();
    expect(errors).toEqual([]);
});

test("Add a version on a PDF reads its notes first, then opens Sources on the same piece", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    await scanServer(page);
    const pdfId = await addPdf(page, pdf(3));
    const bar = page.locator(".bar");

    await bar.getByRole("button", { name: "More" }).click();
    await page.getByRole("dialog", { name: "More" }).getByRole("button", { name: "Add a version" }).click();
    const panel = page.getByRole("dialog", { name: "Add a version" });
    await expect(panel).toContainText("the notes have to be read from this PDF first");
    expect(await violations(page)).toEqual([]);
    await panel.getByRole("button", { name: "Recognise notes" }).click();

    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+&resume=r_[0-9a-z_]+$/);
    expect(new URL(page.url()).searchParams.get("project")).toBe(pdfId);
    const drawer = page.locator(".perf-sheet");
    await expect(drawer).toBeVisible();
    await expect(drawer.locator(".source-card").first()).toContainText("Notes");
    await drawer.getByRole("button", { name: "Add a source" }).click();
    await drawer.getByLabel("Add a MIDI or MusicXML file").setInputFiles(cover);
    await expect(page.locator(".assembly-offer")).toContainText("Matches in bars");
    expect(await violations(page)).toEqual([]);
    expect(errors).toEqual([]);
});