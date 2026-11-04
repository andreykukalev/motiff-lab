// Trying variations in the Insights stream (decision records 0010 and 0018 section 6; Andrei, 2026-11-04) in every browser
// project: a fact's one quiet Try button opens the two or three ways music-core offers for it; a way is heard at once
// against the original, kept and left; the accompaniment re-patterned while the melody plays alone; and Adjust with the
// catalogue's operations.
import { expect, test } from "@playwright/test";
import { openAdjust, openFurElise, selectBar, openInsights, heardChoice, tryWay } from "./steps.mjs";

test("a fact's Try button opens its ways; one is heard, kept, another tried and left", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    const stream = await openInsights(page);
    const card = stream.locator(".explore");
    const selection = page.locator(".selection-status");
    const compare = page.locator(".inspector-head").getByRole("group", { name: "Compare with the original" });

    // The ways stay folded until the fact's Try button is pressed; each way is offered once in the stream.
    await expect(stream.locator(".try-row .chip")).toHaveCount(0);
    const density = stream.locator(".fact-row").filter({ hasText: /^Density/ });
    await density.getByRole("button", { name: "Try", exact: true }).click();
    await expect(density.locator(".try-row .chip")).toHaveText(["Held chord", "Repeated chord in 8ths"]);
    await expect(stream.locator(".try-row").getByRole("button", { name: "Held chord", exact: true })).toHaveCount(1);
    // The rhythm and contour reshapes are not offered any more.
    await expect(stream.getByRole("button", { name: "Long–short" })).toHaveCount(0);

    await tryWay(page, "Held chord");
    await expect(card.locator("h3")).toHaveText("Try a variation");
    await expect(card.locator(".passage")).toHaveText("Selected phrase · bar 2");
    await expect(card.locator(".choices button")).toHaveText(["Held chord", "Repeated chord in 8ths"]);
    await expect(card.locator(".caption").first()).toHaveText("Keeps the melody.");
    await expect.poll(() => heardChoice(page)).toBe("Variation");
    await expect(compare.getByRole("button", { name: "Variation", exact: true })).toHaveAttribute("aria-pressed", "true");
    await compare.getByRole("button", { name: "Original", exact: true }).click();
    await expect.poll(() => heardChoice(page)).toBe("Original");

    await stream.getByRole("button", { name: "Keep this variation" }).click();
    // Für Elise has no project, so the card says the variation is kept while the page is open (#78).
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    await card.getByRole("button", { name: "Try another" }).click();
    await expect(card.locator(".choices button[aria-pressed=true]")).toHaveText("Repeated chord in 8ths");

    // Leave it puts back the variation that was kept.
    await card.getByRole("button", { name: "Leave it" }).click();
    await expect(selection).toContainText("bar 2");
    await expect(card).toHaveCount(0);
    const adjust = await openAdjust(page);
    await expect(adjust.locator(".steps")).toContainText("Held chord");
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("change the accompaniment and hear the melody alone", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await openInsights(page);
    const card = await tryWay(page, "Held chord");
    await expect(card.locator(".what-changed")).toContainText("the accompaniment keeps its pitches");
    await card.getByRole("button", { name: "Melody alone" }).click();
    await expect(page.locator(".selection-status")).toContainText("Looping the melody alone.");
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("the Try button, its ways and what changed read in Russian when the app is in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 2);
    const stream = await openInsights(page);

    await expect(stream.locator(".try-row").getByRole("button", { name: "Попробовать", exact: true }).first()).toBeVisible();
    const card = await tryWay(page, "Выдержанный аккорд");
    await expect(card.locator("h3")).toHaveText("Попробовать вариацию");
    await expect(card.locator(".passage")).toHaveText("Выделенная фраза · такт 2");
    await expect(card.locator(".what-changed")).toContainText("Что изменилось:");
    await expect(card.locator(".what-changed")).not.toContainText(/\b(notes?|bar|beat|changed)\b/);
    await stream.getByRole("button", { name: "Оставить эту вариацию" }).click();
    await expect(card.getByRole("status")).toContainText("Сохранено, пока вы не уйдёте со страницы");
    expect(errors).toEqual([]);
});

test("Adjust names the operations and explains a step in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 1);
    await openInsights(page);
    const panel = await openAdjust(page);

    await expect(panel.locator("select.preset option:checked")).toHaveText("Свой выбор того, что сохранить");
    await expect(panel.locator("select.preset option")).toContainText(["Сохранить порядок высот, изменить ритм"]);
    await expect(panel.getByRole("button", { name: "V1 Изменение ритма (Rhythm reshape)" })).toBeVisible();
    await panel.getByRole("button", { name: "Пунктир (Dotted)" }).click();
    await expect(panel.locator(".steps")).toHaveText("Оригинал › пробуем: Пунктир (Dotted)");
    await expect(panel.locator(".changed p").first()).toContainText("«долгая–короткая»");
    await expect(panel.locator(".changed p").first()).not.toContainText(/\b(notes?|bar|beat)\b/);
    expect(errors).toEqual([]);
});
