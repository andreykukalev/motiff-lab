// Back to the wider context (#126, docs/08-central-product-model.md): previous and next phrase by touch and
// with Page Up and Page Down, keeping the way of working; the piece played on from the phrase and back; and
// the score marking the selected phrase's bracket (0018 section 3).
import { expect, test } from "@playwright/test";
import { openFurElise, openInsights } from "./steps.mjs";

test("step between phrases, play on and come back", async ({ page }) => {
    const errors = await openFurElise(page);
    const selection = page.locator(".selection-status");
    const practise = page.locator(".practise");
    const current = page.locator("div.score button.bracket.on[aria-label]");

    // Page Down with nothing selected starts at the first phrase; its bracket is drawn in the accent.
    await page.keyboard.press("PageDown");
    await expect(selection).toContainText("Selected phrase 1");
    await expect(current).toHaveAccessibleName("Phrase 1, bars 0–4");

    // Stepping keeps the way of working.
    await openInsights(page, "Understand");
    const understand = page.getByRole("tab", { name: "Understand" });
    await page.keyboard.press("PageDown");
    await expect(selection).toContainText("Selected phrase 2");
    await expect(understand).toHaveAttribute("aria-selected", "true");
    await expect(current).toHaveAccessibleName("Phrase 2, bars 4–8");

    // Play on from the phrase, then back to it, looping as before.
    await openInsights(page, "Practise");
    await practise.getByRole("button", { name: "‹ Previous phrase" }).click();
    await expect(selection).toContainText("Selected phrase 1");
    await expect(practise.getByRole("button", { name: "‹ Previous phrase" })).toBeDisabled();
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
