// Practise a phrase (#50, docs/08-central-product-model.md): play it, loop it, slow it down and hear one
// hand while it loops; moving to the next phrase and switching to Explore and back keep the tempo and the loop.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar, openInsights } from "./steps.mjs";

test("practise a phrase: loop it, slow it down, hear one hand and move on", async ({ page }) => {
    const errors = await openFurElise(page);
    const panel = page.locator(".practise");
    const selection = page.locator(".selection-status");
    const tempo = panel.locator(".tempo-steps output");

    await openInsights(page);
    await expect(page.getByRole("tab", { name: "Practise" })).toHaveAttribute("aria-selected", "true");
    await expect(panel).toContainText("Tap a phrase's bracket on the score");
    await page.locator(".score button.bracket[aria-label]").first().click();
    await expect(selection).toContainText("Selected phrase 1");

    // The loop and the tempo, within reach.
    const loop = panel.getByRole("button", { name: "Loop the phrase" });
    await loop.click();
    await expect(loop).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Looping");
    await panel.getByRole("button", { name: "50%" }).click();
    await expect(tempo).toHaveText("50%");
    await panel.getByRole("button", { name: "Faster" }).click();
    await expect(tempo).toHaveText("55%");
    await expect(page.locator(".pill .mono")).toHaveText("55%");

    // One hand alone while the phrase loops; Für Elise marks the hands by staff.
    await panel.getByRole("button", { name: "Left hand" }).click();
    await expect(selection).toContainText("Looping the left hand alone.");

    // The next phrase keeps the loop, the tempo and the hand.
    await panel.getByRole("button", { name: "Next phrase ›" }).click();
    await expect(selection).toContainText("Selected phrase 2");
    await expect(selection).toContainText("Looping the left hand alone.");
    await expect(panel.getByRole("button", { name: "Next phrase ›" })).toBeDisabled();

    // Explore works on the same phrase; coming back, nothing has changed.
    await openInsights(page, "Explore");
    await expect(selection).toContainText("Selected phrase 2");
    await openInsights(page, "Practise");
    await expect(tempo).toHaveText("55%");
    await expect(loop).toHaveAttribute("aria-pressed", "true");

    await panel.getByRole("button", { name: "Play from the start" }).click();
    await panel.getByRole("button", { name: "Pause" }).click();
    await expect(panel.getByRole("button", { name: "Play from the start" })).toBeVisible();
    expect(errors).toEqual([]);
});

test("the workspace reads in Russian when the app is in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 1);

    await expect(page.locator(".selection-status")).toHaveText("Выделено: такт 1: 6 нот.");
    await expect(page.locator(".pill").getByRole("button", { name: "Цикл" })).toBeVisible();
    await openInsights(page, "Поиграть");
    await expect(page.locator(".practise")).toContainText("Повторять фразу");
    await expect(page.locator(".roll-tools")).toContainText("Цвет по");
    await expect(page.locator(".roll-legend")).toContainText("Дорожка 1");
    await expect(page.locator(".roll-legend")).toContainText("Л, П: рука из файла");
    await expect(page.getByRole("application", { name: /^Пиано-ролл/ })).toBeVisible();
    expect(errors).toEqual([]);
});
