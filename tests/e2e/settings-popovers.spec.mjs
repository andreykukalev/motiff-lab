// Contextual settings (decision record 0018, section 10): a long press on an element opens a small popover with its own
// switches, which ends with This piece · Everywhere where the setting can be the piece's own, and the way to the same
// rows in the Settings sheet. Both places change the same settings.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { closePopover, openFurElise, violations } from "./steps.mjs";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

/** Opens a piece saved in the library, which can keep settings of its own. */
async function openSavedPiece(page) {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI or MusicXML file").setInputFiles(minuet);
    await expect(page.locator(".bar .ttl")).toHaveText("Waltz sketch");
    return errors;
}

test("the Phrases popover changes the level everywhere, or for this piece only", async ({ page }) => {
    const errors = await openSavedPiece(page);
    const brackets = page.locator(".score button.bracket[aria-label]");
    await expect(brackets.first()).toHaveAttribute("aria-label", /^Phrase 1/);

    // A long press on the action, here a right click, opens the same popover a tap does.
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click({ button: "right" });
    const popover = page.getByRole("dialog", { name: "Phrases" });
    const scope = popover.getByRole("radiogroup", { name: "Where it applies" });
    await expect(scope.getByRole("radio", { name: "Everywhere" })).toHaveAttribute("aria-checked", "true");
    expect(await violations(page)).toEqual([]);

    // Everywhere: the Settings sheet's level is the one chosen.
    await popover.getByRole("radio", { name: "Sentences" }).click();
    await expect(brackets.first()).toHaveAttribute("aria-label", /^Sentence 1/);
    await popover.getByRole("button", { name: "All settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await expect(sheet.getByRole("tab", { name: "Score" })).toHaveAttribute("aria-selected", "true");
    await expect(sheet.getByRole("radio", { name: "Sentences" })).toHaveAttribute("aria-checked", "true");
    await sheet.getByRole("button", { name: "Close Settings" }).click();

    // This piece: the piece keeps its level while the sheet's changes.
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
    await scope.getByRole("radio", { name: "This piece" }).click();
    await expect(scope.getByRole("radio", { name: "This piece" })).toHaveAttribute("aria-checked", "true");
    await popover.getByRole("button", { name: "All settings" }).click();
    await expect(sheet.getByText("This piece has its own", { exact: false })).toContainText("Phrases: Sentences");
    await sheet.getByRole("radio", { name: "Short" }).click();
    await expect(brackets.first()).toHaveAttribute("aria-label", /^Sentence 1/);
    await sheet.getByRole("button", { name: "Close Settings" }).click();

    // Everywhere again: the piece follows the sheet.
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
    await scope.getByRole("radio", { name: "Everywhere" }).click();
    await expect(brackets.first()).toHaveAttribute("aria-label", /^Short/);
    expect(errors).toEqual([]);
});

test("a long press on the pill opens its settings without playing, and the roll's from its action", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.locator(".score button.bracket[aria-label]").first().click();
    const pill = page.locator(".pill");
    const play = pill.getByRole("button", { name: "Play", exact: true });
    await expect(play).toBeVisible();

    // Held still on the play button: the popover opens, and the release does not start playback.
    const box = await play.boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.waitForTimeout(800);
    await page.mouse.up();
    const settings = page.getByRole("dialog", { name: "Transport pill settings" });
    await expect(settings).toBeVisible();
    await expect(play).toBeVisible();
    const appear = settings.getByRole("switch", { name: "Pill appears with a selection" });
    await expect(appear).toBeChecked();
    expect(await violations(page)).toEqual([]);
    await appear.uncheck();
    await closePopover(page);
    await expect(pill).toHaveCount(0);

    // The roll's settings, from a right click on its action.
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click({ button: "right" });
    const roll = page.getByRole("dialog", { name: "Piano roll settings" });
    await roll.getByRole("switch", { name: "Note names" }).uncheck();
    await expect(roll.getByRole("button", { name: "All settings" })).toBeVisible();
    await expect(roll.getByRole("radiogroup", { name: "Where it applies" })).toHaveCount(0);
    expect(await violations(page)).toEqual([]);
    await closePopover(page);
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    await expect(page.locator(".roll-head").getByRole("button", { name: "Note names" })).toHaveAttribute("aria-pressed", "false");
    expect(errors).toEqual([]);
});
