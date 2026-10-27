// The canvas as the workspace (0012, batch 2): Score, Piano roll or Both with a divider between them, the
// selection made in the score as on the roll, and Focus mode with the phrase, the loop and the transport.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

test("choose a view, move the divider, and work in Focus mode", async ({ page }) => {
    const errors = await openFurElise(page);
    const score = page.locator("div.score");
    const roll = page.locator(".roll");
    const divider = page.getByRole("separator", { name: "Divider between the score and the piano roll" });
    const views = page.locator(".app-header .view-switch");

    // Both views, with the divider between them, is the start; the divider is a keyboard control.
    await expect(score).toBeVisible();
    await expect(roll).toBeVisible();
    await expect(divider).toHaveAttribute("aria-valuenow", "60");
    await divider.focus();
    await page.keyboard.press("ArrowUp");
    await expect(divider).toHaveAttribute("aria-valuenow", "55");
    await page.keyboard.press("End");
    await expect(divider).toHaveAttribute("aria-valuenow", "80");

    // One view alone: the other goes, and the divider with it.
    await views.getByRole("button", { name: "Piano roll" }).click();
    await expect(score).toBeHidden();
    await expect(roll).toBeVisible();
    await expect(divider).toHaveCount(0);
    await expect(page.getByRole("combobox", { name: "Which score shows" })).toBeDisabled();
    await views.getByRole("button", { name: "Score", exact: true }).click();
    await expect(score).toBeVisible();
    await expect(roll).toBeHidden();

    // A bar clicked in the score, away from its notes (here on a staff line), is selected as on the roll.
    const line = score.locator("g.measure").nth(2).locator("g.staff > path").first();
    await line.scrollIntoViewIfNeeded();
    const box = await line.boundingBox();
    await page.mouse.click(box.x + box.width * 0.9, box.y + box.height / 2);
    await expect(page.locator(".inspector .selection-text")).toContainText("Selected bar 2");

    // The choice and the divider come back after a reload.
    await views.getByRole("button", { name: "Both" }).click();
    await expect(divider).toHaveAttribute("aria-valuenow", "80");
    await page.reload();
    await expect(page.getByRole("button", { name: "Für Elise" })).toBeEnabled({ timeout: 30_000 });
    await page.getByRole("button", { name: "Für Elise" }).click();
    await expect(divider).toHaveAttribute("aria-valuenow", "80");
    await expect(page.locator(".app-header .view-switch button.on")).toHaveText("Both");

    // Focus mode: the phrase, previous and next, the loop and the views on one bar; the header and the inspector go.
    await page.keyboard.press("PageDown");
    await expect(page.locator(".inspector .selection-text")).toContainText("Selected phrase 1");
    await page.getByRole("button", { name: "Focus" }).click();
    const bar = page.getByRole("toolbar", { name: "Focus mode" });
    await expect(bar).toBeVisible();
    await expect(page.locator(".app-header")).toBeHidden();
    await expect(page.locator(".inspector")).toBeHidden();
    await expect(bar.locator(".phrase")).toContainText("Selected phrase 1");
    await bar.getByRole("button", { name: "Next phrase" }).click();
    await expect(bar.locator(".phrase")).toContainText("Selected phrase 2");
    await expect(bar.getByRole("button", { name: "Next phrase" })).toBeDisabled();
    await bar.getByRole("button", { name: "Loop", exact: true }).click();
    await expect(bar.getByRole("button", { name: "Looping" })).toHaveAttribute("aria-pressed", "true");
    await bar.locator(".view-switch").getByRole("button", { name: "Score", exact: true }).click();
    await expect(roll).toBeHidden();
    await expect(page.getByRole("button", { name: "Play", exact: true }).or(page.getByRole("button", { name: "Pause", exact: true }))).toBeVisible();

    // Back to exploring: the workspace comes back as it was, the phrase still selected and looping.
    await bar.getByRole("button", { name: "Back to exploring" }).click();
    await expect(page.locator(".app-header")).toBeVisible();
    await expect(page.locator(".inspector .selection-text")).toContainText("Selected phrase 2");
    await expect(page.locator(".inspector").getByRole("button", { name: "Looping" })).toBeVisible();
    expect(errors).toEqual([]);
});
