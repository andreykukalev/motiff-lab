// «Импорт папки» (an experiment, version 0.5, ux-prototype/v0.5/screens/import-web-1..3): Add a folder on Add piece, «Что
// нашлось» with its counters as filters, a guess the player writes over, the import's line over the list, and the folders as
// playlists. The browser here copies (the webkitdirectory pick of Safari and Firefox); Chrome's kept folder is not driven,
// since a test cannot answer its folder picker. The files are only read, and nothing leaves the page.
import { expect, test } from "@playwright/test";
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { pdf } from "./pdf-fixture.mjs";
import { pageErrors, violations } from "./steps.mjs";

const minuet = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/MusicXml/Fixtures/importer-grand-staff.musicxml", import.meta.url));

/** A folder of notes as a player keeps one: two folders of pieces, a repeat, a damaged PDF and a file that is no notes. */
function notesFolder() {
    const root = join(mkdtempSync(join(tmpdir(), "motiff-folder-")), "Notes");
    const files = {
        "Debussy/Arabesque No. 1.pdf": pdf(2),
        "Concert in May/Arabesque No. 1.pdf": pdf(2),
        "Bach/minuet_in_g.musicxml": readFileSync(minuet),
        "Chopin/Etudes.pdf": Buffer.from("this is not a PDF"),
        "readme.txt": Buffer.from("notes"),
    };
    for (const [path, data] of Object.entries(files)) {
        mkdirSync(dirname(join(root, path)), { recursive: true });
        writeFileSync(join(root, path), data);
    }
    return root;
}

/** The app as a browser that copies a folder's files: no folder picker to keep a folder. */
async function copyingBrowser(page, on = true) {
    await page.addInitScript((switched) => {
        delete window.showDirectoryPicker;
        if (switched) {
            localStorage.setItem("motiff-lab:experimental.libraryImport", "on");
        }
    }, on);
}

const counter = (sheet, name) => sheet.locator(".counts").getByRole("button", { name: new RegExp(`^${name} \\d`) });

test("a folder becomes pieces: what was found, a guess written over, the import's line, and folders as playlists", async ({ page }) => {
    const errors = pageErrors(page);
    const elsewhere = [];
    page.on("request", (request) => {
        if (!request.url().startsWith("http://localhost:") && !request.url().startsWith("data:") && !request.url().startsWith("blob:")) {
            elsewhere.push(request.url());
        }
    });
    await copyingBrowser(page);
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    const add = page.getByRole("dialog");
    await add.getByLabel("Add a folder").setInputFiles(notesFolder());

    const sheet = page.getByRole("dialog", { name: "What was found" });
    await expect(sheet.locator(".overline")).toHaveText("Notes");
    await expect(counter(sheet, "Found")).toContainText("5");
    await expect(counter(sheet, "New")).toContainText("2");
    await expect(counter(sheet, "Repeats")).toContainText("1");
    await expect(counter(sheet, "Can't be added")).toContainText("2");
    await expect(sheet.getByText("the same file as in Concert in May")).toBeVisible();
    await expect(sheet.getByText("the file is damaged or not a PDF")).toBeVisible();
    await expect(sheet.getByText("1 more file is not notes", { exact: false })).toBeVisible();
    expect(await violations(page)).toEqual([]);

    // A counter filters the list; the guesses are grey with where they come from.
    await counter(sheet, "New").click();
    await expect(sheet.locator("tbody tr:has(td)")).toHaveCount(2);
    const minuetTitle = sheet.getByRole("textbox", { name: "Title of the piece from minuet_in_g.musicxml" });
    await expect(minuetTitle).toHaveClass(/guess/);
    await minuetTitle.fill("Minuet in G");
    await minuetTitle.press("Tab");
    await expect(minuetTitle).not.toHaveClass(/guess/);
    await expect(sheet.getByRole("radio", { name: "playlists" })).toHaveAttribute("aria-checked", "true");
    await expect(sheet.locator(".space")).toContainText("take");

    await sheet.getByRole("button", { name: "Add 2 pieces" }).click();
    await expect(sheet).toHaveCount(0);
    await expect(page.locator(".run-bar .what")).toContainText("Added 2 of 2");
    const rows = page.locator(".lib-table tbody tr");
    await expect(rows.filter({ hasText: "Minuet in G" })).toHaveCount(1);
    // The repeat is one piece on both its folders' playlists.
    await expect(rows.filter({ hasText: "Arabesque No. 1" })).toHaveCount(1);
    await expect(rows.filter({ hasText: "Arabesque No. 1" }).locator(".col-playlists")).toHaveText("Concert in May, Debussy");
    await expect(rows.filter({ hasText: "Minuet in G" }).locator(".col-playlists")).toHaveText("Bach");

    // The same folder again: what was added is in the library, and nothing is new.
    await page.locator(".run-bar").getByRole("button", { name: "Done" }).click();
    await expect(page.locator(".run-bar")).toHaveCount(0);
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await add.getByLabel("Add a folder").setInputFiles(notesFolder());
    await expect(counter(sheet, "New")).toContainText("0");
    // Both copies of the Arabesque are files of the piece the library has.
    await expect(counter(sheet, "Already in the library")).toContainText("3");

    expect(elsewhere).toEqual([]);
    expect(errors).toEqual([]);
});

test("with the experiment off, Add piece has no folder", async ({ page }) => {
    await copyingBrowser(page, false);
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await expect(page.getByRole("dialog").getByRole("button", { name: /folder/i })).toHaveCount(0);
    await expect(page.getByRole("dialog").getByText("Add a folder")).toHaveCount(0);
});

test("an import stopped by a reload goes on from where it was once the folder is chosen again", async ({ page }) => {
    const root = join(mkdtempSync(join(tmpdir(), "motiff-many-")), "Many");
    mkdirSync(root);
    for (let index = 1; index <= 40; index++) {
        const data = Buffer.concat([pdf(1), Buffer.from(`%${index}\n`, "latin1")]);
        writeFileSync(join(root, `Piece ${String(index).padStart(2, "0")}.pdf`), data);
    }
    await copyingBrowser(page);
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog").getByLabel("Add a folder").setInputFiles(root);
    const sheet = page.getByRole("dialog", { name: "What was found" });
    await sheet.getByRole("button", { name: "Add 40 pieces" }).click();
    await page.locator(".run-bar").getByRole("button", { name: "Pause" }).click();
    await expect(page.locator(".run-bar .what")).toContainText(/Import stopped at \d+ of 40/);

    await page.reload();
    await expect(page.locator(".run-bar .what")).toContainText(/Import stopped at \d+ of 40/);
    await page.locator(".run-bar").getByRole("button", { name: "Go on", exact: true }).click();
    await page.locator(".run-bar input[type=file]").setInputFiles(root);
    await expect(page.locator(".run-bar .what")).toContainText("Added 40 of 40", { timeout: 30_000 });
    await expect(page.locator(".lib-table tbody tr").filter({ hasText: /Piece \d\d/ })).toHaveCount(40);
});
