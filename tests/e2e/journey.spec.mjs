// The core journey of Stage 1 in every browser project: open a piece, select a bar, hear it loop,
// propose a dotted rhythm and hear it (S1-W4, S1-M1, S1-E1).
import { expect, test } from "@playwright/test";
import { openAdjust, openFurElise, selectBar, heardChoice, openInsights } from "./steps.mjs";

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

/** The middle one of an odd number of times. */
function median(times) {
    return [...times].sort((a, b) => a - b)[Math.floor(times.length / 2)];
}

test("open a piece, loop a bar, propose and hear a variation", async ({ page, browserName }) => {
    // The 300 ms target for previews (docs/01-mvp-stage-1.md, Non-functional targets) is the device's, and it stays
    // 300 ms. The WebKit projects (webkit, tablet-ipad) run an emulated WebKit on a shared 2-worker runner, where one
    // start lands anywhere from about 120 to 370 ms from run to run (#401) while Chromium and Firefox measure 0 to
    // 35 ms: there a single start passes or fails by chance. So WebKit starts five times and the median start must be
    // within the target: one start slowed by the shared runner does not fail it, an engine that starts late still does.
    // The other projects keep a single start.
    const starts = browserName === "webkit" ? 5 : 1;
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    const play = page.locator(".pill").getByRole("button", { name: "Play", exact: true });
    const pause = page.locator(".pill").getByRole("button", { name: "Pause", exact: true });
    const stop = async () => {
        await pause.click();
        await expect(play).toBeVisible();
    };

    await page.locator(".pill").getByRole("button", { name: "Loop", exact: true }).click();
    const original = [];
    for (let i = 0; i < starts; i++) {
        original.push(await startTime(page, play));
        await stop();
    }

    // Picking a reshape plays it at once (S1-W5); picked again, the same reshape is proposed and played again.
    await openInsights(page);
    const adjust = await openAdjust(page);
    const dotted = adjust.getByRole("button", { name: "Dotted" });
    const variation = [];
    for (let i = 0; i < starts; i++) {
        variation.push(await startTime(page, dotted));
        await expect(adjust.locator(".changed p").first()).not.toBeEmpty();
        await expect.poll(() => heardChoice(page)).toBe("Variation");
        if (i === 0) {
            await expect(page.getByText(/Bar 1, beat [23]/)).toBeVisible();
        }
        await stop();
    }

    const told = times => times.length === 1
        ? `${Math.round(times[0])} ms`
        : `${times.map(time => `${Math.round(time)} ms`).join(", ")} (median ${Math.round(median(times))} ms)`;
    const report = `original ${told(original)}, variation ${told(variation)}`;
    test.info().annotations.push({ type: "start", description: report });
    console.log(`Preview start, ${test.info().project.name}: ${report}`);
    expect(median(original)).toBeLessThan(300);
    expect(median(variation)).toBeLessThan(300);
    expect(errors).toEqual([]);
});
