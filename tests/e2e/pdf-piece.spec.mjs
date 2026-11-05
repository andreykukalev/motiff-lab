// A PDF read as it is (0023; Andrei, 2026-11-04): added from the library, kept on the device byte for byte, shown page by
// page in its own view with the score's bar, opened again at the page it was left on, and handed back as it was added.
import { expect, test } from "@playwright/test";
import { pdf } from "./pdf-fixture.mjs";
import { violations } from "./steps.mjs";

test("a PDF added from the library is read page by page, opens again where it was left, and downloads as it was added", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    const file = pdf(3);
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "Nocturne.pdf", mimeType: "application/pdf", buffer: file });

    await expect(page).toHaveURL(/\/pdf\?project=p_[0-9a-f]+$/);
    await expect(page.locator(".bar .ttl")).toHaveText("Nocturne");
    await expect(page.locator(".bar .pdf-tag")).toHaveText("PDF");
    const sheets = page.locator(".pdf-view .score-page.pdf-page");
    await expect(sheets).toHaveCount(3);
    await expect(sheets.first()).toHaveAttribute("aria-label", "Page 1 of 3");
    // Page 1 is drawn, at the page's own proportions.
    await expect(sheets.first().locator("canvas")).toHaveCount(1);
    const box = await sheets.first().boundingBox();
    expect(box.height / box.width).toBeCloseTo(842 / 595, 1);
    // Two pages to a row in landscape, like an open book; one in portrait.
    await expect(page.locator(".bar .pg")).toHaveText(/^pp?\. 1(–2)? of 3$/);
    // Nothing plays here: the bar has no transport, roll or Insights.
    await expect(page.locator(".bar").getByRole("button", { name: "Play" })).toHaveCount(0);
    expect(await violations(page)).toEqual([]);

    // Scrolled to page 3, the bar says so, and the page is drawn.
    await sheets.nth(2).scrollIntoViewIfNeeded();
    await page.locator(".pdf-view").evaluate((view) => view.scrollTo({ top: view.scrollHeight }));
    await expect(page.locator(".bar .pg")).toHaveText("p. 3 of 3");
    await expect(sheets.nth(2).locator("canvas")).toHaveCount(1);

    // The library shows it as a PDF with its first page as the cover; opening it again lands on page 3.
    await page.locator(".bar a.act").click();
    const card = page.locator(".lib-grid .card.piece", { hasText: "Nocturne" });
    await expect(card.locator(".what .meta")).toContainText("PDF · 3 pages");
    await expect(card.locator(".thumb img")).toHaveCount(1);
    await expect(card.locator(".resume-name")).toHaveText("The printed pages, as they are");
    await card.getByRole("button", { name: /Resume/ }).click();
    await expect(page.locator(".bar .pg")).toHaveText("p. 3 of 3");

    // More hands back the PDF exactly as it was added.
    await page.getByRole("button", { name: "More" }).click();
    const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: "Download the PDF" }).click()]);
    expect(download.suggestedFilename()).toBe("Nocturne.pdf");
    const saved = await download.createReadStream();
    const chunks = [];
    for await (const chunk of saved) {
        chunks.push(chunk);
    }
    expect(Buffer.concat(chunks).equals(file)).toBe(true);
    expect(errors).toEqual([]);
});

test("a file named .pdf that is not one is refused in the sheet", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    const sheet = page.getByRole("dialog", { name: "Add a piece" });
    await sheet.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "fake.pdf", mimeType: "application/pdf", buffer: Buffer.from("hello") });
    await expect(sheet.getByRole("alert")).toContainText("fake.pdf: This file is not a PDF");
});

test("in Russian the PDF view speaks Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await page.goto("/");
    await page.locator("header button.add-piece").click();
    await page.getByRole("dialog", { name: "Добавить пьесу" }).getByLabel("Выбрать файл MIDI, MusicXML или PDF").setInputFiles({ name: "Ноктюрн.pdf", mimeType: "application/pdf", buffer: pdf(2) });
    await expect(page.locator(".bar .pg")).toHaveText(/1(–2)? из 2$/);
    await expect(page.locator(".pdf-view .score-page").first()).toHaveAttribute("aria-label", "Страница 1 из 2");
});