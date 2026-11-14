// Phrase focus (decision record 0018, section 4): the phrase lifted into a card across the screen over the dimmed page,
// entered by Focus in the menu, left by Back, a tap on the page or Escape; it stays on its passage and plays it
// round and round (#533 2); a variation is engraved as an ossia above the original, with Original / Variation / Alternate
// in the focus bar and the kept variations as chips; no Lead-in (#533 3).
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

test("Expand stays on its passage: no arrows to another phrase, the keys step nowhere, Play loops it (#521 19, #533 2)", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectPhrase(page);
    await focusSelection(page);
    const crumb = bar(page).locator(".crumb");
    await expect(crumb).toContainText("phrase 1 of 21");
    await expect(card(page).getByRole("button", { name: /^(Previous|Next) phrase$/ })).toHaveCount(0);
    await page.keyboard.press("PageDown");
    await expect(crumb).toContainText("phrase 1 of 21");
    await expect(page.locator(".selection-status")).toContainText("bars 0–4");

    // One Play, no Loop: the passage plays round and round, past its end and back to its start.
    const pill = page.locator(".pill");
    await expect(pill.getByRole("button", { name: "Loop" })).toHaveCount(0);
    await pill.getByRole("button", { name: "Play", exact: true }).click();
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    const loops = await page.evaluate(() => window.__audioTrace.posted.filter((message) => message.type === "loop").at(-1));
    expect(loops?.once ?? false).toBe(false);
    await page.waitForTimeout(12_000);
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await expect(crumb).toContainText("phrase 1 of 21");
    await pill.getByRole("button", { name: "Pause", exact: true }).click();

    // Back to the page, the loop as it was: none.
    await bar(page).getByRole("button", { name: "Back to page" }).click();
    await expect(page.locator(".pill").getByRole("button", { name: "Loop" })).toHaveAttribute("aria-pressed", "false");
    expect(errors).toEqual([]);
});

test("in Expand the roll's playhead is taken while it plays, dragged inside the passage, heard, and plays on (#533 2, 3)", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectPhrase(page);
    await focusSelection(page);
    await bar(page).getByRole("radio", { name: "Piano roll" }).click();
    const control = card(page).locator(".roll .roll-playhead");
    await expect(control).toBeAttached();
    const pill = page.locator(".pill");
    await pill.getByRole("button", { name: "Play", exact: true }).click();
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await page.waitForTimeout(800);
    await page.evaluate(() => { window.__audioTrace.posted.length = 0; });

    // The control sits over the playhead's handle under the bar numbers: taken, the music stands; dragged far either way it
    // stays inside the passage; let go, the passage plays on from there.
    // The test's own round trips can take longer than a hand, so the playhead may have run on past the press: then again.
    let from = null;
    await expect(async () => {
        const box = await control.boundingBox();
        from = { x: box.x + box.width / 2, y: box.y + 4 };
        await page.mouse.move(from.x, from.y);
        await page.mouse.down();
        try {
            await expect(pill.getByRole("button", { name: "Play", exact: true })).toBeVisible({ timeout: 1_500 });
        } catch (missed) {
            await page.mouse.up();
            throw missed;
        }
    }).toPass({ timeout: 20_000 });
    await page.mouse.move(from.x + 2000, from.y, { steps: 12 });
    const end = Number(await control.getAttribute("max"));
    await expect.poll(async () => Number(await control.inputValue())).toBeLessThanOrEqual(end);
    await page.mouse.move(from.x - 2000, from.y, { steps: 12 });
    const start = Number(await control.getAttribute("min"));
    await expect.poll(async () => Number(await control.inputValue())).toBe(start);
    await page.mouse.up();
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    const heard = await page.evaluate(() => window.__audioTrace.posted.map((message) => message.type).filter((type) => ["scrub", "scrubEnd", "play", "pause", "loop", "loopOff"].includes(type)));
    expect(heard[0]).toBe("pause");
    expect(heard).toContain("scrub");
    expect(heard.slice(-2)).toEqual(["scrubEnd", "play"]);
    expect(heard).not.toContain("loopOff");
    await expect(page.locator(".selection-status")).toContainText("bars 0–4");
    await pill.getByRole("button", { name: "Pause", exact: true }).click();
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

    // No Lead-in in Expand (#533 3).
    await expect(page.locator(".pill").getByRole("button", { name: "Lead-in" })).toHaveCount(0);
});
