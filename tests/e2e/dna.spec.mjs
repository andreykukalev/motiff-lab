// Piece DNA (decision record 0018, section 11): the map of the whole piece over the score, from the score's overflow and
// from the library card; its lanes say what is not analysed yet, a mark plays its passage and a second tap opens the
// score there; leaving returns to the score as it was.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { openFurElise, openMore, selectBar, violations } from "./steps.mjs";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

test("from the overflow, the map reads form, key, harmony, density and ideas, and a mark leads back into the score", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 1);
    const more = await openMore(page);
    await more.getByRole("button", { name: "Piece DNA" }).click();

    const dna = page.getByRole("dialog", { name: "Für Elise" });
    await expect(dna).toBeFocused();
    await expect(dna.locator(".dna-bar")).toContainText("Piece DNA");

    // Read top to bottom (testing item 22): the form, the key, the harmony, the density, new material, then the ideas.
    const rows = await dna.locator(".dna-row").evaluateAll(all => all.map(row => row.className.replace("dna-row", "").trim()));
    expect(rows.slice(0, 6)).toEqual(["form", "axis", "key", "harmony", "density", "new"]);
    expect(rows.slice(6).every(row => row === "idea")).toBe(true);
    await expect(dna.locator(".dna-row.form")).toContainText("inferred");
    await expect.poll(() => dna.locator(".dna-sec").count()).toBeGreaterThan(1);
    await expect(dna.locator(".dna-sec").first()).toHaveText("A");
    await expect.poll(() => dna.locator(".dna-arc").count()).toBeGreaterThan(0);
    await expect.poll(() => dna.locator(".dna-row.harmony .chord").count()).toBeGreaterThan(1);
    await expect.poll(() => dna.locator(".dna-row.density .lvl").count()).toBeGreaterThan(1);

    // The selection is the first idea, then the ideas found in the piece, a card each.
    const lane = dna.locator(".dna-row.idea");
    await expect(lane.first().locator(".lane-name")).toContainText("Your selection");
    await expect.poll(() => lane.count()).toBeGreaterThan(1);
    await expect(lane.nth(1).locator(".lane-name")).toContainText(/^Idea 1 · /);
    await expect(dna.locator(".dna-card")).toHaveCount(await lane.count());
    await expect(dna.locator(".dna-card").nth(1)).toContainText(/Heard \d+ times/);
    const marks = lane.first().getByRole("button");
    await expect.poll(() => marks.count()).toBeGreaterThan(1);
    await expect(dna.locator(".key-block").first()).toBeVisible();

    // Register and dynamics are folded until asked for.
    await expect(dna.locator(".dna-row.register")).toHaveCount(0);
    await dna.getByRole("button", { name: "More lanes: register, dynamics" }).click();
    await expect(dna.locator(".dna-row.register .rng").first()).toBeVisible();
    await expect(dna.locator(".dna-row.dynamics")).toBeVisible();

    // The map explains itself (testing item 30): a line on what it shows, a line under each lane, the harmony in rows that
    // turn on and off (steps of the key off at first), the closes in words that say what they are, and a chord's circle.
    await expect(dna.locator(".dna-summary")).toContainText(/^Form A A′ B A″ · A minor · \d+ full close/);
    await expect(dna.locator(".dna-row.harmony .why")).toContainText("T rest, S motion, D tension");
    await expect.poll(() => dna.locator(".dna-row.harmony .fn.t").count()).toBeGreaterThan(1);
    await expect(dna.locator(".dna-row.harmony .degree")).toHaveCount(0);
    await dna.getByRole("button", { name: "Steps", exact: true }).click();
    await expect(dna.locator(".dna-row.harmony .degree").first()).toHaveText("i");
    await dna.locator(".dna-row.harmony .cad").first().click();
    await expect(dna.locator(".dna-pop")).toContainText(/^Bar \d+ · \S+\. A (half|full) close: the phrase's melody/);
    await dna.locator(".dna-row.harmony .chord").first().click();
    const circle = dna.getByRole("dialog", { name: "Circle of fifths · section A" });
    await expect(circle.locator(".key-circle.functions .sector.z-dominant").first()).toBeAttached();
    await expect(circle.locator(".role")).toHaveText("Am: i in A minor, the tonic.");
    expect(await violations(page)).toEqual([]);
    await circle.getByRole("button", { name: "Close" }).click();
    await expect(circle).toHaveCount(0);

    // Add Structure bookmarks writes the form's sections into Contents.
    await dna.getByRole("button", { name: "Add Structure bookmarks" }).click();
    await expect(dna.locator(".dna-actions [role=status]")).toHaveText(/^\d+ Structure bookmarks? added$/);

    // The second mark plays; a second tap opens the score with it selected.
    const second = marks.nth(1);
    const name = await second.getAttribute("aria-label");
    await second.click();
    await expect(second).toHaveAttribute("aria-pressed", "true");
    await second.click();
    await expect(dna).toHaveCount(0);
    const bars = name.match(/bars? (\d+)/)[1];
    await expect(page.locator(".selection-status")).toContainText(new RegExp(`bars? ${bars}(?!\\d)`, "i"));
    expect(errors).toEqual([]);
});

test("from the library card, in Russian, with no selection: the lanes are in Russian, and Escape returns", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog", { name: "Add a piece" }).getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(minuet);
    await expect(page.locator(".bar .ttl")).toHaveText("Waltz sketch");
    await page.evaluate(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await page.goto("/");

    await page.getByRole("button", { name: "Ещё: Waltz sketch" }).click();
    await page.getByRole("button", { name: "ДНК пьесы" }).click();
    const dna = page.getByRole("dialog", { name: "Waltz sketch" });
    await expect(dna.locator(".dna-row.form")).toContainText("выведено");
    await expect(dna.locator(".dna-row.harmony .lane-name")).toHaveText("Гармония");
    await expect(dna.locator(".dna-row.density .lane-name")).toHaveText("Плотность");
    await expect(dna.locator(".dna-bar .counts")).toContainText("такт");
    expect(await violations(page)).toEqual([]);
    await expect(page).not.toHaveURL(/view=dna/);

    await dna.press("Escape");
    await expect(dna).toHaveCount(0);
    await expect(page.locator(".bar .ttl")).toHaveText("Waltz sketch");
});
