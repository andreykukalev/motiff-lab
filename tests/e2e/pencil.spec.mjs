// Pencil marks on the score (decision record 0015): the pen writes over the engraving at any time, a
// tap with it picks a bar, the mouse writes with the tools on, the eraser and Undo work, and the
// marks stay with the piece after a reload and in their bars after a new layout. Chromium only: the
// pen is driven through the DevTools protocol, which the other engines do not offer.
import { expect, test } from "@playwright/test";

import { openFurElise } from "./steps.mjs";

/** One pen event at a point in the page, as the Apple Pencil sends them. */
function pen(cdp, type, x, y) {
    return cdp.send("Input.dispatchMouseEvent", {
        type,
        x,
        y,
        button: "left",
        buttons: type === "mouseReleased" ? 0 : 1,
        clickCount: 1,
        pointerType: "pen",
        force: 0.5,
    });
}

async function penStroke(cdp, from, to) {
    await pen(cdp, "mousePressed", from.x, from.y);
    for (let step = 1; step <= 12; step++) {
        await pen(cdp, "mouseMoved", from.x + ((to.x - from.x) * step) / 12, from.y + ((to.y - from.y) * step) / 12);
    }
    await pen(cdp, "mouseReleased", to.x, to.y);
}

test("the Pencil writes on the score, a tap picks a bar, and the marks stay with the piece", async ({ page }) => {
    const errors = await openFurElise(page);
    const score = page.locator("div.score");
    const marks = score.locator(".pencil-mark");
    const tools = page.getByRole("toolbar", { name: "Pencil marks" });
    const toggle = tools.getByRole("button", { name: "Pencil", exact: false }).first();
    await expect(score.locator("g.measure").nth(3)).toBeVisible();
    await expect(toggle).toHaveAttribute("aria-pressed", "false");
    // The score is laid out again once the workspace has its size; let that settle.
    await page.waitForTimeout(800);
    const cdp = await page.context().newCDPSession(page);

    // A line written with the pen above bar 3's staff: drawn, and the tools come up; nothing is selected.
    const bar3 = score.locator("g.measure").nth(3);
    await bar3.scrollIntoViewIfNeeded();
    let box = await bar3.boundingBox();
    await penStroke(cdp, { x: box.x + 10, y: box.y + 4 }, { x: box.x + box.width - 10, y: box.y + 10 });
    await expect(marks).toHaveCount(1);
    await expect(toggle).toHaveAttribute("aria-pressed", "true");
    await expect(tools.getByRole("button", { name: "Undo" })).toBeEnabled();
    await expect(page.locator(".inspector .selection-text")).not.toContainText("Selected");

    // A tap with the pen on a staff line, away from the notes, selects that bar, as a click does.
    const line = score.locator("g.measure").nth(2).locator("g.staff > path").first();
    const lineBox = await line.boundingBox();
    const tap = { x: lineBox.x + lineBox.width * 0.9, y: lineBox.y + lineBox.height / 2 };
    await pen(cdp, "mousePressed", tap.x, tap.y);
    await pen(cdp, "mouseReleased", tap.x, tap.y);
    await expect(page.locator(".inspector .selection-text")).toContainText("Selected bar 2");
    await expect(marks).toHaveCount(1);

    // With the tools on, the mouse writes too, in the colour chosen: here over bar 4.
    await tools.getByRole("button", { name: "Pen, carmine" }).click();
    // Selecting the bar scrolled the score smoothly; let it come to rest.
    await page.waitForTimeout(800);
    const bar4 = score.locator("g.measure").nth(4);
    box = await bar4.boundingBox();
    await page.mouse.move(box.x + 10, box.y + 6);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width - 10, box.y + 6, { steps: 8 });
    await page.mouse.up();
    await expect(marks).toHaveCount(2);
    await expect(score.locator(".pencil-mark.carmine")).toHaveCount(1);

    // The eraser takes the carmine line; Undo brings it back.
    await tools.getByRole("button", { name: "Eraser" }).click();
    await page.mouse.move(box.x + box.width / 2, box.y + 1);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2, box.y + 20, { steps: 10 });
    await page.mouse.up();
    await expect(score.locator(".pencil-mark.carmine")).toHaveCount(0);
    await expect(score.locator(".pencil-mark.graphite")).toHaveCount(1);
    await tools.getByRole("button", { name: "Undo" }).click();
    await expect(score.locator(".pencil-mark.carmine")).toHaveCount(1);

    // Turned off, the mouse picks again and writes nothing.
    await toggle.click();
    await expect(toggle).toHaveAttribute("aria-pressed", "false");
    await page.mouse.move(box.x + 10, box.y + 30);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width - 10, box.y + 30, { steps: 6 });
    await page.mouse.up();
    await expect(marks).toHaveCount(2);

    // A narrower window lays the score out again: the black line is still over bar 3.
    await page.setViewportSize({ width: 900, height: 720 });
    await expect.poll(async () => {
        const bar = await bar3.boundingBox();
        const mark = await score.locator(".pencil-mark.graphite").boundingBox();
        return mark !== null && bar !== null && mark.x >= bar.x - 2 && mark.x + mark.width <= bar.x + bar.width + 2 && mark.y < bar.y + bar.height / 2;
    }).toBe(true);

    // The marks are kept on the device with the piece.
    await page.waitForTimeout(800);
    await page.reload();
    await expect(page.getByRole("button", { name: "Für Elise" })).toBeEnabled({ timeout: 30_000 });
    await page.getByRole("button", { name: "Für Elise" }).click();
    await expect(marks).toHaveCount(2);

    // Clear takes them all; Undo brings them back.
    await toggle.click();
    await tools.getByRole("button", { name: "Clear marks" }).click();
    await expect(marks).toHaveCount(0);
    await tools.getByRole("button", { name: "Undo" }).click();
    await expect(marks).toHaveCount(2);

    expect(errors).toEqual([]);
});
