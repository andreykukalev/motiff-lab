// The score page (decision record 0018, sections 2 to 4): the score is the whole screen with the bar floating over it,
// phrases are brackets on the score at the level the player chooses, and the transport pill, the piano roll drawer and
// the Insights sheet come and go without touching the selection.
import { expect, test } from "@playwright/test";
import { closePopover, openFurElise, openInsights, openRoll } from "./steps.mjs";

test("the score fills the screen, and brackets pick phrases at the level chosen", async ({ page }) => {
    const errors = await openFurElise(page);
    const bar = page.locator(".bar");
    const brackets = page.locator(".score button.bracket[aria-label]");
    const selection = page.locator(".selection-status");
    const pill = page.locator(".pill");

    // The page and the bar, with nothing docked: no pill, roll or sheet until asked for.
    await expect(page.locator(".score .score-page")).toHaveCount(5);
    await expect(bar).toContainText("Für Elise");
    await expect(bar).toContainText(/1(–2)? of 5/);
    await expect(pill).toHaveCount(0);
    await expect(page.locator(".roll")).toHaveCount(0);
    await expect(page.locator(".sheet")).toHaveCount(0);

    // Für Elise is twenty phrases at the default level; a tap on a bracket selects its phrase and brings the pill.
    await expect(brackets).toHaveCount(20);
    await expect(brackets.nth(1)).toHaveAccessibleName("Phrase 2, bars 4–8");
    await brackets.nth(1).click();
    await expect(brackets.nth(1)).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Selected phrase 2");
    await expect(pill).toBeVisible();

    // Page Up and Page Down step from phrase to phrase, as page-turner pedals do.
    await page.keyboard.press("PageUp");
    await expect(selection).toContainText("Selected phrase 1");
    await expect(brackets.nth(0)).toHaveAttribute("aria-pressed", "true");

    // The Phrases popover: a coarser level redraws the brackets and the selection moves to the unit around it.
    await bar.getByRole("button", { name: "Phrases" }).click();
    const popover = page.getByRole("dialog", { name: "Phrases" });
    await popover.getByRole("radio", { name: "Sentences" }).click();
    await expect(brackets).toHaveCount(5);
    await expect(selection).toContainText("Selected sentence");
    await expect(popover).toContainText("5 sentences in the piece");
    await popover.getByRole("radio", { name: "Short" }).click();
    await expect.poll(() => brackets.count()).toBeGreaterThan(20);
    await popover.getByRole("radio", { name: "Phrases" }).click();
    await expect(brackets).toHaveCount(20);
    await expect(selection).toContainText("Selected phrase 1");

    // The brackets can be hidden for a clean page.
    const show = popover.getByRole("switch", { name: "Show brackets" });
    await show.uncheck();
    await expect(brackets).toHaveCount(0);
    await show.check();
    await expect(brackets).toHaveCount(20);
    await closePopover(page);

    // The roll and the sheet come and go; the selection stays as it was.
    await openRoll(page);
    await bar.getByRole("button", { name: "Piano roll" }).click();
    await expect(page.locator(".roll")).toHaveCount(0);
    await openInsights(page);
    await expect(page.locator(".sheet .selection-text")).toHaveText("Phrase 1 · bars 0–4");
    await bar.getByRole("button", { name: "Insights" }).click();
    await expect(page.locator(".sheet")).toHaveCount(0);
    await expect(selection).toContainText("Selected phrase 1");

    // A bar clicked in the score, away from its notes (here on a staff line), is selected.
    const line = page.locator("div.score g.measure").nth(2).locator("g.staff > path").first();
    const box = await line.boundingBox();
    await page.mouse.click(box.x + box.width * 0.9, box.y + box.height / 2);
    await expect(selection).toContainText("Selected bar 2");

    // The pill loops the selection and slows it down.
    await pill.getByRole("button", { name: "Loop" }).click();
    await expect(pill.getByRole("button", { name: "Loop" })).toHaveAttribute("aria-pressed", "true");
    await pill.getByRole("button", { name: "Tempo 100%" }).click();
    await page.getByRole("dialog", { name: "Tempo" }).getByRole("button", { name: "75%" }).click();
    await expect(pill.locator(".mono")).toHaveText("75%");
    await pill.getByRole("button", { name: "Play", exact: true }).click();
    await pill.getByRole("button", { name: "Pause", exact: true }).click();
    expect(errors).toEqual([]);
});

