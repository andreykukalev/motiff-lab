// Facts in the Insights stream (#25; decision record 0018 section 6) in every browser project: the facts music-core finds
// for the selection, grouped by lens and narrowed by filter chips, a fact chosen and its places ringed in the score, one
// place at a time, a fact's Try button opening its ways, and the variation followed back to its fact.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar, openInsights, openLenses } from "./steps.mjs";

test("understand a phrase: highlight a fact's places, try a way and come back", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    const stream = await openInsights(page);
    const highlighted = page.locator(".score .highlight");

    // The stream opens on Main (testing item 14): the harmonic motion first, then the texture, then the strongest of the
    // rest; every other fact under Details, folded by lens with its count, a tap opening it.
    const main = stream.locator(".lens-group.main");
    await expect(main.getByRole("heading", { name: "Main" })).toBeVisible();
    // Bar 1 is the upbeat, one line: no harmonic motion, and the texture says so (the sentence spec reads the motion).
    await expect(main.locator("article.fact-row").first()).toContainText("One line with no accompaniment.");
    expect(await main.locator("article.fact-row").count()).toBeLessThanOrEqual(3);
    await expect(stream.getByRole("heading", { name: "Details" })).toBeVisible();
    const rhythm = stream.locator(".lens-group").filter({ has: page.getByRole("heading", { name: /^Rhythm · / }) });
    const toggle = rhythm.locator(".lens-toggle");
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await expect(rhythm.locator("article.fact-row")).toHaveCount(0);
    await toggle.click();
    await expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(await rhythm.locator("article.fact-row").count()).toBeGreaterThan(0);
    await toggle.click();
    await expect(rhythm.locator("article.fact-row")).toHaveCount(0);
    await openLenses(page);

    // Every fact keeps its certainty and Evidence; the motif's return is a structure fact, inferred.
    const fact = stream.getByRole("button", { name: /The motif recurs 11 times/ });
    const row = stream.locator("article.fact-row").filter({ has: page.getByRole("button", { name: /The motif recurs 11 times/ }) });
    await expect(row.locator(".certainty")).toHaveText("inferred");
    await row.getByRole("button", { name: "Evidence" }).click();
    await expect(row.locator(".basis").first()).not.toBeEmpty();
    await expect(highlighted).toHaveCount(0);

    // The filter chips narrow the stream to one lens, and All brings every lens back.
    const filters = stream.getByRole("group", { name: "Show facts about" });
    await filters.getByRole("button", { name: "Rhythm", exact: true }).click();
    await expect(fact).toHaveCount(0);
    await filters.getByRole("button", { name: "All", exact: true }).click();
    await expect(fact).toBeVisible();

    // Choosing it rings its notes in the score and lists its places.
    await fact.click();
    await expect(fact).toHaveAttribute("aria-pressed", "true");
    await expect(row).toHaveClass(/\bon\b/);
    await expect(highlighted.first()).toBeVisible();
    const count = await highlighted.count();
    expect(count).toBeGreaterThan(0);
    await expect(page.locator(".score .highlight-mark")).toHaveCount(count);

    // A place on its own moves playback to its start.
    const place = row.getByRole("group", { name: "Places" }).getByRole("button", { name: "exactly in bar 5", exact: true });
    await place.click();
    await expect(place).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".pill .position")).toHaveText("Bar 5, beat 1");

    // Chosen again, the highlight clears.
    await fact.click();
    await expect(fact).toHaveAttribute("aria-pressed", "false");
    await expect(highlighted).toHaveCount(0);

    // A melody fact's Try button opens its ways; a way is heard at once, on the same selection.
    const melody = stream.locator("article.fact-row").filter({ hasText: /^6 notes in the melody/ });
    await melody.getByRole("button", { name: "Try", exact: true }).click();
    await melody.locator(".try-row").getByRole("button", { name: "Passing notes" }).click();
    const card = stream.locator(".explore");
    await expect(card.locator("h3")).toHaveText("Try a variation");
    await expect(page.locator(".selection-status")).toContainText("bar 1");

    // The experiment links back to its fact, which is chosen and rings its places on the score.
    await card.getByText("About this idea").click();
    await card.getByRole("group", { name: "Rests on" }).getByRole("button", { name: /^6 notes in the melody/ }).click();
    await expect(melody).toHaveClass(/\bon\b/);
    await expect(highlighted.first()).toBeVisible();

    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("the facts read in Russian when the app is in Russian (#134)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback?start=fur-elise");
    await selectBar(page, 1);
    const stream = await openInsights(page);
    await expect(stream.getByRole("heading", { name: "Главное" })).toBeVisible();
    await expect(stream.getByRole("heading", { name: "Подробности" })).toBeVisible();
    await openLenses(page);

    await expect(stream.getByRole("group", { name: "Показать факты о" }).getByRole("button", { name: "Ритм", exact: true })).toBeVisible();
    await expect(stream.getByText("Тональность: ля минор.")).toBeVisible();
    const fact = stream.getByRole("button", { name: /Мотив повторяется 11 раз/ });
    await expect(stream.locator("article.fact-row").filter({ has: page.getByRole("button", { name: /Мотив повторяется 11 раз/ }) }).locator(".certainty")).toHaveText("предположено");
    await fact.click();
    await expect(stream.getByRole("group", { name: "Места" }).getByRole("button", { name: "точно в такте 5", exact: true })).toBeVisible();
    expect(errors).toEqual([]);
});
