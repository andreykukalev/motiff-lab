// Navigate protects the selection and a long press places the playhead (v0.5.7, #543 sections 2-5 and 9, the lead's decisions
// 1-4; the experiment Two playheads and modes, Settings · Experimental). In Navigate the selection's handles are gone and the
// band stays; a finger's long press on the music puts the black playhead at the place under it, inside or outside the
// selection, and moved on scrubs across bars while a black line stands across the staves; the selection never changes. In
// Select the handles come back. A press whose end was lost never blocks the next selection. Touches go through Chromium's
// DevTools protocol, so these run in Chromium.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

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

const centre = (locator) => locator.evaluate((element) => {
    const box = element.getBoundingClientRect();
    return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
});

/** The selection the status names and its notes, without what is heard: a playhead taken out of it turns Цикл off (T12). */
const named = async (status) => {
    const text = await status.textContent();
    return text.slice(0, text.indexOf(".", text.indexOf(":")) + 1);
};

const barOf = (bars, x) => bars.findIndex((bar) => x >= bar.left - 1 && x <= bar.right + 1);

async function drag(page, from, to) {
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 8 });
    await page.mouse.up();
}

/** A finger held still at a point for longer than the hold, then moved on through `then` points, then lifted. */
async function longPress(page, at, then = []) {
    const touch = await page.context().newCDPSession(page);
    await touch.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ ...at, id: 1 }] });
    await page.waitForTimeout(700);
    for (const point of then) {
        await touch.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: [{ ...point, id: 1 }] });
    }
    return async () => touch.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
}

test.describe("by touch", () => {
    test.use({ hasTouch: true });

    test("in Navigate the handles go, the band stays, and a long press places and scrubs the playhead without selecting", async ({ page, browserName }) => {
        test.skip(browserName !== "chromium", "touches are sent through Chromium's DevTools protocol");
        await withExperiment(page);
        const errors = await openFurElise(page);
        const bars = await firstSystem(page);
        const status = page.locator(".selection-status");
        const dot = page.locator("div.score .gtrack .dot");
        const held = page.locator("div.score .ph-held");
        const handles = page.locator("div.score .sel-handle");

        // Select: a drag selects bars 4-5, and the handles show.
        await page.keyboard.press("s");
        await drag(page, { x: bars[4].left + 4, y: bars[4].middle }, { x: bars[5].right - 4, y: bars[5].middle });
        await expect(status).toContainText("Selected");
        const selected = await named(status);
        await expect(handles).toHaveCount(2);

        // Navigate: the handles go, the band stays.
        await page.keyboard.press("h");
        await expect(handles).toHaveCount(0);
        await expect(page.locator("div.score .sel-band").first()).toBeVisible();

        // A long press outside the selection, three quarters across bar 1: the black playhead goes there, not to the bar's
        // start or a beat; the line shows while held and fades when let go; the selection stays.
        const outside = { x: bars[1].left + (bars[1].right - bars[1].left) * 0.75, y: bars[1].middle };
        const lift = await longPress(page, outside);
        await expect(held).toHaveClass(/(^|\s)held(\s|$)/);
        await lift();
        await expect(held).not.toHaveClass(/(^|\s)held(\s|$)/);
        await expect.poll(async () => barOf(bars, (await centre(dot)).x)).toBe(1);
        expect((await centre(dot)).x).toBeGreaterThan((bars[1].left + bars[1].right) / 2);
        await expect(status).toContainText(selected);
        await expect(handles).toHaveCount(0);

        // Inside the selection, and moved on into the next bars: it scrubs across bars and lands where it was let go.
        const inside = { x: (bars[4].left + bars[4].right) / 2, y: bars[4].middle };
        const on = [1, 2, 3, 4, 5, 6].map((step) => ({ x: inside.x + step * ((bars[6].left + 10 - inside.x) / 6), y: inside.y }));
        const lifted = await longPress(page, inside, on);
        await expect.poll(async () => barOf(bars, (await centre(held)).x)).toBe(6);
        await lifted();
        await expect.poll(async () => barOf(bars, (await centre(dot)).x)).toBe(6);
        await expect(status).toContainText(selected);

        // Select again: the handles come back on the same selection.
        await page.keyboard.press("s");
        await expect(handles).toHaveCount(2);
        await expect(status).toContainText(selected);
        expect(errors).toEqual([]);
    });

    test("a press whose end was lost does not block the next selection (#543 9)", async ({ page, browserName }) => {
        test.skip(browserName !== "chromium", "the same in every engine: the pointer events are the page's own");
        await withExperiment(page);
        const errors = await openFurElise(page);
        const bars = await firstSystem(page);
        const status = page.locator(".selection-status");
        await page.keyboard.press("s");
        await drag(page, { x: bars[2].left + 4, y: bars[2].middle }, { x: bars[3].right - 4, y: bars[3].middle });
        await expect(status).toContainText("Selected");
        const first = await named(status);

        // A finger comes down on bar 1 and its pointerup never reaches the page (taken by the system, a lost capture).
        const press = (type, id, x, y) => page.evaluate(({ type, id, x, y }) => {
            const target = document.elementFromPoint(x, y);
            target.dispatchEvent(new PointerEvent(type, { pointerId: id, pointerType: "touch", isPrimary: true, clientX: x, clientY: y, button: 0, bubbles: true, cancelable: true }));
        }, { type, id, x, y });
        await press("pointerdown", 11, bars[1].left + 6, bars[1].middle);
        await page.waitForTimeout(100);

        // The next press, held and dragged over bars 5-6, replaces the selection: before 0.5.7 the layer ignored it.
        await press("pointerdown", 12, bars[5].left + 6, bars[5].middle);
        await page.waitForTimeout(700);
        for (let step = 1; step <= 6; step++) {
            await press("pointermove", 12, bars[5].left + 6 + step * ((bars[6].right - bars[5].left - 12) / 6), bars[5].middle);
        }
        await press("pointerup", 12, bars[6].right - 6, bars[5].middle);
        await expect(status).toContainText("Selected");
        expect(await named(status)).not.toBe(first);
        expect(errors).toEqual([]);
    });
});