test("the bar fades while the player reads and comes back with the next touch", async ({ page }) => {
    const errors = await openFurElise(page);
    const bar = page.locator(".bar");
    await page.mouse.move(600, 500);
    await expect(bar).not.toHaveClass(/faded/);
    await expect(bar).toHaveClass(/faded/, { timeout: 5_000 });
    await page.mouse.move(610, 510);
    await expect(bar).not.toHaveClass(/faded/);
    expect(errors).toEqual([]);
});

test("bars dragged across on the score are selected, the handles move the edges, and the menu over them edits phrases", async ({ page }) => {
    const errors = await openFurElise(page);
    const bars = page.locator("div.score g.measure");
    const selection = page.locator(".selection-status");
    const menu = page.getByRole("toolbar", { name: "Selection" });
    const brackets = page.locator(".score button.bracket[aria-label]");
    await expect(menu).toBeHidden();

    // A drag with the mouse from bar 1 to bar 3 selects them, banded, with the menu above.
    const from = await bars.nth(1).boundingBox();
    const to = await bars.nth(3).boundingBox();
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 8 });
    await page.mouse.up();
    await expect(selection).toContainText("Selected bars 1–3");
    await expect(page.locator(".score .sel-band").first()).toBeVisible();
    await expect(menu).toBeVisible();
    await expect(menu.getByRole("button", { name: "Loop" })).toBeVisible();

    // The end handle moves a bar on with the arrow keys; the start handle drags to a barline.
    await page.getByRole("button", { name: /^End of the selection/ }).focus();
    await page.keyboard.press("ArrowRight");
    await expect(selection).toContainText("Selected bars 1–4");
    const start = await page.getByRole("button", { name: /^Start of the selection/ }).boundingBox();
    const bar2 = await bars.nth(2).boundingBox();
    await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
    await page.mouse.down();
    await page.mouse.move(bar2.x + 2, start.y + start.height / 2, { steps: 6 });
    await page.mouse.up();
    await expect(selection).toContainText("Selected bars 2–4");

    // The overflow holds what applies: the range is not a phrase, so Make a phrase, and nothing else.
    await menu.getByRole("button", { name: "More for the selection" }).click();
    await expect(menu.getByRole("button", { name: "Merge with the next phrase" })).toHaveCount(0);
    await menu.getByRole("button", { name: "Make a phrase" }).click();
    await expect(brackets).toHaveCount(21);
    await expect(brackets.nth(1)).toHaveAttribute("aria-pressed", "true");
    await expect(selection).toContainText("Selected phrase 2");

    // Merge with the next phrase takes the boundary after it away.
    await menu.getByRole("button", { name: "More for the selection" }).click();
    await menu.getByRole("button", { name: "Merge with the next phrase" }).click();
    await expect(brackets).toHaveCount(20);
    await expect(selection).toContainText("Selected phrase 2");

    // A phrase with the playhead outside it can be merged on, but not made or split.
    await menu.getByRole("button", { name: "More for the selection" }).click();
    await expect(menu.getByRole("button", { name: "Highlight in amber" })).toBeVisible();
    await expect(menu.getByRole("button", { name: "Merge with the next phrase" })).toBeVisible();
    await expect(menu.getByRole("button", { name: /^(Make a phrase|Split here)$/ })).toHaveCount(0);
    await menu.getByRole("button", { name: "More for the selection" }).click();

    // Split here splits the phrase where the playhead stands, a note tapped in a later bar of it.
    // (Dispatched, as the menu may stand over the bar under the band.)
    await bars.nth(5).locator("g.note").first().dispatchEvent("click");
    await menu.getByRole("button", { name: "More for the selection" }).click();
    await menu.getByRole("button", { name: "Split here" }).click();
    await expect(brackets).toHaveCount(21);
    await expect(selection).toContainText("Selected phrase 2");

    // Reset to detected brings the suggested phrases back.
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
    await page.getByRole("dialog", { name: "Phrases" }).getByRole("button", { name: "Reset to detected" }).click();
    await expect(brackets).toHaveCount(20);
    await closePopover(page);

    // With the pen on, the menu goes away and the mouse writes rather than selects.
    await page.locator(".bar").getByRole("button", { name: "Pen", exact: true }).click();
    await expect(menu).toBeHidden();
    await page.locator(".bar").getByRole("button", { name: "Pen", exact: true }).click();
    await expect(menu).toBeVisible();
    expect(errors).toEqual([]);
});
