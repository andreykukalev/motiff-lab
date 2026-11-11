// The library as the place to manage the pieces (version 0.2, ux-prototype/v2/screens/lib-1-list.png to lib-4-props.png):
// the tabs, the list with its columns ordered by a heading, search and the Composer, Tag and Playlist filters, the index,
// the composers and the playlists with their pieces, the sessions as cards, and a piece's properties kept in its project file.
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
        await page.locator(".lib-head").getByRole("button", { name: /^(Add piece|Добавить пьесу)$/ }).click();
        await page.getByRole("dialog").locator("input[type=file]").setInputFiles(file);
        await expect(page).toHaveURL(/\/playback\?project=/);
        await expect(page.locator(".bar .ttl")).not.toBeEmpty();
    }
    await page.goto("/");
}

const filter = (page, name) => page.locator(".lib-tabs .filters").getByRole("button", { name, exact: true });
const titles = (page) => page.locator(".lib-table tbody .ttl");
const row = (page, name) => page.locator(".lib-table tbody tr").filter({ has: page.getByRole("button", { name, exact: true }) });

/** Opens a piece's properties from the ••• of its line. */
async function properties(page, name) {
    await row(page, name).getByRole("button", { name: `More for ${name}` }).click();
    await row(page, name).getByRole("button", { name: "Piece properties" }).click();
    return page.getByRole("dialog", { name: "Piece properties" });
}

test("the list shows every piece by its columns, ordered by a heading, and search and the filters narrow it", async ({ page }) => {
    const errors = pageErrors(page);
    await addPieces(page, [minuet, elise, ode]);

    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Library");
    await expect(page.getByRole("tab", { selected: true })).toHaveText("All pieces");
    await expect(page.getByRole("tab")).toHaveText(["All pieces", "Composers", "Recent", "Playlists", "Sessions"]);
    // From A by title, with the index at the edge; the score's own title and composer show until the player sets theirs.
    await expect(titles(page)).toHaveText(["Fur Elise, opening", "Ode to Joy, theme", "Waltz sketch"]);
    await expect(row(page, "Fur Elise, opening").locator(".col-composer")).toHaveText("Ludwig van Beethoven");
    await expect(page.getByRole("navigation", { name: "Index" })).toBeVisible();
    await page.getByRole("navigation", { name: "Index" }).getByRole("button", { name: "W", exact: true }).click();
    expect(await violations(page)).toEqual([]);

    const heading = (name) => page.locator(".lib-table thead").getByRole("button", { name });
    await heading("Title").click();
    await expect(titles(page)).toHaveText(["Waltz sketch", "Ode to Joy, theme", "Fur Elise, opening"]);
    await expect(page.getByRole("navigation", { name: "Index" })).toHaveCount(0);
    await heading("Opened").click();
    await expect(titles(page)).toHaveText(["Ode to Joy, theme", "Fur Elise, opening", "Waltz sketch"]);
    await expect(page.locator(".lib-table thead th.col-opened")).toHaveAttribute("aria-sort", "descending");
    await heading("Composer").click();
    await expect(titles(page)).toHaveText(["Fur Elise, opening", "Ode to Joy, theme", "Waltz sketch"]);

    const search = page.getByRole("searchbox", { name: "Search the library" });
    await search.fill("waltz");
    await expect(titles(page)).toHaveText(["Waltz sketch"]);
    // Letter case and accents do not matter.
    await search.fill("FÜR");
    await expect(titles(page)).toHaveText(["Fur Elise, opening"]);
    await search.fill("nothing like this");
    await expect(page.getByText("No pieces match.")).toBeVisible();
    await page.getByRole("button", { name: "Clear search and filters" }).click();
    await expect(titles(page)).toHaveCount(3);

    await filter(page, "Composer").click();
    await page.getByRole("group", { name: "Composer" }).getByRole("button", { name: "Ludwig van Beethoven" }).click();
    await expect(titles(page)).toHaveCount(2);
    await filter(page, "Composer: Ludwig van Beethoven").click();
    await page.getByRole("group", { name: "Composer" }).getByRole("button", { name: "All composers" }).click();
    await expect(titles(page)).toHaveCount(3);
    expect(errors).toEqual([]);
});

