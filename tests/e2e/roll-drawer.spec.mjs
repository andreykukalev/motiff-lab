// The piano roll drawer (decision record 0018, section 4): its head names the passage and carries Note names, the colour
// and the overflow; its handle drags it taller or shorter, remembered on the device, and a tap on the handle puts it away.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

const drawer = page => page.locator(".roll-drawer");

test("the roll's head: Note names, the colour by hand, and the overflow with Colour by and Zoom", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    const head = page.locator(".roll-head");
    await expect(head).toContainText("Piano roll");

    const names = head.getByRole("button", { name: "Note names" });
    await expect(names).toHaveAttribute("aria-pressed", "true");
    await names.click();
    await expect(names).toHaveAttribute("aria-pressed", "false");

    await expect(head.getByRole("button", { name: "Colour: hands" })).toBeVisible();
    await expect(page.locator(".roll-legend")).toContainText("Right hand");

    const more = head.getByRole("button", { name: "Piano roll options" });
    await more.click();
    await expect(more).toHaveAttribute("aria-expanded", "true");
    await expect(page.locator(".roll-menu").getByRole("button", { name: "Zoom in" })).toBeVisible();
    expect(errors).toEqual([]);
});

test("the handle drags the drawer taller, the height is kept, and a tap puts the drawer away", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    await expect(drawer(page)).toBeVisible();
    const before = (await drawer(page).boundingBox()).height;

    const grab = drawer(page).locator(".grab");
    const box = await grab.boundingBox();
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x, y - 60, { steps: 4 });
    await page.mouse.move(x, y - 120, { steps: 4 });
    await page.mouse.up();
    // The drag is not a tap: the drawer stays, taller.
    await expect(drawer(page)).toBeVisible();
    await expect.poll(async () => (await drawer(page).boundingBox()).height).toBeGreaterThan(before + 80);
    expect(await page.evaluate(() => Number(localStorage.getItem("ml-roll-height")))).toBeGreaterThan(0.2);

    // Arrow keys on the handle move it in steps.
    const dragged = (await drawer(page).boundingBox()).height;
    await grab.focus();
    await page.keyboard.press("ArrowDown");
    await expect.poll(async () => (await drawer(page).boundingBox()).height).toBeLessThan(dragged - 10);

    await grab.click();
    await expect(drawer(page)).toHaveCount(0);
    expect(errors).toEqual([]);
});
