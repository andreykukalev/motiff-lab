// The modes of version 0.5 on the Piano Roll (the experiment Two playheads and modes, Settings · Experimental; product spec
// interaction-modes, «Правила выделения по режимам»): Navigate moves the roll and selects nothing; Select selects by beats
// with a drag from outside the selection, and a drag or a click inside it leaves it alone; Edit notes draws a box round
// notes and opens the editor on them, which is not a selection. No click on the roll removes the selection.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

const status = (page) => page.locator(".selection-status");

/** The selection the status names and its notes, without what is heard: under the playback model (v0.5.2) a click outside the selection turns Цикл off. */
const named = async (page) => {
    const text = await status(page).textContent();
    return text.slice(0, text.indexOf(".", text.indexOf(":")) + 1);
};

async function drag(page, from, to) {
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 8 });
    await page.mouse.up();
}

test("on the roll each mode does its own thing, and only Select's drag selects", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.playheads", "on"));
    const errors = await openFurElise(page);
    await expect(page.locator(".score svg g.measure").first()).toBeVisible();
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    const roll = page.locator(".roll-drawer canvas.roll-notes");
    await expect(roll).toBeVisible();
    await expect(roll).toHaveAttribute("data-mode", "navigate");
    const box = await roll.boundingBox();
    const at = (share, down = 0.5) => ({ x: box.x + 62 + (box.width - 62) * share, y: box.y + 24 + (box.height - 24) * down });

    // Navigate: a drag moves the roll and selects nothing.
    await drag(page, at(0.6), at(0.3));
    await expect(status(page)).toContainText("Nothing selected");

    // Select: a drag from outside selects by beats; a drag and a click inside leave it as it is, and so does a click outside.
    await page.keyboard.press("s");
    await expect(roll).toHaveAttribute("data-mode", "select");
    await drag(page, at(0.2), at(0.45));
    await expect(status(page)).toContainText(/^Selected bars? \d+/);
    const selected = await named(page);
    await drag(page, at(0.3), at(0.8));
    await page.mouse.click(at(0.3).x, at(0.3).y);
    await page.mouse.click(at(0.8).x, at(0.8).y);
    await expect(status(page)).toContainText(selected);

    // Edit notes: a box round notes opens the editor on them; the selection stays.
    await page.keyboard.press("e");
    await expect(roll).toHaveAttribute("data-mode", "edit");
    await drag(page, at(0.01, 0.02), at(0.99, 0.98));
    await expect(page.locator(".roll-drawer").getByRole("button", { name: "Cancel" })).toBeVisible();
    await expect(status(page)).toContainText(selected);
    await page.locator(".roll-drawer").getByRole("button", { name: "Cancel" }).click();
    expect(errors).toEqual([]);
});

// #543 2–4: the bar's tool decides on the roll. In Navigate a long press puts the black playhead where it is held, inside
// the selection or outside it, on the sixteenth grid, and nothing selects: neither the notes, nor the bar numbers.
test("in Navigate a long press on the roll moves the playhead there and never selects (#543 3.1)", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.playheads", "on"));
    const errors = await openFurElise(page);
    await expect(page.locator(".score svg g.measure").first()).toBeVisible();
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    const roll = page.locator(".roll-drawer canvas.roll-notes");
    await expect(roll).toBeVisible();
    const box = await roll.boundingBox();
    const at = (share, down = 0.5) => ({ x: box.x + 62 + (box.width - 62) * share, y: box.y + 24 + (box.height - 24) * down });
    // Where the position is: the Play button names it (bar.beat), inside the selection or out of it.
    const play = page.locator(".pill .play-at");
    const place = async () => (await play.getAttribute("aria-label")) ?? (await play.innerText());

    await page.keyboard.press("s");
    await drag(page, at(0.2), at(0.45));
    await expect(status(page)).toContainText(/^Selected bars? \d+/);
    const selected = await named(page);
    await page.keyboard.press("h");
    await expect(roll).toHaveAttribute("data-mode", "navigate");

    // A hold takes the black playhead: the engine scrubs to the place and lets go (a click would only seek).
    const heard = () => page.evaluate(() => {
        const trace = window.__audioTrace;
        const types = trace.posted.map((message) => message.type).filter((type) => ["scrub", "scrubEnd", "seek"].includes(type));
        trace.posted.length = 0;
        return types;
    });
    const hold = async (point, then = point) => {
        await heard();
        await page.mouse.move(point.x, point.y);
        await page.mouse.down();
        await page.waitForTimeout(800);
        await page.mouse.move(then.x, then.y, { steps: 6 });
        await page.mouse.up();
        const types = await heard();
        expect(types).toContain("scrub");
        expect(types.at(-1)).toBe("scrubEnd");
    };
    const before = await place();
    await hold(at(0.75), at(0.85));
    await expect.poll(place).not.toBe(before);
    const outside = await place();
    await expect(status(page)).toContainText(selected);

    await hold(at(0.3));
    await expect.poll(place).not.toBe(outside);
    await expect(status(page)).toContainText(selected);

    // The bar numbers: a drag moves the roll, a hold moves the playhead; neither selects.
    const moved = await place();
    await drag(page, { x: at(0.6).x, y: box.y + 10 }, { x: at(0.9).x, y: box.y + 10 });
    await hold({ x: at(0.7).x, y: box.y + 10 });
    await expect.poll(place).not.toBe(moved);
    await expect(status(page)).toContainText(selected);
    expect(errors).toEqual([]);
});
