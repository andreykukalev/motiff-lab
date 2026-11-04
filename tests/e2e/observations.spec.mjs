// Recording observations (#78, docs/08-central-product-model.md) in every browser project: Keep this variation
// keeps what was tried on the phrase with a note in the player's words; the phrase lists what was kept, and
// opening one puts its selection and variation back to hear and compare; rename, export and delete. Für Elise
// has no project, so it says what is kept lasts while the page is open; an imported piece saves it with its project.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { openAdjust, openFurElise, openInsights, heardChoice, tryExperiment } from "./steps.mjs";

/** Lists the kept ideas whole from the strip at the top of the Insights stream. */
async function manageIdeas(page) {
    const manage = page.locator(".ideas-strip").getByRole("button", { name: "Manage" });
    if (await manage.count() > 0) {
        await manage.click();
    }
}

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

test("keep a variation with a note, come back to it, rename, export and delete it", async ({ page }) => {
    const errors = await openFurElise(page);
    const phrases = page.locator(".score button.bracket[aria-label]");
    const selection = page.locator(".selection-status");
    const card = page.locator(".stream .explore");
    const kept = page.locator(".observations");

    await phrases.nth(0).click();
    await openInsights(page);
    await expect(kept.getByRole("heading", { name: "Kept for phrase 1" })).toBeVisible();
    await expect(kept).toContainText("Nothing kept for this phrase yet.");

    // Try an experiment and keep it: with no project, the card and the list say it lasts while the page is open.
    await tryExperiment(page, "Give the melody more space");
    await card.getByRole("button", { name: "Short", exact: true }).click();
    await page.locator(".stream").getByRole("button", { name: "Keep this variation" }).click();
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    // The kept variation is a plum chip in the ideas strip; Manage lists the ideas whole.
    await expect(page.locator(".ideas-strip").getByRole("button", { name: "Give the melody more space: Short" })).toBeVisible();
    await manageIdeas(page);
    await expect(kept).toContainText("not saved as a project");

    // A note in the player's own words, saved with the button (or when the field loses focus).
    await card.getByLabel("Add a note in your own words (optional)").fill("Lighter, like a dance on tiptoe.");
    await card.getByRole("button", { name: "Save note" }).click();
    await expect(card).toContainText("Note saved.");

    const item = kept.getByRole("listitem", { name: "Give the melody more space: Short" });
    await expect(item).toBeVisible();
    await expect(item).toContainText("the pickup and bars 1–4 · an experiment");
    await expect(item).toContainText("Your note: Lighter, like a dance on tiptoe.");
    await expect(item).toContainText("You hear this");
    await expect(item.getByRole("button", { name: "Open" })).toBeDisabled();
    await expect(phrases.nth(0)).toHaveAccessibleName(/, 1 kept$/);

    // Another phrase lists nothing; clearing leaves no variation to hear.
    await phrases.nth(1).click();
    await expect(kept.getByRole("heading", { name: "Kept for phrase 2" })).toBeVisible();
    await expect(kept.getByRole("listitem")).toHaveCount(0);
    await page.locator(".inspector-head").getByRole("button", { name: "Clear" }).click();
    await expect(page.locator(".inspector-head .compare-switch")).toHaveCount(0);

    // Back on phrase 1, opening the kept one puts its variation back, heard at once and ready to compare.
    await phrases.nth(0).click();
    await expect(item).not.toContainText("You hear this");
    await item.getByRole("button", { name: "Open" }).click();
    await expect(selection).toContainText("Opened “Give the melody more space: Short”.");
    await expect(item).toContainText("You hear this");
    await expect.poll(() => heardChoice(page)).toBe("Variation");
    const compare = page.locator(".inspector-head").getByRole("group", { name: "Compare with the original" });
    await expect(compare.getByRole("button", { name: "Variation", exact: true })).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".stream + .compare")).toBeVisible();
    const adjust = await openAdjust(page);
    await expect(adjust.locator(".steps")).toContainText("Short");

    // Rename.
    await item.getByRole("button", { name: "Rename" }).click();
    await item.getByLabel("Name").fill("Tiptoe");
    await item.getByRole("button", { name: "Save name" }).click();
    const renamed = kept.getByRole("listitem", { name: "Tiptoe" });
    await expect(renamed).toBeVisible();

    // Export: the kept variation as a MIDI file, named by the piece, its bars and its name. The name is read
    // from the link the page clicks, as some headless browsers report a name with non-ASCII letters as "download".
    await page.evaluate(() => {
        const click = HTMLAnchorElement.prototype.click;
        HTMLAnchorElement.prototype.click = function () {
            window.__downloaded = this.download;
            click.call(this);
        };
    });
    const download = page.waitForEvent("download");
    await renamed.getByRole("button", { name: "Export MIDI" }).click();
    const file = await (await download).createReadStream();
    const chunks = [];
    for await (const chunk of file) {
        chunks.push(chunk);
    }
    expect(Buffer.concat(chunks).subarray(0, 4).toString("latin1")).toBe("MThd");
    expect(await page.evaluate(() => window.__downloaded)).toBe("Für Elise (Beethoven, opening) – bars 0–4 – Tiptoe.mid");

    // A note alone about the phrase.
    await kept.getByRole("button", { name: "Write a note about this phrase" }).click();
    await kept.getByLabel("A note about this phrase, in your own words").fill("Count the pickup as a breath.");
    await kept.getByRole("button", { name: "Keep note" }).click();
    await expect(kept.getByRole("listitem")).toHaveCount(2);
    await expect(kept.getByRole("listitem").nth(1)).toContainText("Count the pickup as a breath.");
    await expect(phrases.nth(0)).toHaveAccessibleName(/, 2 kept$/);

    // Delete asks first.
    await renamed.getByRole("button", { name: "Delete" }).click();
    await renamed.getByRole("button", { name: "Keep", exact: true }).click();
    await expect(renamed).toBeVisible();
    await renamed.getByRole("button", { name: "Delete" }).click();
    await renamed.getByRole("button", { name: "Delete for good" }).click();
    await expect(kept.getByRole("listitem")).toHaveCount(1);
    await expect(kept).toContainText("Deleted “Tiptoe”.");
    await expect(phrases.nth(0)).toHaveAccessibleName(/, 1 kept$/);

    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("what is kept is saved with the project and comes back when it is opened again", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/import");
    await page.setInputFiles("input[type=file]", minuet);
    await page.getByText("Open the piece").click();
    const phrases = page.locator(".score button.bracket[aria-label]");
    const kept = page.locator(".observations");
    const card = page.locator(".stream .explore");

    await phrases.nth(0).click();
    await openInsights(page);
    await expect(kept).not.toContainText("not saved as a project");
    await tryExperiment(page, "Give the melody more space");
    await page.locator(".stream").getByRole("button", { name: "Keep this variation" }).click();
    await expect(card.getByRole("status")).toContainText("Kept with the project.");
    // The note is saved when the field loses focus.
    await card.getByLabel("Add a note in your own words (optional)").fill("Airier.");
    await card.getByLabel("Add a note in your own words (optional)").blur();
    await expect(card).toContainText("Note saved.");
    await manageIdeas(page);
    await expect(kept.getByRole("listitem")).toContainText("Your note: Airier.");
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();

    // The project page lists the idea in its library (0012), and continuing from the resume point comes back to the phrase.
    await page.goto("/");
    await page.getByRole("link", { name: "importer-grand-staff.musicxml" }).click();
    const idea = page.locator(".idea-card").filter({ hasText: "Give the melody more space: Detached" });
    await expect(idea).toContainText("Your note: Airier.");
    await expect(page.locator(".session-card")).toHaveCount(1);
    await page.locator(".session-card").getByRole("button", { name: "Continue" }).click();
    await expect(page.locator(".selection-status")).toContainText("Selected phrase 1");
    await expect(phrases.nth(0)).toHaveAccessibleName(/, 1 kept$/);
    await phrases.nth(0).click();
    await openInsights(page);
    await manageIdeas(page);
    const item = kept.getByRole("listitem", { name: "Give the melody more space: Detached" });
    await expect(item).toContainText("Your note: Airier.");
    await page.locator(".inspector-head").getByRole("button", { name: "Clear" }).click();
    await phrases.nth(0).click();
    await item.getByRole("button", { name: "Open" }).click();
    await expect(item).toContainText("You hear this");
    await expect(page.locator(".inspector-head .compare-switch")).toBeVisible();
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});
