// The two playheads of version 0.5 (the experiment Two playheads and modes, Settings · Experimental; product spec
// two-playheads): the global one, a track under its bar with a dot (v0.5.2, #479 item 19), placed by a click outside the
// selection and played by the bar's Play, which fills dark and fades nothing; the selection's, a cobalt line, placed by a click inside it and
// played by Play first in its menu or Space; when the selection goes, the global playhead takes the selection playhead's place.
import { expect, test } from "@playwright/test";
import { openFurElise, withAudioDiagnostic } from "./steps.mjs";

async function withExperiment(page) {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.playheads", "on"));
}

/** The engraved bars of the first system, left to right, as the page draws them. */
async function firstSystem(page) {
    await expect(page.locator(".score svg g.measure").first()).toBeVisible();
    return page.evaluate(() => {
        const first = document.querySelector(".score svg g.system");
        return [...first.querySelectorAll("g.measure")].map((measure) => {
            const box = measure.getBoundingClientRect();
            return { left: box.left, right: box.right, middle: (box.top + box.bottom) / 2 };
        });
    });
}

/** The middle of a playhead mark across the screen. */
const across = (locator) => locator.evaluate((element) => {
    const box = element.getBoundingClientRect();
    return box.left + box.width / 2;
});

/** The index of the bar a point across lies in, or -1. */
const barOf = (bars, x) => bars.findIndex((bar) => x >= bar.left - 1 && x <= bar.right + 1);

async function drag(page, from, to) {
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 8 });
    await page.mouse.up();
}

test("two playheads: each placed by a click, each with its Play, and the global one takes the selection's place", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    const bars = await firstSystem(page);
    const track = page.locator("div.score .gtrack");
    const line = page.locator("div.score .ph-sel");
    const score = page.locator("div.score");

    // The global playhead stands at the start, stopped: a hollow dot; the one playhead of before is not drawn.
    await expect(track).toBeVisible();
    await expect(track).toHaveClass(/stop/);
    await expect(page.locator("div.score .playhead")).toBeHidden();

    // A click on a bar places it there.
    await page.mouse.click(bars[2].left + 12, bars[2].middle);
    await expect.poll(async () => barOf(bars, await across(track))).toBe(2);

    // A selection by beats: its playhead stands at its start, and a click inside places it; the global one stays.
    await page.keyboard.press("s");
    await drag(page, { x: bars[4].left + 4, y: bars[4].middle }, { x: bars[5].right - 4, y: bars[5].middle });
    await expect(page.locator(".selection-status")).toContainText("Selected");
    await expect(line).toBeVisible();
    await expect.poll(async () => barOf(bars, await across(line))).toBe(4);
    await page.mouse.click(bars[5].left + 12, bars[5].middle);
    await expect.poll(async () => barOf(bars, await across(line))).toBe(5);
    expect(barOf(bars, await across(track))).toBe(2);

    // Play first in the selection's menu plays the selection; the global playhead waits and nothing fades. Space pauses.
    const menu = page.locator(".ctx");
    await expect(menu.getByRole("button").first()).toHaveAccessibleName("Play the selection");
    await expect(page.locator(".pill").getByRole("button", { name: "Play", exact: true })).toHaveCount(0);
    await menu.getByRole("button", { name: "Play the selection" }).click();
    await withAudioDiagnostic(page, test.info(), async () => {
        await expect(menu.getByRole("button", { name: "Pause" })).toBeVisible();
    });
    await expect(score).not.toHaveClass(/fading/);
    expect(barOf(bars, await across(track))).toBe(2);
    await page.keyboard.press("Space");
    await expect(menu.getByRole("button", { name: "Play the selection" })).toBeVisible();
    const left = barOf(bars, await across(line));
    expect(left === 4 || left === 5).toBe(true);
    expect(barOf(bars, await across(track))).toBe(2);

    // Escape removes the selection: the global playhead takes the selection playhead's place, which goes.
    await page.keyboard.press("Escape");
    await expect(page.locator(".selection-status")).toContainText("Nothing selected");
    await expect(line).toBeHidden();
    await expect.poll(async () => barOf(bars, await across(track))).toBe(left);

    // The bar's Play plays the piece from there: it fills dark, the dot is solid and no bar fades (#479 item 19).
    await page.locator(".bar").getByRole("button", { name: "Play", exact: true }).click();
    await expect(page.locator(".bar .act.gplay")).toHaveClass(/\bon\b/);
    await expect(track).not.toHaveClass(/stop/);
    await expect(score).not.toHaveClass(/fading/);
    await expect(page.locator("div.score svg g.measure.lit")).toHaveCount(0);
    await page.keyboard.press("Space");
    await expect(page.locator(".bar .act.gplay")).not.toHaveClass(/\bon\b/);
    await expect(score).not.toHaveClass(/fading/);
    expect(errors).toEqual([]);
});
