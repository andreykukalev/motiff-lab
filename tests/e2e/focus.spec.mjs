// Phrase focus (decision record 0018, section 4): the phrase lifted into a card across the screen over the dimmed page,
// entered by Focus in the menu, left by Back, a tap on the page or Escape; previous and next
// walk the piece; a variation is engraved as an ossia above the original, with Original / Variation / Alternate in the
// focus bar and the kept variations as chips; Lead-in on the pill.
import { expect, test } from "@playwright/test";
import { focusSelection, openFurElise, openInsights, selectPhrase, tryWay } from "./steps.mjs";

const card = page => page.locator(".focus-card");
const bar = page => page.locator(".bar");

test("Focus lifts the selected phrase; Back, the dimmed page and Escape put it back", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectPhrase(page);
    await expect(card(page)).toHaveCount(0);

    await focusSelection(page);
    await expect(card(page).locator(".phrase-card-score > .card-page > svg")).toBeVisible();
    await expect(page.locator(".workspace")).toHaveClass(/focused/);
    await expect(bar(page).locator(".crumb")).toHaveText("the pickup and bars 1–4 · phrase 1 of 21");
    // The page's menu gives way to the card.
    await expect(page.locator(".ctx .menu")).toHaveCount(0);
    // The card engraves only the phrase: its notes are those of bars 1 to 4, the first bar numbered.
    await expect(card(page).locator("g.note").first()).toBeVisible();

    await bar(page).getByRole("button", { name: "Back to page" }).click();
    await expect(card(page)).toHaveCount(0);
    await expect(page.locator(".selection-status")).toContainText("bars 0–4");
    await expect(page.locator(".ctx .menu")).toBeVisible();

    // Again from the contextual menu; a tap on the dimmed page leaves.
    await focusSelection(page);
    await page.locator(".focus-layer .veil").click({ position: { x: 6, y: 300 } });
    await expect(card(page)).toHaveCount(0);

    await focusSelection(page);
    await page.keyboard.press("Escape");
    await expect(card(page)).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("previous and next walk the phrases, and the card follows", async ({ page }) => {
    // Twenty-one phrases, each engraved in the card: two to three seconds a phrase on a CI runner, near the minute in
    // every engine (#309).
    test.slow(true, "walks all twenty-one phrases");
    await openFurElise(page);
    await selectPhrase(page);
    await focusSelection(page);
    const previous = card(page).getByRole("button", { name: "Previous phrase" });
    const next = card(page).getByRole("button", { name: "Next phrase" });
    await expect(previous).toBeDisabled();
    await next.click();
    await expect(bar(page).locator(".crumb")).toContainText("phrase 2 of 21");
    await expect(page.locator(".selection-status")).toContainText("bars 4–8");
    await page.keyboard.press("PageUp");
    await expect(bar(page).locator(".crumb")).toContainText("phrase 1 of 21");
    // The last phrase of the piece has no next one.
    for (let phrase = 2; phrase <= 21; phrase++) {
        await page.keyboard.press("PageDown");
        await expect(bar(page).locator(".crumb")).toContainText(`phrase ${phrase} of 21`);
    }
    await expect(next).toBeDisabled();
});

test("a variation is the ossia above the phrase, heard with Original, Variation or Alternate; kept ones are chips", async ({ page }) => {
    await openFurElise(page);
    await selectPhrase(page);
    await openInsights(page);
    await tryWay(page, "Held chord");
    await page.locator(".stream").getByRole("button", { name: "Keep this variation" }).click();
    await bar(page).getByRole("button", { name: "Insights" }).click();

    await focusSelection(page);
    const frame = card(page).locator(".ossia-frame").first();
    await expect(frame).toBeVisible();
    await expect(frame).toContainText(/^Ossia · Held chord/);
    // The ossia's notes carry their own ids, above the original's.
    await expect(card(page).locator("g.note[id^='ossia-']").first()).toBeAttached();

    const listen = bar(page).getByRole("radiogroup", { name: "What you hear" });
    await expect(listen.getByRole("radio", { name: "Variation" })).toHaveAttribute("aria-checked", "true");
    await listen.getByRole("radio", { name: "Original" }).click();
    await expect(listen.getByRole("radio", { name: "Original" })).toHaveAttribute("aria-checked", "true");
    await listen.getByRole("radio", { name: "Alternate" }).click();
    await expect(listen.getByRole("radio", { name: "Alternate" })).toHaveAttribute("aria-checked", "true");
    await expect(page.locator(".selection-status")).toContainText("Looping the original, then the variation.");

    const chips = page.getByRole("group", { name: "Saved variations of this phrase" });
    await expect(chips.getByRole("button")).toHaveCount(1);
    await expect(chips.getByRole("button").first()).toContainText("Held chord");

    // Lead-in, on the pill in focus only.
    const leadIn = page.locator(".pill").getByRole("button", { name: "Lead-in" });
    await leadIn.click();
    await expect(leadIn).toHaveAttribute("aria-pressed", "true");
    await bar(page).getByRole("button", { name: "Back to page" }).click();
    await expect(page.locator(".pill").getByRole("button", { name: "Lead-in" })).toHaveCount(0);
});
