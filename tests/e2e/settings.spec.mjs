// The global Settings sheet (decision record 0018, section 10): over the library or the score, three presets and a
// switch for each element they set, kept on the device; a change made over the score shows on it at once.
import { expect, test } from "@playwright/test";
import { openFurElise, openMore, violations } from "./steps.mjs";

test("the Settings sheet opens over the library, and its presets and switches are kept on the device", async ({ page }) => {
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await expect(sheet).toBeFocused();
    await expect(sheet.getByRole("tab", { name: "Guidance" })).toHaveAttribute("aria-selected", "true");
    await expect(sheet.getByRole("radio", { name: "Practising" })).toHaveAttribute("aria-checked", "true");
    await expect(sheet.getByText("For daily work", { exact: false })).toBeVisible();
    expect(await violations(page)).toEqual([]);

    // A preset sets the switches; changing one of them makes the mix the player's own.
    await sheet.getByRole("radio", { name: "Clean" }).click();
    await expect(sheet.getByText("Just the music", { exact: false })).toBeVisible();
    await expect(sheet.getByRole("switch", { name: "Phrase brackets" })).not.toBeChecked();
    await expect(sheet.getByRole("switch", { name: "Pill appears with a selection" })).not.toBeChecked();
    await expect(sheet.getByRole("radio", { name: "1 s" })).toHaveAttribute("aria-checked", "true");
    await sheet.getByRole("switch", { name: "Phrase brackets" }).check();
    await expect(sheet.getByText("Your own mix of switches", { exact: false })).toBeVisible();
    await expect(sheet.getByRole("radio", { name: "Clean" })).toHaveAttribute("aria-checked", "false");

    // The sections: by click or with the arrow keys.
    await sheet.getByRole("tab", { name: "Marks and layers" }).click();
    await expect(sheet.getByRole("switch", { name: "Check flags" })).not.toBeChecked();
    await sheet.getByRole("tab", { name: "Marks and layers" }).press("ArrowDown");
    await expect(sheet.getByRole("tab", { name: "Piano roll" })).toHaveAttribute("aria-selected", "true");
    await expect(sheet.getByRole("tab", { name: "Piano roll" })).toBeFocused();
    await sheet.getByRole("tab", { name: "Appearance" }).click();
    await sheet.getByRole("radio", { name: "Night stand", exact: true }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    expect(await violations(page)).toEqual([]);
    await sheet.press("Escape");
    await expect(sheet).toHaveCount(0);

    // Kept on the device.
    await page.reload();
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    await expect(sheet.getByRole("switch", { name: "Phrase brackets" })).toBeChecked();
    await expect(sheet.getByRole("radio", { name: "1 s" })).toHaveAttribute("aria-checked", "true");
    await sheet.getByRole("tab", { name: "Appearance" }).click();
    await sheet.getByRole("radio", { name: "Русский" }).click();
    const russian = page.getByRole("dialog", { name: "Настройки" });
    await expect(russian.getByRole("tab", { name: "Оформление" })).toBeVisible();
    expect(await violations(page)).toEqual([]);
    await russian.getByRole("button", { name: "Закрыть настройки" }).click();
    await expect(page.locator(".lib-head").getByRole("button", { name: "Добавить пьесу" })).toBeVisible();
});

test("over the score, the sheet's changes show on the score at once", async ({ page }) => {
    const errors = await openFurElise(page);
    const brackets = page.locator(".score button.bracket[aria-label]");
    await expect(brackets.first()).toBeVisible();

    const more = await openMore(page);
    await more.getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("radio", { name: "Clean" }).click();
    await expect(brackets).toHaveCount(0);

    await sheet.getByRole("radio", { name: "Practising" }).click();
    await expect(brackets.first()).toBeVisible();

    // A piece without a level of its own opens in the one the sheet sets.
    await sheet.getByRole("tab", { name: "Score" }).click();
    await sheet.getByRole("radio", { name: "Sentences" }).click();
    await expect(brackets.first()).toHaveAttribute("aria-label", /^Sentence 1/);
    expect(await violations(page)).toEqual([]);
    await sheet.getByRole("button", { name: "Close Settings" }).click();
    await expect(sheet).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("Even bars in Score lays the score out again, with bars as wide as their notes need when off (testing item 35)", async ({ page }) => {
    const errors = await openFurElise(page);
    // Where the barlines of the first page fall: they move when the bars are spaced otherwise.
    const barlines = () => page.locator("div.score > .score-page").first().evaluate((first) =>
        [...first.querySelectorAll("g.measure")].slice(0, 12).map((measure) => Math.round(measure.getBoundingClientRect().width)).join(","));
    const even = await barlines();

    const more = await openMore(page);
    await more.getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Score" }).click();
    const evenBars = sheet.getByRole("switch", { name: "Even bars" });
    await expect(evenBars).toBeChecked();
    await evenBars.uncheck();
    await expect.poll(barlines).not.toBe(even);
    await evenBars.check();
    await expect.poll(barlines).toBe(even);
    expect(errors).toEqual([]);
});

test("in a section taller than the sheet, each row keeps its height and its hint never runs over the next row", async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Settings" }).click();
    const sheet = page.getByRole("dialog", { name: "Settings" });
    await sheet.getByRole("tab", { name: "Experimental" }).click();
    await expect(sheet.getByRole("switch").first()).toBeVisible();
    const overlaps = await page.evaluate(() => {
        const rows = [...document.querySelectorAll(".settings-sheet .settings-pane > *")].filter(e => e.offsetParent);
        const bottom = row => Math.max(row.getBoundingClientRect().bottom,
            ...[...row.querySelectorAll(".row-hint")].map(hint => hint.getBoundingClientRect().bottom));
        return rows.slice(0, -1).flatMap((row, i) => bottom(row) > rows[i + 1].getBoundingClientRect().top + 0.5 ? [i] : []);
    });
    expect(overlaps).toEqual([]);
});
