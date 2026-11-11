// A PDF read as it is (0023; Andrei, 2026-11-04): added from the library, kept on the device byte for byte, shown page by
// page in its own view with the score's bar, opened again at the page it was left on, and handed back as it was added.
import { expect, test } from "@playwright/test";
import { pdf } from "./pdf-fixture.mjs";
import { violations, librarySessions } from "./steps.mjs";

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
    await librarySessions(page);
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
test("the pen writes on a PDF's pages and the bookmarks mark pages; both stay with the piece", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "Etude.pdf", mimeType: "application/pdf", buffer: pdf(3) });
    const sheets = page.locator(".pdf-view .score-page.pdf-page");
    await expect(sheets.first().locator("canvas")).toHaveCount(1);
    const bar = page.locator(".bar");

    // The mouse writes with the pen on: a line across page 1, with the palette up.
    await bar.getByRole("button", { name: "Pen", exact: true }).click();
    const tools = page.getByRole("toolbar", { name: "Pencil marks" });
    await expect(tools).toBeVisible();
    const box = await sheets.first().boundingBox();
    await page.mouse.move(box.x + box.width * 0.2, box.y + box.height * 0.5);
    await page.mouse.down();
    for (let step = 1; step <= 10; step++) {
        await page.mouse.move(box.x + box.width * (0.2 + step * 0.05), box.y + box.height * 0.5 + step * 2);
    }
    await page.mouse.up();
    const marks = page.locator(".pdf-view .pencil-mark");
    await expect(marks).toHaveCount(1);
    await expect(tools.getByRole("button", { name: "Undo" })).toBeEnabled();

    // The bookmark in the bar marks the page under it, which then carries a ribbon; the Contents lists it, named.
    await bar.getByRole("button", { name: "Bookmark this page" }).click();
    await expect(bar.getByRole("button", { name: "Bookmark this page" })).toHaveAttribute("aria-pressed", "true");
    await expect(sheets.first()).toHaveClass(/\bbookmarked\b/);
    await bar.locator("button.ttl").click();
    const contents = page.getByRole("dialog", { name: "Contents" });
    await contents.getByLabel("Name for the bookmark on p. 1").fill("Opening");
    await contents.getByLabel("Name for the bookmark on p. 1").press("Enter");

    // After a reload the stroke and the bookmark are still there.
    await page.waitForTimeout(800);
    await page.reload();
    await expect(sheets.first().locator("canvas")).toHaveCount(1);
    await expect(marks).toHaveCount(1);
    await expect(sheets.first()).toHaveClass(/\bbookmarked\b/);
    await bar.locator("button.ttl").click();
    await expect(contents.getByLabel("Name for the bookmark on p. 1")).toHaveValue("Opening");

    // The Contents goes to a page, and the bookmark comes off again.
    await contents.getByRole("button", { name: "Remove the bookmark on p. 1" }).click();
    await expect(contents).toContainText("No pages bookmarked yet");
    await expect(sheets.first()).not.toHaveClass(/\bbookmarked\b/);
    expect(errors).toEqual([]);
});

test("the metronome clicks at a tempo the player sets, shows the beat, and keeps the tempo for the piece", async ({ page }) => {
    const errors = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "Waltz.pdf", mimeType: "application/pdf", buffer: pdf(1) });
    await expect(page.locator(".pdf-view .score-page canvas")).toHaveCount(1);

    await page.locator(".bar").getByRole("button", { name: "Metronome" }).click();
    const pill = page.getByRole("toolbar", { name: "Metronome" });
    await expect(pill).toBeVisible();
    await expect(pill.locator(".beat")).toHaveCount(4);
    expect(await violations(page)).toEqual([]);

    // Faster twice, and three beats to the bar: a waltz.
    await pill.getByRole("button", { name: "Faster" }).click();
    await pill.getByRole("button", { name: "Faster" }).click();
    await expect(pill.locator(".bpm .mono")).toHaveText("88");
    // The beats go 4, 6, 2, 3 and round again.
    const beats = pill.getByRole("button", { name: /beats to the bar/ });
    await beats.click();
    await expect(beats).toContainText("6");
    await beats.click();
    await beats.click();
    await expect(pill.locator(".beat")).toHaveCount(3);

    // Started, the beat moves from dot to dot; stopped, no dot is lit.
    await pill.getByRole("button", { name: "Start the metronome" }).click();
    await expect(pill.locator(".beat.now")).toHaveCount(1);
    const seen = new Set();
    for (let i = 0; i < 20 && seen.size < 2; i++) {
        seen.add(await pill.locator(".beat").evaluateAll((dots) => dots.findIndex((dot) => dot.classList.contains("now"))));
        await page.waitForTimeout(150);
    }
    expect(seen.size).toBeGreaterThan(1);
    await pill.getByRole("button", { name: "Stop the metronome" }).click();
    await expect(pill.locator(".beat.now")).toHaveCount(0);

    // Tapping in time sets the tempo.
    const tap = pill.getByRole("button", { name: /beats a minute/ });
    for (let i = 0; i < 4; i++) {
        await tap.click();
        await page.waitForTimeout(500);
    }
    await expect(pill.locator(".bpm .mono")).toHaveText(/^1[01]\d$/);

    // The piece keeps its tempo and beats.
    const kept = await pill.locator(".bpm .mono").textContent();
    await page.reload();
    await page.locator(".bar").getByRole("button", { name: "Metronome" }).click();
    await expect(pill.locator(".bpm .mono")).toHaveText(kept);
    await expect(pill.locator(".beat")).toHaveCount(3);
    expect(errors).toEqual([]);
});