test("the Composers tab counts each composer's pieces, those without one last, and opens a composer's pieces", async ({ page }) => {
    const errors = pageErrors(page);
    await addPieces(page, [minuet, elise, ode]);

    await page.getByRole("tab", { name: "Composers" }).click();
    const groups = page.locator(".lib-groups .group");
    await expect(groups.locator(".name")).toHaveText(["Ludwig van Beethoven", "No composer"]);
    await expect(groups.locator(".count")).toHaveText(["2 pieces", "1 piece"]);
    expect(await violations(page)).toEqual([]);

    await groups.filter({ hasText: "Ludwig van Beethoven" }).click();
    await expect(page.locator(".lib-picked .ttl")).toHaveText("Ludwig van Beethoven");
    await expect(titles(page)).toHaveText(["Fur Elise, opening", "Ode to Joy, theme"]);
    await page.locator(".lib-picked").getByRole("button", { name: "Composers" }).click();
    await groups.filter({ hasText: "No composer" }).click();
    await expect(titles(page)).toHaveText(["Waltz sketch"]);

    // The tab stays after a reload.
    await page.reload();
    await expect(page.getByRole("tab", { selected: true })).toHaveText("Composers");
    expect(errors).toEqual([]);
});

test("a piece's properties: title, a composer from the library, tags and playlists, kept and shown after a reload", async ({ page }) => {
    const errors = pageErrors(page);
    await addPieces(page, [minuet, ode]);

    const sheet = await properties(page, "Waltz sketch");
    await expect(sheet.getByLabel("Title")).toBeFocused();
    await expect(sheet.getByLabel("Title")).toHaveAttribute("placeholder", "Waltz sketch");
    await sheet.getByLabel("Title").fill("My waltz");
    await sheet.getByLabel("Composer", { exact: true }).fill("beet");
    await sheet.getByRole("button", { name: "Ludwig van Beethoven already in the library" }).click();
    await expect(sheet.getByLabel("Composer", { exact: true })).toHaveValue("Ludwig van Beethoven");
    await sheet.getByRole("button", { name: "New tag" }).click();
    await sheet.getByRole("textbox", { name: "New tag" }).fill("Exam");
    await sheet.getByRole("textbox", { name: "New tag" }).press("Enter");
    await sheet.getByRole("textbox", { name: "New tag" }).fill(" exam ");
    await sheet.getByRole("textbox", { name: "New tag" }).press("Enter");
    await sheet.getByRole("textbox", { name: "New tag" }).fill("Waltzes");
    await sheet.getByRole("button", { name: "New playlist" }).click();
    await sheet.getByRole("textbox", { name: "Name of the new playlist" }).fill("Recital");
    await sheet.getByRole("textbox", { name: "Name of the new playlist" }).press("Enter");
    await expect(sheet.getByRole("checkbox", { name: "Recital" })).toBeChecked();
    expect(await violations(page)).toEqual([]);
    await sheet.getByRole("button", { name: "Done" }).click();
    await expect(sheet).toHaveCount(0);

    await expect(row(page, "My waltz").locator(".col-composer")).toHaveText("Ludwig van Beethoven");
    await expect(row(page, "My waltz").locator(".col-playlists")).toHaveText("Recital");

    // The other piece goes on the same playlist and takes a tag the library has; Escape leaves a piece as it was.
    const second = await properties(page, "Ode to Joy, theme");
    await second.getByRole("checkbox", { name: "Recital" }).check();
    await second.getByRole("button", { name: "New tag" }).click();
    await second.getByRole("button", { name: "Add tag Exam" }).click();
    await second.getByRole("button", { name: "Done" }).click();
    const third = await properties(page, "Ode to Joy, theme");
    await third.getByLabel("Title").fill("Not kept");
    await third.press("Escape");
    await expect(third).toHaveCount(0);
    await expect(titles(page)).toHaveText(["My waltz", "Ode to Joy, theme"]);

    await filter(page, "Tag").click();
    await page.getByRole("group", { name: "Tag" }).getByRole("button", { name: "Waltzes" }).click();
    await expect(titles(page)).toHaveText(["My waltz"]);
    await filter(page, "Tag: Waltzes").click();
    await page.getByRole("group", { name: "Tag" }).getByRole("button", { name: "All tags" }).click();

    await page.getByRole("tab", { name: "Playlists" }).click();
    await expect(page.locator(".lib-groups .group .name")).toHaveText(["Recital"]);
    await expect(page.locator(".lib-groups .group .count")).toHaveText(["2 pieces"]);
    await page.reload();
    await page.locator(".lib-groups .group").filter({ hasText: "Recital" }).click();
    await expect(titles(page)).toHaveText(["My waltz", "Ode to Joy, theme"]);

    // Taking off a tag and clearing the title bring the file's back; the piece opens with the player's title in its bar.
    await page.getByRole("tab", { name: "All pieces" }).click();
    const again = await properties(page, "My waltz");
    await expect(again.getByLabel("Title")).toHaveValue("My waltz");
    await again.getByRole("button", { name: "Remove tag Waltzes" }).click();
    await again.getByRole("checkbox", { name: "Recital" }).uncheck();
    await again.getByLabel("Title").fill("");
    await again.getByRole("button", { name: "Done" }).click();
    await expect(row(page, "Waltz sketch").locator(".col-playlists")).toHaveText("");
    const ode2 = await properties(page, "Ode to Joy, theme");
    await ode2.getByLabel("Title").fill("An die Freude");
    await ode2.getByRole("button", { name: "Done" }).click();
    await row(page, "An die Freude").getByRole("button", { name: "An die Freude", exact: true }).click();
    await expect(page.locator(".bar .ttl")).toHaveText("An die Freude");
    expect(errors).toEqual([]);
});

