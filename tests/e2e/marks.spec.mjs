// Margin notes, highlights and the layers of marks (decision record 0018, section 5): Note in the contextual menu writes
// the player's words about the bars, shown as a numbered marker that opens them again; Highlight in the menu's overflow
// lays a band in an ink behind the staves; the Contents list them under Notes; each layer can be hidden.
import { expect, test } from "@playwright/test";
import { closePopover, openFurElise, openMore, selectBar } from "./steps.mjs";

const card = page => page.locator(".note-card");
const status = page => page.locator(".marks-status");

async function menuMore(page) {
    const more = page.locator(".ctx").getByRole("button", { name: "More for the selection" });
    if ((await more.getAttribute("aria-expanded")) !== "true") {
        await more.click();
    }
    return page.locator(".ctx .menu-more");
}

test("a margin note is written from the menu, opened from its marker, changed and deleted", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "Note", exact: true }).click();
    const words = card(page).getByRole("textbox");
    await expect(words).toBeFocused();
    await words.fill("Lighter thumb on the E");
    await card(page).getByRole("button", { name: "Done" }).click();
    await expect(status(page)).toHaveText("Note written on bar 2");

    const marker = page.locator(".score .marker");
    await expect(marker).toHaveText("1");
    await expect(marker).toHaveAttribute("aria-label", "Note 1, bar 2: Lighter thumb on the E");

    await selectBar(page, 5);
    await marker.click();
    await expect(page.locator(".selection-status")).toContainText(/bar 2(?!\d)/);
    await expect(card(page).getByRole("textbox")).toHaveValue("Lighter thumb on the E");
    await card(page).getByRole("textbox").fill("Lighter thumb, then wait");
    await card(page).getByRole("button", { name: "Done" }).click();
    await expect(status(page)).toHaveText("Note changed");
    await expect(marker).toHaveAttribute("aria-label", /Lighter thumb, then wait/);

    await marker.click();
    await card(page).getByRole("button", { name: "Delete" }).click();
    await expect(marker).toHaveCount(0);
    await expect(status(page)).toHaveText("Note deleted");
    expect(errors).toEqual([]);
});

test("a highlight is laid in an ink from the menu's overflow and taken away again", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 3);
    await (await menuMore(page)).getByRole("button", { name: "Highlight in carmine" }).click();
    await expect(status(page)).toHaveText("Highlighted bar 3");
    await expect(page.locator(".score .hl.ink-carmine").first()).toBeVisible();

    const more = await menuMore(page);
    await expect(more.getByRole("button", { name: "Highlight in carmine" })).toHaveAttribute("aria-pressed", "true");
    // Another ink takes the place of the first.
    await more.getByRole("button", { name: "Highlight in amber" }).click();
    await expect(page.locator(".score .hl.ink-carmine")).toHaveCount(0);
    await expect(page.locator(".score .hl.ink-amber").first()).toBeVisible();

    await (await menuMore(page)).getByRole("button", { name: "Remove the highlight" }).click();
    await expect(page.locator(".score .hl")).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("the Contents' notes list the margin notes and the highlights, by colour, and go to them", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "Note", exact: true }).click();
    await card(page).getByRole("textbox").fill("Count the rest");
    await card(page).getByRole("button", { name: "Done" }).click();
    await selectBar(page, 4);
    await (await menuMore(page)).getByRole("button", { name: "Highlight in forest green" }).click();

    await page.locator(".bar").getByRole("button", { name: /open the contents/ }).click();
    const drawer = page.locator(".sheet.contents");
    await drawer.getByRole("button", { name: "Notes", exact: true }).click();
    await expect(drawer.locator("p.cat")).toHaveText(["Margin notes · 1", "Highlights · 1"]);
    await expect(drawer.getByRole("button", { name: /Count the rest/ })).toContainText("bar 2");

    await drawer.getByRole("button", { name: "forest green", exact: true }).click();
    await expect(drawer.getByRole("button", { name: "forest green", exact: true })).toHaveAttribute("aria-pressed", "true");
    await drawer.getByRole("button", { name: /Highlight, forest green/ }).click();
    await expect(page.locator(".selection-status")).toContainText(/bar 4(?!\d)/);
    expect(errors).toEqual([]);
});

test("the layers hide the notes, the bookmarks and the analysis, one by one or all at once", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "Note", exact: true }).click();
    await card(page).getByRole("textbox").fill("Pedal");
    await card(page).getByRole("button", { name: "Done" }).click();
    await expect(page.locator(".score .marker")).toBeVisible();

    const more = await openMore(page);
    await more.getByRole("button", { name: "Extra information" }).click();
    const layers = page.locator(".layers-popover");
    await layers.getByRole("switch", { name: "Notes and highlights" }).click();
    await expect(page.locator(".score .marker")).toBeHidden();
    await expect(page.locator(".score[role=figure]")).toHaveClass(/hide-notes/);

    await layers.getByRole("button", { name: "Hide all marks" }).click();
    await expect(page.locator(".score[role=figure]")).toHaveClass(/hide-ink/);
    await expect(page.locator(".score[role=figure]")).toHaveClass(/hide-bookmarks/);
    await expect(page.locator(".score button.bracket").first()).toBeHidden();
    await layers.getByRole("button", { name: "Show all marks" }).click();
    await expect(page.locator(".score .marker")).toBeVisible();
    await expect(layers.getByRole("switch", { name: "Analysis: phrase brackets and facts" })).toBeChecked();
    await closePopover(page);
    expect(errors).toEqual([]);
});

test("in Russian, the note card, the highlight and the layers speak Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "Заметка", exact: true }).click();
    await expect(card(page)).toContainText("Заметка: такт 2");
    await card(page).getByRole("textbox").fill("Тише");
    await card(page).getByRole("button", { name: "Готово" }).click();
    await expect(status(page)).toHaveText("Заметка записана: такт 2");

    await page.locator(".ctx").getByRole("button", { name: "Ещё для выделения" }).click();
    await expect(page.locator(".ctx .menu-more").getByRole("button", { name: "Выделить: охра" })).toBeVisible();
    expect(errors).toEqual([]);
});
