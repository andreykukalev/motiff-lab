// The core journey of Stage 1 in every browser project: open a piece, select a bar, hear it loop,
// propose a dotted rhythm and hear it (S1-W4, S1-M1, S1-E1).
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

/**
 * Clicks a button and measures how long until the playback engine starts, in audio time: from the
 * audio clock when the button is pressed to the frame the engine reports it started playing on
 * (decision record 0007). Reports reach the page only when its thread is free, so their arrival
 * time would count the page's rendering, which does not delay the sound. The device's output
 * latency comes on top.
 */
async function startTime(page, button) {
    await page.evaluate(() => {
        window.__started = new Promise(resolve => {
            document.addEventListener("pointerdown", () => {
                const pressed = window.__audio.currentTime;
                window.__engineStarted = started => resolve((started - pressed) * 1000);
            }, { capture: true, once: true });
        });
    });
    await button.click();
    return page.evaluate(() => window.__started);
}

test("open a piece, loop a bar, propose and hear a variation", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    const play = page.locator(".transport-bar").getByRole("button", { name: "Play" });
    const pause = page.locator(".transport-bar").getByRole("button", { name: "Pause" });

    await page.getByRole("button", { name: "Loop", exact: true }).click();
    const original = await startTime(page, play);
    await pause.click();

    // Picking a reshape plays it at once (S1-W5).
    await page.getByRole("tab", { name: "Tools", exact: true }).click();
    const variation = await startTime(page, page.getByRole("button", { name: "Dotted" }));
    await expect(page.locator(".inspector .changed p").first()).not.toBeEmpty();
    await expect(page.locator("select[aria-label='What is heard'] option:checked")).toHaveText("Variation");
    await expect(page.getByText(/Bar 1, beat [23]/)).toBeVisible();
    await pause.click();

    // The 300 ms target for previews (docs/01-mvp-stage-1.md, Non-functional targets).
    test.info().annotations.push({ type: "start", description: `original ${Math.round(original)} ms, variation ${Math.round(variation)} ms` });
    expect(original).toBeLessThan(300);
    expect(variation).toBeLessThan(300);
    expect(errors).toEqual([]);
});
