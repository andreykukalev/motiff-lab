// The piano roll drawer (decision record 0018, section 4): its head names the passage and carries Note names, the colour
// and the overflow; its handle drags it taller or shorter, remembered on the device, and a tap on the handle puts it away.
import { expect, test } from "@playwright/test";
import { openFurElise, openRoll, selectBar, selectPhrase } from "./steps.mjs";

const drawer = page => page.locator(".roll-drawer");

test("the roll's head: Note names, the colour by hand, the overflow with Colour by, and the floating zoom", async ({ page }) => {
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
    await expect(page.locator(".roll-menu")).toContainText("Colour by");
    await more.click();

    // The zoom floats over the notes (testing item 13): − 100 % + and Fit.
    const zoom = page.locator(".roll").getByRole("group", { name: "Zoom" });
    await expect(zoom).toContainText("100 %");
    await zoom.getByRole("button", { name: "Zoom in" }).click();
    await expect(zoom).toContainText("125 %");
    await zoom.getByRole("button", { name: "Zoom out" }).click();
    await zoom.getByRole("button", { name: "Zoom out" }).click();
    await expect(zoom).toContainText("80 %");
    // Fit without a selection shows the overview round the playhead (#521 2.1): the zoom changes from where it was.
    await zoom.getByRole("button", { name: "Fit" }).click();
    await expect(zoom).not.toContainText("80 %");
    expect(errors).toEqual([]);
});

test("the roll's zoom is the player's: a selection keeps it, Expand lays the passage across, and Collapse brings it back (#511 section 5, #521 17)", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 3);
    const level = (scope) => page.locator(`${scope} .roll-zoom-level`);
    await expect(level(".roll-drawer")).toContainText("100 %");
    // A tap outside the selection leaves it as it is (#511 section 2, scenario C): Escape lets it go first.
    await page.keyboard.press("Escape");
    await expect(page.locator(".selection-status")).not.toContainText(/(bar|такт) 3(?!\d)/i);
    await selectBar(page, 2);
    await expect(level(".roll-drawer")).toContainText("100 %");
    await page.locator(".roll-drawer .roll").getByRole("button", { name: "Zoom in" }).click();
    await expect(level(".roll-drawer")).toContainText("125 %");
    await selectPhrase(page, 2);
    await expect(level(".roll-drawer")).toContainText("125 %");

    // Expand lays the passage across the card at once (#521 19), and Fit there is the same; no arrow steps elsewhere.
    await page.locator(".roll-head").getByRole("button", { name: "Expand" }).click();
    await expect(level(".focus-roll")).not.toContainText("125 %");
    const passage = await level(".focus-roll").innerText();
    await page.locator(".focus-roll .roll").getByRole("button", { name: "Fit" }).click();
    await page.waitForTimeout(600);
    await expect(level(".focus-roll")).toHaveText(passage, { useInnerText: true });
    await expect(page.locator(".focus-card").getByRole("button", { name: "Next phrase" })).toHaveCount(0);

    // Collapse: back in the drawer, the zoom is the one the player left (#521 17).
    await page.locator(".bar").getByRole("button", { name: "Collapse" }).click();
    await expect(level(".roll-drawer")).toContainText("125 %");
    expect(errors).toEqual([]);
});

/**
 * What covers the roll's head or spills out of its buttons (#432): each button is what shows at its middle, its icons lie
 * inside it, and its label is one line.
 */
