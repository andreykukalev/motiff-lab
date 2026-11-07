// The circle of fifths (Andrei, 2026-11-06; the independent assessment's reference card) in every browser project: opened
// in the key's row of the Insights stream, it shows the selection's key in cobalt among the twelve places with the
// passage's chords on their roots; a chord or the notes outside the scale ring on the score, and Hear the scale plays it once.
import { expect, test } from "@playwright/test";
import { openFurElise, openInsights, openLenses, violations } from "./steps.mjs";

test("the key opens the circle of fifths: the chords and the notes outside the scale ring on the score, the scale is heard", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.locator(".score button.bracket[aria-label]").nth(0).click();
    const stream = await openInsights(page);
    await openLenses(page);
    const row = stream.locator("article.fact-row", { hasText: "Key: A minor." }).first();

    await row.getByRole("button", { name: "On the circle" }).click();
    const circle = row.locator(".key-circle");
    await expect(circle.getByRole("img", { name: "Circle of fifths: A minor" })).toBeVisible();
    await expect(circle.locator(".sector.key")).toHaveCount(1);
    await expect(circle.locator(".sector.minor.key")).toHaveCount(1);
    await expect(circle).toContainText("A minor: no sharps or flats, the same as its relative key, C major.");
    await expect(circle).toContainText("E minor (dominant) and D minor (subdominant)");
    expect(await circle.locator(".chord").count()).toBeGreaterThan(0);

    // A chord rings its notes on the score; pressed again, it is put away.
    const places = circle.getByRole("group", { name: "Show on the score" });
    const chord = places.getByRole("button", { name: /^1\. / });
    await chord.click();
    await expect(chord).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".score .highlight").first()).toBeVisible();
    await expect(circle.locator(".chord.on")).toHaveCount(1);
    await chord.click();
    await expect(chord).toHaveAttribute("aria-pressed", "false");

    // The notes outside A minor's scale (D♯, G♯) are one place.
    const outside = places.getByRole("button", { name: /outside the scale$/ });
    await outside.click();
    await expect(outside).toHaveAttribute("aria-pressed", "true");

    expect(await violations(page)).toEqual([]);

    // Hear the scale plays it once from the selection's start; pressed again, it stops, and Play plays the piece again.
    await circle.getByRole("button", { name: "Hear the scale" }).click();
    await expect(page.locator(".pill").getByRole("button", { name: "Pause" })).toBeVisible();
    await circle.getByRole("button", { name: "Stop the scale" }).click();
    await expect(page.locator(".pill").getByRole("button", { name: "Play", exact: true })).toBeVisible();
    await expect(circle.getByRole("button", { name: "Hear the scale" })).toBeVisible();

    // Pressed again, On the circle closes it.
    await row.getByRole("button", { name: "On the circle" }).click();
    await expect(row.locator(".key-circle")).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("in Russian, the circle speaks Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await openFurElise(page);
    await page.locator(".score button.bracket[aria-label]").nth(0).click();
    const stream = await openInsights(page);
    await openLenses(page);
    const row = stream.locator("article.fact-row", { hasText: "Тональность: ля минор." }).first();
    await row.getByRole("button", { name: "На круге" }).click();
    const circle = row.locator(".key-circle");
    await expect(circle.getByRole("img", { name: "Квинтовый круг: ля минор" })).toBeVisible();
    await expect(circle).toContainText("Ля минор: без диезов и бемолей, как у параллельной тональности, до мажор.");
    await expect(circle.getByRole("button", { name: "Услышать гамму" })).toBeVisible();
});
