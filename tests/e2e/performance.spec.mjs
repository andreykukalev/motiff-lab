// Another version compared with the piece (decision record 0022, sections 1, 4 and 5): a MIDI file of a performance added in
// the Other versions drawer, a plum marker above each bar it plays differently, the printed notes it did not play tinted, a bar
// taken into the score and restored, all kept with the project. The performance is Für Elise's opening with three notes
// added in bar 5 and the left hand of bar 7 left out (fixtures/fur-elise-cover.mid, made from the public-domain score). Which is the base is only which was opened first: the score
// in MusicXML can be added over the cover opened as the piece.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { violations } from "./steps.mjs";

const score = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Experiments/Fixtures/fur-elise-original.musicxml", import.meta.url));
const cover = fileURLToPath(new URL("fixtures/fur-elise-cover.mid", import.meta.url));

test("a MIDI performance marks the bars it plays differently, and a bar is taken into the score and restored", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);

    const more = page.locator(".bar").getByRole("button", { name: "More" });
    await more.click();
    await page.locator(".more-popover").getByRole("button", { name: "Add a version" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.locator("input[type=file]").setInputFiles(cover);
    await expect(drawer.getByRole("status")).toHaveText("Lined up: 50 of 53 notes found in the version, 2 bars differ.");

    const markers = page.locator(".score button.perf-marker");
    await expect(markers).toHaveText(["+3 · added notes", "−3 · simplified"]);
    await expect(markers.nth(1)).toHaveAccessibleName("Bar 7 in fur-elise-cover: −3 · simplified");
    await expect(page.locator(".score g.note.not-heard")).toHaveCount(3);
    expect(await violations(page)).toEqual([]);

    // A marker selects its bar in the drawer.
    await markers.nth(1).click();
    await expect(drawer.locator(".check-entry.on")).toContainText("Bar 7");

    // Bar 5 taken: its marker goes, it is listed as taken, and so it stays after a reload.
    await drawer.locator(".check-entry", { hasText: "Bar 5" }).getByRole("button", { name: "Take into the score" }).click();
    await expect(markers).toHaveText(["−3 · simplified"]);
    await expect(drawer.locator(".resolved .check-entry")).toContainText("Bar 5");
    await page.reload();
    await expect(page.locator(".score button.perf-marker")).toHaveText(["−3 · simplified"]);

    // Restore these notes: the marker comes back. Not a difference: bar 7's marker goes.
    await page.locator(".bar").getByRole("button", { name: "More" }).click();
    await page.locator(".more-popover").getByRole("button", { name: "Other versions" }).click();
    await drawer.getByRole("button", { name: "Restore these notes" }).click();
    await expect(markers).toHaveText(["+3 · added notes", "−3 · simplified"]);
    await drawer.locator(".check-entry", { hasText: "Bar 7" }).getByRole("button", { name: "Not a difference" }).click();
    await expect(markers).toHaveText(["+3 · added notes"]);
    await expect(page.locator(".score g.note.not-heard")).toHaveCount(0);

    // Removed, asked once: no markers are left.
    await drawer.getByRole("button", { name: "Remove the version" }).click();
    await drawer.getByRole("button", { name: "Remove it for good" }).click();
    await expect(markers).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("the official score in MusicXML is added over notes from a performance", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(cover);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);

    await page.locator(".bar").getByRole("button", { name: "More" }).click();
    await page.locator(".more-popover").getByRole("button", { name: "Add a version" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.locator("input[type=file]").setInputFiles(score);
    await expect(drawer.getByRole("status")).toContainText("Lined up:");
    // The mirror of the first test: what the cover added is now left out of the score, and the left hand it left out is added.
    await expect(page.locator(".score button.perf-marker")).toHaveText(["−3 · simplified", "+3 · added notes"]);
    await expect(drawer.getByLabel("Name")).toHaveValue("fur-elise-original");
    expect(errors).toEqual([]);
});