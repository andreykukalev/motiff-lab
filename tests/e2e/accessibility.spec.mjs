// WCAG 2.2 AA on every screen, checked with axe (S1 non-functional targets), and the journey by
// keyboard. It runs in desktop Chromium only (playwright.config.mjs): the rules read the page's
// markup and styles, which are the same in each engine.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { closePopover, openFurElise, openInsights, openMore, openRoll, selectBar } from "./steps.mjs";

const midi = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Midi/Fixtures/two-hands-type1.mid", import.meta.url));

const tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

async function violations(page) {
    const results = await new AxeBuilder({ page }).withTags(tags).analyze();
    return results.violations.map(v => `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.slice(0, 5).map(n => n.target.join(" ")).join("\n  ")}`);
}

for (const path of ["/", "/settings", "/privacy", "/terms"]) {
    test(`page ${path}`, async ({ page }) => {
        await page.goto(path);
        await expect(page.locator("h1")).toBeVisible();
        expect(await violations(page)).toEqual([]);
    });
}

test("import, its report, the opened piece and the project on the home screen", async ({ page }) => {
    await page.goto("/import");
    await expect(page.locator("h1")).toBeVisible();
    expect(await violations(page)).toEqual([]);

    await page.setInputFiles("input[type=file]", midi);
    await expect(page.locator(".report")).toBeVisible();
    expect(await violations(page)).toEqual([]);

    await page.getByText("Open the piece").click();
    await selectBar(page, 1, { pickup: 0, barQuarters: 4 });
    await openInsights(page, "Tools");
    await page.locator("[aria-label='Correct notes'] button.section-toggle").click();
    expect(await violations(page)).toEqual([]);

    await page.goto("/");
    await expect(page.getByText("two-hands-type1.mid")).toBeVisible();
    expect(await violations(page)).toEqual([]);
});

test("the score page: the bar, its popovers, the pill and its tempo, the roll drawer, the Pen palette, in both themes", async ({ page }) => {
    await openFurElise(page);
    for (const theme of ["daylight", "night"]) {
        await page.evaluate(name => document.documentElement.setAttribute("data-theme", name), theme);
        expect(await violations(page)).toEqual([]);
        await page.locator(".bar").getByRole("button", { name: "Phrases" }).click();
        await expect(page.locator(".phrases-popover")).toBeVisible();
        expect(await violations(page)).toEqual([]);
        await closePopover(page);
        await openMore(page);
        expect(await violations(page)).toEqual([]);
        await closePopover(page);
        // A phrase not selected yet: a second tap on the selected one would lift it into focus.
        await page.locator(".score button.bracket[aria-label][aria-pressed='false']").first().click();
        await page.locator(".pill").getByRole("button", { name: /^Tempo/ }).click();
        expect(await violations(page)).toEqual([]);
        await page.locator(".pill").getByRole("button", { name: /^Tempo/ }).click();
        await openRoll(page);
        await page.locator(".bar").getByRole("button", { name: "Pen", exact: true }).click();
        await expect(page.getByRole("toolbar", { name: "Pencil marks" })).toBeVisible();
        expect(await violations(page)).toEqual([]);
        await page.locator(".bar").getByRole("button", { name: "Pen", exact: true }).click();
        await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    }
});

test("phrase focus: the card with its ossia, the variation chips, the focus bar and Lead-in, in both themes", async ({ page }) => {
    await openFurElise(page);
    const bracket = page.locator(".score button.bracket[aria-label]").first();
    await bracket.click();
    await openInsights(page, "Explore");
    await page.locator(".explore").getByRole("button", { name: /Give the melody more space/ }).click();
    await page.locator(".explore").getByRole("button", { name: "Keep this variation" }).click();
    await page.locator(".bar").getByRole("button", { name: "Insights" }).click();
    await bracket.click();
    await expect(page.locator(".focus-card .ossia-frame").first()).toBeVisible();
    await page.locator(".pill").getByRole("button", { name: "Lead-in" }).click();
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    for (const theme of ["daylight", "night"]) {
        await page.evaluate(name => document.documentElement.setAttribute("data-theme", name), theme);
        expect(await violations(page)).toEqual([]);
    }
});