function headTrouble(page, scope) {
    return page.evaluate((css) => {
        const buttons = [...document.querySelectorAll(`${css} .roll-head button`)];
        if (buttons.length === 0) {
            return ["no head"];
        }
        return buttons.flatMap((button) => {
            const box = button.getBoundingClientRect();
            const name = button.textContent.trim() || button.getAttribute("aria-label");
            const top = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
            const covered = button.contains(top) ? [] : [`${name}: covered by ${top?.tagName} ${top?.getAttribute("class")}`];
            const spilled = [...button.querySelectorAll("svg")].filter((icon) => {
                const inner = icon.getBoundingClientRect();
                return inner.left < box.left || inner.top < box.top || inner.right > box.right || inner.bottom > box.bottom;
            }).map(() => `${name}: an icon spills out`);
            const words = document.createTreeWalker(button, NodeFilter.SHOW_TEXT);
            const lines = new Set();
            for (let text = words.nextNode(); text !== null; text = words.nextNode()) {
                const label = document.createRange();
                label.selectNodeContents(text);
                [...label.getClientRects()].filter((rect) => rect.width > 1).forEach((rect) => lines.add(Math.round(rect.top)));
            }
            const wrapped = lines.size > 1 ? [`${name}: ${lines.size} lines`] : [];
            return [...covered, ...spilled, ...wrapped];
        });
    }, scope);
}

test("nothing covers the roll's head, its icons stay in their buttons and its labels on one line, in the drawer and in focus (#432)", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    await expect(page.locator(".roll-drawer .roll-head").getByRole("button", { name: "Edit notes" })).toBeVisible();
    await expect.poll(() => headTrouble(page, ".roll-drawer")).toEqual([]);
    await selectPhrase(page);
    await page.locator(".roll-head").getByRole("button", { name: "Expand" }).click();
    // Expand edits no notes (#521 19).
    await expect(page.locator(".focus-roll .roll-head").getByRole("button", { name: "Note names" })).toBeVisible();
    await expect(page.locator(".focus-roll .roll-head").getByRole("button", { name: "Edit notes" })).toHaveCount(0);
    await expect.poll(() => headTrouble(page, ".focus-roll")).toEqual([]);
    expect(errors).toEqual([]);
});

test("Expand takes the selection into focus as a roll; Notes | Piano roll switches the card, and Collapse brings the drawer back", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.locator(".bar").getByRole("button", { name: "Piano roll" }).click();
    // Only with a selection (#521 19.1): without one Expand is dimmed and says why.
    const expand = page.locator(".roll-head").getByRole("button", { name: "Expand" });
    await expect(expand).toBeDisabled();
    await expect(expand).toHaveAttribute("title", "Select a passage first, then expand it");
    await selectPhrase(page);
    await expand.click();

    const card = page.locator(".focus-card");
    await expect(card.locator(".focus-roll canvas.roll-notes")).toBeVisible();
    await expect(page.locator(".bar .crumb")).toContainText("phrase 1 of");
    await expect(drawer(page)).toHaveCount(0);
    const showAs = page.locator(".bar").getByRole("radiogroup", { name: "Show the phrase as" });
    await expect(showAs.getByRole("radio", { name: "Piano roll" })).toHaveAttribute("aria-checked", "true");

    // The roll is bound to the passage: no arrow steps to another phrase (#521 19).
    await expect(card.getByRole("button", { name: /^(Previous|Next) phrase$/ })).toHaveCount(0);

    await showAs.getByRole("radio", { name: "Notes" }).click();
    await expect(card.locator(".phrase-card-score")).toBeVisible();
    await expect(card.locator(".focus-roll")).toHaveCount(0);
    await showAs.getByRole("radio", { name: "Piano roll" }).click();
    await expect(card.locator(".focus-roll")).toBeVisible();

    // The way back is the way in (#521 17).
    await expect(page.locator(".bar").getByRole("button", { name: "Back to page" })).toHaveCount(0);
    await page.locator(".bar").getByRole("button", { name: "Collapse" }).click();
    await expect(card).toHaveCount(0);
    await expect(drawer(page)).toBeVisible();
    expect(errors).toEqual([]);
});

