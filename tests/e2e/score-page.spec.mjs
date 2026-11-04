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
    await expect(page.locator(".score .score-page")).toHaveCount(1);
    await expect(bar).toContainText("Für Elise (Beethoven, opening)");
    await expect(bar).toContainText("p. 1 of 1");
    await expect(pill).toHaveCount(0);
    await expect(page.locator(".roll")).toHaveCount(0);
    await expect(page.locator(".sheet")).toHaveCount(0);

    // Für Elise's opening is two phrases at the default level; a tap on a bracket selects its phrase and brings the pill.
    await expect(brackets).toHaveCount(2);
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
    await expect(brackets).toHaveCount(1);
    await expect(selection).toContainText("Selected sentence A");
    await expect(popover).toContainText("1 sentence in the piece");
    await popover.getByRole("radio", { name: "Short" }).click();
    await expect.poll(() => brackets.count()).toBeGreaterThan(2);
    await popover.getByRole("radio", { name: "Phrases" }).click();
    await expect(brackets).toHaveCount(2);
    await expect(selection).toContainText("Selected phrase 1");

    // The brackets can be hidden for a clean page.
    const show = popover.getByRole("switch", { name: "Show brackets" });
    await show.uncheck();
    await expect(brackets).toHaveCount(0);
    await show.check();
    await expect(brackets).toHaveCount(2);
    await closePopover(page);

    // The roll and the sheet come and go; the selection stays as it was.
    await openRoll(page);
    await bar.getByRole("button", { name: "Piano roll" }).click();
    await expect(page.locator(".roll")).toHaveCount(0);
    await openInsights(page);
    await expect(page.locator(".sheet .selection-text")).toContainText("Selected phrase 1");
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
