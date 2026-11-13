// Batch recognition from the library (version 0.5, «Импорт папки», ux-prototype/v0.5/screens/import-web-4..5), behind
// experimental.libraryImport: the PDF pieces with no notes are picked in the list, one consent names the pieces, the pages
// and the server, and each piece is read page by page, its pages sent as images one at a time and their notes joined into
// the same piece. «Без нот» lists the pieces still to read. The scan server is played by the test, answering each page with
// what homr read from Für Elise. Runs in desktop Chromium only (playwright.config.mjs).
import { expect, test } from "@playwright/test";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { pdf } from "./pdf-fixture.mjs";
import { pageErrors, violations } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const server = "http://localhost:8765";

test.use({ serviceWorkers: "block" });

/** Plays the scan server and points the app at it; returns the files it was sent. */
async function scanServer(page) {
    const uploads = [];
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Page-Report" };
    const report = JSON.stringify([{ page: 1, rotation: 0, sharpness: 1.1, contrast: 50, staves: 4, noteheads: 60, barLines: 12, spacePx: 15, measures: 11 }]);
    await page.route(`${server}/health`, (route) => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, (route) => {
        uploads.push(route.request().postDataBuffer());
        return route.fulfill({ body: musicXml, contentType: "application/vnd.recordare.musicxml+xml", headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "1", "X-Page-Report": report } });
    });
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();
    return uploads;
}

async function addPdf(page, name, file) {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name, mimeType: "application/pdf", buffer: file });
    await expect(page).toHaveURL(/\/pdf\?project=p_[0-9a-f]+/);
}

const rows = (page) => page.locator(".lib-table tbody tr");

test("PDF pieces are picked in the list and read after one consent, page by page, into the same pieces", async ({ page }) => {
    const errors = pageErrors(page);
    await page.addInitScript(() => {
        localStorage.setItem("motiff-lab:settings.presetChosen", "on");
        localStorage.setItem("motiff-lab:experimental.libraryImport", "on");
    });
    const uploads = await scanServer(page);
    await addPdf(page, "Etude.pdf", pdf(2));
    await addPdf(page, "Prelude.pdf", pdf(1));
    await page.goto("/");
    await expect(rows(page)).toHaveCount(2);

    // «Без нот» lists the pieces whose notes are not read yet.
    const withoutNotes = page.getByRole("button", { name: "Without notes" });
    await withoutNotes.click();
    await expect(withoutNotes).toHaveAttribute("aria-pressed", "true");
    await expect(rows(page)).toHaveCount(2);
    await withoutNotes.click();

    await page.locator(".lib-head").getByRole("button", { name: "More" }).click();
    await page.locator(".lib-menu").getByRole("button", { name: "Read the notes of several pieces" }).click();
    await page.getByRole("checkbox", { name: "Pick every PDF without notes" }).check();
    await expect(page.locator(".lib-pickbar")).toContainText("Picked: 2 pieces · 3 pages");
    await page.locator(".lib-pickbar").getByRole("button", { name: "Read the notes" }).click();

    // One consent: the pieces, the pages, where they go, how long, and that the notes go into the same pieces.
    const consent = page.getByRole("dialog", { name: "Read the notes" });
    await expect(consent).toContainText("2 pieces, 3 pages");
    await expect(consent).toContainText("Etude");
    await expect(consent).toContainText("Prelude");
    await expect(consent).toContainText("they don't leave it");
    await expect(consent).toContainText("The notes go into these same pieces.");
    expect(await violations(page)).toEqual([]);
    expect(uploads).toHaveLength(0);
    await consent.getByRole("button", { name: "Recognise notes" }).click();
    await expect(consent).toBeHidden();

    // Each page goes as an image, one at a time; each piece has its notes, and its line says so.
    await expect(page.locator(".run-bar")).toContainText("Notes read: 2 of 2", { timeout: 30000 });
    expect(uploads).toHaveLength(3);
    for (const upload of uploads) {
        expect(upload.includes(Buffer.from("\x89PNG", "latin1"))).toBe(true);
    }
    await expect(page.locator(".lib-table .st.done")).toHaveCount(2);
    // No piece is without notes now, so the filter is not offered.
    await expect(withoutNotes).toHaveCount(0);
    await page.locator(".run-bar").getByRole("button", { name: "Done" }).click();
    await expect(page.locator(".run-bar")).toHaveCount(0);

    // The piece opens as notes now.
    await rows(page).filter({ hasText: "Etude" }).getByRole("button", { name: "Etude", exact: true }).click();
    await expect(page).toHaveURL(/\/playback\?project=/);
    expect(errors).toEqual([]);
});

test("with the experiment off, the library offers no batch and no Without notes", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:settings.presetChosen", "on"));
    await addPdf(page, "Etude.pdf", pdf(1));
    await page.goto("/");
    await expect(rows(page)).toHaveCount(1);
    await expect(page.getByRole("button", { name: "Without notes" })).toHaveCount(0);
    await page.locator(".lib-head").getByRole("button", { name: "More" }).click();
    await expect(page.locator(".lib-menu").getByRole("button", { name: "Read the notes of several pieces" })).toHaveCount(0);
});

test("Read the notes now, for all, in the folder sheet adds the pieces and reads each PDF after one consent", async ({ page }) => {
    const errors = pageErrors(page);
    // A browser that copies the folder's files (Safari, Firefox): no folder picker to keep it.
    await page.addInitScript(() => {
        delete window.showDirectoryPicker;
        localStorage.setItem("motiff-lab:settings.presetChosen", "on");
        localStorage.setItem("motiff-lab:experimental.libraryImport", "on");
    });
    const uploads = await scanServer(page);
    const root = join(mkdtempSync(join(tmpdir(), "motiff-folder-")), "Notes");
    mkdirSync(join(root, "Debussy"), { recursive: true });
    writeFileSync(join(root, "Debussy", "Arabesque No. 1.pdf"), pdf(2));
    writeFileSync(join(root, "Debussy", "Reverie.pdf"), pdf(1));
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog").getByLabel("Add a folder").setInputFiles(root);
    const sheet = page.getByRole("dialog", { name: "What was found" });
    await expect(sheet.locator("tbody tr:has(td)")).toHaveCount(2);
    await sheet.getByRole("radio", { name: "now, all of them" }).click();
    await sheet.getByRole("button", { name: "Add 2 pieces" }).click();

    // Nothing is added or sent before the consent's yes.
    const consent = page.getByRole("dialog", { name: "Read the notes" });
    await expect(consent).toContainText("2 pieces, 3 pages");
    expect(uploads).toHaveLength(0);
    await consent.getByRole("button", { name: "Recognise notes" }).click();

    await expect(page.locator(".recognition-bar")).toContainText("Notes read: 2 of 2", { timeout: 30000 });
    expect(uploads).toHaveLength(3);
    await expect(page.locator(".lib-table .st.done")).toHaveCount(2);
    expect(errors).toEqual([]);
});
