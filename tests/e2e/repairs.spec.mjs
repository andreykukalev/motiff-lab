// Repairs for the bars to check (an experiment, version 0.6; the AI companion study, section 2.2): under a flagged bar in
// Check, Repair offers the ways music-core works out from the flag's reason, with no AI, as a draft engraved over the bar;
// it is heard as read or corrected, accepted into the corrections as one Undo step, or dismissed. The scan server is played
// by the test with what homr read from a page of Für Elise, whose bar 27 runs a 32nd long.
// Runs in desktop Chromium only (playwright.config.mjs), with the WCAG checks on the new card.
import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { violations } from "./steps.mjs";

const scanned = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Scan/Fixtures/homr-fur-elise-page1.musicxml", import.meta.url));
const photo = fileURLToPath(new URL("../../src/MotiffLab.Web/wwwroot/icon-192.png", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

async function openScannedPiece(page) {
    const musicXml = await readFile(scanned);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Page-Report" };
    await page.route(`${server}/health`, route => route.fulfill({ json: { engine: "homr", version: "0.7.0", maxPages: 8 }, headers: cors }));
    await page.route(`${server}/recognise`, route => route.fulfill({
        body: musicXml,
        contentType: "application/vnd.recordare.musicxml+xml",
        headers: { ...cors, "X-Engine": "homr 0.7.0", "X-Pages": "1" },
    }));
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();
    await page.goto("/import/scan");
    await page.getByLabel("Choose a PDF or photo of printed music to scan").setInputFiles(photo);
    await page.getByText("Open the piece").click();
    const drawer = page.locator(".sheet.check-sheet");
    await expect(drawer.getByRole("heading", { level: 2 })).toHaveText("7 bars to review");
    return drawer;
}

test("without the experiment the Check drawer has no Repair", async ({ page }) => {
    const drawer = await openScannedPiece(page);
    await expect(drawer.locator(".check-entry").first().getByRole("button", { name: "Fix notes" })).toBeVisible();
    await expect(drawer.getByRole("button", { name: "Repair", exact: true })).toHaveCount(0);
});

test("a flagged bar's repair is shown over the bar, heard, accepted as corrections and undone in one step", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.assistantRepairs", "on"));
    const drawer = await openScannedPiece(page);
    const bar = drawer.locator(".check-entry").filter({ hasText: "Bar 27" });
    await bar.getByRole("button", { name: "Repair", exact: true }).click();

    // The engine's way for a bar a 32nd too long, with the bar engraved and the correction above it in plum.
    const draft = bar.locator(".repair-draft");
    await expect(draft).toContainText("Proposal · correction · bar 27");
    await expect(draft.getByRole("radio", { name: "Shorten F5 (27.R.2) by 1/32 and move what follows earlier" })).toBeChecked();
    await expect(draft).toContainText("with no AI");
    await expect(draft.locator(".repair-card .ossia-frame")).toContainText("Correction");
    await expect(draft.locator(".repair-card g.note.version-added").first()).toBeAttached();
    await expect(draft.locator(".repair-changes")).not.toBeEmpty();
    // Bar 27's grace notes have no repair, and say why.
    await expect(draft.locator(".repair-none")).toContainText("Grace notes");
    expect(await violations(page)).toEqual([]);

    // As read / Correction / One after the other: the switch shows what is heard.
    const corrected = draft.locator(".compare-switch").getByRole("button", { name: "Correction" });
    await corrected.click();
    await expect(corrected).toHaveAttribute("aria-pressed", "true");
    const asRead = draft.locator(".compare-switch").getByRole("button", { name: "As read" });
    await asRead.click();
    await expect(asRead).toHaveAttribute("aria-pressed", "true");
    await expect(corrected).toHaveAttribute("aria-pressed", "false");

    // Accept: the draft becomes the player's corrections; the flag stays to resolve.
    await draft.getByRole("button", { name: "Accept" }).click();
    await expect(bar.locator(".repair-draft")).toHaveCount(0);
    await expect(bar.locator(".repair-accepted")).toContainText("Bar 27 corrected");
    await expect(drawer.getByRole("heading", { level: 2 })).toHaveText("7 bars to review");
    await bar.getByRole("button", { name: "Fix notes" }).click();
    await expect(bar.locator(".check-fix")).toContainText(/[1-9]\d* corrections? in the edit log/);
    expect(await violations(page)).toEqual([]);

    // One Undo takes the whole repair back.
    await bar.locator(".repair-accepted").getByRole("button", { name: "Undo" }).click();
    await expect(bar.locator(".repair-accepted")).toHaveCount(0);
    await expect(bar.locator(".check-fix")).toContainText("0 corrections in the edit log");
});

test("a bar with no safe repair says why, and Close puts the proposal away", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:experimental.assistantRepairs", "on"));
    const drawer = await openScannedPiece(page);
    const bar = drawer.locator(".check-entry").filter({ hasText: "Bar 16" });
    await bar.getByRole("button", { name: "Repair", exact: true }).click();
    // Bar 16 runs long in rests only: no way is safe, and the drawer says why.
    await expect(bar.locator(".repair-draft")).toContainText("in rests");
    await expect(bar.locator(".repair-draft").getByRole("radio")).toHaveCount(0);
    await bar.locator(".repair-draft").getByRole("button", { name: "Close" }).click();
    await expect(bar.locator(".repair-draft")).toHaveCount(0);
});
