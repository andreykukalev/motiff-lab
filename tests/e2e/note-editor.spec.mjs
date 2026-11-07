// The note editor on the roll (testing item 15): Edit notes on the roll's head; Add puts a note in where the player
// presses, Undo steps back, Cancel puts the notes back, and nothing is kept until Save.
import { expect, test } from "@playwright/test";
import { openFurElise, openRoll } from "./steps.mjs";

const GUTTER = 62;
const HEADER = 24;

test("Edit notes adds a note on the roll, undoes it, cancels and saves", async ({ page }) => {
    const errors = await openFurElise(page);
    await openRoll(page);
    const notes = page.locator(".score g.note");
    await expect(notes.first()).toBeVisible();
    const before = await notes.count();

    await page.locator(".roll-head").getByRole("button", { name: "Edit notes" }).click();
    const head = page.locator(".roll-head.editing");
    await expect(head).toContainText("Editing notes");
    const save = head.getByRole("button", { name: "Save" });
    const undo = head.getByRole("button", { name: "Undo the last edit" });
    await expect(save).toBeDisabled();
    await expect(undo).toBeDisabled();

    // Add, then a press high above the notes of bar 1: one note more, sounding and engraved at once.
    await head.getByRole("radio", { name: "Add" }).click();
    await expect(head.getByRole("radio", { name: "Add" })).toHaveAttribute("aria-checked", "true");
    const roll = page.locator(".roll .roll-notes");
    const box = await roll.boundingBox();
    const addAt = async () => page.mouse.click(box.x + GUTTER + 48 * 1.2, box.y + HEADER + 3);
    await addAt();
    await expect(notes).toHaveCount(before + 1);
    await expect(save).toBeEnabled();
    await expect(undo).toBeEnabled();

    // The note added is picked: the arrows move it, Delete takes it out again.
    await roll.focus();
    await page.keyboard.press("ArrowDown");
    await expect(notes).toHaveCount(before + 1);
    await page.keyboard.press("Delete");
    await expect(notes).toHaveCount(before);
    await expect(save).toBeEnabled();
    await undo.click();
    await expect(notes).toHaveCount(before + 1);
    await undo.click();
    await undo.click();
    await expect(notes).toHaveCount(before);
    await expect(save).toBeDisabled();
    await expect(undo).toBeDisabled();

    // Cancel puts the notes back as they were.
    await addAt();
    await expect(notes).toHaveCount(before + 1);
    await head.getByRole("button", { name: "Cancel" }).click();
    await expect(notes).toHaveCount(before);
    await expect(page.locator(".roll-head").getByRole("button", { name: "Edit notes" })).toBeVisible();

    // Saved, the note stays.
    await page.locator(".roll-head").getByRole("button", { name: "Edit notes" }).click();
    await page.locator(".roll-head.editing").getByRole("radio", { name: "Add" }).click();
    await addAt();
    await expect(notes).toHaveCount(before + 1);
    await page.locator(".roll-head.editing").getByRole("button", { name: "Save" }).click();
    await expect(page.locator(".roll-head.editing")).toHaveCount(0);
    await expect(notes).toHaveCount(before + 1);
    expect(errors).toEqual([]);
});
