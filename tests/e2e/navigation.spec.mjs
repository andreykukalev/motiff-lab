// Back to the wider context (#126, docs/08-central-product-model.md): previous and next phrase by touch and
// with Page Up and Page Down, keeping the way of working; the next phrase added to hear the transition; the
// piece played on from the phrase and back; and the score marking the selected phrase.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

test("step between phrases, hear the transition, play on and come back", async ({ page }) => {
    const errors = await openFurElise(page);
    const strip = page.locator(".phrases");
    const selection = page.locator(".inspector .selection-text");
    const practise = page.locator(".practise");

    // Page Down with nothing selected starts at the first phrase; the score marks it.
    await page.keyboard.press("PageDown");
    await expect(selection).toContainText("Selected phrase 1");
    await expect(page.locator("div.score .phrase-mark.current")).toHaveText("Phrase 1");

    // Stepping keeps the way of working.
    const understand = page.getByRole("tab", { name: "Understand" });
    await understand.click();
    await page.keyboard.press("PageDown");
    await expect(selection).toContainText("Selected phrase 2");
    await expect(understand).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("div.score .phrase-mark.current")).toHaveText("Phrase 2");
    await expect(strip.getByRole("button", { name: "Next phrase", exact: true })).toBeDisabled();
    await strip.getByRole("button", { name: "Previous phrase", exact: true }).click();
    await expect(selection).toContainText("Selected phrase 1");

    // The next phrase added, to hear one lead into the other. With the pickup the two phrases touch nine
    // bars, one more than a selection holds, so the selection stops at bar 7 and says so.
    await strip.getByRole("button", { name: "Add the next phrase" }).click();
    await expect(selection).toContainText("Selected bars 0–7");
    await expect(selection).toContainText("A selection holds at most 8 bars, so it stops at bar 7.");
    await expect(strip.getByRole("button", { name: "Add the next phrase" })).toBeDisabled();
    await expect(strip.getByRole("button", { name: "Add the previous phrase" })).toBeDisabled();
    await strip.locator("button.phrase").nth(1).click();
    await strip.getByRole("button", { name: "Add the previous phrase" }).click();
    await expect(selection).toContainText("Selected bars 1–8");
    await expect(selection).toContainText("so it stops at bar 1.");

    // Play on from the phrase, then back to it, looping as before.
    await strip.locator("button.phrase").first().click();
    await page.getByRole("tab", { name: "Practise" }).click();
    const loop = practise.getByRole("button", { name: "Loop the phrase" });
    await loop.click();
    await expect(loop).toHaveAttribute("aria-pressed", "true");
    await practise.getByRole("button", { name: "Play on to the end" }).click();
    await expect(selection).toContainText("Playing on from here to the end.");
    await expect(loop).toHaveAttribute("aria-pressed", "false");
    await practise.getByRole("button", { name: "Back to the phrase" }).click();
    await expect(loop).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Selected phrase 1");
    await expect(selection).toContainText("Looping");
    expect(errors).toEqual([]);
});
