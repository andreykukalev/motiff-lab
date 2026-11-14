// The sentence as the level above the phrase (#133, decision record 0013 sections 3 and 9; 0018 section 3): the
// sentences music-core groups over the phrases, stepped through at the Sentences level and selected whole as the
// working context, and the Insights stream opening on the one observation to hear first, with a fact's Try button whose
// way is heard and kept.
import { expect, test } from "@playwright/test";
import { closePopover, openFurElise, openInsights, openLenses, selectPhrase } from "./steps.mjs";

test("select a sentence, understand it now and try a way", async ({ page }) => {
    const errors = await openFurElise(page);
    const selection = page.locator(".selection-status");

    // Für Elise's opening and its return make its first sentence, of four, at the Sentences level.
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
    await page.getByRole("dialog", { name: "Phrases" }).getByRole("radio", { name: "Sentences" }).click();
    await expect(page.getByRole("dialog", { name: "Phrases" })).toContainText("4 sentences in the piece");
    await closePopover(page);

    // Page Down selects the sentence whole: nine bars, more than a range holds.
    await selectPhrase(page);
    await expect(selection).toContainText("Selected sentence A, bars 0–8 (phrases 1–2)");

    // The stream opens on Main, the harmonic motion first with its kind and Evidence, then the facts by lens under Details.
    const stream = await openInsights(page);
    const main = stream.locator(".lens-group.main");
    const motion = main.locator("article.fact-row").first();
    await expect(motion.locator(".certainty")).toHaveText(/detected|inferred|uncertain/);
    await motion.getByRole("button", { name: "Evidence" }).click();
    await expect(motion).toContainText("Inferred from the chords read from the notes");
    await expect(stream.locator(".lens-group")).not.toHaveCount(1);
    await openLenses(page);
    await expect(stream.getByText("Sentence A holds phrases 1–2, bars 0–8.")).toBeVisible();

    // A fact's Try button opens its ways for the sentence; a way is heard at once, and Keep saves it for this sentence.
    await stream.locator(".try-row .btn.try").first().click();
    await stream.locator(".try-row .chip").first().click();
    const card = stream.locator(".explore");
    await expect(card.locator("h3")).toHaveText("Try a variation");
    await expect(card.locator(".passage")).toContainText("Selected sentence");
    await stream.getByRole("button", { name: "Keep this variation" }).click();
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    await expect(selection).toContainText("Selected sentence A");

    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});
