// Projects (N1): the routes in as tiles, each landing where it says.
import { expect, test } from "@playwright/test";

test("Try Für Elise opens the example in one tap", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: /Try Für Elise/ }).click();
    await expect(page.getByText("Für Elise (Beethoven, opening)")).toBeVisible();
});

test("each way in has its own screen, whose picker takes only what it reads", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: /Notes from a recording/ }).click();
    await expect(page).toHaveURL(/import\/recording$/);
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Notes from a recording");
    await expect(page.getByLabel("Choose a MIDI or MusicXML file")).toHaveCount(0);

    await page.goto("/");
    await page.getByRole("link", { name: /Scan printed music/ }).click();
    await expect(page).toHaveURL(/import\/scan$/);
    await expect(page.getByLabel("Choose a MIDI or MusicXML file")).toHaveCount(0);

    await page.goto("/");
    await page.getByRole("link", { name: /Open a file/ }).click();
    await expect(page.getByLabel("Choose a MIDI or MusicXML file")).toHaveCount(1);
});
