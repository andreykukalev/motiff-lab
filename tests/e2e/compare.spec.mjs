// Return to the original and compare (#43, step 6 of docs/08-central-product-model.md) in every browser
// project: after an experiment, Original and Variation in the inspector head switch what is heard and
// what the score shows, One after the other loops the two in turn, and Compare says what changed and
// what was kept.
import { expect, test } from "@playwright/test";
import { closePopover, openAdjust, openFurElise, openMore, selectBar, openInsights, heardChoice, scoreChoice, tryWay } from "./steps.mjs";

/**
 * While the two loop one after the other, with the overflow open: which one the heading says sounds, the compare
 * button marked current, and the score the overflow's Notation choice shows, all read at one moment.
 */
function sounding(page) {
    return page.evaluate(() => {
        const head = document.querySelector(".inspector-head");
        return [
            head.querySelector(".selection-text")?.textContent.match(/Now the \w+\./)?.[0] ?? null,
            head.querySelector('.compare-switch [aria-current="true"]')?.textContent.trim() ?? null,
            document.querySelector('.more-popover [role="radiogroup"][aria-label="Which score shows"] [aria-checked="true"]')?.textContent.trim() ?? null,
        ];
    });
}

test("go back to the original, compare, and hear the two one after the other", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    await openInsights(page);
    const head = page.locator(".inspector-head");
    // The heading says which one sounds; the status line says what loops.
    const selection = head.locator(".selection-text");
    const status = page.locator(".selection-status");

    // No variation yet, so nothing to compare.
    await expect(head.locator(".compare-switch")).toHaveCount(0);

    await tryWay(page, "Passing notes");
    const compare = head.getByRole("group", { name: "Compare with the original" });
    const original = compare.getByRole("button", { name: "Original", exact: true });
    const variation = compare.getByRole("button", { name: "Variation", exact: true });
    await expect(variation).toHaveAttribute("aria-pressed", "true");
    await expect.poll(() => heardChoice(page)).toBe("Variation");
    await expect.poll(() => scoreChoice(page)).toBe("With the variation");

    // Back to the original: the sound and the score switch, and the variation stays to come back to.
    await original.click();
    await expect(original).toHaveAttribute("aria-pressed", "true");
    await expect.poll(() => heardChoice(page)).toBe("Original");
    await expect.poll(() => scoreChoice(page)).toBe("As written");
    await expect(status).toContainText("Looping; only these notes play.");
    await expect(page.locator(".explore h3")).toHaveText("Try a variation");

    await variation.click();
    await expect(variation).toHaveAttribute("aria-pressed", "true");
    await expect.poll(() => heardChoice(page)).toBe("Variation");
    await expect.poll(() => scoreChoice(page)).toBe("With the variation");

    // One after the other: the original, then the variation, the views following the one that sounds.
    const alternate = compare.getByRole("button", { name: "One after the other" });
    await alternate.click();
    await expect(alternate).toHaveAttribute("aria-pressed", "true");
    await expect(status).toContainText("Looping the original, then the variation.");
    await expect.poll(() => heardChoice(page)).toBe("The original, then the variation");
    await expect(selection).toContainText(/Now the (original|variation)\./);
    // A pass lasts about a second, less than opening and closing the overflow can take, so the score's choice is
    // read with the overflow open, in the same moment as the heading and the current button.
    await openMore(page);
    await expect.poll(() => sounding(page), { timeout: 20_000 }).toEqual(["Now the variation.", "Variation", "With the variation"]);
    await expect.poll(() => sounding(page), { timeout: 20_000 }).toEqual(["Now the original.", "Original", "As written"]);
    await closePopover(page);

    // Pressed again, the selection keeps looping what Hear says.
    await alternate.click();
    await expect(alternate).toHaveAttribute("aria-pressed", "false");
    await expect(status).not.toContainText("then the variation");
    await expect(status).toContainText("Looping; only these notes play.");
    await expect.poll(() => heardChoice(page)).toBe("Variation");
    await expect(variation).toHaveAttribute("aria-pressed", "true");

    // One after the other again, and Original ends it.
    await alternate.click();
    await expect(status).toContainText("Looping the original, then the variation.");
    await original.click();
    await expect(status).not.toContainText("then the variation");
    await expect(alternate).toHaveAttribute("aria-pressed", "false");

    // Compare, under the stream and in Adjust: what changed and what was kept, from the diff and the checks.
    const panel = page.locator(".stream + .compare");
    await expect(panel.getByRole("heading", { name: "What changed" })).toBeVisible();
    await expect(panel.locator("li").first()).toContainText(/of \d+ notes/);
    await expect(panel.getByRole("heading", { name: "What was kept" })).toBeVisible();
    await expect(panel.locator(".checks .kept .property").first()).not.toBeEmpty();
    const adjust = await openAdjust(page);
    await expect(adjust.locator(".changed + .compare .checks .kept")).toContainText("Kept:");

    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});
