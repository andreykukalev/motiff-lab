// Automatic bookmarks, part of the experiment Several pieces (Settings · Experimental, off by default; 2026-11-11): in a
// long score the Contents propose where pieces, movements and large sections begin, marked as found automatically with the
// facts each rests on. A proposal is confirmed as an ordinary Structure bookmark, moved to another bar or dismissed; nothing is
// saved until it is confirmed.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { violations } from "./steps.mjs";

const twoPieces = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Analysis/Fixtures/two-pieces.musicxml", import.meta.url));

test("with the experiment on, the Contents propose where the second piece begins, to move, confirm or dismiss", async ({ page }) => {
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    const experiment = sheet.getByRole("switch", { name: "Several pieces in a score or recording" });
    await expect(experiment).not.toBeChecked();
    await experiment.check();
    await sheet.press("Escape");

    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(twoPieces);
    await expect(page.locator(".bar .ttl")).toHaveText(/Two pieces/);
    await page.locator(".bar").getByRole("button", { name: "Contents", exact: true }).click();
    const drawer = page.locator(".sheet.contents");
    const found = drawer.locator("#contents-found");
    await expect(found.locator("h3")).toHaveText("Found automatically · 2");
    const ode = found.locator(".found-entry").nth(1);
    await expect(ode).toContainText("Ode to Joy");
    await expect(ode).toContainText("bars 106–121");
    await expect(ode).toContainText("final bar line, key changes to C major, heading “Ode to Joy”");
    await expect(ode.locator(".found-tag")).toHaveText("found automatically");
    expect(await violations(page)).toEqual([]);

    // Move: the second piece starts two bars later, and says where it was found.
    await ode.getByRole("button", { name: "Move" }).click();
    await ode.getByLabel("Starts at bar").fill("108");
    await ode.getByRole("button", { name: "Done" }).click();
    await expect(ode).toContainText("bars 108–121");
    await expect(ode).toContainText("moved from bar 106");

    // Confirm: an ordinary Structure bookmark, and the proposal leaves the list.
    await ode.getByRole("button", { name: "Confirm" }).click();
    await expect(drawer.locator("button.cat")).toHaveText("Structure · 1");
    await expect(drawer.getByRole("button", { name: /^Ode to Joy, bars 108–121/ })).toBeVisible();
    await expect(found.locator(".found-entry")).toHaveCount(1);

    // Dismiss the opening: nothing is left to confirm.
    await found.locator(".found-entry").first().getByRole("button", { name: "Dismiss" }).click();
    await expect(found).toContainText("Every boundary found is in your bookmarks.");
});

test("with the experiment off, the Contents propose nothing", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(twoPieces);
    await expect(page.locator(".bar .ttl")).toHaveText(/Two pieces/);
    await page.locator(".bar").getByRole("button", { name: "Contents", exact: true }).click();
    await expect(page.locator(".sheet.contents")).toBeVisible();
    await expect(page.locator("#contents-found")).toHaveCount(0);
});
