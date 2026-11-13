// Recording observations (#78, docs/08-central-product-model.md) in every browser project: Keep this variation
// keeps what was tried on the phrase with a note in the player's words; the phrase lists what was kept, and
// opening one puts its selection and variation back to hear and compare; rename, export and delete. Für Elise
// has no project, so it says what is kept lasts while the page is open; an imported piece saves it with its project.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { clearByPage, openAdjust, openFurElise, openInsights, heardChoice, tryWay, librarySessions, focusSelection, selectPhrase } from "./steps.mjs";

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
    const selection = page.locator(".selection-status");
    const card = page.locator(".stream .explore");
    const kept = page.locator(".observations");

    await selectPhrase(page);
    await openInsights(page);
    await expect(kept.getByRole("heading", { name: "Kept for phrase 1" })).toBeVisible();
    await expect(kept).toContainText("Nothing kept for this phrase yet.");

    // Try a way and keep it: with no project, the card and the list say it lasts while the page is open.
    await tryWay(page, "Held chord");
    await card.getByRole("button", { name: "Repeated chord in 8ths", exact: true }).click();
    await page.locator(".stream").getByRole("button", { name: "Keep this variation" }).click();
    await expect(card.getByRole("status")).toContainText("Kept until you leave the page");
    // The kept variation is a plum chip in the ideas strip; Manage lists the ideas whole.
    await expect(page.locator(".ideas-strip").getByRole("button", { name: "Repeated chord in 8ths" })).toBeVisible();
    await manageIdeas(page);
    await expect(kept).toContainText("not saved as a project");

    // A note in the player's own words, saved with the button (or when the field loses focus).
    await card.getByLabel("Add a note in your own words (optional)").fill("Lighter, like a dance on tiptoe.");
    await card.getByRole("button", { name: "Save note" }).click();
    await expect(card).toContainText("Note saved.");

    const item = kept.getByRole("listitem", { name: "Repeated chord in 8ths" });
    await expect(item).toBeVisible();
    await expect(item).toContainText("the pickup and bars 1–4 · an experiment");
    await expect(item).toContainText("Your note: Lighter, like a dance on tiptoe.");
    await expect(item).toContainText("You hear this");
    await expect(item.getByRole("button", { name: "Open" })).toBeDisabled();

    // Another phrase lists nothing; clearing leaves no variation to hear.
    await selectPhrase(page, 1);
    await expect(kept.getByRole("heading", { name: "Kept for phrase 2" })).toBeVisible();
    await expect(kept.getByRole("listitem")).toHaveCount(0);
    await clearByPage(page);
    await expect(page.locator(".inspector-head .compare-switch")).toHaveCount(0);

    // Back on phrase 1, opening the kept one puts its variation back, heard at once and ready to compare.
    await selectPhrase(page);
    await expect(item).not.toContainText("You hear this");
    await item.getByRole("button", { name: "Open" }).click();
    await expect(selection).toContainText("Opened “Repeated chord in 8ths”.");
    await expect(item).toContainText("You hear this");
    await expect.poll(() => heardChoice(page)).toBe("Variation");
    const compare = page.locator(".inspector-head").getByRole("group", { name: "Compare with the original" });
    await expect(compare.getByRole("button", { name: "Variation", exact: true })).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".stream + .compare")).toBeVisible();
    const adjust = await openAdjust(page);
    await expect(adjust.locator(".steps")).toContainText("Repeated chord");

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
    expect(await page.evaluate(() => window.__downloaded)).toBe("Für Elise – bars 0–4 – Tiptoe.mid");

    // A note alone about the phrase.
    await kept.getByRole("button", { name: "Write a note about this phrase" }).click();
    await kept.getByLabel("A note about this phrase, in your own words").fill("Count the pickup as a breath.");
    await kept.getByRole("button", { name: "Keep note" }).click();
    await expect(kept.getByRole("listitem")).toHaveCount(2);
    await expect(kept.getByRole("listitem").nth(1)).toContainText("Count the pickup as a breath.");

    // Delete asks first.
    await renamed.getByRole("button", { name: "Delete" }).click();
    await renamed.getByRole("button", { name: "Keep", exact: true }).click();
    await expect(renamed).toBeVisible();
    await renamed.getByRole("button", { name: "Delete" }).click();
    await renamed.getByRole("button", { name: "Delete for good" }).click();
    await expect(kept.getByRole("listitem")).toHaveCount(1);
    await expect(kept).toContainText("Deleted “Tiptoe”.");

    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});

test("what is kept is saved with the project and comes back when it is opened again", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/import");
    await page.setInputFiles("input[type=file]", minuet);
    await page.getByText("Open the piece").click();
    const kept = page.locator(".observations");
    const card = page.locator(".stream .explore");

    await selectPhrase(page);
    await openInsights(page);
    await expect(kept).not.toContainText("not saved as a project");
    await tryWay(page, "Passing notes");
    await page.locator(".stream").getByRole("button", { name: "Keep this variation" }).click();
    await expect(card.getByRole("status")).toContainText("Kept with the project.");
    // The note is saved when the field loses focus.
    await card.getByLabel("Add a note in your own words (optional)").fill("Airier.");
    await card.getByLabel("Add a note in your own words (optional)").blur();
    await expect(card).toContainText("Note saved.");
    await manageIdeas(page);
    await expect(kept.getByRole("listitem")).toContainText("Your note: Airier.");
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();

    // The library resumes the piece where it was left (0018 section 9): the card counts the idea, and Resume comes back to the phrase.
    await page.goto("/");
    await librarySessions(page);
    const piece = page.locator(".card.piece").filter({ has: page.getByRole("heading", { name: "Waltz sketch" }) });
    await expect(piece.locator(".tag.saved")).toHaveText("1 idea");
    await piece.getByRole("button", { name: "Resume Waltz sketch" }).click();
    await expect(page.locator(".selection-status")).toContainText("Selected phrase 1");
    await focusSelection(page);
    await openInsights(page);
    await manageIdeas(page);
    const item = kept.getByRole("listitem", { name: "Passing notes" });
    await expect(item).toContainText("Your note: Airier.");
    // Focus lifted the phrase: back to the page, then a tap on the paper puts the selection away.
    await page.locator(".bar").getByRole("button", { name: "Back to page" }).click();
    await clearByPage(page);
    await selectPhrase(page);
    await item.getByRole("button", { name: "Open" }).click();
    await expect(item).toContainText("You hear this");
    await expect(page.locator(".inspector-head .compare-switch")).toBeVisible();
    await page.locator(".pill").getByRole("button", { name: "Pause" }).click();
    expect(errors).toEqual([]);
});
