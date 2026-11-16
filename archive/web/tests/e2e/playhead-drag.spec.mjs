// Dragging the playheads and hearing what they pass on the web (v0.5.2, #495 UX-08, UX-09 and UX-10, slice W3; the
// experiment Two playheads and modes, Settings · Experimental). On the score the black dot is dragged across bars (v0.5.7,
// #543 5) and the blue knob inside the selection; on the roll the black dot goes anywhere and lands on the sixteenth
// grid. A drag never selects a bar. The engine is told to scrub where the playhead goes and to let its notes fade when it
// is let go; playing, playback stands while a handle is held and goes on from the new place. Mouse and touch both.
import { expect, test } from "@playwright/test";
import { openFurElise, openRoll, withAudioDiagnostic } from "./steps.mjs";

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

/** The centre of an element on screen. */
const centre = (locator) => locator.evaluate((element) => {
    const box = element.getBoundingClientRect();
    return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
});

/** The index of the bar a point across lies in, or -1. */
const barOf = (bars, x) => bars.findIndex((bar) => x >= bar.left - 1 && x <= bar.right + 1);

/** The messages the page posted the engine since the last call, scrubs and transport only. */
async function posted(page) {
    return page.evaluate(() => {
        const trace = window.__audioTrace;
        const types = trace.posted.map((message) => message.type).filter((type) => ["scrub", "scrubEnd", "play", "pause", "seek"].includes(type));
        trace.posted.length = 0;
        return types;
    });
}

async function drag(page, from, to) {
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 10 });
    await page.mouse.up();
}

test("the black dot is dragged across bars and the blue knob inside the selection, each heard, and no bar is selected", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    const bars = await firstSystem(page);
    const track = page.locator("div.score .gtrack");
    const dot = track.locator(".dot");
    const blue = page.locator("div.score .ph-sel");
    const status = page.locator(".selection-status");
    const cycle = page.locator(".pill").getByRole("button", { name: /^Loop \d+\.\d+ – \d+\.\d+$/ });

    // A click on a bar moves the black playhead to it; the drag of its dot goes on into the bar under the pointer (#543 5),
    // while a black line stands across the staves; let go, the line fades.
    await page.mouse.click(bars[2].left + 12, bars[2].middle);
    await expect.poll(async () => barOf(bars, (await centre(dot)).x)).toBe(2);
    const from = await centre(dot);
    await posted(page);
    const held = page.locator("div.score .ph-held");
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(bars[4].left + 10, from.y, { steps: 10 });
    await expect(held).toHaveClass(/(^|\s)held(\s|$)/);
    expect(barOf(bars, (await centre(held)).x)).toBe(4);
    await page.mouse.up();
    await expect(held).not.toHaveClass(/(^|\s)held(\s|$)/);
    const to = await centre(dot);
    expect(to.x).toBeGreaterThan(from.x + 8);
    expect(barOf(bars, to.x)).toBe(4);
    await expect(status).toContainText("Nothing selected");
    const heard = await posted(page);
    expect(heard).toContain("scrub");
    expect(heard.at(-1)).toBe("scrubEnd");
    expect(heard).not.toContain("seek");

    // A selection of bars 4–5: Цикл turns on and the blue knob waits at its start. Dragged past the selection's end it
    // stays at its last sixteenth, and the black playhead goes with it; Цикл and the selection stay as they were.
    await page.keyboard.press("s");
    await drag(page, { x: bars[4].left + 4, y: bars[4].middle }, { x: bars[5].right - 4, y: bars[5].middle });
    await expect(status).toContainText("Selected");
    const selected = await status.textContent();
    await expect(cycle).toHaveAttribute("aria-pressed", "true");
    await expect.poll(async () => barOf(bars, (await centre(blue.locator(".knob"))).x)).toBe(4);
    await posted(page);
    const knob = await centre(blue.locator(".knob"));
    await drag(page, knob, { x: bars[bars.length - 1].left + 4, y: knob.y });
    await expect.poll(async () => barOf(bars, (await centre(blue.locator(".knob"))).x)).toBe(5);
    await expect.poll(async () => barOf(bars, (await centre(dot)).x)).toBe(5);
    await expect(cycle).toHaveAttribute("aria-pressed", "true");
    await expect(status).toHaveText(selected);
    expect((await posted(page)).at(-1)).toBe("scrubEnd");
    expect(errors).toEqual([]);
});

test("playing, a held playhead stands and plays on from where it was let go", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    const bars = await firstSystem(page);
    const dot = page.locator("div.score .gtrack .dot");
    const play = page.locator(".pill .play-at");

    await page.mouse.click(bars[2].left + 12, bars[2].middle);
    await expect.poll(async () => barOf(bars, (await centre(dot)).x)).toBe(2);
    await play.click();
    await withAudioDiagnostic(page, test.info(), async () => {
        await expect(play).toHaveAccessibleName(/^Pause · \d+\.\d+$/);
    });
    await posted(page);
    const from = await centre(dot);
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(from.x + 12, from.y, { steps: 4 });
    // Held, playback stands; the drag sounds what it passes.
    await expect(play).toHaveAccessibleName(/^Play from \d+\.\d+$/);
    await page.mouse.up();
    await expect(play).toHaveAccessibleName(/^Pause · \d+\.\d+$/);
    const heard = await posted(page);
    expect(heard[0]).toBe("pause");
    expect(heard.slice(-2)).toEqual(["scrubEnd", "play"]);
    await play.click();
    expect(errors).toEqual([]);
});

