// The new sources (version 0.6, behind experimental.sources; decision record 0030; sources sheet, 2026-11-14): the switch
// of all the piece's views where PDF | Notes stood, the Sources button after it, and the Sources sheet of Originals and
// Notes: an original added without recognising it, the eye that hides a view from the switch, Recognise as new Notes beside
// the working ones, Make working with Undo, and Delete with its warning, a tombstone and Undo. The scan server is played by
// the test, answering with what homr read from Für Elise. Runs in desktop Chromium only (playwright.config.mjs).
import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { pdf } from "./pdf-fixture.mjs";
import { violations } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../../../../tests/backend/Unit/MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const server = "http://localhost:8765";
const shots = process.env.SOURCES_SHOTS;

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

async function scanServer(page) {
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages" };
    await page.route(`${server}/health`, (route) => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, (route) =>
        route.fulfill({ body: musicXml, contentType: "application/vnd.recordare.musicxml+xml", headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "1" } }));
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();
}

/** A PDF piece recognised into its working notes: the notes' page, with PDF | Notes as two views. */
async function recognisedPiece(page) {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "Elise.pdf", mimeType: "application/pdf", buffer: pdf(3) });
    await expect(page.locator(".pdf-view .score-page.pdf-page").first().locator("canvas")).toHaveCount(1);
    const bar = page.locator(".bar");
    await bar.getByRole("button", { name: "Recognise notes" }).click();
    await page.getByRole("dialog", { name: "Recognise notes" }).getByRole("button", { name: "Recognise notes" }).click();
    await expect(page).toHaveURL(/\/playback\?project=p_/);
    await expect(page.locator(".score .score-page svg").first()).toBeVisible();
}

const shoot = async (page, name) => {
    if (shots) {
        await page.screenshot({ path: `${shots}/${name}.png` });
    }
};

test("the Sources sheet lists the originals and the notes, adds a PDF, recognises it as new notes, and the switch shows every view", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.sources", "on"));
    await scanServer(page);
    await recognisedPiece(page);
    const bar = page.locator(".bar");

    // Two views, the PDF first, then the working notes with their dot (#582 item 2); the Sources button after them, and no «N версии».
    const views = bar.getByRole("radiogroup", { name: "View" });
    await expect(views.getByRole("radio")).toHaveText(["PDF", "Notes"]);
    await expect(views.getByRole("radio", { name: "Notes" })).toHaveAttribute("aria-checked", "true");
    await expect(views.locator(".working-dot")).toHaveCount(1);
    const sourcesButton = bar.locator(".sources-button");
    await expect(sourcesButton).toHaveText("Sources");
    await expect(bar.locator(".versions-label")).toHaveCount(0);

    // The sheet: the PDF among the originals, the working notes among the notes, tinted as the view shown, with no Open.
    await sourcesButton.click();
    await expect(sourcesButton).toHaveAttribute("aria-expanded", "true");
    const sheet = page.locator("aside.sources-sheet");
    await expect(sheet.getByRole("heading", { name: "Sources" })).toBeVisible();
    await expect(sheet.locator(".overline")).toHaveText(["Originals · 1", "Notes · 1"]);
    const originals = sheet.getByRole("list", { name: "Originals" });
    const notes = sheet.getByRole("list", { name: "Notes" });
    const elise = originals.locator(".source-row", { hasText: "Elise.pdf" });
    await expect(elise.locator(".caption").first()).toHaveText("PDF · 3 pages");
    await expect(elise.getByRole("button", { name: "Show in the switch" })).toHaveAttribute("aria-pressed", "true");
    const working = notes.locator(".source-row").first();
    await expect(working).toHaveClass(/\bselected\b/);
    await expect(working.locator(".tag.working")).toHaveText("Working");
    await expect(working.getByRole("button", { name: "Open" })).toHaveCount(0);
    await expect(working.getByRole("button", { name: /^More/ })).toHaveCount(0);
    await expect(working.getByRole("img", { name: "Recognised from a PDF" })).toHaveCount(1);
    expect(await violations(page)).toEqual([]);
    await shoot(page, "1-sheet");

    // + Add more (#582 item 5): a PDF kept as it is, not recognised, with Recognise and Pro offered where the server is Motiff Lab's.
    await sheet.getByRole("button", { name: "Add more" }).click();
    const menu = sheet.getByRole("menu", { name: "Add more" });
    await expect(menu.getByRole("menuitem")).toHaveCount(4);
    await shoot(page, "2-add-original");
    await menu.getByLabel("PDF", { exact: true }).setInputFiles({ name: "Henle.pdf", mimeType: "application/pdf", buffer: pdf(2) });
    await expect(sheet.locator(".overline").first()).toHaveText("Originals · 2");
    const henle = originals.locator(".source-row", { hasText: "Henle.pdf" });
    await expect(henle.locator(".caption").first()).toHaveText("PDF · 2 pages");
    await expect(page.locator(".merge-toast")).toContainText("“Henle.pdf” added");
    await expect(views.getByRole("radio")).toHaveText(["PDF", "Notes", "PDF"]);

    // The eye hides it from the switch, and shows it again.
    await henle.getByRole("button", { name: "Show in the switch" }).click();
    await expect(henle.getByRole("button", { name: "Show in the switch" })).toHaveAttribute("aria-pressed", "false");
    await expect(views.getByRole("radio")).toHaveText(["PDF", "Notes"]);
    await henle.getByRole("button", { name: "Show in the switch" }).click();
    await expect(views.getByRole("radio")).toHaveText(["PDF", "Notes", "PDF"]);

    // Recognise: one line says where the pages go, the button sends them, and the new notes stand beside the working ones.
    await henle.getByRole("button", { name: "Recognise" }).click();
    const panel = henle.getByRole("group", { name: "Recognise notes" });
    await expect(panel).toContainText(`2 pages go to the scan server set in Settings, ${server}/`);
    await shoot(page, "3-recognise");
    await panel.getByRole("button", { name: "Send 2 pages" }).click();
    await expect(page.locator(".merge-toast")).toContainText("Notes recognised");
    await expect(sheet.locator(".overline").nth(1)).toHaveText("Notes · 2");
    const read = notes.locator(".source-row", { hasText: "Henle.pdf" });
    await expect(read).toHaveClass(/\bfresh\b/);
    await expect(read.locator(".caption").first()).toContainText("homr 0.7.0");
    await expect(henle.getByRole("button", { name: "Recognise" })).toHaveCount(0);

    // Four views: the switch is a menu now, a group per file, each PDF before its notes; the foot opens Sources.
    await expect(views).toHaveCount(0);
    const viewsButton = bar.getByRole("button", { name: "View: Notes" });
    await viewsButton.click();
    const list = bar.getByRole("menu", { name: "View" });
    await expect(list.locator(".views-group")).toHaveText(["Elise.pdf", "Henle.pdf"]);
    await expect(list.getByRole("menuitemradio")).toHaveCount(4);
    await expect(list.getByRole("menuitemradio").nth(1)).toHaveAttribute("aria-checked", "true");
    await expect(list.getByRole("menuitem")).toHaveText(/Sources/);
    expect(await violations(page)).toEqual([]);
    await shoot(page, "4-switch-menu");

    // The notes read from Henle.pdf, shown read-only; the sheet tints their row.
    await list.getByRole("menuitemradio").nth(3).click();
    await expect(read).toHaveClass(/\bselected\b/);
    await expect(page.locator(".score .score-page svg").first()).toBeVisible();

    // Make working: the badge moves, the toast says so, and Undo puts the notes until now back.
    await read.getByRole("button", { name: /^More/ }).click();
    await read.getByRole("menuitem", { name: "Make working" }).click();
    await expect(page.locator(".merge-toast")).toContainText("Now working: the notes from Henle.pdf");
    await shoot(page, "5-made-working");
    await page.locator(".merge-toast").getByRole("button", { name: "Undo" }).click();
    expect(errors).toEqual([]);
});

