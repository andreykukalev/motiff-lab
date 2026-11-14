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
import { openRoll, selectPhrase, violations } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const cover = fileURLToPath(new URL("fixtures/fur-elise-cover.mid", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

/**
 * Plays the scan server and points the app at it; returns the PDFs it was sent. It reads `maxPages` pages a request, as
 * it says on /health, and `hold` (a promise) keeps the answer to the first upload back until it settles.
 */
async function scanServer(page, { maxPages = 8, hold = null } = {}) {
    const uploads = [];
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Page-Report" };
    // A report on each of the PDF's 3 pages (version 0.4), while the notes it sends are one page's: the reports do not
    // match the pages of the notes, so the notes are kept without them.
    const report = JSON.stringify([1, 2, 3].map((number) => ({ page: number, rotation: 0, sharpness: 1.1, contrast: 50, staves: 4, noteheads: 60, barLines: 12, spacePx: 15, measures: 11 })));
    await page.route(`${server}/health`, (route) => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages }, headers: cors }));
    await page.route(`${server}/recognise`, async (route) => {
        uploads.push(route.request().postDataBuffer());
        if (uploads.length === 1 && hold) await hold;
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
    await expect(panel).toContainText(`to the scan server set in Settings, ${server}/ on this computer`);
    await expect(panel.getByRole("switch")).toHaveCount(0);
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

    // Original PDF shows the PDF as it was: its pages, Download hands back the PDF, and no Recognise notes now. It was left
    // at page 1 for Recognise notes, so it comes back there (#511 section 10), with no page asked for.
    await views.getByRole("radio", { name: "Original PDF" }).click();
    await expect(page).toHaveURL(new RegExp(`/pdf\\?project=${pdfId}$`));
    await expect(bar.locator(".pg")).toHaveText(/^pp?\. 1(–2)? of 3$/);
    await expect(page.locator(".pdf-view .score-page.pdf-page").first().locator("canvas")).toHaveCount(1);
    await expect(views.getByRole("radio", { name: "Original PDF" })).toHaveAttribute("aria-checked", "true");
    await expect(bar.getByRole("button", { name: "Recognise notes" })).toHaveCount(0);
    await bar.getByRole("button", { name: "More" }).click();
    const more = page.getByRole("dialog", { name: "More" });
    const [download] = await Promise.all([page.waitForEvent("download"), more.getByRole("button", { name: "Download the PDF" }).click()]);
    expect(download.suggestedFilename()).toBe("Elise.pdf");
    expect(await violations(page)).toEqual([]);

    // With no "Notes" view kept on the device, from page 3 of the PDF the notes open where the scan's page 3 starts: two
    // thirds through the bars.
    await page.locator(".pdf-view").evaluate((view) => view.scrollTo({ top: view.scrollHeight }));
    await expect(bar.locator(".pg")).toHaveText("p. 3 of 3");
    await page.evaluate((id) => localStorage.removeItem(`motiff-lab:notes.view.${id}`), pdfId);
    await views.getByRole("radio", { name: "Notes" }).click();
    await expect(page).toHaveURL(new RegExp(`/playback\\?project=${pdfId}&.*at=([1-9]\\d*)$`));

    // The library holds one piece.
    await page.goto("/");
    await expect(page.locator(".lib-table tbody tr", { hasText: "Elise" })).toHaveCount(1);
    await expect(page.locator("#blazor-error-ui")).toBeHidden();
    expect(errors).toEqual([]);
});