test("playback lab with a selection, Practise, the Explore card, a variation and the note editor", async ({ page }) => {
    await openFurElise(page);
    // A phrase picked: its bracket on the score, and Practise (the default tab) playing on.
    await page.locator(".score button.bracket[aria-label]").nth(1).click();
    await openInsights(page);
    await page.getByRole("button", { name: "Play on to the end" }).click();
    expect(await violations(page)).toEqual([]);
    await page.getByRole("button", { name: "Back to the phrase" }).click();
    await selectBar(page, 1);
    expect(await violations(page)).toEqual([]);
    await openInsights(page, "Explore");
    await page.getByRole("button", { name: "I have a question" }).click();
    expect(await violations(page)).toEqual([]);
    await page.getByRole("button", { name: "Which changes keep the motif recognisable?" }).click();
    await expect(page.locator(".explore h3")).toHaveText("Answer the motif differently");
    expect(await violations(page)).toEqual([]);
    await page.getByRole("button", { name: "Leave it" }).click();
    await openInsights(page, "Tools");
    await page.locator(".variation button.section-toggle", { hasText: "V2 Accompaniment pattern" }).click();
    await page.getByRole("button", { name: "Dotted" }).click();
    // Compare (#43): the panel next to What changed, and the original and the variation one after the other.
    await expect(page.locator(".changed + .compare")).toBeVisible();
    await page.getByRole("button", { name: "One after the other" }).click();
    await expect(page.locator(".selection-status")).toContainText("Looping the original, then the variation.");
    expect(await violations(page)).toEqual([]);
    await openInsights(page, /^Understand/);
    // Now, then Details with every fact (#132); both scanned.
    expect(await violations(page)).toEqual([]);
    await page.getByRole("button", { name: "Details" }).click();
    await page.getByRole("button", { name: /The motif recurs once/ }).click();
    await expect(page.locator(".inspector .highlighted")).toBeVisible();
    expect(await violations(page)).toEqual([]);
    await openInsights(page, "Tools");
    await page.locator("[aria-label='Edit variation notes'] button.section-toggle").click();
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(await violations(page)).toEqual([]);
});

test("kept for this phrase: a kept variation with its note, renaming, a note alone and deleting", async ({ page }) => {
    await openFurElise(page);
    await page.locator(".score button.bracket[aria-label]").first().click();
    await openInsights(page, "Explore");
    const card = page.locator(".explore");
    const kept = page.locator(".observations");
    await card.getByRole("button", { name: /Give the melody more space/ }).click();
    await card.getByRole("button", { name: "Keep this variation" }).click();
    await card.getByLabel("Add a note in your own words (optional)").fill("Lighter.");
    await card.getByRole("button", { name: "Save note" }).click();
    await expect(kept.getByRole("listitem")).toContainText("You hear this");
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(await violations(page)).toEqual([]);

    const item = kept.getByRole("listitem").first();
    await item.getByRole("button", { name: "Rename" }).click();
    await item.getByRole("button", { name: "Edit note" }).click();
    await kept.getByRole("button", { name: "Write a note about this phrase" }).click();
    expect(await violations(page)).toEqual([]);
    await item.getByRole("button", { name: "Cancel" }).first().click();
    await item.getByRole("button", { name: "Delete" }).click();
    await expect(item.getByRole("button", { name: "Delete for good" })).toBeVisible();
    expect(await violations(page)).toEqual([]);
});

test("the journey by keyboard: open a piece, select bars, loop them", async ({ page }) => {
    await page.goto("/playback");
    const load = page.getByRole("button", { name: "Für Elise" });
    await expect(load).toBeEnabled({ timeout: 30_000 });
    await load.focus();
    await page.keyboard.press("Enter");
    await expect(page.locator(".bar .ttl")).toHaveText("Für Elise (Beethoven, opening)");

    // The roll from the bar by keyboard; then ] selects the first bar (the pickup), then the next; Shift+] extends by a bar; Space loops it.
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).focus();
    await page.keyboard.press("Enter");
    await page.getByRole("application").focus();
    await page.keyboard.press("BracketRight");
    await page.keyboard.press("BracketRight");
    await page.keyboard.press("Shift+BracketRight");
    await expect(page.locator(".selection-status")).toContainText("bars 1–2");
    await page.keyboard.press("Space");
    await expect(page.locator(".pill .state")).toHaveText("Playing");
});
