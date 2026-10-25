// Return to the original and compare (#43, step 6 of docs/08-central-product-model.md) in every browser
// project: after an experiment, Original and Variation in the inspector head switch what is heard and
// what the score shows, One after the other loops the two in turn, and Compare says what changed and
// what was kept.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

test("go back to the original, compare, and hear the two one after the other", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    await page.getByRole("tab", { name: "Explore" }).click();
    const head = page.locator(".inspector-head");
    const selection = head.locator(".selection-text");
    const heard = page.locator("select[aria-label='What is heard'] option:checked");
    const score = page.locator("select[aria-label='Which score shows'] option:checked");

    // No variation yet, so nothing to compare.
    await expect(head.locator(".compare-switch")).toHaveCount(0);

    await page.locator(".explore").getByRole("button", { name: /Give the melody more space/ }).click();
    const compare = head.getByRole("group", { name: "Compare with the original" });
    const original = compare.getByRole("button", { name: "Original", exact: true });
    const variation = compare.getByRole("button", { name: "Variation", exact: true });
    await expect(variation).toHaveAttribute("aria-pressed", "true");
    await expect(heard).toHaveText("Variation");
    await expect(score).toHaveText("With the variation");

    // Back to the original: the sound and the score switch, and the variation stays to come back to.
    await original.click();
    await expect(original).toHaveAttribute("aria-pressed", "true");
    await expect(heard).toHaveText("Original");
    await expect(score).toHaveText("Generated from the notes");
    await expect(selection).toContainText("Looping; only these notes play.");
    await expect(page.locator(".explore h3")).toHaveText("Give the melody more space");
    await expect(page.locator(".explore").getByRole("button", { name: "Original", exact: true })).toHaveAttribute("aria-pressed", "true");

    await variation.click();
    await expect(variation).toHaveAttribute("aria-pressed", "true");
    await expect(heard).toHaveText("Variation");
    await expect(score).toHaveText("With the variation");

    // The switch is in the head, so it is there on every tab.
    await page.getByRole("tab", { name: "Understand" }).click();
    await expect(original).toBeVisible();
    await page.getByRole("tab", { name: "Explore" }).click();

    // One after the other: the original, then the variation, the views following the one that sounds.
    const alternate = compare.getByRole("button", { name: "One after the other" });
    await alternate.click();
    await expect(alternate).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Looping the original, then the variation.");
    await expect(heard).toHaveText("The original, then the variation");
    await expect(selection).toContainText(/Now the (original|variation)\./);
    await expect(selection).toContainText("Now the variation.", { timeout: 20_000 });
    await expect(variation).toHaveAttribute("aria-current", "true");
    await expect(score).toHaveText("With the variation");
    await expect(selection).toContainText("Now the original.", { timeout: 20_000 });
    await expect(original).toHaveAttribute("aria-current", "true");
    await expect(score).toHaveText("Generated from the notes");

    // Pressed again, the selection keeps looping what Hear says.
    await alternate.click();
    await expect(alternate).toHaveAttribute("aria-pressed", "false");
    await expect(selection).not.toContainText("then the variation");
    await expect(selection).toContainText("Looping; only these notes play.");
    await expect(heard).toHaveText("Variation");
    await expect(variation).toHaveAttribute("aria-pressed", "true");

    // The Hear select offers the same, and any other choice there ends it.
    await page.locator("select[aria-label='What is heard']").selectOption({ label: "The original, then the variation" });
    await expect(selection).toContainText("Looping the original, then the variation.");
    await page.locator("select[aria-label='What is heard']").selectOption({ label: "Original" });
    await expect(selection).not.toContainText("then the variation");
    await expect(alternate).toHaveAttribute("aria-pressed", "false");

    // Compare, under the card and in the Selection tab: what changed and what was kept, from the diff and the checks.
    const panel = page.locator(".explore + .compare");
    await expect(panel.getByRole("heading", { name: "What changed" })).toBeVisible();
    await expect(panel.locator("li").first()).toContainText(/of \d+ notes/);
    await expect(panel.getByRole("heading", { name: "What was kept" })).toBeVisible();
    await expect(panel.locator(".checks .kept .property").first()).not.toBeEmpty();
    await page.getByRole("tab", { name: "Selection" }).click();
    await expect(page.locator(".changed + .compare .checks .kept")).toContainText("Kept:");

    await page.locator(".transport-bar").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});
