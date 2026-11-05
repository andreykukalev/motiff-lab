// The labelled piano roll (0018 section 4; Andrei, 2026-11-04): a keyboard down the left with every row's name and octave,
// a name on every note spelled for the key as the score spells it, in letters (C4) or Russian syllables (до¹), and the roll
// and the score pointing at the same note both ways.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

/** Taps a notehead on the score, as a finger would. */
async function tapNotehead(page, index) {
    const box = await page.locator(".score g.note g.notehead").nth(index).boundingBox();
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
}

function rollStatus(page) {
    return page.locator(".drawer p.meta[aria-live]");
}

test("a note tapped on the score is chosen on the roll with its spelled name, and the roll's choice shows on the score", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);

    // Für Elise's pickup: E5, then D♯5, spelled with a sharp as the score has it in A minor.
    await tapNotehead(page, 1);
    await expect(rollStatus(page)).toContainText(/^Selected: D♯5, bar 0/);
    await expect(page.locator(".score g.note.pointed")).toHaveCount(1);
    // The phrase selected stays selected.
    await expect(page.locator(".selection-status")).toContainText("bar 2");

    // Choosing the next note on the roll moves the score's pointer with it.
    await page.locator(".roll canvas.roll-notes").focus();
    await page.keyboard.press("ArrowRight");
    await expect(rollStatus(page)).toContainText(/^Selected: E5, bar 1/);
    await expect(page.locator(".score g.note.pointed")).toHaveCount(1);
    expect(errors).toEqual([]);
});

test("in Russian the roll names notes in syllables with the octave marked", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await tapNotehead(page, 1);
    await expect(rollStatus(page)).toContainText(/ре♯², такт 0/);
    expect(errors).toEqual([]);
});

test("the naming setting keeps letters in Russian", async ({ page }) => {
    await page.addInitScript(() => {
        localStorage.setItem("motiff-lab:ml-language", "ru");
        localStorage.setItem("motiff-lab:notes.naming", "letters");
    });
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await tapNotehead(page, 1);
    await expect(rollStatus(page)).toContainText(/D♯5, такт 0/);
    expect(errors).toEqual([]);
});
