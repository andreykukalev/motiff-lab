// Suggested phrases (#18, docs/08-central-product-model.md): the piece as phrases to pick as the working
// context, each marked with how its start was found, and the player's own boundaries.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

test("pick a suggested phrase, make your own and go back to the suggestions", async ({ page }) => {
    const errors = await openFurElise(page);
    const strip = page.locator(".phrases");
    const phrases = strip.locator("button.phrase");
    const selection = page.locator(".inspector .selection-text");

    // Für Elise's opening comes back at the end of bar 4: two phrases, the second inferred from the return.
    await expect(phrases).toHaveCount(2);
    await expect(phrases.nth(1)).toHaveAccessibleName(/Phrase 2, bars 4–8, starts where an earlier passage comes back note for note \(inferred/);
    await expect(phrases.nth(1)).toHaveClass(/inferred/);
    await expect(page.locator(".score .phrase-mark")).toHaveCount(1);

    // Picking a phrase selects it as the working context; Practise and Explore read it.
    await phrases.nth(1).click();
    await expect(phrases.nth(1)).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Selected phrase 2");
    await expect(page.locator(".practise").getByRole("button", { name: "Play from the start" })).toBeVisible();
    await page.getByRole("tab", { name: "Explore" }).click();
    await expect(page.locator(".explore h4, .explore .experiment, .explore button").first()).toBeVisible();

    // A bar of your own becomes a phrase, marked as yours; back to the suggestions undoes it.
    await selectBar(page, 2);
    // The boundary tools fold away under the strip (0012).
    await strip.getByRole("button", { name: "Adjust phrases and hear transitions" }).click();
    const make = strip.getByRole("button", { name: "Make the selection a phrase" });
    await expect(make).toBeEnabled();
    await make.click();
    await expect(phrases).toHaveCount(4);
    await expect(selection).toContainText("Selected phrase 2, bar 2");
    await expect(phrases.nth(1)).toHaveClass(/yours/);
    await strip.getByRole("button", { name: "Back to the suggested phrases" }).click();
    await expect(phrases).toHaveCount(2);

    // Joining the two phrases leaves the whole opening as one.
    await phrases.first().click();
    await strip.getByRole("button", { name: "Join with the next phrase" }).click();
    await expect(phrases).toHaveCount(1);
    expect(errors).toEqual([]);
});
