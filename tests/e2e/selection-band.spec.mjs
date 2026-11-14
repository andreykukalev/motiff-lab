// The selection's band lies over the selected bar's staves wherever the bar is (#434): far down a long score, after
// scrolling away and back, with the roll drawer open and after it closes. The score keeps where its pages are between
// draws (#419), so a stale place shows here as a band shifted off its staves.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { closePopover, openRoll } from "./steps.mjs";

const piece = fileURLToPath(new URL("./fixtures/large-score.mid", import.meta.url));

/** The band and the selected bar's staff lines, as left, top, right and bottom in the page's pixels. */
function bandAndStaves(page, bar) {
    return page.evaluate((id) => {
        const band = document.querySelector(".score .sel-band");
        const lines = [...document.getElementById(id).querySelectorAll("g.staff > path")].map((line) => line.getBoundingClientRect());
        const box = band?.getBoundingClientRect();
        return {
            band: box === undefined ? null : [box.left, box.top, box.right, box.bottom],
            staves: [
                Math.min(...lines.map((line) => line.left)),
                Math.min(...lines.map((line) => line.top)),
                Math.max(...lines.map((line) => line.right)),
                Math.max(...lines.map((line) => line.bottom)),
            ],
        };
    }, bar);
}

/** The band runs from the bar's first barline to its last and a little above and below its outer staff lines. */
async function expectBandOnStaves(page, bar) {
    await expect.poll(async () => {
        const { band, staves } = await bandAndStaves(page, bar);
        if (band === null) {
            return "no band";
        }
        const [left, top, right, bottom] = band;
        const [linesLeft, linesTop, linesRight, linesBottom] = staves;
        const fits = Math.abs(left - linesLeft) <= 2 && Math.abs(right - linesRight) <= 2
            && top <= linesTop && top >= linesTop - 16 && bottom >= linesBottom && bottom <= linesBottom + 16;
        return fits ? "on the staves" : `band ${band.map(Math.round)} over staves ${staves.map(Math.round)}`;
    }).toBe("on the staves");
}

test("the selection's band stays on its bar's staves far down a long score", async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: /^(Add piece|Добавить пьесу)$/ }).click();
    await page.getByRole("dialog").locator("input[type=file]").setInputFiles(piece);
    await expect(page).toHaveURL(/\/playback\?project=/);
    // The phrases are read after the score shows: the Phrases popover counts them once they are.
    await page.locator(".bar").getByRole("button", { name: /^(Phrases|Фразы)$/ }).click();
    await expect(page.locator(".phrases-popover .caption")).not.toBeEmpty({ timeout: 120_000 });
    await closePopover(page);

    // A bar in the middle of a page more than half way down, once that page is drawn (pages away from the view are drawn
    // after the first, #419).
    await page.evaluate(() => {
        const host = document.querySelector(".score");
        host.scrollTop = host.scrollHeight * 0.6;
    });
    await expect.poll(() => page.evaluate(() => {
        const host = document.querySelector(".score");
        const view = host.getBoundingClientRect();
        const middle = (view.top + view.bottom) / 2;
        const sheet = [...host.querySelectorAll(":scope > .score-page")].find((candidate) => {
            const box = candidate.getBoundingClientRect();
            return box.top <= middle && box.bottom > middle;
        });
        return sheet?.querySelector("svg g.measure") !== null && sheet !== undefined;
    })).toBe(true);
    const bar = await page.evaluate(() => {
        const host = document.querySelector(".score");
        const view = host.getBoundingClientRect();
        const middle = (view.top + view.bottom) / 2;
        const sheet = [...host.querySelectorAll(":scope > .score-page")].find((candidate) => {
            const box = candidate.getBoundingClientRect();
            return box.top <= middle && box.bottom > middle;
        });
        const measures = [...sheet.querySelectorAll("svg g.measure")];
        const measure = measures[Math.floor(measures.length / 2)];
        measure.querySelector("g.staff > path").dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
        return measure.id;
    });
    await expect(page.locator(".selection-status")).toContainText(/Selected|Выделено/);
    await expectBandOnStaves(page, bar);

    // Away and back again.
    await page.evaluate(() => {
        document.querySelector(".score").scrollTop -= 4000;
    });
    await page.evaluate(() => new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done))));
    await page.evaluate(() => {
        document.querySelector(".score").scrollTop += 4000;
    });
    await expectBandOnStaves(page, bar);

    // The roll drawer opened over the score, and closed.
    await openRoll(page);
    await expectBandOnStaves(page, bar);
    await page.locator(".bar").getByRole("button", { name: /^(Piano roll|Фортепианный ролл)$/ }).click();
    await expect(page.locator(".roll-drawer")).toHaveCount(0);
    await expectBandOnStaves(page, bar);
});
