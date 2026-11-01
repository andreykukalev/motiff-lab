// A reload keeps the open piece (S1-P1): the workspace's address names the project, which is saved on the
// device, whether the file came through the import screen or the workspace's own Open file. The address names
// the session too (0012), so the work has a context to come back to from the project page.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));
const title = page => page.locator(".app-title strong");

test("a piece opened from the import screen is open again after a reload", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/import");
    await page.setInputFiles("input[type=file]", minuet);
    await page.getByText("Open the piece").click();
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+&resume=r_\d{4}$/);
    await expect(title(page)).toHaveText("importer-grand-staff.musicxml");
    const phrases = page.locator(".phrases button.phrase");
    await phrases.nth(0).click();
    await expect(page.locator(".inspector .selection-text")).toContainText("Selected phrase 1");

    await page.reload();
    await expect(title(page)).toHaveText("importer-grand-staff.musicxml", { timeout: 30_000 });
    await expect(phrases.first()).toBeVisible();
    // The selection is saved as the project's motif, so it comes back too.
    await expect(page.locator(".inspector .selection-text")).toContainText("Selected phrase 1");
    expect(errors).toEqual([]);
});

test("a file opened in the workspace is saved as a project and survives a reload", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/playback");
    const open = page.locator(".app-actions input[type=file]");
    await expect(open).toBeEnabled({ timeout: 30_000 });
    await open.setInputFiles(minuet);
    await expect(title(page)).toHaveText("importer-grand-staff.musicxml");
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+&resume=r_\d{4}$/);

    await page.reload();
    await expect(title(page)).toHaveText("importer-grand-staff.musicxml", { timeout: 30_000 });

    await page.goto("/");
    await expect(page.getByRole("link", { name: "importer-grand-staff.musicxml" })).toBeVisible();
    expect(errors).toEqual([]);
});

test("an address naming a project that is gone says so", async ({ page }) => {
    await page.goto("/playback?project=p_missing");
    await expect(page.getByText("This project is no longer saved in this browser.")).toBeVisible({ timeout: 30_000 });
});
