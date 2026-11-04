// Practise a phrase (#50, docs/08-central-product-model.md; 0018 section 2): the transport pill plays it, loops it, slows
// it down and plays one hand; moving to the next phrase and opening the Insights sheet keep the tempo, the loop and the hand.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar, openInsights } from "./steps.mjs";

test("practise a phrase: loop it, slow it down, hear one hand and move on", async ({ page }) => {
    const errors = await openFurElise(page);
    const pill = page.locator(".pill");
    const selection = page.locator(".selection-status");
    const tempo = pill.locator(".mono");

    await page.locator(".score button.bracket[aria-label]").first().click();
    await expect(selection).toContainText("Selected phrase 1");

    // The loop and the tempo, within reach on the pill.
    const loop = pill.getByRole("button", { name: "Loop", exact: true });
    await loop.click();
    await expect(loop).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Looping");
    await pill.getByRole("button", { name: "Tempo 100%" }).click();
    await page.getByRole("dialog", { name: "Tempo" }).getByRole("button", { name: "50%" }).click();
    await expect(tempo).toHaveText("50%");

    // One hand alone while the phrase loops; Für Elise marks the hands by staff.
    await pill.getByRole("button", { name: "Which notes play, light up and can be selected" }).click();
    await page.getByRole("dialog", { name: "Which notes play, light up and can be selected" }).getByRole("button", { name: "Left hand" }).click();
    await expect(pill.getByRole("button", { name: "Which notes play, light up and can be selected" })).toContainText("Left hand");

    // The next phrase keeps the loop, the tempo and the hand.
    await page.keyboard.press("PageDown");
    await expect(selection).toContainText("Selected phrase 2");
    await expect(loop).toHaveAttribute("aria-pressed", "true");
    await expect(pill.getByRole("button", { name: "Which notes play, light up and can be selected" })).toContainText("Left hand");

    // The Insights sheet works on the same phrase; nothing in the loop changes.
    await openInsights(page);
    await expect(page.locator(".sheet .selection-text")).toContainText("Selected phrase 2");
    await page.locator(".bar").getByRole("button", { name: "Insights" }).click();
    await expect(tempo).toHaveText("50%");
    await expect(loop).toHaveAttribute("aria-pressed", "true");

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
    await openInsights(page);
    await expect(page.locator(".stream").getByRole("group", { name: "Показать факты о" })).toContainText("Все");
    // The roll's head: note names on, the notes coloured by hand (0018 section 4).
    await expect(page.locator(".roll-head")).toContainText("Названия нот");
    await expect(page.locator(".roll-head")).toContainText("Цвет: руки");
    await expect(page.locator(".roll-legend")).toContainText("Правая рука");
    await expect(page.locator(".roll-legend")).toContainText("Л, П: рука из файла");
    await expect(page.getByRole("application", { name: /^Пиано-ролл/ })).toBeVisible();
    expect(errors).toEqual([]);
});
