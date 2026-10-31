// Understand a phrase (#25) in every browser project: the facts music-core finds for the selection,
// a fact chosen and its places highlighted in the score, one place at a time, a fact followed to the
// experiment resting on it, and the experiment followed back to its fact.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

test("understand a phrase: highlight a fact's places, try its experiment and come back", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    const tab = page.getByRole("tab", { name: "Understand" });
    await tab.click();
    const panel = page.locator(".tab-panel");
    const highlighted = page.locator(".score .highlight");

    // Every fact keeps its certainty and basis; the motif's return is a structure fact, inferred.
    const fact = panel.getByRole("button", { name: /The motif recurs once/ });
    await expect(fact.locator("xpath=ancestor::li").locator(".certainty")).toHaveText("inferred");
    await expect(highlighted).toHaveCount(0);

    // Choosing it highlights its notes in the score, each boxed, and lists its places.
    await fact.click();
    await expect(fact).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".inspector .highlighted")).toContainText("The motif recurs once");
    await expect(highlighted.first()).toBeVisible();
    const count = await highlighted.count();
    expect(count).toBeGreaterThan(0);
    await expect(page.locator(".score .highlight-mark")).toHaveCount(count);

    // A place on its own moves playback to its start.
    const place = panel.getByRole("group", { name: "Places" }).getByRole("button", { name: "exactly in bar 5" });
    await place.click();
    await expect(place).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".transport-bar .position")).toHaveText("Bar 5, beat 1");

    // Chosen again, the highlight clears.
    await fact.click();
    await expect(fact).toHaveAttribute("aria-pressed", "false");
    await expect(highlighted).toHaveCount(0);

    // The fact links to the experiment that rests on it, which Explore tries at once.
    await fact.click();
    await panel.getByRole("button", { name: "Try: Answer the motif differently" }).click();
    await expect(page.getByRole("tab", { name: "Explore" })).toHaveAttribute("aria-selected", "true");
    const card = page.locator(".explore");
    await expect(card.locator("h3")).toHaveText("Answer the motif differently");
    await expect(page.locator(".inspector .selection-text")).toContainText("bar 5");

    // The experiment links back to its fact, which Understand shows highlighted.
    await card.getByRole("group", { name: "Rests on" }).getByRole("button", { name: /The motif recurs once/ }).click();
    await expect(tab).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".inspector .highlighted")).toContainText("The motif recurs once");
    await page.locator("select[aria-label='Which score shows']").selectOption({ label: "Generated from the notes" });
    await expect(highlighted.first()).toBeVisible();
    await page.locator(".inspector .highlighted").getByRole("button", { name: "Clear highlight" }).click();
    await expect(highlighted).toHaveCount(0);

    await page.locator(".transport-bar").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("the facts read in Russian when the app is in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 1);
    await page.getByRole("tab", { name: "Понять" }).click();
    const panel = page.locator(".tab-panel");

    await expect(panel.getByRole("button", { name: /^Ритм/ })).toBeVisible();
    await expect(panel.getByText("Тональность: ля минор.")).toBeVisible();
    const fact = panel.getByRole("button", { name: /Мотив повторяется 1 раз/ });
    await expect(fact.locator("xpath=ancestor::li").locator(".certainty")).toHaveText("предположено");
    await fact.click();
    await expect(panel.getByRole("group", { name: "Места" }).getByRole("button", { name: "точно в такте 5" })).toBeVisible();
    expect(errors).toEqual([]);
});
