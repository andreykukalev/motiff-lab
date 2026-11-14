// Phrase focus (decision record 0018, section 4): the phrase lifted into a card across the screen over the dimmed page,
// entered by Focus in the menu, left by Back, a tap on the page or Escape; it stays on its passage and plays it once
// (#521 19); a variation is engraved as an ossia above the original, with Original / Variation / Alternate in the focus
// bar and the kept variations as chips; Lead-in on the pill.
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

test("Expand stays on its passage: no arrows to another phrase, the keys step nowhere, Play plays it once and stops (#521 19)", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectPhrase(page);
    await focusSelection(page);
    const crumb = bar(page).locator(".crumb");
    await expect(crumb).toContainText("phrase 1 of 21");
    await expect(card(page).getByRole("button", { name: /^(Previous|Next) phrase$/ })).toHaveCount(0);
    await page.keyboard.press("PageDown");
    await expect(crumb).toContainText("phrase 1 of 21");
    await expect(page.locator(".selection-status")).toContainText("bars 0–4");

    // One Play, no Loop: the passage plays once and stops at its end, ready at its start.
    const pill = page.locator(".pill");
    await expect(pill.getByRole("button", { name: "Loop" })).toHaveCount(0);
    await pill.getByRole("button", { name: "Play", exact: true }).click();
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await expect(pill.getByRole("button", { name: "Play", exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(crumb).toContainText("phrase 1 of 21");

    // Back to the page, the loop as it was: none.
    await bar(page).getByRole("button", { name: "Back to page" }).click();
    await expect(page.locator(".pill").getByRole("button", { name: "Loop" })).toHaveAttribute("aria-pressed", "false");
    expect(errors).toEqual([]);
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
