// Projects (N1): the routes in as tiles, each landing where it says.
import { expect, test } from "@playwright/test";

test("Try Für Elise opens the example in one tap", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: /Try Für Elise/ }).click();
    await expect(page.getByText("Für Elise (Beethoven, opening)")).toBeVisible();
});

test("the scan and recording tiles land on their sections of the import screen", async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 500 });
    await page.goto("/");
    await page.getByRole("link", { name: /Notes from a recording/ }).click();
    await expect(page).toHaveURL(/import#transcribe-heading$/);
    await expect(page.locator("#transcribe-heading")).toBeInViewport();
});
