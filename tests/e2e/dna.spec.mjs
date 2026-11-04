// Piece DNA (decision record 0018, section 11): the map of the whole piece over the score, from the score's overflow and
// from the library card; its lanes say what is not analysed yet, a mark plays its passage and a second tap opens the
// score there; leaving returns to the score as it was.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { openFurElise, openMore, selectBar, violations } from "./steps.mjs";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

test("from the overflow, the map shows where the selection returns, and a mark leads back into the score", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    const more = await openMore(page);
    await more.getByRole("button", { name: "Piece DNA" }).click();

    const dna = page.getByRole("dialog", { name: "Für Elise (Beethoven, opening)" });
    await expect(dna).toBeFocused();
    await expect(dna.locator(".dna-bar")).toContainText("Piece DNA");
    await expect(dna).toContainText("The sections of the whole piece are not analysed yet.");
    const lane = dna.locator(".dna-row.idea");
    await expect(lane).toHaveCount(1);
    await expect(lane.locator(".lane-name")).toContainText("Your selection");
    const marks = lane.getByRole("button");
    await expect.poll(() => marks.count()).toBeGreaterThan(1);
    await expect(dna.locator(".dna-card")).toHaveCount(1);
    await expect(dna.locator(".key-block").first()).toBeVisible();
    expect(await violations(page)).toEqual([]);

    // The second mark plays; a second tap opens the score with it selected.
    const second = marks.nth(1);
    const name = await second.getAttribute("aria-label");
    await second.click();
    await expect(second).toHaveAttribute("aria-pressed", "true");
    await second.click();
    await expect(dna).toHaveCount(0);
    const bars = name.match(/bars? (\d+)/)[1];
    await expect(page.locator(".selection-status")).toContainText(new RegExp(`bars? ${bars}(?!\\d)`, "i"));
    expect(errors).toEqual([]);
});

test("from the library card, in Russian, with no selection: the ideas lane says how to fill it, and Escape returns", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI or MusicXML file").setInputFiles(minuet);
    await expect(page.locator(".bar .ttl")).toHaveText("Waltz sketch");
    await page.evaluate(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await page.goto("/");

    await page.getByRole("button", { name: "Ещё: Waltz sketch" }).click();
    await page.getByRole("button", { name: "ДНК пьесы" }).click();
    const dna = page.getByRole("dialog", { name: "Waltz sketch" });
    await expect(dna).toContainText("Выделите фрагмент в нотах");
    await expect(dna.locator(".dna-bar .counts")).toContainText("такт");
    expect(await violations(page)).toEqual([]);
    await expect(page).not.toHaveURL(/view=dna/);

    await dna.press("Escape");
    await expect(dna).toHaveCount(0);
    await expect(page.locator(".bar .ttl")).toHaveText("Waltz sketch");
});
