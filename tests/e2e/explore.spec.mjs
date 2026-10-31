// The Explore card (decision record 0010) in every browser project: the experiments music-core offers
// for a phrase, tried, heard against the original, kept, and left; a question that changes the motif's
// return; and the accompaniment re-patterned while the melody plays alone.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

test("explore a phrase: try an experiment, keep it, answer the motif and leave", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    await page.getByRole("tab", { name: "Explore" }).click();
    const card = page.locator(".explore");
    const selection = page.locator(".inspector .selection-text");
    const heard = page.locator("select[aria-label='What is heard'] option:checked");

    // Bar 1 offers space, groove and an answer; trying one plays its variation at once.
    await expect(card.getByRole("button", { name: /Same notes, different groove/ })).toBeVisible();
    await card.getByRole("button", { name: /Give the melody more space/ }).click();
    await expect(card.locator("h3")).toHaveText("Give the melody more space");
    await expect(card.locator(".passage")).toHaveText("Selected phrase · bar 1");
    await expect(heard).toHaveText("Variation");
    await expect(card.getByRole("button", { name: "Variation", exact: true })).toHaveAttribute("aria-pressed", "true");
    await card.getByRole("button", { name: "Original", exact: true }).click();
    await expect(heard).toHaveText("Original");

    await card.getByRole("button", { name: "Keep this variation" }).click();
    // Für Elise has no project, so the card says the variation is kept while the page is open (#78).
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    await card.getByRole("button", { name: "Try another" }).click();
    await expect(card.locator("h3")).toHaveText("Same notes, different groove");

    // A question about the motif changes its return in bar 5, and hears the motif and the answer together.
    await card.getByRole("button", { name: "I have a question" }).click();
    await card.getByRole("button", { name: "Which changes keep the motif recognisable?" }).click();
    await expect(card.locator("h3")).toHaveText("Answer the motif differently");
    await expect(card.locator(".passage")).toHaveText("The motif's return · bar 5");
    await expect(selection).toContainText("bar 5");
    await card.getByRole("button", { name: "Upside down" }).click();
    await card.getByRole("button", { name: "Hear the motif, then the answer" }).click();
    await expect(selection).toContainText("Looping the motif and its new answer.");
    await expect(heard).toHaveText("The piece with the variation");

    // Leave it puts back the selection and the variation that was kept.
    await card.getByRole("button", { name: "Leave it" }).click();
    await expect(selection).toContainText("bar 1");
    await expect(page.locator(".inspector .tabs")).toBeVisible();
    await page.getByRole("tab", { name: "Tools", exact: true }).click();
    await expect(page.locator(".inspector .steps")).toContainText("Detached (the melody)");
    await page.locator(".transport-bar").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("change the accompaniment and hear the melody alone", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.getByRole("tab", { name: "Explore" }).click();
    const card = page.locator(".explore");
    await card.getByRole("button", { name: /Keep the melody, change the accompaniment/ }).click();
    await card.getByRole("button", { name: "Held chord", exact: true }).click();
    await expect(card.locator(".what-changed")).toContainText("the accompaniment keeps its pitches");
    await card.getByRole("button", { name: "Melody alone" }).click();
    await expect(page.locator(".inspector .selection-text")).toContainText("Looping the melody alone.");
    await page.locator(".transport-bar").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("the experiments and what changed read in Russian when the app is in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 1);
    await page.getByRole("tab", { name: "Исследовать" }).click();
    const card = page.locator(".explore");

    await expect(card.getByRole("button", { name: /Те же ноты, другой ритм/ })).toBeVisible();
    await card.getByRole("button", { name: /Дать мелодии больше воздуха/ }).click();
    await expect(card.locator("h3")).toHaveText("Дать мелодии больше воздуха");
    await expect(card.locator(".passage")).toHaveText("Выделенная фраза · такт 1");
    await expect(card.locator(".what-changed")).toContainText("Что изменилось:");
    await expect(card.locator(".what-changed")).not.toContainText(/\b(notes?|bar|beat|changed)\b/);
    await card.getByRole("button", { name: "Оставить эту вариацию" }).click();
    await expect(card.getByRole("status")).toContainText("Сохранено, пока вы не уйдёте со страницы");
    expect(errors).toEqual([]);
});

test("the Tools tab names the operations and explains a step in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 1);
    await page.getByRole("tab", { name: "Инструменты", exact: true }).click();
    const panel = page.locator(".tab-panel");

    await expect(panel.locator("select.preset option:checked")).toHaveText("Свой выбор того, что сохранить");
    await expect(panel.locator("select.preset option")).toContainText(["Сохранить порядок высот, изменить ритм"]);
    await expect(panel.getByRole("button", { name: "V1 Изменение ритма (Rhythm reshape)" })).toBeVisible();
    await panel.getByRole("button", { name: "Пунктир (Dotted)" }).click();
    await expect(panel.locator(".steps")).toHaveText("Оригинал › пробуем: Пунктир (Dotted)");
    await expect(panel.locator(".changed p").first()).toContainText("«долгая–короткая»");
    await expect(panel.locator(".changed p").first()).not.toContainText(/\b(notes?|bar|beat)\b/);
    expect(errors).toEqual([]);
});
