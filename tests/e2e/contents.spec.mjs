// Bookmarks and the Contents drawer (decision record 0018, section 5): Bookmark in the contextual menu, or B, opens a card
// over the selection with the title pre-filled and the category chips; the bookmark is a bracket in the category's ink on
// the score, and the drawer, opened from the piece's title, lists it by category to go to, focus on, change or delete. The
// detected form can be added as Structure bookmarks, and the Pages tab shows the pages with their phrases.
import { expect, test } from "@playwright/test";
import { openFurElise, selectBar } from "./steps.mjs";

const card = page => page.locator(".bookmark-card");
const drawer = page => page.locator(".sheet.contents");

test("a bookmark made from the menu is drawn on the score and listed in the Contents", async ({ page }) => {
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "Bookmark" }).click();
    await expect(card(page).getByRole("textbox", { name: "Title" })).toHaveValue(/bar/i);
    await card(page).getByRole("textbox", { name: "Title" }).fill("The leap");
    await card(page).getByRole("radio", { name: "Structure" }).click();
    await expect(card(page).getByRole("radio", { name: "Structure" })).toHaveAttribute("aria-checked", "true");
    await card(page).getByRole("button", { name: "Done" }).click();

    await expect(card(page)).toHaveCount(0);
    await expect(page.locator(".marks-status")).toHaveText("Bookmarked: The leap");
    await expect(page.locator(".score .bmb.ink-graphite").first()).toHaveAttribute("aria-label", "Bookmark: The leap, bar 2");
    await expect(page.locator(".score .bmb .w").first()).toHaveText("The leap");

    await page.locator(".bar").getByRole("button", { name: /open the contents/ }).click();
    await expect(drawer(page)).toBeVisible();
    await expect(drawer(page).locator("button.cat")).toHaveText("Structure · 1");
    const entry = drawer(page).getByRole("button", { name: /^The leap/ });
    await expect(entry).toContainText("bar 2");

    // Tapping the entry selects the passage; tapping it again lifts it into focus.
    await selectBar(page, 5);
    await entry.click();
    await expect(page.locator(".selection-status")).toContainText(/bar 2(?!\d)/);
    await entry.click();
    await expect(page.locator(".focus-card")).toBeVisible();
    await expect(drawer(page)).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("a bookmark across a system break keeps its hooks and its title on every system", async ({ page }) => {
    const errors = await openFurElise(page);
    // Phrase 2 (bars 4–8) runs from the first system onto the second.
    await page.locator(".score button.bracket[aria-label]").nth(1).click();
    await page.locator(".ctx").getByRole("button", { name: "Bookmark" }).click();
    await card(page).getByRole("textbox", { name: "Title" }).fill("A long title for the answer to the opening phrase");
    await card(page).getByRole("button", { name: "Done" }).click();

    const runs = page.locator(".score .bmb");
    const titles = page.locator(".score .bmb .w");
    await expect(runs.first()).toHaveAttribute("aria-label", /^Bookmark: A long title/);
    const count = await runs.count();
    expect(count).toBeGreaterThan(1);
    await expect(titles).toHaveCount(count);
    for (let i = 1; i < count; i++) {
        await expect(titles.nth(i)).toHaveText("…A long title for the answer to the opening phrase");
    }
    // The title is cut with an ellipsis rather than running past the bracket.
    expect(await titles.first().evaluate(title => getComputedStyle(title).textOverflow)).toBe("ellipsis");
    expect(errors).toEqual([]);
});

test("B opens the card over the selection, Escape puts it away, and an entry can be changed and deleted", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.keyboard.press("b");
    await expect(page.locator(".marks-status")).toHaveText(/Select bars first/);

    await selectBar(page, 3);
    await page.locator("body").press("b");
    const title = card(page).getByRole("textbox", { name: "Title" });
    await expect(title).toBeVisible();
    await title.press("Escape");
    await expect(card(page)).toHaveCount(0);

    await page.locator("body").press("b");
    await expect(title).toBeFocused();
    await title.press("Enter");
    await expect(page.locator(".score .bmb.ink-carmine")).not.toHaveCount(0);

    await page.locator(".bar").getByRole("button", { name: /open the contents/ }).click();
    await drawer(page).getByRole("button", { name: /^Change / }).click();
    await card(page).getByRole("textbox", { name: "Title" }).fill("Slow, then hands apart");
    await card(page).getByRole("radio", { name: "Teacher" }).click();
    await card(page).getByRole("button", { name: "Done" }).click();
    await expect(drawer(page).locator("button.cat")).toHaveText("Teacher · 1");
    await expect(page.locator(".score .bmb.ink-ochre .w")).toHaveText("Slow, then hands apart");

    await drawer(page).getByRole("button", { name: /^Change / }).click();
    await card(page).getByRole("button", { name: "Delete" }).click();
    await expect(page.locator(".score .bmb")).toHaveCount(0);
    await expect(drawer(page)).toContainText("No bookmarks yet");
    expect(errors).toEqual([]);
});

test("the form is added as Structure bookmarks, the search finds them, and Pages shows the pages", async ({ page }) => {
    const errors = await openFurElise(page);
    await page.locator(".bar").getByRole("button", { name: /open the contents/ }).click();
    await drawer(page).getByRole("button", { name: "Add Structure bookmarks" }).click();
    await expect(page.locator(".marks-status")).toContainText("Structure bookmark");
    await expect(drawer(page).locator("button.cat")).toHaveText(/^Structure · \d+$/);
    await expect(drawer(page).getByRole("button", { name: /^Sentence A, / })).toBeVisible();
    await expect(drawer(page).getByRole("button", { name: "Add Structure bookmarks" })).toHaveCount(0);

    await drawer(page).getByRole("searchbox").fill("nothing like this");
    await expect(drawer(page)).toContainText("No bookmark matches.");
    await drawer(page).getByRole("searchbox").fill("sentence a");
    await expect(drawer(page).getByRole("button", { name: /^Sentence A, / })).toBeVisible();

    await drawer(page).getByRole("tab", { name: "Pages" }).click();
    const first = drawer(page).getByRole("button", { name: "Page 1" });
    await expect(first).toBeVisible();
    await expect(first).toContainText(/p\. 1 · phrases? 1/);
    await expect(first.locator(".tick")).not.toHaveCount(0);
    await first.click();
    expect(errors).toEqual([]);
});

test("in Russian, the card and the drawer speak Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    const errors = await openFurElise(page);
    await selectBar(page, 2);
    await page.locator(".ctx").getByRole("button", { name: "Закладка" }).click();
    await expect(card(page).getByRole("radio", { name: "Поиграть" })).toHaveAttribute("aria-checked", "true");
    await expect(card(page).getByRole("textbox", { name: "Название" })).toHaveValue(/такт/i);
    await card(page).getByRole("button", { name: "Готово" }).click();

    await page.locator(".bar").getByRole("button", { name: /открыть оглавление/ }).click();
    await expect(drawer(page).getByRole("tab", { name: "Оглавление" })).toHaveAttribute("aria-selected", "true");
    await expect(drawer(page).locator("button.cat")).toHaveText("Поиграть · 1");
    await drawer(page).getByRole("radio", { name: "Мой порядок" }).click();
    await expect(drawer(page).getByRole("radio", { name: "Мой порядок" })).toHaveAttribute("aria-checked", "true");
    expect(errors).toEqual([]);
});
