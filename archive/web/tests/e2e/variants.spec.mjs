// «Что если» variants of a phrase (an experiment, version 0.6; the AI companion study, sections 2.4 and 4.6): in phrase
// focus the panel under the card makes variants with the app's own operations, no AI; each is the plum ossia over the
// phrase, compared Original / the variant / One after the other, kept as a chip in the card's header, made from another
// variant, and taken into work: a carry of the assembly journal in place of the phrase, made notes at once, with Undo.
// The piece is Für Elise's score added to the library, so it has a project to keep variants and the journal in.
// Runs in desktop Chromium only (playwright.config.mjs), with the WCAG checks on the new panel.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { focusSelection, selectPhrase, violations } from "./steps.mjs";

const score = fileURLToPath(new URL("../../../../tests/backend/Unit/MotiffLab.MusicCore.Tests/Experiments/Fixtures/fur-elise-original.musicxml", import.meta.url));

/** The score added to the library and opened, its first phrase lifted into focus. */
async function focusFirstPhrase(page) {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
    await selectPhrase(page);
    await focusSelection(page);
}

test("without the experiment phrase focus has no What if panel", async ({ page }) => {
    await focusFirstPhrase(page);
    await expect(page.locator(".variants-panel")).toHaveCount(0);
});

test("a variant is made with no AI, heard over the phrase, kept, built on, taken into work and undone", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.assistantVariants", "on"));
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await focusFirstPhrase(page);
    const panel = page.locator(".variants-panel");
    const card = page.locator(".focus-card");
    await expect(panel).toContainText("What if…");
    await expect(panel).toContainText("with no AI");

    // A chip makes Variant 1: the draft is the plum ossia over the phrase and a dashed chip in the card's header.
    await panel.getByRole("button", { name: "Falling arpeggio" }).click();
    const variant = panel.locator(".variant-card");
    await expect(variant.locator(".overline").first()).toHaveText("Variant 1");
    await expect(variant.locator(".variant-title")).toHaveText("Falling arpeggio");
    await expect(variant.locator(".variant-changes")).not.toBeEmpty();
    await expect(variant.locator(".variant-checks")).toContainText("Melody");
    await expect(card.locator(".ossia-frame")).toContainText("Variant 1");
    await expect(card.locator("g.note.version-added").first()).toBeAttached();
    await expect(card.locator(".var-chip.draft")).toContainText("Variant 1");
    expect(await violations(page)).toEqual([]);

    // Original / Variant 1 / One after the other: the switch shows what is heard.
    const compare = variant.locator(".compare-switch");
    await compare.getByRole("button", { name: "Original" }).click();
    await expect(compare.getByRole("button", { name: "Original" })).toHaveAttribute("aria-pressed", "true");
    await compare.getByRole("button", { name: "Variant 1" }).click();
    await expect(compare.getByRole("button", { name: "Variant 1" })).toHaveAttribute("aria-pressed", "true");

    // Keep: the chip in the header is a kept variant now.
    await variant.getByRole("button", { name: "Keep", exact: true }).click();
    await expect(panel.locator(".variant-status")).toContainText("Variant 1 is kept");
    await expect(card.locator(".var-chip:not(.draft)")).toHaveText(["Variant 1"]);

    // Built on Variant 1: Variant 2 carries its steps and names it.
    await expect(panel.getByRole("radio", { name: "Variant 1" })).toHaveAttribute("aria-checked", "true");
    await panel.getByRole("button", { name: "Pedal bass" }).click();
    await expect(variant.locator(".overline").first()).toHaveText("Variant 2 · from Variant 1");
    await expect(variant.locator(".variant-title")).toHaveText("Falling arpeggio › Pedal bass");

    // Take into work: kept, carried in place of the phrase and made notes; the toast says the original is not changed.
    await variant.getByRole("button", { name: "Take into work" }).click();
    const toast = page.locator(".merge-toast").filter({ hasText: "working notes" });
    await expect(toast).toContainText("Variant 2 is now in the working notes. The original is not changed.");
    await expect(card).toHaveCount(0);

    // The journal holds the carry and the notes before the assembly are kept.
    await selectPhrase(page);
    await focusSelection(page);
    await expect(page.locator(".focus-card .var-chip.in-work")).toContainText("Variant 2");
    await page.locator(".bar").getByRole("button", { name: "Back to page" }).click();

    // Undo puts the piece back as it was before.
    await toast.getByRole("button", { name: "Undo" }).click();
    await expect(toast).toHaveCount(0);
    await selectPhrase(page);
    await focusSelection(page);
    await expect(page.locator(".focus-card .var-chip.in-work")).toHaveCount(0);
    await expect(page.locator(".focus-card .var-chip")).toHaveText(["Variant 1", "Variant 2"]);
    expect(errors).toEqual([]);
});