test("deleting an original warns what was made from it, leaves a tombstone, and Undo brings it back", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.sources", "on"));
    await scanServer(page);
    await recognisedPiece(page);
    await page.locator(".bar .sources-button").click();
    const sheet = page.locator("aside.sources-sheet");
    const elise = sheet.getByRole("list", { name: "Originals" }).locator(".source-row", { hasText: "Elise.pdf" });

    // The warning names the notes made from it; the working notes were, so only the original can go.
    await elise.getByRole("button", { name: /^More/ }).click();
    await elise.getByRole("menuitem", { name: "Delete…" }).click();
    const dialog = page.getByRole("dialog", { name: "Delete “Elise.pdf”?" });
    await expect(dialog).toContainText("Notes made from this original:");
    await expect(dialog.getByRole("button", { name: "Delete with its notes" })).toHaveCount(0);
    expect(await violations(page)).toEqual([]);
    await shoot(page, "6-delete-warning");
    await dialog.getByRole("button", { name: "Delete only the original" }).click();

    // The tombstone: its notes stay and say so; the PDF leaves the switch.
    await expect(page.locator(".merge-toast")).toContainText("Original deleted, the notes stay");
    await expect(elise.locator(".caption").first()).toHaveText("Original deleted · the notes stay");
    await expect(elise.getByText("Add the same file")).toBeVisible();
    await expect(sheet.locator(".overline").first()).toHaveText("Originals · 0");
    await expect(sheet.getByRole("list", { name: "Notes" }).locator(".source-row").first()).toContainText("Original deleted");
    await expect(page.locator(".bar").getByRole("radiogroup", { name: "View" })).toHaveCount(0);
    await shoot(page, "7-tombstone");

    await page.locator(".merge-toast").getByRole("button", { name: "Undo" }).click();
    await expect(elise.locator(".caption").first()).toHaveText("PDF · 3 pages");
    await expect(page.locator(".bar").getByRole("radiogroup", { name: "View" })).toHaveCount(1);
    expect(errors).toEqual([]);
});

test("in Russian the sheet and the switch speak Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.sources", "on"));
    await scanServer(page);
    await page.evaluate(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await page.goto("/");
    await page.locator("header button.add-piece").click();
    await page.getByRole("dialog", { name: "Добавить пьесу" }).getByLabel("Выбрать файл MIDI, MusicXML или PDF").setInputFiles({ name: "Элиза.pdf", mimeType: "application/pdf", buffer: pdf(3) });
    await expect(page.locator(".pdf-view .score-page.pdf-page").first().locator("canvas")).toHaveCount(1);
    await page.locator(".bar").getByRole("button", { name: "Распознать ноты" }).click();
    await page.getByRole("dialog", { name: "Распознать ноты" }).getByRole("button", { name: "Распознать ноты" }).click();
    await expect(page.locator(".score .score-page svg").first()).toBeVisible();
    await expect(page.locator(".bar").getByRole("radiogroup", { name: "Вид" }).getByRole("radio")).toHaveText(["PDF", "Ноты"]);
    await page.locator(".bar .sources-button").click();
    const sheet = page.locator("aside.sources-sheet");
    await expect(sheet.locator(".overline")).toHaveText(["Оригиналы · 1", "Ноты · 1"]);
    await expect(sheet.locator(".source-row .caption").first()).toHaveText("PDF · 3 страницы");
    await expect(sheet.locator(".tag.working")).toHaveText("Рабочие");
    await shoot(page, "8-sheet-ru");
});
