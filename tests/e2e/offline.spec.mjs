// The core journey offline, once the app has loaded (S1 non-functional targets): the service worker
// installed from the published app serves it with no network.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

test("the journey works offline after the app has loaded", async ({ page, context, browserName }) => {
    // Playwright's offline mode in WebKit fails navigations before the service worker can answer
    // them, so offline cannot be emulated there; the worker is the same script in every browser.
    test.skip(browserName === "webkit", "WebKit's offline emulation bypasses service workers");
    await page.goto("/");
    // Ready once the worker has installed, which caches every file the app needs.
    await page.evaluate(() => navigator.serviceWorker.ready);

    await context.setOffline(true);
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.getByRole("button", { name: "Loop selection" }).click();
    await page.locator(".transport").getByRole("button", { name: "Play" }).click();
    await expect(page.getByText("Playing", { exact: true })).toBeVisible();
    expect(errors).toEqual([]);
});
