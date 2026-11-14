// Several pieces in a score or recording, an experiment (Settings · Experimental, off by default; version 0.5): in a long
// score the Contents propose where each piece begins, with the facts each boundary rests on, marked as found
// automatically. The player removes, moves, adds and names them; Confirm keeps them as the pieces layer, ordinary
// bookmarks in the category Pieces, in one save. Nothing is saved before Confirm.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { violations } from "./steps.mjs";

const twoPieces = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Analysis/Fixtures/two-pieces.musicxml", import.meta.url));

test("with the experiment on, the Contents propose where the second piece begins, to move, name and confirm", async ({ page }) => {
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
    const review = drawer.locator("#contents-pieces");
    await expect(review.locator("h3")).toHaveText("Pieces · 2");
    const ode = review.locator(".found-entry").nth(1);
    await expect(ode.getByLabel("Title of piece 2")).toHaveValue("Ode to Joy");
    await expect(ode).toContainText("bars 106–121");
    await expect(ode).toContainText("final bar line, key changes to C major, heading “Ode to Joy”");
    await expect(ode.locator(".found-tag")).toHaveText("found automatically");
    // Nothing is kept before Confirm.
    await expect(drawer.locator("button.cat")).toHaveCount(0);
    expect(await violations(page)).toEqual([]);

    // Move: the second piece starts two bars later, and says where it was found.
    await ode.getByRole("button", { name: "A bar later" }).click();
    await review.locator(".found-entry").nth(1).getByRole("button", { name: "A bar later" }).click();
    const moved = review.locator(".found-entry").nth(1);
    await expect(moved).toContainText("bars 108–121");
    await expect(moved).toContainText("moved from bar 106");

    // Name the first piece, then confirm: the pieces layer, ordinary bookmarks in the category Pieces.
    const first = review.getByLabel("Title of piece 1");
    await first.fill("Für Elise");
    await first.blur();
    await review.getByRole("button", { name: "Confirm" }).click();
    await expect(review.locator(".entry .t")).toHaveText(["Für Elise", "Ode to Joy"]);
    await expect(drawer.locator("button.cat")).toHaveText("Pieces · 2");

    // Review again: the kept boundary is the player's; removing it and cancelling keeps the layer as it was.
    await review.getByRole("button", { name: "Review the pieces" }).click();
    await expect(review.locator(".found-entry").nth(1).locator(".found-tag")).toHaveText("your boundary");
    await review.locator(".found-entry").nth(1).getByRole("button", { name: "Remove boundary" }).click();
    await expect(review.locator(".found-entry")).toHaveCount(1);
    await review.getByRole("button", { name: "Cancel" }).click();
    await expect(review.locator(".entry .t")).toHaveText(["Für Elise", "Ode to Joy"]);
});

test("with the experiment off, the Contents propose nothing", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(twoPieces);
    await expect(page.locator(".bar .ttl")).toHaveText(/Two pieces/);
    await page.locator(".bar").getByRole("button", { name: "Contents", exact: true }).click();
    await expect(page.locator(".sheet.contents")).toBeVisible();
    await expect(page.locator("#contents-pieces")).toHaveCount(0);
});
