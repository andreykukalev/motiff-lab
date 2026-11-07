// The place of playing (testing item 4): a cobalt line at the sounding moment and a wash over its bar, which the score
// follows unless the player turns it off in Settings; scrolled away from it, a chip leads back to it.
import { expect, test } from "@playwright/test";
import { openFurElise } from "./steps.mjs";

test("the playhead moves with the playing, and Back to playback finds it after scrolling away", async ({ page }) => {
    // A small upright window: the page is fitted to its width, so the opening runs below the screen.
    await page.setViewportSize({ width: 400, height: 440 });
    const errors = await openFurElise(page);
    const line = page.locator("div.score .playhead");
    const wash = page.locator("div.score .play-bar");
    await expect(line).toBeHidden();

    await page.locator(".bar").getByRole("button", { name: "Play", exact: true }).click();
    await expect(line).toBeVisible();
    await expect(wash).toBeVisible();
    const first = await line.evaluate(element => element.offsetLeft + element.offsetTop * 10_000);
    await expect.poll(() => line.evaluate(element => element.offsetLeft + element.offsetTop * 10_000)).not.toBe(first);

    // Scrolled far from the playing, the chip says where it is and brings the score back to it.
    const score = page.locator("div.score");
    const box = await score.boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.wheel(0, 5000);
    const back = page.getByRole("button", { name: /^Back to playback · bar \d+$/ });
    await expect(back).toBeVisible();
    await back.click();
    await expect(back).toHaveCount(0);
    await expect(line).toBeInViewport();

    await page.locator(".bar").getByRole("button", { name: "Pause", exact: true }).click();
    await expect(line).toBeHidden();
    expect(errors).toEqual([]);
});
