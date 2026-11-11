// The library as the place to manage the pieces (version 0.2): search, order and filters by composer and tag, cards or a
// list, and each piece's title, composer and tags edited from its card and kept in its project file.
import { expect, test } from "@playwright/test";
import { fileURLToPath } from "node:url";
import { pageErrors, violations } from "./steps.mjs";

const fixture = (path) => fileURLToPath(new URL(`../MotiffLab.MusicCore.Tests/${path}`, import.meta.url));
const minuet = fixture("Import/MusicXml/Fixtures/importer-grand-staff.musicxml");
const elise = fixture("Experiments/Fixtures/fur-elise-original.musicxml");
const ode = fixture("Experiments/Fixtures/ode-to-joy.musicxml");

/** Adds each file through Add piece, back to the library after each. */
async function addPieces(page, files) {
    for (const file of files) {
        await page.goto("/");
        await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
        await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(file);
        await expect(page).toHaveURL(/\/playback\?project=/);
        await expect(page.locator(".bar .ttl")).not.toBeEmpty();
    }
    await page.goto("/");
}

const headings = (page) => page.locator(".lib-grid .card.piece h3, .lib-list .lib-row h3");

test("search, the composer and tag chips and the order narrow and sort the shelf", async ({ page }) => {
    const errors = pageErrors(page);
    await addPieces(page, [minuet, elise, ode]);

    await expect(page.locator(".lib-count")).toHaveText("3 pieces");
    await expect(headings(page)).toHaveCount(3);
    // The score's own title and composer show until the player sets theirs.
    const elises = page.locator(".card.piece").filter({ has: page.getByRole("heading", { name: "Fur Elise, opening" }) });
    await expect(elises).toContainText("Ludwig van Beethoven");

    const search = page.getByRole("searchbox", { name: "Search the library" });
    await search.fill("waltz");
    await expect(headings(page)).toHaveText(["Waltz sketch"]);
    await expect(page.locator(".lib-count")).toHaveText("Showing 1 of 3");
    // Letter case and accents do not matter.
    await search.fill("FÜR");
    await expect(headings(page)).toHaveText(["Fur Elise, opening"]);
    await search.fill("nothing like this");
    await expect(page.getByText("No pieces match.")).toBeVisible();
    await page.getByRole("button", { name: "Clear search and filters" }).click();
    await expect(headings(page)).toHaveCount(3);

    const composers = page.getByRole("group", { name: "Composer", exact: true });
    await composers.getByRole("button", { name: "Ludwig van Beethoven" }).click();
    await expect(composers.getByRole("button", { name: "Ludwig van Beethoven" })).toHaveAttribute("aria-pressed", "true");
    await expect(headings(page)).toHaveCount(2);
    await composers.getByRole("button", { name: "Ludwig van Beethoven" }).click();

    await page.getByLabel("Order").selectOption({ label: "By title" });
    await expect(headings(page)).toHaveText(["Fur Elise, opening", "Ode to Joy, theme", "Waltz sketch"]);
    expect(await violations(page)).toEqual([]);
    expect(errors).toEqual([]);
});

