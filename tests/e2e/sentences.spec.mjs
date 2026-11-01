// The sentence as the level above the phrase (#133, decision record 0013 sections 3 and 9): the sentences
// music-core groups over the phrases, one selected whole as the working context with its phrases as its
// children, the hierarchy line from the piece to the selection, ‹ › by level, and Understand opening on
// the one observation to hear first with a recommended next step that Explore tries and keeps.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

test("select a sentence, read its levels, understand it now and try the recommended step", async ({ page }) => {
    const errors = await openFurElise(page);
    const strip = page.locator(".phrases");
    const sentences = strip.locator("button.sentence");
    const phrases = strip.locator("button.phrase");
    const levels = page.getByRole("navigation", { name: "Levels" });
    const selection = page.locator(".inspector .selection-text");

    // Für Elise's opening and its return make one sentence, inferred; the line says what to pick until something is.
    await expect(sentences).toHaveCount(1);
    await expect(sentences.first()).toHaveAccessibleName(/Sentence A, bars 0–8, phrases 1–2, inferred \(\d+% sure\)/);
    await expect(sentences.first()).toHaveClass(/inferred/);
    await expect(levels).toContainText("Pick a sentence or a phrase below");

    // Picking the sentence selects it whole: nine bars, more than a range holds; its phrases are outlined as its children.
    await sentences.first().click();
    await expect(sentences.first()).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Selected sentence A, bars 0–8 (phrases 1–2)");
    await expect(phrases.nth(0)).toHaveClass(/child/);
    await expect(phrases.nth(1)).toHaveClass(/child/);
    const sentenceCrumb = levels.getByRole("button", { name: /Sentence A/ });
    await expect(sentenceCrumb).toHaveAttribute("aria-current", "true");
    await expect(levels.getByRole("button", { name: /Phrase/ })).toHaveCount(0);

    // ‹ › step among sentences while one is selected; with a single sentence, neither leads anywhere.
    await expect(strip.getByRole("button", { name: "Previous sentence", exact: true })).toBeDisabled();
    await expect(strip.getByRole("button", { name: "Next sentence", exact: true })).toBeDisabled();

    // A phrase inside it keeps the sentence as context: on the line, and marked on its chip; the crumb goes back up.
    await phrases.nth(1).click();
    await expect(selection).toContainText("Selected phrase 2, bars 4–8");
    await expect(levels.getByRole("button", { name: /Phrase 2/ })).toHaveAttribute("aria-current", "true");
    await expect(sentenceCrumb).not.toHaveAttribute("aria-current", "true");
    await expect(sentences.first()).toHaveClass(/around/);
    await expect(phrases.nth(1)).not.toHaveClass(/child/);
    await expect(strip.getByRole("button", { name: "Previous phrase", exact: true })).toBeEnabled();
    await sentenceCrumb.click();
    await expect(selection).toContainText("Selected sentence A");

    // Understand opens on Now: one observation with its kind, why that one, and a recommended next step.
    await page.getByRole("tab", { name: "Understand" }).click();
    const panel = page.locator(".tab-panel");
    const key = panel.locator(".key-observation");
    await expect(key.locator(".certainty")).toHaveText(/detected|inferred|uncertain/);
    await key.getByRole("button", { name: "Why?" }).click();
    await expect(key).toContainText("Why this one:");
    await panel.getByRole("button", { name: "Details" }).click();
    await expect(panel.locator(".lens-group")).not.toHaveCount(0);
    await expect(panel.getByText("Sentence A holds phrases 1–2, bars 0–8.")).toBeVisible();
    await panel.getByRole("button", { name: "Now", exact: true }).click();

    // Try it opens Explore on the recommended experiment for the sentence; Keep saves it for this sentence.
    const recommended = panel.locator(".recommended");
    await expect(recommended.locator("strong")).not.toBeEmpty();
    const title = await recommended.locator("strong").textContent();
    await recommended.getByRole("button", { name: "Try it" }).click();
    await expect(page.getByRole("tab", { name: "Explore" })).toHaveAttribute("aria-selected", "true");
    const card = page.locator(".explore");
    await expect(card.locator("h3")).toHaveText(title);
    await expect(card.locator(".passage")).toContainText("Selected sentence");
    await card.getByRole("button", { name: "Keep this variation" }).click();
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    await expect(selection).toContainText("Selected sentence A");

    await page.locator(".transport-bar").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});
