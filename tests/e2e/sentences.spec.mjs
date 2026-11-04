// The sentence as the level above the phrase (#133, decision record 0013 sections 3 and 9; 0018 section 3): the
// sentences music-core groups over the phrases, shown as brackets at the Sentences level and selected whole as the
// working context, and the Insights stream opening on the one observation to hear first with a recommended next step
// that its Try chip tries and Keep keeps.
import { expect, test } from "@playwright/test";
import { closePopover, openFurElise, openInsights } from "./steps.mjs";

test("select a sentence, understand it now and try the recommended step", async ({ page }) => {
    const errors = await openFurElise(page);
    const brackets = page.locator(".score button.bracket[aria-label]");
    const selection = page.locator(".selection-status");

    // Für Elise's opening and its return make one sentence: one bracket at the Sentences level.
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
    await page.getByRole("dialog", { name: "Phrases" }).getByRole("radio", { name: "Sentences" }).click();
    await closePopover(page);
    await expect(brackets).toHaveCount(1);
    await expect(brackets.first()).toHaveAccessibleName("Sentence 1, bars 0–8");

    // Tapping it selects the sentence whole: nine bars, more than a range holds.
    await brackets.first().click();
    await expect(brackets.first()).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Selected sentence A, bars 0–8 (phrases 1–2)");

    // The stream opens on the one observation to hear first, with its kind, why that one, and the recommended next step.
    const stream = await openInsights(page);
    const key = stream.locator(".lens-group.key");
    await expect(key.locator(".certainty")).toHaveText(/detected|inferred|uncertain/);
    await key.getByRole("button", { name: "Evidence" }).click();
    await expect(key).toContainText("Why this one:");
    await expect(stream.locator(".lens-group")).not.toHaveCount(1);
    await expect(stream.getByText("Sentence A holds phrases 1–2, bars 0–8.")).toBeVisible();

    // The recommended Try chip tries the experiment for the sentence at once; Keep saves it for this sentence.
    const recommended = stream.locator(".try-row .chip.recommended").first();
    const title = (await recommended.textContent()).trim();
    await recommended.click();
    const card = stream.locator(".explore");
    await expect(card.locator("h3")).toHaveText(title);
    await expect(card.locator(".passage")).toContainText("Selected sentence");
    await stream.getByRole("button", { name: "Keep this variation" }).click();
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    await expect(selection).toContainText("Selected sentence A");

    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});
