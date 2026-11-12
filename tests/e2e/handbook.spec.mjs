// «Справочник», an experiment (version 0.5, Settings · Experimental, off by default; Andrei approved the mockup on
// 2026-11-12): Circle opens the Reference as sections. Keys: the circle of fifths shaded by accidentals, with the chosen
// key's card. Chords and scales: what sounds, a chord or a scale from a note, apart from how it sounds, the spacing, the
// inversion and the way it is played, in notes and on a keyboard, played once or in a loop. Nothing goes into the piece.
import { expect, test } from "@playwright/test";
import { openFurElise, violations } from "./steps.mjs";

async function turnOn(page) {
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    const experiment = sheet.getByRole("switch", { name: "Reference: chords, scales and the harmony explorer" });
    await expect(experiment).not.toBeChecked();
    await experiment.check();
    await sheet.press("Escape");
}

test("with the experiment on, Circle opens the Reference: a key on the shaded circle, then a chord and a scale", async ({ page }) => {
    await page.goto("/");
    await turnOn(page);

    await page.locator(".lib-head").getByRole("button", { name: "Circle", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Reference" });
    await expect(sheet.getByRole("tab", { name: "Keys" })).toHaveAttribute("aria-selected", "true");
    await expect(sheet.locator(".ref-circle.shaded .ref-key.acc-6")).toHaveCount(2);
    await expect(sheet.locator(".ref-card .ref-name")).toHaveText("C major");

    await sheet.getByRole("button", { name: "E minor, 1 sharp", exact: true }).click();
    await expect(sheet.locator(".ref-card .ref-name")).toHaveText("E minor");
    await expect(sheet.locator(".ref-key.minor.on")).toHaveCount(1);
    expect(await violations(page)).toEqual([]);

    // A main chord of the card becomes the chord of «Chords and scales».
    await sheet.locator(".ref-card .ref-chords .ref-sound.fn-dominant").click();
    await sheet.getByRole("tab", { name: "Chords and scales" }).click();
    await expect(sheet.locator(".ref-result-symbol")).toHaveText("B");
    await expect(sheet.getByRole("img", { name: "On the keyboard: B D♯ F♯" })).toBeVisible();

    // G7 in its first inversion, open: the name, the bass and the notes follow.
    await sheet.getByRole("group", { name: "Root" }).getByRole("button", { name: "G", exact: true }).click();
    await sheet.getByRole("button", { name: "G7, dominant seventh" }).click();
    await sheet.getByRole("radiogroup", { name: "Inversion" }).getByRole("radio", { name: "6/5" }).click();
    await sheet.getByRole("radiogroup", { name: "Spacing" }).getByRole("radio", { name: "Open" }).click();
    await expect(sheet.locator(".ref-result-symbol")).toHaveText("G7");
    await expect(sheet.locator(".ref-result-kind")).toContainText("bass B");
    await expect(sheet.locator(".ref-score-page > svg")).toBeVisible();
    await expect(sheet.locator(".ref-score-page .note")).toHaveCount(4 + 7);
    expect(await violations(page)).toEqual([]);

    // Played, it shows Pause; paused, Play again.
    await sheet.getByRole("button", { name: "Play", exact: true }).click();
    await expect(sheet.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await sheet.getByRole("button", { name: "Pause", exact: true }).click();
    await expect(sheet.getByRole("button", { name: "Play", exact: true })).toBeVisible();

    // D harmonic minor: one flat in the signature, the leading note sharp.
    await sheet.getByRole("radio", { name: "Scale", exact: true }).click();
    await sheet.getByRole("group", { name: "Root" }).getByRole("button", { name: "D", exact: true }).click();
    await sheet.getByRole("button", { name: "harmonic minor", exact: true }).click();
    await expect(sheet.locator(".ref-result-name")).toHaveText("D harmonic minor");
    await expect(sheet.locator(".ref-result-head")).toContainText("C♯");
    expect(await violations(page)).toEqual([]);

    await sheet.press("Escape");
    await expect(sheet).toHaveCount(0);
});

test("in a piece the Reference marks the piece's key and plays over the piece", async ({ page }) => {
    await page.goto("/");
    await turnOn(page);
    await openFurElise(page);

    await page.locator(".bar").getByRole("button", { name: "Circle", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Reference" });
    await expect(sheet.locator(".ref-key.piece")).toHaveCount(1);
    await expect(sheet.locator(".ref-card .ref-name")).toHaveText("A minor");
    await expect(sheet.locator(".ref-legend")).toContainText("A minor");

    await sheet.getByRole("tab", { name: "Chords and scales" }).click();
    await sheet.getByRole("button", { name: "Play", exact: true }).click();
    await expect(sheet.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
    await sheet.press("Escape");
    await expect(sheet).toHaveCount(0);
    await expect(page.locator(".bar .ttl")).toHaveText("Für Elise");
});
