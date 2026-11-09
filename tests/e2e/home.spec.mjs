// The library (decision record 0018, section 9): the pieces as cards, each resumed where it was left; one Add piece
// action with the routes in; the resume points of a piece listed, named and removed in a sheet.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { pageErrors, violations } from "./steps.mjs";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

test("the empty library offers Add a piece and the demo piece", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Motiff Lab");
    await expect(page.locator(".card.add")).toContainText("Transcribe a recording");
    await page.locator(".card.add").getByRole("link", { name: "or try the demo piece" }).click();
    await expect(page.locator(".bar .ttl")).toHaveText("Für Elise");
});

test("Add piece opens a file on the device straight into the score, and leads to scanning and recording", async ({ page }) => {
    const errors = pageErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    const sheet = page.getByRole("dialog", { name: "Add a piece" });
    await expect(sheet).toBeFocused();
    await expect(sheet.getByRole("link", { name: /Scan a page/ })).toHaveAttribute("href", "import/scan");
    await expect(sheet.getByRole("link", { name: /From a recording/ })).toHaveAttribute("href", "import/recording");
    await sheet.press("Escape");
    await expect(sheet).toHaveCount(0);

    // A file the importer cannot read is said so in the sheet, which stays open.
    await page.getByRole("button", { name: "Add piece" }).click();
    await sheet.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles({ name: "notes.mid", mimeType: "audio/midi", buffer: Buffer.from("not a midi file") });
    await expect(sheet.getByRole("alert")).toContainText("notes.mid");

    await sheet.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(minuet);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+&resume=r_0001$/);
    await expect(page.locator(".bar .ttl")).toHaveText("Waltz sketch");
    expect(errors).toEqual([]);
});

test("a card resumes the piece where it was left, opens page 1 afresh, and lists its resume points to name and remove", async ({ page }) => {
    const errors = pageErrors(page);
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(minuet);
    const phrases = page.locator(".score button.bracket[aria-label]");
    await phrases.nth(0).click();
    await expect(page.locator(".selection-status")).toContainText("Selected phrase 1");
    await page.locator(".bar").getByRole("link", { name: "Library" }).click();

    const card = page.locator(".card.piece").filter({ has: page.getByRole("heading", { name: "Waltz sketch" }) });
    await expect(card).toContainText("1 page · 1 phrase");
    await expect(card).toContainText("today · p. 1 · phrase 1");
    await card.getByRole("button", { name: "Resume Waltz sketch" }).click();
    await expect(page).toHaveURL(/resume=r_0001$/);
    await expect(page.locator(".selection-status")).toContainText("Selected phrase 1");

    await page.goto("/");
    await card.getByRole("button", { name: "More for Waltz sketch" }).click();
    await card.getByRole("button", { name: "From page 1", exact: true }).click();
    await expect(page).toHaveURL(/resume=r_0002$/);

    await page.goto("/");
    await card.getByRole("button", { name: "More for Waltz sketch" }).click();
    await card.getByRole("button", { name: "Resume points" }).click();
    const sheet = page.getByRole("dialog", { name: "Waltz sketch" });
    await expect(sheet.locator(".resume-item")).toHaveCount(2);
    const name = sheet.getByRole("textbox", { name: "Name of this resume point" }).last();
    await name.fill("Slow, hands apart");
    await name.press("Enter");
    await sheet.locator(".resume-item").first().getByRole("button", { name: "Remove" }).click();
    await sheet.locator(".resume-item").first().getByRole("button", { name: "Remove for good" }).click();
    await expect(sheet.locator(".resume-item")).toHaveCount(1);
    await expect(sheet.getByRole("textbox", { name: "Name of this resume point" })).toHaveValue("Slow, hands apart");
    await expect(card.locator(".resume-name")).toHaveText("Slow, hands apart");
    await sheet.getByRole("button", { name: "Close" }).click();
    expect(errors).toEqual([]);
});

test("in Russian, the library speaks Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await page.goto("/");
    const add = page.locator(".lib-head").getByRole("button", { name: "Добавить пьесу" });
    await expect(add).toBeVisible();
    await expect(page.locator(".card.add")).toContainText("Расшифровать запись");
    await add.click();
    await expect(page.getByRole("dialog", { name: "Добавить пьесу" })).toContainText("читается на этом устройстве");
});

test("the scan, recording, account and not-found screens open as sheets over the library and close back to it", async ({ page }) => {
    const errors = pageErrors(page);
    for (const [path, heading] of [["/import/scan", "Scan printed music"], ["/import/recording", "Notes from a recording"], ["/settings", "Settings"], ["/not-found", "Not found"]]) {
        await page.goto(path);
        const sheet = page.locator("main.page-sheet");
        await expect(sheet.getByRole("heading", { level: 1, name: heading })).toBeVisible();
        await expect(page.locator(".page-sheet-scrim")).toHaveCount(1);
        expect(await violations(page)).toEqual([]);
    }
    await page.locator("main.page-sheet").getByRole("link", { name: "Back to the library" }).click();
    await expect(page.getByRole("button", { name: "Add piece" }).first()).toBeVisible();

    // The Pro routes in Add a piece say what to do without the word "server".
    await page.getByRole("button", { name: "Add piece" }).first().click();
    await expect(page.locator(".add-sheet .routes-list")).not.toContainText("server");

    await page.goto("/import/scan");
    await page.locator("main.page-sheet").getByRole("link", { name: "Close" }).click();
    await expect(page).toHaveURL(/\/$/);
    expect(errors).toEqual([]);
});
