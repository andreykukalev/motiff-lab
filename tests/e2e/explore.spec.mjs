// Experiments in the Insights stream (decision records 0010 and 0018 section 6) in every browser project: the Try chips
// music-core offers for a phrase, tried, heard against the original, kept and left; a question that changes the motif's
// return; the accompaniment re-patterned while the melody plays alone; and Adjust with the catalogue's operations.
import { expect, test } from "@playwright/test";
import { openAdjust, openFurElise, selectBar, openInsights, heardChoice, tryExperiment } from "./steps.mjs";

test("explore a phrase: try an experiment, keep it, answer the motif and leave", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    const stream = await openInsights(page);
    const card = stream.locator(".explore");
    const selection = page.locator(".selection-status");
    const compare = page.locator(".inspector-head").getByRole("group", { name: "Compare with the original" });

    // Bar 1 offers space, groove and an answer as Try chips; trying one plays its variation at once.
    await expect(stream.locator(".try-row").getByRole("button", { name: "Same notes, different groove" }).first()).toBeVisible();
    await tryExperiment(page, "Give the melody more space");
    await expect(stream.locator(".try-row button[aria-pressed=true]").first()).toHaveText("Give the melody more space");
    await expect(card.locator(".passage")).toHaveText("Selected phrase · bar 1");
    await expect.poll(() => heardChoice(page)).toBe("Variation");
    await expect(compare.getByRole("button", { name: "Variation", exact: true })).toHaveAttribute("aria-pressed", "true");
    await compare.getByRole("button", { name: "Original", exact: true }).click();
    await expect.poll(() => heardChoice(page)).toBe("Original");

    await stream.getByRole("button", { name: "Keep this variation" }).click();
    // Für Elise has no project, so the card says the variation is kept while the page is open (#78).
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    await card.getByRole("button", { name: "Try another" }).click();
    await expect(card.locator("h3")).toHaveText("Same notes, different groove");

    // A question about the motif changes its return in bar 5, and hears the motif and the answer together.
    await stream.getByRole("button", { name: "I have a question" }).click();
    await stream.getByRole("button", { name: "Which changes keep the motif recognisable?" }).click();
    await expect(card.locator("h3")).toHaveText("Answer the motif differently");
    await expect(card.locator(".passage")).toHaveText("The motif's return · bar 5");
    await expect(selection).toContainText("bar 5");
    await card.getByRole("button", { name: "Upside down" }).click();
    await card.getByRole("button", { name: "Hear the motif, then the answer" }).click();
    await expect(selection).toContainText("Looping the motif and its new answer.");
    await expect.poll(() => heardChoice(page)).toBe("The piece with the variation");

    // Leave it puts back the selection and the variation that was kept.
    await card.getByRole("button", { name: "Leave it" }).click();
    await expect(selection).toContainText("bar 1");
    await expect(card).toHaveCount(0);
    const adjust = await openAdjust(page);
    await expect(adjust.locator(".steps")).toContainText("Detached (the melody)");
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("change the accompaniment and hear the melody alone", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    const stream = await openInsights(page);
    const card = stream.locator(".explore");
    await tryExperiment(page, "Keep the melody, change the accompaniment");
    await card.getByRole("button", { name: "Held chord", exact: true }).click();
    await expect(card.locator(".what-changed")).toContainText("the accompaniment keeps its pitches");
    await card.getByRole("button", { name: "Melody alone" }).click();
    await expect(page.locator(".selection-status")).toContainText("Looping the melody alone.");
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("the experiments and what changed read in Russian when the app is in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 1);
    const stream = await openInsights(page);
    const card = stream.locator(".explore");

    await expect(stream.locator(".try-row").getByRole("button", { name: "Те же ноты, другой ритм" }).first()).toBeVisible();
    await tryExperiment(page, "Дать мелодии больше воздуха");
    await expect(card.locator(".passage")).toHaveText("Выделенная фраза · такт 1");
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
