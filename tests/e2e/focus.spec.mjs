// Phrase focus (decision record 0018, section 4): the phrase lifted into a card across the screen over the dimmed page,
// entered by a second tap on its bracket or Focus in the menu, left by Back, a tap on the page or Escape; previous and next
// walk the piece; a variation is engraved as an ossia above the original, with Original / Variation / Alternate in the
// focus bar and the kept variations as chips; Lead-in on the pill.
import { expect, test } from "@playwright/test";
import { openFurElise, openInsights, tryWay } from "./steps.mjs";

const card = page => page.locator(".focus-card");
const bar = page => page.locator(".bar");

test("a second tap on the selected bracket lifts the phrase; Back, the dimmed page and Escape put it back", async ({ page }) => {
    const errors = await openFurElise(page);
    const bracket = page.locator(".score button.bracket[aria-label]").first();
    await bracket.click();
    await expect(card(page)).toHaveCount(0);

    await bracket.click();
    await expect(card(page).locator(".phrase-card-score > .card-page > svg")).toBeVisible();
    await expect(page.locator(".workspace")).toHaveClass(/focused/);
    await expect(bar(page).locator(".crumb")).toHaveText("the pickup and bars 1–4 · phrase 1 of 2");
    // The page's menu gives way to the card.
    await expect(page.locator(".ctx .menu")).toHaveCount(0);
    // The card engraves only the phrase: its notes are those of bars 1 to 4, the first bar numbered.
    await expect(card(page).locator("g.note").first()).toBeVisible();

    await bar(page).getByRole("button", { name: "Back to page" }).click();
    await expect(card(page)).toHaveCount(0);
    await expect(page.locator(".selection-status")).toContainText("bars 0–4");
    await expect(page.locator(".ctx .menu")).toBeVisible();

    // Focus from the contextual menu; a tap on the dimmed page leaves.
    await page.locator(".ctx .menu").getByRole("button", { name: "Focus" }).click();
    await expect(card(page)).toBeVisible();
    await page.locator(".focus-layer .veil").click({ position: { x: 6, y: 300 } });
    await expect(card(page)).toHaveCount(0);

    await bracket.click();
    await expect(card(page)).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(card(page)).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("previous and next walk the phrases, and the card follows", async ({ page }) => {
    await openFurElise(page);
    const bracket = page.locator(".score button.bracket[aria-label]").first();
    await bracket.click();
    await bracket.click();
    const previous = card(page).getByRole("button", { name: "Previous phrase" });
    const next = card(page).getByRole("button", { name: "Next phrase" });
    await expect(previous).toBeDisabled();
    await next.click();
    await expect(bar(page).locator(".crumb")).toContainText("phrase 2 of 2");
    await expect(page.locator(".selection-status")).toContainText("bars 4–8");
    await expect(next).toBeDisabled();
    await page.keyboard.press("PageUp");
    await expect(bar(page).locator(".crumb")).toContainText("phrase 1 of 2");
});

test("a variation is the ossia above the phrase, heard with Original, Variation or Alternate; kept ones are chips", async ({ page }) => {
    await openFurElise(page);
    const bracket = page.locator(".score button.bracket[aria-label]").first();
    await bracket.click();
    await openInsights(page);
    await tryWay(page, "Held chord");
    await page.locator(".stream").getByRole("button", { name: "Keep this variation" }).click();
    await bar(page).getByRole("button", { name: "Insights" }).click();

    await bracket.click();
    await expect(card(page)).toBeVisible();
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
