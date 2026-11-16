// The first run (decision record 0018, section 10): before the library, one screen asks once for a preset, three cards
// with a one-line description each, and the language; the choice shows in Settings and the screen never asks again.
import { expect, test } from "@playwright/test";
import { violations } from "./steps.mjs";

// A device that never chose a preset.
test.use({ storageState: { cookies: [], origins: [] } });

test("the first run asks once for a preset and the language, then opens the library", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Choose how to begin");
    await expect(page.getByRole("radio", { name: /^Practising/ })).toBeChecked();
    await expect(page.getByText("Just the music: the score, your ink and bookmarks", { exact: false })).toBeVisible();
    await expect(page.locator(".lib-head")).toHaveCount(0);
    expect(await violations(page)).toEqual([]);

    await page.getByRole("radio", { name: "Русский" }).click();
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Выберите, с чего начать");
    await page.getByText("Чистый лист", { exact: true }).click();
    await expect(page.getByRole("radio", { name: /^Чистый лист/ })).toBeChecked();
    expect(await violations(page)).toEqual([]);
    await page.getByRole("button", { name: "Продолжить" }).click();

    const head = page.locator(".lib-head");
    await expect(head.getByRole("button", { name: "Добавить пьесу" })).toBeVisible();
    await head.getByRole("button", { name: "Настройки" }).click();
    const sheet = page.getByRole("dialog", { name: "Настройки" });
    await expect(sheet.getByRole("radio", { name: "Чистый лист" })).toHaveAttribute("aria-checked", "true");
    await sheet.press("Escape");

    // Never asked again.
    await page.reload();
    await expect(head.getByRole("button", { name: "Добавить пьесу" })).toBeVisible();
    await expect(page.getByText("Выберите, с чего начать")).toHaveCount(0);
});