test("Recent lists the pieces opened last, and Sessions shows the work to come back to as cards", async ({ page }) => {
    const errors = pageErrors(page);
    await addPieces(page, [minuet, elise]);

    await page.getByRole("tab", { name: "Recent" }).click();
    await expect(titles(page)).toHaveText(["Fur Elise, opening", "Waltz sketch"]);
    await page.getByRole("tab", { name: "Sessions" }).click();
    const card = page.locator(".card.piece").filter({ has: page.getByRole("heading", { name: "Waltz sketch" }) });
    await expect(page.locator(".card.piece")).toHaveCount(2);
    await expect(page.locator(".lib-tabs .filters")).toHaveCount(0);
    expect(await violations(page)).toEqual([]);
    await card.getByRole("button", { name: "Resume Waltz sketch" }).click();
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+&resume=r_/);
    expect(errors).toEqual([]);
});

test("in Russian, the library's tabs, search and properties speak Russian", async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("motiff-lab:ml-language", "ru"));
    await addPieces(page, [minuet]);
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Библиотека");
    await expect(page.getByRole("tab")).toHaveText(["Все пьесы", "Композиторы", "Недавние", "Плейлисты", "Сессии"]);
    await expect(page.getByRole("searchbox", { name: "Поиск по библиотеке" })).toHaveAttribute("placeholder", "Название, композитор или тег");
    await page.getByRole("button", { name: "Ещё: Waltz sketch" }).click();
    await page.getByRole("button", { name: "Свойства пьесы" }).click();
    const sheet = page.getByRole("dialog", { name: "Свойства пьесы" });
    await expect(sheet.getByLabel("Композитор", { exact: true })).toHaveAttribute("placeholder", "Не указан");
    await expect(sheet.getByRole("button", { name: "Готово" })).toBeVisible();
    await expect(sheet.getByRole("button", { name: "Новый плейлист" })).toBeVisible();
    await sheet.press("Escape");
    await page.getByRole("tab", { name: "Плейлисты" }).click();
    await expect(page.getByText("Плейлистов пока нет.", { exact: false })).toBeVisible();
});
