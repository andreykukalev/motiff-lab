// The modes of version 0.5 on the Piano Roll (the experiment Two playheads and modes, Settings · Experimental; product spec
// interaction-modes, «Правила выделения по режимам»): Navigate moves the roll and selects nothing; Select selects by beats
// with a drag from outside the selection, and a drag or a click inside it leaves it alone; Edit notes draws a box round
// notes and opens the editor on them, which is not a selection. No click on the roll removes the selection.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

const status = (page) => page.locator(".selection-status");

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
    const selected = await status(page).textContent();
    await drag(page, at(0.3), at(0.8));
    await page.mouse.click(at(0.3).x, at(0.3).y);
    await page.mouse.click(at(0.8).x, at(0.8).y);
    await expect(status(page)).toHaveText(selected);

    // Edit notes: a box round notes opens the editor on them; the selection stays.
    await page.keyboard.press("e");
    await expect(roll).toHaveAttribute("data-mode", "edit");
    await drag(page, at(0.01, 0.02), at(0.99, 0.98));
    await expect(page.locator(".roll-drawer").getByRole("button", { name: "Cancel" })).toBeVisible();
    await expect(status(page)).toHaveText(selected);
    await page.locator(".roll-drawer").getByRole("button", { name: "Cancel" }).click();
    expect(errors).toEqual([]);
});
