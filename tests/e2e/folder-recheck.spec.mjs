// A PDF left in the player's folder (version 0.5, «Импорт папки», ux-prototype/v0.5/screens/import-web-6..8): it opens from
// the folder; when the folder was reorganised, «Файл не найден» → «Проверить папку» finds it by its fingerprint and opens it;
// a changed file is offered («Похоже, это та же пьеса»), never taken by itself; «Проверить папку» in the library's menu shows
// what changed. Chrome keeps the folder: here its picker answers a folder of the page's own private storage (OPFS), which the
// test fills and reorganises, as a player would their own folder. The app itself only reads it. A handle read back from
// IndexedDB closes Chromium in an incognito context (as every test context is), so these tests run in a profile of their own.
import { chromium, expect, test } from "@playwright/test";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pdf } from "./pdf-fixture.mjs";
import { pageErrors } from "./steps.mjs";

test.skip(({ browserName }) => browserName !== "chromium", "Chrome and Edge keep a folder; the other browsers copy it (folder-import.spec)");

let profile;
let context;

test.afterEach(async () => {
    await context?.close();
    rmSync(profile, { recursive: true, force: true });
});

/**
 * The app in a profile of its own, with Chrome's folder and file pickers answering the folder "Notes" and a file of the
 * page's private storage.
 */
async function keptFolder(testInfo) {
    profile = mkdtempSync(join(tmpdir(), "motiff-profile-"));
    context = await chromium.launchPersistentContext(profile, { baseURL: testInfo.project.use.baseURL ?? "http://localhost:5180" });
    const page = context.pages()[0] ?? (await context.newPage());
    await page.addInitScript(() => {
        localStorage.setItem("motiff-lab:settings.presetChosen", "on");
        localStorage.setItem("motiff-lab:experimental.libraryImport", "on");
        const root = () => navigator.storage.getDirectory();
        window.showDirectoryPicker = async () => (await root()).getDirectoryHandle("Notes", { create: true });
        window.showOpenFilePicker = async () => [await (await root()).getFileHandle("picked.pdf")];
    });
    return page;
}