test("Expand while the music plays: it plays on round the passage, never past it, and the selection stays (#521 19.4, #533 2)", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectPhrase(page);
    await openRoll(page);
    const crumb = page.locator(".bar .crumb");
    const pill = page.locator(".pill");
    await pill.getByRole("button", { name: "Play", exact: true }).click();
    await page.locator(".roll-head").getByRole("button", { name: "Expand" }).click();
    await expect(crumb).toContainText("phrase 1 of");
    const text = await crumb.textContent();
    // Focus has one Play, the pill's (#511 8.1), and no Loop.
    await expect(page.locator(".bar").getByRole("button", { name: /^(Play|Pause)$/ })).toHaveCount(0);
    await expect(pill.getByRole("button", { name: "Loop" })).toHaveCount(0);

    // The passage ends and the music goes round it again, the roll's playhead never past it; the selection stays as it was.
    const playhead = page.locator(".focus-roll .roll-playhead");
    for (let look = 0; look < 6; look++) {
        await page.waitForTimeout(2_000);
        await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
        const atBar = Number((/Bar (\d+)/.exec((await playhead.getAttribute("aria-valuetext")) ?? "") ?? [])[1] ?? 0);
        expect(atBar).toBeLessThanOrEqual(4);
    }
    await expect(crumb).toHaveText(text);
    await pill.getByRole("button", { name: "Pause", exact: true }).click();
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

test("the playhead's handle drags it inside the looping selection, playing on; paused, the arrows nudge it a beat and Play goes on from there", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    const pill = page.locator(".pill");
    const playhead = page.locator(".roll").getByRole("slider", { name: "Playhead" });
    const value = async () => Number(await playhead.inputValue());

    // Space loops the selection and plays it; the playhead keeps to the loop.
    await page.keyboard.press("Space");
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    const min = Number(await playhead.getAttribute("min"));
    const max = Number(await playhead.getAttribute("max"));
    expect(max).toBeGreaterThan(min);

    // The handle hangs under the bar numbers, where the control sits: dragged, the music plays on from the new place.
    // It is taken while paused and the music started under the finger: a handle that moves with the playing would be
    // pressed where it stood a moment before, a beat or more behind on a slow runner, and the press would select notes.
    await pill.getByRole("button", { name: "Pause", exact: true }).click();
    await expect(pill.getByRole("button", { name: "Play", exact: true })).toBeVisible();
    const box = await playhead.boundingBox();
    const x = box.x + box.width / 2;
    await page.mouse.move(x, box.y + 8);
    await page.mouse.down();
    await page.keyboard.press("Space");
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await page.mouse.move(x + 400, box.y + 8, { steps: 6 });
    await page.mouse.up();
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await expect.poll(value).toBeGreaterThanOrEqual(min);
    expect(await value()).toBeLessThanOrEqual(max);
    await expect(page.locator(".selection-status")).toContainText("bar 2");

    // Paused, the place waits; → moves it a beat on, and Play goes on from there, not from the loop's start.
    await pill.getByRole("button", { name: "Pause", exact: true }).click();
    await expect(pill.getByRole("button", { name: "Play", exact: true })).toBeVisible();
    // Dragged far to the left while paused, it stops at the loop's start and waits there.
    const paused = await playhead.boundingBox();
    await page.mouse.move(paused.x + paused.width / 2, paused.y + 8);
    await page.mouse.down();
    await page.mouse.move(paused.x + paused.width / 2 - 600, paused.y + 8, { steps: 6 });
    await page.mouse.up();
    await expect.poll(value).toBe(min);
    await expect(pill.getByRole("button", { name: "Play", exact: true })).toBeVisible();
    await playhead.focus();
    await expect(playhead).toHaveAttribute("aria-valuetext", /^Bar 2, beat 1$/);
    await page.keyboard.press("ArrowRight");
    await expect.poll(value).toBeGreaterThan(min);
    const nudged = await value();
    await page.waitForTimeout(300);
    expect(await value()).toBe(nudged);
    await expect(playhead).toHaveAttribute("aria-valuetext", /^Bar 2, beat 2$/);
    await page.keyboard.press("Space");
    await expect(pill.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    // The place shown is the one heard, which trails the start by the sound card's output latency for a moment: a few
    // ticks before the nudged beat, never back at the loop's start a beat earlier.
    expect(await value()).toBeGreaterThanOrEqual(nudged - 120);
    expect(errors).toEqual([]);
});

