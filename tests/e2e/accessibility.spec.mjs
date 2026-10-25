// WCAG 2.2 AA on every screen, checked with axe (S1 non-functional targets), and the journey by
// keyboard. It runs in desktop Chromium only (playwright.config.mjs): the rules read the page's
// markup and styles, which are the same in each engine.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { openFurElise, selectBar } from "./steps.mjs";

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
    await page.getByRole("tab", { name: "Edit" }).click();
    await page.locator("[aria-label='Correct notes'] button.section-toggle").click();
    expect(await violations(page)).toEqual([]);

    await page.goto("/");
    await expect(page.getByText("two-hands-type1.mid")).toBeVisible();
    expect(await violations(page)).toEqual([]);
});

test("playback lab with a selection, Practise, the Explore card, a variation and the note editor", async ({ page }) => {
    await openFurElise(page);
    await selectBar(page, 1);
    // Practise, the default tab.
    expect(await violations(page)).toEqual([]);
    await page.getByRole("tab", { name: "Explore" }).click();
    await page.getByRole("button", { name: "I have a question" }).click();
    expect(await violations(page)).toEqual([]);
    await page.getByRole("button", { name: "Which changes keep the motif recognisable?" }).click();
    await expect(page.locator(".explore h3")).toHaveText("Answer the motif differently");
    expect(await violations(page)).toEqual([]);
    await page.getByRole("button", { name: "Leave it" }).click();
    await page.getByRole("tab", { name: "Selection" }).click();
    await page.locator(".variation button.section-toggle", { hasText: "V2 Accompaniment pattern" }).click();
    await page.getByRole("button", { name: "Dotted" }).click();
    // Compare (#43): the panel next to What changed, and the original and the variation one after the other.
    await expect(page.locator(".changed + .compare")).toBeVisible();
    await page.getByRole("button", { name: "One after the other" }).click();
    await expect(page.locator(".inspector .selection-text")).toContainText("Looping the original, then the variation.");
    expect(await violations(page)).toEqual([]);
    await page.getByRole("tab", { name: /^Understand/ }).click();
    await page.getByRole("button", { name: /The motif recurs once/ }).click();
    await expect(page.locator(".inspector .highlighted")).toBeVisible();
    expect(await violations(page)).toEqual([]);
    await page.getByRole("tab", { name: "Edit" }).click();
    await page.locator("[aria-label='Edit variation notes'] button.section-toggle").click();
    await page.locator(".transport-bar").getByRole("button", { name: "Pause" }).click();
    expect(await violations(page)).toEqual([]);
});

test("the journey by keyboard: open a piece, select bars, loop them", async ({ page }) => {
    await page.goto("/playback");
    const load = page.getByRole("button", { name: "Für Elise" });
    await expect(load).toBeEnabled({ timeout: 30_000 });
    await load.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByText("Für Elise (Beethoven, opening)")).toBeVisible();

    // ] selects the first bar (the pickup), then the next; Shift+] extends by a bar; Space loops it.
    await page.getByRole("application").focus();
    await page.keyboard.press("BracketRight");
    await page.keyboard.press("BracketRight");
    await page.keyboard.press("Shift+BracketRight");
    await expect(page.locator(".inspector .selection-text")).toContainText("bars 1–2");
    await page.keyboard.press("Space");
    await expect(page.getByText("Playing", { exact: true })).toBeVisible();
});