/** Writes, moves or removes files of the folder, as the player would. */
async function folderFiles(page, files) {
    await page.evaluate(async (entries) => {
        let notes = await (await navigator.storage.getDirectory()).getDirectoryHandle("Notes", { create: true });
        const dirOf = async (path, create) => {
            let at = path.startsWith("/") ? await navigator.storage.getDirectory() : notes;
            const parts = path.replace(/^\//, "").split("/");
            for (const part of parts.slice(0, -1)) {
                at = await at.getDirectoryHandle(part, { create });
            }
            return [at, parts.at(-1)];
        };
        for (const [path, bytes] of entries) {
            const [dir, name] = await dirOf(path, true);
            if (bytes === null) {
                await dir.removeEntry(name);
                continue;
            }
            const writable = await (await dir.getFileHandle(name, { create: true })).createWritable();
            await writable.write(new Uint8Array(bytes));
            await writable.close();
        }
        notes = null;
    }, files.map(([path, data]) => [path, data === null ? null : [...data]]));
}

async function addFolder(page) {
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Add a folder" }).click();
    return page.getByRole("dialog", { name: "What was found" });
}

const pageCount = (page) => page.locator(".bar .pg");

test("a linked PDF opens from the folder, is found again after the folder was reorganised, and a changed file is offered", async ({}, testInfo) => {
    const page = await keptFolder(testInfo);
    const errors = pageErrors(page);
    await page.goto("/");
    await folderFiles(page, [["Chopin/Ballade No. 1.pdf", pdf(3)]]);
    const sheet = await addFolder(page);
    await sheet.getByRole("button", { name: "Add 1 piece" }).click();
    await expect(page.locator(".run-bar .what")).toContainText("Added 1 of 1");

    // Linked, not copied: it opens from the folder.
    await page.locator(".lib-table tbody tr").filter({ hasText: "Ballade No. 1" }).getByRole("button", { name: "Ballade No. 1", exact: true }).click();
    await expect(pageCount(page)).toContainText("3");

    // Moved into a folder of its own: «Файл не найден» → «Проверить папку», and it opens.
    await folderFiles(page, [["Chopin/Ballades/Ballade No. 1.pdf", pdf(3)], ["Chopin/Ballade No. 1.pdf", null]]);
    await page.goto("/");
    await page.locator(".lib-table tbody tr").filter({ hasText: "Ballade No. 1" }).getByRole("button", { name: "Ballade No. 1", exact: true }).click();
    const missing = page.getByRole("dialog", { name: "File not found" });
    await expect(missing).toContainText("Perhaps the folder was reorganised.");
    await missing.getByRole("button", { name: "Check the folder" }).click();
    await expect(missing).toHaveCount(0);
    await expect(pageCount(page)).toContainText("3");

    // Its content changed in place: offered, and opened once the player says it is the piece.
    await folderFiles(page, [["Chopin/Ballades/Ballade No. 1.pdf", pdf(4)]]);
    await page.reload();
    await missing.getByRole("button", { name: "Check the folder" }).click();
    const alike = page.getByRole("dialog", { name: "This looks like the same piece" });
    await expect(alike).toContainText("The piece's notes, marks and edits stay as they are");
    await alike.getByRole("button", { name: "It is: open this file" }).click();
    await expect(pageCount(page)).toContainText("4");
    expect(errors).toEqual([]);
});

test("Check the folder in the library's menu shows what changed: new files, and pieces moved are linked again", async ({}, testInfo) => {
    const page = await keptFolder(testInfo);
    await page.goto("/");
    await folderFiles(page, [["Bach/Aria.pdf", pdf(2)], ["Satie/Gymnopedie No. 1.pdf", pdf(3)]]);
    await (await addFolder(page)).getByRole("button", { name: "Add 2 pieces" }).click();
    await expect(page.locator(".run-bar .what")).toContainText("Added 2 of 2");
    await page.locator(".run-bar").getByRole("button", { name: "Done" }).click();

    await folderFiles(page, [["Bach/Goldberg/Aria.pdf", pdf(2)], ["Bach/Aria.pdf", null], ["Debussy/Pagodes.pdf", pdf(7)],
        ["Satie/Gymnopedie No. 1.pdf", null], ["Satie/Gymnopedie No. 1 (urtext).pdf", Buffer.concat([pdf(3), Buffer.from("%changed\n")])]]);
    await page.locator(".lib-head").getByRole("button", { name: "More" }).click();
    await expect(page.locator(".lib-menu")).toContainText("checked today at");
    await page.locator(".lib-menu").getByRole("button", { name: "Check the folder “Notes”" }).click();
    const sheet = page.getByRole("dialog", { name: "What changed in the folder" });
    await expect(sheet.locator(".same")).toContainText("Gymnopedie No. 1 (urtext).pdf");
    // Until the player answers, the changed file is not among the new ones.
    await expect(sheet.locator(".counts")).toContainText("New 1");
    await expect(sheet.getByText("1 piece moved in the folder and is linked again.")).toBeVisible();
    await expect(sheet.locator("tbody tr:has(td)")).toHaveCount(1);
    await sheet.getByRole("button", { name: "No, add it as another piece" }).click();
    await expect(sheet.locator(".counts")).toContainText("New 2");
    // By title, Gymnopedie comes first; left out, so only Pagodes is added.
    await sheet.locator("tbody tr:has(td)").first().getByRole("checkbox").uncheck();
    await sheet.getByRole("button", { name: "Add 1 piece" }).click();
    await expect(page.locator(".lib-table tbody tr").filter({ hasText: "Pagodes" })).toHaveCount(1);

    await page.locator(".lib-table tbody tr").filter({ hasText: "Aria" }).getByRole("button", { name: "Aria", exact: true }).click();
    await expect(pageCount(page)).toContainText("2");
});

test("a PDF piece whose file is not linked on this browser says so, and takes only the same file", async ({}, testInfo) => {
    const page = await keptFolder(testInfo);
    await page.goto("/");
    await folderFiles(page, [["Bach/Prelude in C.pdf", pdf(2)]]);
    await (await addFolder(page)).getByRole("button", { name: "Add 1 piece" }).click();
    await expect(page.locator(".run-bar .what")).toContainText("Added 1 of 1");

    // This browser forgets the folder, as another browser or device never had it.
    await page.evaluate(async () => {
        const db = await new Promise((resolve) => { const open = indexedDB.open("motiff-lab-import"); open.onsuccess = () => resolve(open.result); });
        await new Promise((resolve) => {
            const forget = db.transaction(["folders", "links"], "readwrite");
            forget.objectStore("folders").clear();
            forget.objectStore("links").clear();
            forget.oncomplete = resolve;
        });
        db.close();
    });
    await page.locator(".lib-table tbody tr").filter({ hasText: "Prelude in C" }).getByRole("button", { name: "Prelude in C", exact: true }).click();
    const away = page.getByRole("dialog", { name: "The original is not on this device" });
    await expect(away).toContainText("Prelude in C.pdf");

    await folderFiles(page, [["/picked.pdf", pdf(1)]]);
    await away.getByRole("button", { name: "Link the original" }).click();
    await expect(away).toContainText("This is a different file");

    await folderFiles(page, [["/picked.pdf", pdf(2)]]);
    await away.getByRole("button", { name: "Link the original" }).click();
    await expect(away).toHaveCount(0);
    await expect(pageCount(page)).toContainText("2");

    // The file picked is read where it is from now on.
    await page.reload();
    await expect(pageCount(page)).toContainText("2");
});
