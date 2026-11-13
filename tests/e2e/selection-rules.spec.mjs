// The rules of the selection in version 0.5 (the experiment Two playheads and modes, Settings · Experimental; product spec
// interaction-modes, «Правила выделения по режимам»): the modes in the bar and their keys; a selection by beats made by a
// drag in Select; a click, a drag that starts inside the selection and any drag in Navigate leave it as it is; Shift and
// a click in Select extend it; ✕ in its menu or Escape removes it. Off, the bar keeps its Pen and no group of modes.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

/** The experiment on, as Settings · Experimental keeps it on the device. */
async function withExperiment(page) {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.playheads", "on"));
}

/** The engraved bars of the first system, left to right, as the page draws them: left, top, right and bottom. */
async function firstSystem(page) {
    await expect(page.locator(".score svg g.measure").first()).toBeVisible();
    return page.evaluate(() => {
        const first = document.querySelector(".score svg g.system");
        return [...first.querySelectorAll("g.measure")].map((measure) => {
            const box = measure.getBoundingClientRect();
            return { left: box.left, top: box.top, right: box.right, bottom: box.bottom, middle: (box.top + box.bottom) / 2 };
        });
    });
}

async function drag(page, from, to) {
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 8 });
    await page.mouse.up();
}

const status = (page) => page.locator(".selection-status");

test("the modes choose what a drag does, and only the handles, ✕ and Escape change the selection", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    const modes = page.locator(".bar").getByRole("radiogroup", { name: "Mode" });
    await expect(modes.getByRole("radio", { name: "Navigate" })).toHaveAttribute("aria-checked", "true");
    await expect(page.locator(".bar").getByRole("button", { name: "Pen", exact: true })).toHaveCount(0);
    const bars = await firstSystem(page);

    // Navigate: a drag across the bars selects nothing.
    await drag(page, { x: bars[2].left + 10, y: bars[2].middle }, { x: bars[4].right - 10, y: bars[4].middle });
    await expect(status(page)).toContainText("Nothing selected");

    // Select, by its key: a drag selects by beats, from the beat it starts on to the one it ends on.
    await page.keyboard.press("s");
    await expect(modes.getByRole("radio", { name: "Select" })).toHaveAttribute("aria-checked", "true");
    await expect(page.locator("div.score")).toHaveAttribute("data-mode", "select");
    await drag(page, { x: bars[2].left + 4, y: bars[2].middle }, { x: (bars[4].left + bars[4].right) / 2, y: bars[4].middle });
    await expect(status(page)).toContainText(/Selected bar \d+, beat \d+, through bar \d+, beat \d+/);
    const selected = await status(page).textContent();

    // A click inside or outside, and a drag that starts inside, leave it as it is.
    await page.mouse.click(bars[3].left + 12, bars[3].middle);
    await page.mouse.click(bars[5].left + 12, bars[5].middle);
    await drag(page, { x: bars[3].left + 12, y: bars[3].middle }, { x: bars[5].right - 4, y: bars[5].middle });
    await expect(status(page)).toHaveText(selected);

    // Shift and a click in Select extend it to the beat clicked.
    await page.keyboard.down("Shift");
    await page.mouse.click(bars[5].right - 6, bars[5].middle);
    await page.keyboard.up("Shift");
    await expect(status(page)).not.toHaveText(selected);
    const extended = await status(page).textContent();

    // Navigate again: a drag elsewhere moves nothing of the selection; ✕ in the menu removes it.
    await page.keyboard.press("h");
    await drag(page, { x: bars[0].left + 10, y: bars[0].middle }, { x: bars[1].right - 4, y: bars[1].middle });
    await expect(status(page)).toHaveText(extended);
    await page.locator(".ctx").getByRole("button", { name: "Clear the selection" }).click();
    await expect(status(page)).toContainText("Nothing selected");

    // Escape removes a selection too.
    await page.keyboard.press("s");
    await drag(page, { x: bars[2].left + 4, y: bars[2].middle }, { x: bars[3].right - 4, y: bars[3].middle });
    await expect(status(page)).toContainText("Selected");
    await page.keyboard.press("Escape");
    await expect(status(page)).toContainText("Nothing selected");

    // Pen by its key: the pencil is on and the selection drag is not.
    await page.keyboard.press("p");
    await expect(modes.getByRole("radio", { name: "Pen" })).toHaveAttribute("aria-checked", "true");
    await expect(page.locator("div.score")).toHaveClass(/pencil-on/);
    await page.keyboard.press("h");
    await expect(page.locator("div.score")).not.toHaveClass(/pencil-on/);
    expect(errors).toEqual([]);
});

test("in Edit notes a click on a note opens it on the roll's editor", async ({ page }) => {
    await withExperiment(page);
    const errors = await openFurElise(page);
    await firstSystem(page);
    // The mode keys listen once the page has attached them after its first render: a key pressed before that is lost.
    await expect(async () => {
        await page.keyboard.press("e");
        await expect(page.locator("div.score")).toHaveAttribute("data-mode", "edit", { timeout: 1000 });
    }).toPass();
    // A notehead of the second system, clear of the bar floating over the top of the page.
    const head = await page.evaluate(() => {
        const system = document.querySelectorAll(".score svg g.system")[1];
        const box = system.querySelector("g.note g.notehead").getBoundingClientRect();
        return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
    });
    await page.mouse.click(head.x, head.y);
    await expect(page.locator(".roll-drawer")).toBeVisible();
    await expect(status(page)).toContainText("Nothing selected");
    expect(errors).toEqual([]);
});

test("without the experiment the bar keeps its Pen and no modes", async ({ page }) => {
    await openFurElise(page);
    await expect(page.locator(".bar").getByRole("radiogroup", { name: "Mode" })).toHaveCount(0);
    await expect(page.locator("div.score")).not.toHaveAttribute("data-mode", /.+/);
});
