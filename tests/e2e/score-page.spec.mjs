// The score page (decision record 0018, sections 2 to 4): the score is the whole screen with the bar floating over it,
// the selection steps by phrases at the level the player chooses, and the transport pill, the piano roll drawer and the
// Insights sheet come and go without touching the selection. The phrases are not drawn as brackets above the top staff
// (Andrei, 2026-11-13, #479 item 14).
import { expect, test } from "@playwright/test";
import { closePopover, openFurElise, openInsights, openRoll, selectPhrase } from "./steps.mjs";

/** The Phrases popover says how many units the piece has at the level shown. */
async function expectUnits(page, text) {
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
    await expect(page.getByRole("dialog", { name: "Phrases" })).toContainText(text);
    await closePopover(page);
}

test("the score fills the screen, draws no phrase brackets, and the selection steps by phrases at the level chosen", async ({ page }) => {
    const errors = await openFurElise(page);
    const bar = page.locator(".bar");
    const brackets = page.locator(".score button.bracket");
    const selection = page.locator(".selection-status");
    const pill = page.locator(".pill");

    // The page and the bar, with nothing docked: no pill, roll or sheet until asked for.
    await expect(page.locator(".score .score-page")).toHaveCount(5);
    await expect(bar).toContainText("Für Elise");
    await expect(bar).toContainText(/1(–2)? of 5/);
    await expect(pill).toHaveCount(0);
    await expect(page.locator(".roll")).toHaveCount(0);
    await expect(page.locator(".sheet")).toHaveCount(0);

    // Für Elise is twenty-one phrases at the default level, none drawn over the staves; Page Down selects the first and
    // the next and brings the pill.
    await expect(brackets).toHaveCount(0);
    await selectPhrase(page, 1);
    await expect(selection).toContainText("Selected phrase 2, bars 4–8");
    await expect(pill).toBeVisible();

    // Page Up and Page Down step from phrase to phrase, as page-turner pedals do.
    await page.keyboard.press("PageUp");
    await expect(selection).toContainText("Selected phrase 1");

    // The Phrases popover: at a coarser level the selection moves to the unit around it; it has no switch for brackets.
    await bar.getByRole("button", { name: "Phrases" }).click();
    const popover = page.getByRole("dialog", { name: "Phrases" });
    await expect(popover).toContainText("21 phrases in the piece");
    await expect(popover.getByRole("switch")).toHaveCount(0);
    await popover.getByRole("radio", { name: "Sentences" }).click();
    await expect(selection).toContainText("Selected sentence");
    await expect(popover).toContainText("4 sentences in the piece");
    await popover.getByRole("radio", { name: "Short" }).click();
    await expect(popover).toContainText("short phrases in the piece");
    await popover.getByRole("radio", { name: "Phrases" }).click();
    await expect(popover).toContainText("21 phrases in the piece");
    await expect(selection).toContainText("Selected phrase 1");
    await expect(brackets).toHaveCount(0);
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

    // A bar clicked in the score, away from its notes (here on a staff line), is selected. The line is a fraction of a
    // pixel thick, and Firefox and WebKit take a click's point in whole pixels, so the click is on the nearest one.
    const line = page.locator("div.score g.measure").nth(2).locator("g.staff > path").first();
    const box = await line.boundingBox();
    await page.mouse.click(Math.round(box.x + box.width * 0.9), Math.round(box.y + box.height / 2));
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

/**
 * Records in the page, for each pointer move, whether the bar was faded as the move arrived (before the app's handler on
 * the document) and once the app had handled it (after that handler), and returns a reader of the moves since a point.
 */
async function watchBarMoves(page) {
    await page.evaluate(() => {
        const faded = () => document.querySelector(".bar")?.classList.contains("faded") ?? null;
        const moves = [];
        let before = null;
        window.__barMoves = moves;
        window.addEventListener("pointermove", () => { before = faded(); }, { capture: true });
        window.addEventListener("pointermove", () => { moves.push({ before, after: faded() }); });
    });
    const count = () => page.evaluate(() => window.__barMoves.length);
    const firstSince = from => page.evaluate(start => window.__barMoves[start] ?? null, from);
    return { count, firstSince };
}

test("the bar fades while the player reads and comes back with the next touch", async ({ page }) => {
    const errors = await openFurElise(page);
    const bar = page.locator(".bar");
    // The fade comes two seconds after the last activity. A slow runner once took 2.5 s between a move and the next
    // look at the bar (#408), by when it had rightly faded again; so what the bar was right after each move is read in
    // the page as the move is handled, and the test waits for the faded state rather than for time.
    const moves = await watchBarMoves(page);
    await page.mouse.move(600, 500);
    await expect.poll(() => moves.firstSince(0)).toMatchObject({ after: false });
    await expect(bar).toHaveClass(/faded/, { timeout: 5_000 });

    // Faded, the bar comes back with the next touch.
    const from = await moves.count();
    await page.mouse.move(610, 510);
    await expect.poll(() => moves.firstSince(from)).toEqual({ before: true, after: false });
    expect(errors).toEqual([]);
});

test("bars dragged across on the score are selected, the handles move the edges, and the menu over them edits phrases", async ({ page }) => {
    const errors = await openFurElise(page);
    const bars = page.locator("div.score g.measure");
    const selection = page.locator(".selection-status");
    const menu = page.getByRole("toolbar", { name: "Selection" });
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
    await expect(selection).toContainText("Selected phrase 2");
    await expectUnits(page, "22 phrases in the piece");

    // Merge with the next phrase takes the boundary after it away.
    await menu.getByRole("button", { name: "More for the selection" }).click();
    await menu.getByRole("button", { name: "Merge with the next phrase" }).click();
    await expect(selection).toContainText("Selected phrase 2");
    await expectUnits(page, "21 phrases in the piece");

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
    await expect(selection).toContainText("Selected phrase 2");
    await expectUnits(page, "22 phrases in the piece");

    // Reset to detected brings the suggested phrases back.
    await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
    await page.getByRole("dialog", { name: "Phrases" }).getByRole("button", { name: "Reset to detected" }).click();
    await expect(page.getByRole("dialog", { name: "Phrases" })).toContainText("21 phrases in the piece");
    await closePopover(page);

    // With the pen on, the menu goes away and the mouse writes rather than selects.
    await page.locator(".bar").getByRole("button", { name: "Pen", exact: true }).click();
    await expect(menu).toBeHidden();
    await page.locator(".bar").getByRole("button", { name: "Pen", exact: true }).click();
    await expect(menu).toBeVisible();
    expect(errors).toEqual([]);
});
