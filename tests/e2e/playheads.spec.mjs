// The playback model of v0.5.2 on the web (#495 UX-10, slices W1 and W2; the experiment Two playheads and modes, Settings ·
// Experimental): one Play button, in the pill, cobalt where the selection plays (or, stopped, will play) and ink where the
// piece goes on, with the place as bar.beat; Цикл with the selection's span, on by itself with a new selection, which takes
// the position to the selection's start; a click on a beat outside the selection turns it off. No Play in the bar and none
// and no Loop in the selection's menu. The black playhead is a track under its bar with a dot (#479 item 19); the blue one,
// from the same model, is held to the selection.
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

test("one Play button whose colour follows the place, and Цикл that loops the selection from its start", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    const bars = await firstSystem(page);
    const track = page.locator("div.score .gtrack");
    const blue = page.locator("div.score .ph-sel");
    const pill = page.locator(".pill");
    const play = pill.locator(".play-at");
    const cycle = pill.getByRole("button", { name: /^Loop \d+\.\d+ – \d+\.\d+$/ });

    // The pill always shows the one Play, in ink with nothing selected; the bar has none.
    await expect(play).toBeVisible();
    await expect(play).toHaveClass(/\bpiece\b/);
    await expect(play).toHaveAccessibleName(/^Play from \d+\.\d+$/);
    await expect(page.locator(".bar").getByRole("button", { name: "Play", exact: true })).toHaveCount(0);
    await expect(cycle).toHaveCount(0);

    // A click on a bar moves the black playhead there.
    await page.mouse.click(bars[2].left + 12, bars[2].middle);
    await expect.poll(async () => barOf(bars, await across(track))).toBe(2);

    // A selection by beats: Цикл shows with its span and turns on by itself; the position stays. Stopped outside it with
    // Цикл on, Play is cobalt and names the selection's start, where it will play. The menu has neither Play nor Loop.
    await page.keyboard.press("s");
    await drag(page, { x: bars[4].left + 4, y: bars[4].middle }, { x: bars[5].right - 4, y: bars[5].middle });
    await expect(page.locator(".selection-status")).toContainText("Selected");
    await expect(cycle).toHaveAttribute("aria-pressed", "true");
    const span = (await cycle.textContent()).trim().match(/(\d+\.\d+) – (\d+\.\d+)/);
    await expect(play).not.toHaveClass(/\bpiece\b/);
    await expect(play).toHaveAccessibleName(`Play from ${span[1]}`);
    expect(barOf(bars, await across(track))).toBe(2);
    // Both playheads come from the model (W2): the blue one waits at the selection's start.
    await expect.poll(async () => barOf(bars, await across(blue))).toBe(4);
    await expect(page.locator(".ctx").getByRole("button", { name: /^(Loop|Play the selection)$/ })).toHaveCount(0);

    // Play plays the selection from its start; Space pauses inside it, where Play stays cobalt.
    await play.click();
    await withAudioDiagnostic(page, test.info(), async () => {
        await expect(play).toHaveAccessibleName(/^Pause · \d+\.\d+$/);
    });
    await expect.poll(async () => barOf(bars, await across(track))).toBeGreaterThanOrEqual(4);
    // The label is the place alone, bar.beat, at one width whether it plays or not (#511 section 8.2).
    await expect(play.locator(".lbl")).toHaveText(/^\d+\.\d+$/);
    const playingWidth = (await play.boundingBox()).width;
    await page.keyboard.press("Space");
    await expect(play).toHaveAccessibleName(/^Play from \d+\.\d+$/);
    await expect(play.locator(".lbl")).toHaveText(/^\d+\.\d+$/);
    expect(Math.abs((await play.boundingBox()).width - playingWidth)).toBeLessThan(1);
    await expect(play).not.toHaveClass(/\bpiece\b/);
    const inside = barOf(bars, await across(track));
    expect(inside === 4 || inside === 5).toBe(true);
    expect(Math.abs((await across(blue)) - (await across(track))) < 16).toBe(true);

    // Цикл off: the position stays; a click on a beat after the selection makes Play the piece's, in ink.
    await cycle.click();
    await expect(cycle).toHaveAttribute("aria-pressed", "false");
    expect(barOf(bars, await across(track))).toBe(inside);
    const after = bars.length - 1;
    expect(after).toBeGreaterThan(5);
    // In the bar's middle, clear of the selection's handle at its edge.
    await page.mouse.click((bars[after].left + bars[after].right) / 2, bars[after].middle);
    await expect.poll(async () => barOf(bars, await across(track))).toBe(after);
    await expect(play).toHaveClass(/\bpiece\b/);
    // Past the selection with Цикл off, the blue playhead stands at its end, at the right edge of its last bar.
    await expect.poll(async () => barOf(bars, await across(blue))).toBe(5);

    // Цикл on takes the position to the selection's start: Play is cobalt there.
    await cycle.click();
    await expect(cycle).toHaveAttribute("aria-pressed", "true");
    await expect.poll(async () => barOf(bars, await across(track))).toBe(4);
    await expect(play).not.toHaveClass(/\bpiece\b/);
    await expect(play).toHaveAccessibleName(`Play from ${span[1]}`);

    // A click on a beat outside the selection takes the black playhead out of it and turns Цикл off (T12).
    await page.mouse.click(bars[1].left + 12, bars[1].middle);
    await expect(cycle).toHaveAttribute("aria-pressed", "false");
    await expect.poll(async () => barOf(bars, await across(track))).toBe(1);
    await expect(play).toHaveClass(/\bpiece\b/);

    // Escape removes the selection: Цикл goes, the black playhead stays where it was.
    await page.keyboard.press("Escape");
    await expect(page.locator(".selection-status")).toContainText("Nothing selected");
    await expect(cycle).toHaveCount(0);
    expect(barOf(bars, await across(track))).toBe(1);
    await expect(blue).toBeHidden();
    expect(errors).toEqual([]);
});