test.describe("by touch", () => {
    test.use({ hasTouch: true });

    test("a finger drags the black dot along the bars, and the score does not scroll or select under it", async ({ page, browserName }) => {
        test.skip(browserName !== "chromium", "touches are sent through Chromium's DevTools protocol");
        await withExperiment(page);
        const errors = await openFurElise(page);
        const bars = await firstSystem(page);
        const dot = page.locator("div.score .gtrack .dot");
        await page.mouse.click(bars[2].left + 12, bars[2].middle);
        await expect.poll(async () => barOf(bars, (await centre(dot)).x)).toBe(2);
        const from = await centre(dot);
        const scrolled = await page.locator("div.score").evaluate((score) => score.scrollTop);
        await posted(page);

        const touch = await page.context().newCDPSession(page);
        const at = (x) => [{ x, y: from.y, id: 1 }];
        await touch.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: at(from.x) });
        for (let step = 1; step <= 8; step++) {
            await touch.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: at(from.x + step * 8) });
        }
        await touch.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });

        await expect.poll(async () => (await centre(dot)).x).toBeGreaterThan(from.x + 8);
        expect(barOf(bars, (await centre(dot)).x)).toBeGreaterThanOrEqual(2);
        await expect(page.locator(".selection-status")).toContainText("Nothing selected");
        expect(await page.locator("div.score").evaluate((score) => score.scrollTop)).toBe(scrolled);
        const heard = await posted(page);
        expect(heard).toContain("scrub");
        expect(heard.at(-1)).toBe("scrubEnd");
        expect(errors).toEqual([]);
    });
});

test("on the roll the black dot is dragged across the piece onto the sixteenth grid, and selects nothing", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    await expect(page.locator(".score svg g.measure").first()).toBeVisible();
    await openRoll(page);
    const control = page.locator(".roll .roll-playhead");
    await expect(control).toBeAttached();
    const before = Number(await control.inputValue());
    // The control sits over the black dot: its middle across, its top on the bar numbers' edge, where the dot is.
    const box = await control.boundingBox();
    const from = { x: box.x + box.width / 2, y: box.y };
    await posted(page);
    await drag(page, from, { x: from.x + 160, y: from.y });

    await expect.poll(async () => Number(await control.inputValue())).toBeGreaterThan(before);
    const after = Number(await control.inputValue());
    // Für Elise's bars start on the sixteenth grid (a pickup of an eighth, then 3/8), so the place is on it too.
    expect(after % 120).toBe(0);
    await expect(page.locator(".selection-status")).toContainText("Nothing selected");
    const heard = await posted(page);
    expect(heard).toContain("scrub");
    expect(heard.at(-1)).toBe("scrubEnd");
    expect(errors).toEqual([]);
});

test("on the roll, playing, the black dot is taken at Fit and zoomed in: the music stands, the drag is heard, it plays on (#533 4)", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    await expect(page.locator(".score svg g.measure").first()).toBeVisible();
    await openRoll(page);
    const control = page.locator(".roll .roll-playhead");
    await expect(control).toBeAttached();
    const play = page.locator(".pill .play-at");
    for (const zoom of ["Fit", "Zoom in"]) {
        for (let step = 0; step < (zoom === "Fit" ? 1 : 4); step++) {
            await page.locator(".roll").getByRole("button", { name: zoom, exact: true }).click();
        }
        await play.click();
        await withAudioDiagnostic(page, test.info(), async () => {
            await expect(play).toHaveAccessibleName(/^Pause · \d+\.\d+$/);
        });
        await page.waitForTimeout(600);
        await posted(page);
        // Held, playback stands at once, before the dot moves. The test's own round trips can take longer than a hand, so
        // the dot may have run on past the press: then again.
        let from = null;
        await expect(async () => {
            const box = await control.boundingBox();
            from = { x: box.x + box.width / 2, y: box.y };
            await page.mouse.move(from.x, from.y);
            await page.mouse.down();
            try {
                await expect(play).toHaveAccessibleName(/^Play from \d+\.\d+$/, { timeout: 1_500 });
            } catch (missed) {
                await page.mouse.up();
                throw missed;
            }
        }).toPass({ timeout: 20_000 });
        await page.mouse.move(from.x + 60, from.y, { steps: 6 });
        await page.mouse.move(from.x - 30, from.y, { steps: 6 });
        await page.mouse.up();
        await expect(play).toHaveAccessibleName(/^Pause · \d+\.\d+$/);
        const heard = await posted(page);
        expect(heard[0]).toBe("pause");
        expect(heard).toContain("scrub");
        expect(heard.slice(-2)).toEqual(["scrubEnd", "play"]);
        await play.click();
        await expect(play).toHaveAccessibleName(/^Play from \d+\.\d+$/);
    }
    expect(errors).toEqual([]);
});
