// «Мои правки: вкл / выкл» (an experiment, Settings · Experimental, version 0.5): with the experiment on, a piece with
// corrections shows the switch with how many it has; off, the notes are as recognised and cannot be corrected, and the
// piece remembers it on this device through a reload; on again, the corrections are back.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { openInsights, openMoreTools, violations } from "./steps.mjs";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

/** The corrections in Insights · More tools, opened. */
async function corrections(page) {
    await openInsights(page);
    const tools = await openMoreTools(page);
    const section = tools.locator("[aria-label='Correct notes']");
    const toggle = section.locator("button.section-toggle");
    if ((await toggle.getAttribute("aria-expanded")) !== "true") {
        await toggle.click();
    }
    return section;
}

test("my corrections turn off and on again for a piece, and off lasts through a reload", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    await sheet.getByRole("switch", { name: "My corrections: on / off" }).check();
    await sheet.press("Escape");

    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(minuet);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
    await page.locator(".score button.bracket[aria-label]").nth(0).click();
    await expect(page.locator(".selection-status")).toContainText("Selected phrase 1");
    let section = await corrections(page);
    await section.getByTitle("A semitone higher").first().click();
    await expect(section.locator("button.section-toggle")).toContainText("1 note corrected");

    // A piece opened from the device lays its corrections one by one, with the switch on.
    await page.reload();
    await expect(page.locator(".bar .ttl")).toHaveText(/Waltz sketch|importer-grand-staff/, { timeout: 30_000 });
    section = await corrections(page);
    const layer = section.locator(".edits-layer");
    await expect(layer).toContainText("1 correction");
    const mine = layer.getByRole("switch", { name: "My corrections" });
    await expect(mine).toBeChecked();
    expect(await violations(page)).toEqual([]);

    await mine.uncheck();
    section = await corrections(page);
    await expect(section.locator(".edits-layer")).toContainText("Corrections are off");
    await expect(section.getByTitle("A semitone higher")).toHaveCount(0);

    await page.reload();
    await expect(page.locator(".bar .ttl")).toHaveText(/Waltz sketch|importer-grand-staff/, { timeout: 30_000 });
    section = await corrections(page);
    await expect(section.locator(".edits-layer").getByRole("switch", { name: "My corrections" })).not.toBeChecked();

    await section.locator(".edits-layer").getByRole("switch", { name: "My corrections" }).check();
    section = await corrections(page);
    await expect(section.locator(".edits-layer")).not.toContainText("Corrections are off");
    await expect(section.locator(".edits-layer").getByRole("switch", { name: "My corrections" })).toBeChecked();
    await expect(section.locator("button.section-toggle")).toContainText("1 note corrected");
    expect(errors).toEqual([]);
});
