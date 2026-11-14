// Back to the wider context (#126, docs/08-central-product-model.md): previous and next phrase with Page Up and Page Down,
// keeping the way of working (the Insights sheet stays open on the new phrase). Playing on into the piece is Play with
// Loop off on the pill.
import { expect, test } from "@playwright/test";
import { openFurElise, openInsights } from "./steps.mjs";

test("step between phrases and play on from one", async ({ page }) => {
    const errors = await openFurElise(page);
    const selection = page.locator(".selection-status");
    const pill = page.locator(".pill");

    // Page Down with nothing selected starts at the first phrase.
    // The keys are listened to a moment after the title shows (PlaybackLab.Layout.cs), so a press that came before is pressed again.
    await expect(async () => {
        if (!(await selection.textContent() ?? "").includes("Selected phrase")) {
            await page.keyboard.press("PageDown");
        }
        await expect(selection).toContainText("Selected phrase 1", { timeout: 3_000 });
    }).toPass({ timeout: 20_000 });
    await expect(selection).toContainText("Selected phrase 1, bars 0–4");

    // Stepping keeps the way of working: the sheet stays open, on the new phrase.
    const stream = await openInsights(page);
    await page.keyboard.press("PageDown");
    await expect(selection).toContainText("Selected phrase 2");
    await expect(stream).toBeVisible();
    await expect(page.locator(".sheet .selection-text")).toHaveText("Phrase 2 · bars 4–8");
    await page.keyboard.press("PageUp");
    await expect(selection).toContainText("Selected phrase 1");
    await page.keyboard.press("PageUp");
    await expect(selection).toContainText("Selected phrase 1");

    // Looping, then Loop off: Play carries on past the phrase into the piece.
    const loop = pill.getByRole("button", { name: "Loop", exact: true });
    await loop.click();
    await expect(loop).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Looping");
    await loop.click();
    await expect(loop).toHaveAttribute("aria-pressed", "false");
    await expect(selection).not.toContainText("Looping");
    expect(errors).toEqual([]);
});