test("a piece's title, composer and tags are edited from its card, filter the shelf and stay after a reload", async ({ page }) => {
    const errors = pageErrors(page);
    await addPieces(page, [minuet, ode]);

    const card = (name) => page.locator(".card.piece").filter({ has: page.getByRole("heading", { name }) });
    await card("Waltz sketch").getByRole("button", { name: "More for Waltz sketch" }).click();
    await card("Waltz sketch").getByRole("button", { name: "Title, composer, tags" }).click();
    const sheet = page.getByRole("dialog", { name: "Waltz sketch" });
    await expect(sheet.getByLabel("Title")).toBeFocused();
    await expect(sheet.getByLabel("Title")).toHaveAttribute("placeholder", "Waltz sketch");
    expect(await violations(page)).toEqual([]);

    await sheet.getByLabel("Title").fill("My waltz");
    await sheet.getByLabel("Composer").fill("Anon.");
    await sheet.getByRole("textbox", { name: "New tag" }).fill("Exam");
    await sheet.getByRole("textbox", { name: "New tag" }).press("Enter");
    await sheet.getByRole("textbox", { name: "New tag" }).fill(" exam ");
    await sheet.getByRole("button", { name: "Add", exact: true }).click();
    await sheet.getByRole("textbox", { name: "New tag" }).fill("Waltzes");
    await sheet.getByRole("button", { name: "Save" }).click();
    await expect(sheet).toHaveCount(0);

    await expect(card("My waltz")).toContainText("Anon.");
    await expect(card("My waltz").locator(".tag.own")).toHaveText(["Exam", "Waltzes"]);
    const tags = page.getByRole("group", { name: "Tag", exact: true });
    await tags.getByRole("button", { name: "Exam" }).click();
    await expect(headings(page)).toHaveText(["My waltz"]);
    await tags.getByRole("button", { name: "Exam" }).click();

    // The other piece picks a tag the library has; taking off a tag and clearing the title bring the file's back.
    await card("Ode to Joy, theme").getByRole("button", { name: "More for Ode to Joy, theme" }).click();
    await card("Ode to Joy, theme").getByRole("button", { name: "Title, composer, tags" }).click();
    const ode2 = page.getByRole("dialog", { name: "Ode to Joy, theme" });
    await ode2.getByRole("button", { name: "Add tag Exam" }).click();
    await ode2.getByRole("button", { name: "Save" }).click();
    await tags.getByRole("button", { name: "Exam" }).click();
    await expect(headings(page)).toHaveCount(2);
    await tags.getByRole("button", { name: "Exam" }).click();

    await page.reload();
    await expect(card("My waltz").locator(".tag.own")).toHaveText(["Exam", "Waltzes"]);
    await card("My waltz").getByRole("button", { name: "More for My waltz" }).click();
    await card("My waltz").getByRole("button", { name: "Title, composer, tags" }).click();
    const again = page.getByRole("dialog", { name: "My waltz" });
    await expect(again.getByLabel("Title")).toHaveValue("My waltz");
    await again.getByRole("button", { name: "Remove tag Waltzes" }).click();
    await again.getByLabel("Title").fill("");
    await again.getByRole("button", { name: "Save" }).click();
    await expect(card("Waltz sketch").locator(".tag.own")).toHaveText(["Exam"]);

    // The piece opens with the player's title in its bar.
    await card("Ode to Joy, theme").getByRole("button", { name: "More for Ode to Joy, theme" }).click();
    await card("Ode to Joy, theme").getByRole("button", { name: "Title, composer, tags" }).click();
    await page.getByRole("dialog").getByLabel("Title").fill("An die Freude");
    await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
    await card("An die Freude").getByRole("button", { name: /^(Open|Resume) An die Freude$/ }).click();
    await expect(page.locator(".bar .ttl")).toHaveText("An die Freude");
    expect(errors).toEqual([]);
});

test("the shelf shows as a compact list, kept on the device, and a row resumes its piece", async ({ page }) => {
    const errors = pageErrors(page);
    await addPieces(page, [minuet, elise]);
    await page.getByRole("group", { name: "View", exact: true }).getByRole("button", { name: "List" }).click();
    await expect(page.locator(".lib-list .lib-row")).toHaveCount(2);
    await expect(page.locator(".card.piece")).toHaveCount(0);
    expect(await violations(page)).toEqual([]);

    await page.reload();
    await expect(page.getByRole("group", { name: "View", exact: true }).getByRole("button", { name: "List" })).toHaveAttribute("aria-pressed", "true");
    const row = page.locator(".lib-row").filter({ has: page.getByRole("heading", { name: "Waltz sketch" }) });
    await row.locator(".ttl").click();
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+&resume=r_/);
    expect(errors).toEqual([]);
});

test("in Russian, the library's search and the edit sheet speak Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Добавить пьесу" }).click();
    await page.getByRole("dialog").locator("input[type=file]").setInputFiles(minuet);
    await expect(page).toHaveURL(/\/playback\?project=/);
    await page.goto("/");
    await expect(page.getByRole("searchbox", { name: "Поиск по библиотеке" })).toHaveAttribute("placeholder", "Название, композитор или тег");
    await expect(page.locator(".lib-count")).toHaveText("1 пьеса");
    await page.getByRole("button", { name: "Ещё: Waltz sketch" }).click();
    await page.getByRole("button", { name: "Название, композитор, теги" }).click();
    const sheet = page.getByRole("dialog", { name: "Waltz sketch" });
    await expect(sheet.getByLabel("Композитор")).toHaveAttribute("placeholder", "Не указан");
    await expect(sheet.getByRole("button", { name: "Сохранить" })).toBeVisible();
});
