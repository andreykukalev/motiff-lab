// The Reference, an experiment (Settings · Experimental, off by default; Andrei, 2026-11-11): Circle in the library and in
// the score's bar opens the circle of fifths. The key signatures come first: five sharps mark B major and G♯ minor on the
// circle and open B major's card, with its signature, scale and the three main chords, each heard and shown on a small
// keyboard. In a piece, the piece's key is marked on the circle.
import { expect, test } from "@playwright/test";
import { openFurElise, violations } from "./steps.mjs";

async function turnOn(page) {
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    const experiment = sheet.getByRole("switch", { name: "Reference: circle of fifths" });
    await expect(experiment).not.toBeChecked();
    await experiment.check();
    await sheet.press("Escape");
}

test("with the experiment on, Circle in the library looks a key up by its signature and plays its chords", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator(".lib-head").getByRole("button", { name: "Circle", exact: true })).toHaveCount(0);
    await turnOn(page);

    await page.locator(".lib-head").getByRole("button", { name: "Circle", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Circle of fifths" });
    await expect(sheet.getByRole("button", { name: "5 sharps", exact: true })).toBeVisible();
    // No key to go by in the library: C major, only so the circle opens filled (#533 7), and no word on a piece.
    await expect(sheet.locator(".ref-card .ref-name")).toHaveText("C major");
    await expect(sheet.locator(".ref-key-unknown")).toHaveCount(0);

    await sheet.getByRole("button", { name: "5 sharps", exact: true }).click();
    await expect(sheet.getByRole("button", { name: "5 sharps", exact: true })).toHaveAttribute("aria-pressed", "true");
    await expect(sheet.locator(".ref-key.major.on")).toHaveCount(1);
    await expect(sheet.locator(".ref-key.minor.pair")).toHaveCount(1);
    const card = sheet.locator(".ref-card");
    await expect(card.locator(".ref-name")).toHaveText("B major");
    await expect(card).toContainText("G♯ minor");
    await expect(card).toContainText("C♭ major");
    await expect(card).toContainText("5 sharps: F♯, C♯, G♯, D♯, A♯");
    await expect(card).toContainText("B C♯ D♯ E F♯ G♯ A♯");
    await expect(card.locator(".ref-chords .ref-sound")).toHaveCount(3);
    await expect(card.locator(".ref-chords")).toContainText("F♯ A♯ C♯");
    expect(await violations(page)).toEqual([]);

    // A chord plays and shows its three keys.
    const dominant = card.locator(".ref-chords .ref-sound", { hasText: "dominant" }).last();
    await dominant.click();
    await expect(dominant).toHaveAttribute("aria-pressed", "true");
    await expect(card.locator(".ref-keyboard .on")).toHaveCount(3);
    await expect(card.getByRole("img", { name: "On the keyboard: F♯ A♯ C♯" })).toBeVisible();

    // A minor key on the circle: natural and harmonic minor, and V from the harmonic minor.
    await sheet.getByRole("button", { name: "G♯ minor, 5 sharps", exact: true }).click();
    await expect(card.locator(".ref-name")).toHaveText("G♯ minor");
    await expect(card).toContainText("G♯ A♯ B C♯ D♯ E F𝄪");
    await expect(card.locator(".ref-chords")).toContainText("D♯ F𝄪 A♯");

    await sheet.press("Escape");
    await expect(sheet).toHaveCount(0);
});

test("Circle in the score's bar marks the piece's key", async ({ page }) => {
    await page.goto("/");
    await turnOn(page);
    await openFurElise(page);

    await page.locator(".bar").getByRole("button", { name: "Circle", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Circle of fifths" });
    await expect(sheet).toContainText("Key of the piece");
    await expect(sheet.locator(".ref-key.piece")).toHaveCount(1);
    await expect(sheet.locator(".ref-card .ref-name")).toHaveText("A minor");
    expect(await violations(page)).toEqual([]);
});
