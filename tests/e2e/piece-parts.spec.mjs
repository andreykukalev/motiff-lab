// Pieces in a file, part of the experiment Several pieces in a score or recording (Settings · Experimental, off by default;
// v0.4, made from the pieces layer in 0.5; ideas bank «Несколько произведений внутри одного файла или записи»): a file
// holding two pieces, its pieces confirmed in the Contents, is split into two pieces of the library in one playlist. Nothing is cut: the collection stays and says what was made from it,
// and each piece opens on its own bars and says where it came from.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { librarySessions, pageErrors, violations } from "./steps.mjs";

const twoPieces = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Analysis/Fixtures/two-pieces.musicxml", import.meta.url));

/** Turns the experiments on in Settings · Experimental. */
async function experiments(page, names) {
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    for (const name of names) {
        const experiment = sheet.getByRole("switch", { name });
        await expect(experiment).not.toBeChecked();
        await experiment.check();
    }
    await sheet.press("Escape");
}

async function addTwoPieces(page) {
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(twoPieces);
    await expect(page.locator(".bar .ttl")).toHaveText(/Two pieces/);
    await page.locator(".bar").getByRole("button", { name: "Contents", exact: true }).click();
    return page.locator(".sheet.contents");
}

test("the confirmed boundaries of a file with two pieces split it into two pieces of the library, in one playlist", async ({ page }) => {
    const errors = pageErrors(page);
    await experiments(page, ["Several pieces in a score or recording"]);
    const drawer = await addTwoPieces(page);

    // No piece is kept yet; confirming the two pieces found keeps them as the pieces layer.
    const parts = drawer.locator("#contents-parts");
    await expect(parts).toContainText("Confirm the pieces above to make each one a piece of its own.");
    const review = drawer.locator("#contents-pieces");
    await expect(review.locator(".found-entry")).toHaveCount(2);
    await review.getByRole("button", { name: "Confirm" }).click();
    await expect(drawer.locator("button.cat")).toHaveText("Pieces · 2");

    await expect(parts).toContainText("2 pieces. Each can become a piece of its own in the library.");
    await parts.getByRole("button", { name: "Split into pieces" }).click();
    const form = parts.getByRole("group", { name: "Split into pieces" });
    await expect(form.getByLabel("Playlist")).toHaveValue("Two pieces: Für Elise and Ode to Joy");
    const rows = form.locator(".part-row");
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0).locator(".c")).toHaveText("the pickup and bars 1–105");
    await expect(rows.nth(1).locator(".c")).toHaveText("bars 106–121");
    await expect(rows.nth(1).getByLabel("Title of piece 2")).toHaveValue(/Ode to Joy/);
    await rows.nth(0).getByLabel("Title of piece 1").fill("Für Elise");
    await rows.nth(1).getByLabel("Title of piece 2").fill("Ode to Joy");
    await form.getByLabel("Playlist").fill("Two pieces");
    expect(await violations(page)).toEqual([]);

    // Unticking every piece leaves nothing to make.
    await rows.nth(0).getByRole("checkbox").uncheck();
    await rows.nth(1).getByRole("checkbox").uncheck();
    await expect(form.getByRole("button", { name: "Split", exact: true })).toBeDisabled();
    await expect(form).toContainText("Tick at least one piece.");
    await rows.nth(0).getByRole("checkbox").check();
    await rows.nth(1).getByRole("checkbox").check();

    await form.getByRole("button", { name: "Split", exact: true }).click();
    await expect(parts.locator(".parts-done")).toHaveText("Split into 2 pieces · playlist “Two pieces”");
    await expect(parts.locator(".entry .t")).toHaveText(["Für Elise", "Ode to Joy"]);
    // The collection is not cut: its pieces and bars are as they were.
    await expect(drawer.locator("button.cat")).toHaveText("Pieces · 2");

    // The library has three pieces, two of them in the playlist; each piece's card says where it came from.
    await page.goto("/");
    await librarySessions(page);
    await expect(page.locator(".card.piece").filter({ has: page.getByRole("heading", { name: "Ode to Joy", exact: true }) }).locator(".what .meta")).toContainText("From “Two pieces: Für Elise and Ode to Joy”");
    await page.getByRole("tab", { name: "Playlists" }).click();
    await expect(page.locator(".lib-groups .group .name")).toHaveText(["Two pieces"]);
    await expect(page.locator(".lib-groups .group .count")).toHaveText(["2 pieces"]);
    await page.locator(".lib-groups .group").filter({ hasText: "Two pieces" }).click();
    const titles = page.locator(".lib-table tbody .ttl");
    await expect(titles).toHaveText(["Für Elise", "Ode to Joy"]);

    // Each piece opens on its own bars, from the start, and says where it came from.
    await page.locator(".lib-table tbody").getByRole("button", { name: "Ode to Joy", exact: true }).click();
    await expect(page.locator(".bar .ttl")).toHaveText("Ode to Joy");
    await page.locator(".bar").getByRole("button", { name: "Contents", exact: true }).click();
    const own = page.locator(".sheet.contents #contents-parts");
    // Read from the piece's own file (version 0.4): a score cut by bars keeps its collection, and no place in it.
    await expect(own.locator(".parts-done")).toHaveText("Split from “Two pieces: Für Elise and Ode to Joy” · playlist “Two pieces”");
    await expect(page.locator(".sheet.contents button.cat")).toHaveCount(0);

    // The collection still says what was made from it after a reload.
    await page.goto("/");
    await page.getByRole("tab", { name: "All pieces" }).click();
    await page.locator(".lib-table tbody").getByRole("button", { name: "Two pieces: Für Elise and Ode to Joy", exact: true }).click();
    await page.locator(".bar").getByRole("button", { name: "Contents", exact: true }).click();
    await expect(page.locator(".sheet.contents #contents-parts .parts-done")).toHaveText("Split into 2 pieces · playlist “Two pieces”");
    await expect(page.locator(".sheet.contents #contents-parts .entry .t")).toHaveText(["Für Elise", "Ode to Joy"]);
    expect(errors).toEqual([]);
});

test("with the experiment off, the Contents offer no split", async ({ page }) => {
    await page.goto("/");
    const drawer = await addTwoPieces(page);
    await expect(drawer).toBeVisible();
    await expect(drawer.locator("#contents-parts")).toHaveCount(0);
});