test("PDF | Notes keeps each view as it was left: the notes' selection, place and roll, the PDF's page and place (#511 10)", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    await scanServer(page);
    const pdfId = await addPdf(page, pdf(3));
    const bar = page.locator(".bar");
    await bar.getByRole("button", { name: "Recognise notes" }).click();
    await page.getByRole("dialog", { name: "Recognise notes" }).getByRole("button", { name: "Recognise notes" }).click();
    await expect(page).toHaveURL(new RegExp(`/playback\\?project=${pdfId}&resume=`));
    await expect(page.locator(".score .score-page svg").first()).toBeVisible();
    const views = bar.getByRole("radiogroup", { name: "View" });
    const notes = views.getByRole("radio", { name: "Notes" });
    const original = views.getByRole("radio", { name: "Original PDF" });

    // In Notes: a passage selected, the roll's drawer open and zoomed in, and the score scrolled down by hand.
    const status = await selectPhrase(page, 1);
    const selected = await status.textContent();
    await openRoll(page);
    const zoom = page.locator(".roll-drawer .roll-zoom-level");
    // The Check sheet of the scan's bars lies over the roll's zoom on this screen.
    await page.locator(".roll-drawer .roll").getByRole("button", { name: "Zoom in" }).evaluate((button) => button.click());
    await expect(zoom).toContainText("125 %");
    const score = page.locator("div.score");
    const scrolled = await score.evaluate((host) => {
        host.scrollTop = Math.min(host.scrollHeight - host.clientHeight, 260);
        return host.scrollTop;
    });
    expect(scrolled).toBeGreaterThan(0);
    // The scroll is read when the view goes, after a frame.
    await page.waitForTimeout(100);

    await original.click();
    await expect(page.locator(".pdf-view .score-page.pdf-page").first().locator("canvas")).toHaveCount(1);
    // In the PDF: a third of the way down the last page (two pages to a row on a wide screen).
    const pdfTop = await page.locator(".pdf-view").evaluate((view) => {
        const last = view.querySelector('.pdf-page[data-page="3"]');
        view.scrollTo({ top: Math.min(view.scrollHeight - view.clientHeight, last.offsetTop + last.offsetHeight / 3), behavior: "instant" });
        return view.scrollTop;
    });
    await expect(bar.locator(".pg")).toHaveText("p. 3 of 3");

    // Back and forth three times: each view comes back as it was left, and nothing drifts.
    for (let round = 0; round < 3; round++) {
        await notes.click();
        await expect(page).toHaveURL(new RegExp(`/playback\\?project=${pdfId}&resume=r_[0-9a-z_]+$`));
        await expect(page.locator(".selection-status")).toHaveText(selected ?? "");
        await expect(zoom).toContainText("125 %");
        await expect.poll(() => score.evaluate((host) => host.scrollTop)).toBeCloseTo(scrolled, 0);

        await original.click();
        await expect(page).toHaveURL(new RegExp(`/pdf\\?project=${pdfId}$`));
        await expect(bar.locator(".pg")).toHaveText("p. 3 of 3");
        await expect.poll(() => page.locator(".pdf-view").evaluate((view) => view.scrollTop)).toBeCloseTo(pdfTop, 0);
    }

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

test("a PDF longer than the server reads at a time goes in parts, one after another, with a bar while it is read (#479)", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    let release;
    const hold = new Promise((resolve) => (release = resolve));
    const uploads = await scanServer(page, { maxPages: 4, hold });
    const pdfId = await addPdf(page, pdf(6));

    // No switch to turn on: Recognise notes sends it, and the bar says which part is read and about how long is left.
    await page.locator(".bar").getByRole("button", { name: "Recognise notes" }).click();
    const panel = page.getByRole("dialog", { name: "Recognise notes" });
    await expect(panel.getByRole("switch")).toHaveCount(0);
    await panel.getByRole("button", { name: "Recognise notes" }).click();
    await expect(panel.getByRole("progressbar", { name: "Recognising the notes" })).toBeVisible();
    await expect(panel.locator(".scan-progress")).toContainText("Part 1 of 2 · pages 1–4");
    await expect(panel.locator(".scan-progress")).toContainText(/about \d+ min\. left/);
    expect(await violations(page)).toEqual([]);
    expect(uploads).toHaveLength(1);
    release();

    // Two PDFs went, pages 1 to 4 and then 5 and 6, each with its pages as they are; the notes of both open as one piece.
    await expect(page).toHaveURL(new RegExp(`/playback\\?project=${pdfId}&resume=r_[0-9a-z_]+&at=0$`));
    expect(uploads).toHaveLength(2);
    const pagesIn = (upload) => [1, 2, 3, 4, 5, 6].filter((number) => upload.toString("latin1").includes(`(${number}) Tj`));
    expect(pagesIn(uploads[0])).toEqual([1, 2, 3, 4]);
    expect(pagesIn(uploads[1])).toEqual([5, 6]);
    await expect(page.locator(".score .score-page svg").first()).toBeVisible();
    expect(errors).toEqual([]);
});

test("a PDF of more than 100 pages is not sent, and the panel says why", async ({ page }) => {
    const uploads = await scanServer(page, { maxPages: 4 });
    await addPdf(page, pdf(101));

    await page.locator(".bar").getByRole("button", { name: "Recognise notes" }).click();
    const panel = page.getByRole("dialog", { name: "Recognise notes" });
    await panel.getByRole("button", { name: "Recognise notes" }).click();
    await expect(panel.getByRole("alert")).toContainText("This PDF has 101 pages. Recognition reads scores of up to 100 pages");
    expect(uploads).toHaveLength(0);
});