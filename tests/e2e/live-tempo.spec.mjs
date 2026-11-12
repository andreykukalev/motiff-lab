// «Темп живого исполнения» (an experiment, Settings · Experimental, version 0.5): with the experiment on, Insights opens with
// the piece's Tempo card, music-core's tempo bar by bar; off, there is none.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { openInsights, violations } from "./steps.mjs";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

async function addMinuet(page) {
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(minuet);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
}

test("the tempo card shows the piece's tempo only with the experiment on", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await addMinuet(page);
    let insights = await openInsights(page);
    await expect(insights.getByRole("region", { name: "Tempo" })).toHaveCount(0);

    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    await sheet.getByRole("switch", { name: "Live performance tempo" }).check();
    await sheet.press("Escape");
    await addMinuet(page);
    insights = await openInsights(page);
    const card = insights.getByRole("region", { name: "Tempo" });
    await expect(card).toBeVisible();
    await expect(card).toContainText(/Tempo: /);
    await expect(card).toContainText("From the file");
    expect(await violations(page)).toEqual([]);
    expect(errors).toEqual([]);
});
