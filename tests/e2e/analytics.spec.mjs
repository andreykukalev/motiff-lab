// Anonymous analytics with consent (#64): nothing is recorded until the player turns them on, the
// events hold no titles or file names, and turning them off forgets the device's ID and the events.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

test("analytics record nothing until turned on, never the piece, and forget everything when turned off", async ({ page }) => {
    await openFurElise(page);
    await page.goto("/settings");
    const toggle = page.getByLabel("Send anonymous usage analytics");
    await expect(toggle).not.toBeChecked();
    await expect(page.getByText("Nothing is recorded while analytics are off.")).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem("motifflab.analytics.device"))).toBeNull();

    await toggle.check();
    await expect(page.getByText("Waiting to be sent (0)")).toBeVisible();
    // A new load of the app reads the choice back from this browser.
    await openFurElise(page);
    await selectBar(page, 1);
    await page.getByRole("tab", { name: "Explore" }).click();
    await page.getByRole("button", { name: /Give the melody more space/ }).click();
    await page.getByRole("button", { name: "Keep this variation" }).click();
    await page.getByRole("tab", { name: "Tools", exact: true }).click();
    await page.getByRole("button", { name: "Dotted" }).click();

    // Settings is reached from Projects (the gear by the account), within the same load of the app.
    await page.getByRole("link", { name: "‹ Projects" }).click();
    await page.getByRole("link", { name: "Settings" }).click();
    await expect(page.locator(".events")).toContainText("app_opened");
    const events = page.locator(".events li");
    await expect(events.first()).toBeVisible();
    const text = (await events.allInnerTexts()).join("\n");
    expect(text).toContain("starter_opened piece: fur_elise");
    expect(text).toContain("motif_selected bars: 1");
    expect(text).toContain("variation_proposed op: V1, kind: dotted");
    expect(text).toContain("playback_started hear: variation");
    expect(text).toContain("experiment_tried experiment: melody.space, alternative: 0, entry: offer");
    expect(text).toContain("experiment_kept experiment: melody.space, alternative: 0");
    expect(text).not.toContain("Beethoven");
    expect(await page.evaluate(() => localStorage.getItem("motifflab.analytics.device"))).toMatch(/^[0-9a-f]{32}$/);

    await page.getByLabel("Send anonymous usage analytics").uncheck();
    await expect(page.locator(".events li")).toHaveCount(0);
    expect(await page.evaluate(() => localStorage.getItem("motifflab.analytics.device"))).toBeNull();
});
