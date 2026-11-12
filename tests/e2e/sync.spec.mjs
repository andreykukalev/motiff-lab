// Sync between the devices (version 0.2; Andrei, 2026-11-11): signed in, the web app saves its pieces online by itself
// and brings the ones saved from the iPad, when both changed one the last save wins and the previous copy is kept beside,
// and a free account keeps 300 pieces online, the others staying here with a line that says so. The build under test has
// no sign-in, so the test gives it sign-in settings, plays MSAL's script as a signed-in player, and plays the API.
import { expect, test } from "@playwright/test";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { pageErrors } from "./steps.mjs";

const api = "https://api.example.test";
const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*", "Access-Control-Expose-Headers": "ETag" };
const fixture = (path) => fileURLToPath(new URL(`../MotiffLab.MusicCore.Tests/${path}`, import.meta.url));
const elise = fixture("Experiments/Fixtures/fur-elise-original.musicxml");
// A project saved on the iPad: "Waltz sketch", with its own GUID id.
const ipadFile = readFileSync(fixture("Golden/v15-worked.motiff.json"), "utf8");
const ipadId = JSON.parse(ipadFile).id;

test.use({ serviceWorkers: "block" });

/** A signed-in free player and the API with the projects given, keeping what the app sends. */
async function signedIn(page, { online = {}, quota = false } = {}) {
    const stored = { ...online };
    const sent = [];
    const fetched = [];
    // While true, the API cannot be reached, as with no connection.
    const network = { down: false };
    let etag = 1;
    await page.route("**/appsettings.json", route => route.fulfill({
        json: {
            Account: {
                Authority: "https://motifflab.ciamlogin.com/00000000-0000-0000-0000-000000000001",
                ClientId: "00000000-0000-0000-0000-000000000003",
                ApiScope: "api://00000000-0000-0000-0000-000000000002/Projects.ReadWrite",
                ScanScope: "api://00000000-0000-0000-0000-000000000002/Scan",
                Api: api,
                Scan: "",
            },
        },
    }));
    await page.route("**/AuthenticationService.js", route => route.fulfill({
        contentType: "text/javascript",
        body: `window.AuthenticationService = {
            init: async () => {},
            getUser: async () => ({ name: "Andrei" }),
            getAccessToken: async request => ({
                status: "success",
                token: { value: "token", grantedScopes: (request && request.scopes) || [], expires: new Date(Date.now() + 3600000).toISOString() },
            }),
            signIn: async () => ({ status: "success" }),
            signOut: async () => ({ status: "success" }),
            completeSignIn: async () => ({ status: "success" }),
            completeSignOut: async () => ({ status: "success" }),
        };`,
    }));
    await page.route(`${api}/**`, async route => {
        if (network.down) {
            return route.abort("internetdisconnected");
        }
        const request = route.request();
        const path = new URL(request.url()).pathname;
        if (request.method() === "OPTIONS") {
            return route.fulfill({ status: 204, headers: { ...cors, "Access-Control-Allow-Methods": "GET, PUT, DELETE" } });
        }
        if (path === "/account") {
            return route.fulfill({ headers: cors, json: { plan: "free", tester: false, chosen: null, features: { scan: false, transcribe: false, saveOnline: true, assistant: false } } });
        }
        if (path === "/projects") {
            fetched.push("list");
            return route.fulfill({
                headers: cors,
                json: Object.entries(stored).map(([id, project]) => ({
                    id, fileName: JSON.parse(project.file).fileName, motifCount: 0, variationCount: 0,
                    lastOpened: "2026-11-11T09:00:00+00:00", resumeCount: 0, ideaCount: 0, savedAt: project.savedAt,
                })),
            });
        }
        if (!path.startsWith("/projects/")) {
            // The profile and the rest of the account are not projects.
            return route.fulfill({ headers: cors, json: {} });
        }
        const id = decodeURIComponent(path.replace("/projects/", ""));
        if (request.method() === "GET") {
            fetched.push(id);
            return stored[id]
                ? route.fulfill({ headers: { ...cors, ETag: stored[id].etag }, contentType: "application/json", body: stored[id].file })
                : route.fulfill({ status: 404, headers: cors, json: { message: "There is no such project." } });
        }
        if (request.method() === "PUT") {
            sent.push({ id, ifMatch: request.headers()["if-match"] ?? null, file: request.postData() });
            if (quota && !stored[id]) {
                return route.fulfill({ status: 403, headers: cors, json: { message: "A free account keeps 300 pieces online.", code: "quota" } });
            }
            const ifMatch = request.headers()["if-match"];
            if (ifMatch && stored[id] && stored[id].etag !== ifMatch) {
                return route.fulfill({ status: 412, headers: cors, json: { message: "The project changed since it was read." } });
            }
            stored[id] = { file: request.postData(), etag: `"e${++etag}"`, savedAt: new Date().toISOString() };
            return route.fulfill({ status: 204, headers: { ...cors, ETag: stored[id].etag } });
        }
        return route.fulfill({ status: 204, headers: cors });
    });
    return { stored, sent, fetched, network };
}

const titles = (page) => page.locator(".lib-table tbody .ttl");

test("signed in, a piece added here goes online by itself and one saved on the iPad comes here", async ({ page }) => {
    const errors = pageErrors(page);
    const { stored, sent } = await signedIn(page, { online: { [ipadId]: { file: ipadFile, etag: '"e1"', savedAt: "2026-11-11T09:00:00+00:00" } } });

    await page.goto("/");
    await expect(titles(page)).toHaveText(["Waltz sketch"]);

    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog").locator("input[type=file]").setInputFiles(elise);
    await expect(page).toHaveURL(/\/playback\?project=/);
    const id = new URL(page.url()).searchParams.get("project");
    await expect.poll(() => sent.filter(put => put.id === id).length, { timeout: 15000 }).toBeGreaterThan(0);
    expect(sent.find(put => put.id === id).ifMatch).toBeNull();
    expect(JSON.parse(stored[id].file).id).toBe(id);
    // The piece from the iPad was not sent back: nothing changed it here.
    expect(sent.some(put => put.id === ipadId)).toBe(false);

    await page.goto("/");
    await expect(titles(page)).toHaveText(["Fur Elise, opening", "Waltz sketch"]);
    await expect(page.getByText("Saved online, not on this device")).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("Settings turns saving online off, and then nothing is sent or fetched", async ({ page }) => {
    const { sent, fetched } = await signedIn(page, { online: { [ipadId]: { file: ipadFile, etag: '"e1"', savedAt: "2026-11-11T09:00:00+00:00" } } });
    await page.goto("/");
    await expect(titles(page)).toHaveText(["Waltz sketch"]);
    await page.goto("settings");
    const toggle = page.getByLabel("Save my projects online and sync my devices");
    await expect(toggle).toBeChecked();
    await expect(page.locator("#online-status")).toContainText("Free for up to 300 pieces.");
    await toggle.uncheck();
    await expect(page.locator("#online-status")).toHaveText("Saving online is off. Your projects stay on this device only and do not sync.");

    // A new load of the app: a pass started before the switch went off ends with the page it ran on.
    await page.goto("/");
    await expect(titles(page)).toHaveText(["Waltz sketch"]);
    sent.length = 0;
    fetched.length = 0;
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog").locator("input[type=file]").setInputFiles(elise);
    await expect(page).toHaveURL(/\/playback\?project=/);
    await page.goto("/");
    await expect(titles(page)).toHaveText(["Fur Elise, opening", "Waltz sketch"]);
    expect(sent).toEqual([]);
    expect(fetched).toEqual([]);
});

test("when both changed a piece, the last save wins and the previous copy is kept beside", async ({ page }) => {
    const { stored, sent, fetched, network } = await signedIn(page);
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog").locator("input[type=file]").setInputFiles(elise);
    await expect(page).toHaveURL(/\/playback\?project=/);
    const id = new URL(page.url()).searchParams.get("project");
    await expect.poll(() => Boolean(stored[id]), { timeout: 15000 }).toBe(true);
    // Back in the library, the sync sees its own save online.
    await page.goto("/");
    await expect.poll(() => fetched.includes(id), { timeout: 15000 }).toBe(true);

    // Offline, this browser changes the piece; meanwhile the iPad changed it online, in an earlier save.
    network.down = true;
    await page.getByRole("button", { name: "More for Fur Elise, opening" }).click();
    await page.getByRole("button", { name: "Piece properties" }).click();
    const sheet = page.getByRole("dialog", { name: "Piece properties" });
    await sheet.getByLabel("Title").fill("Für Elise");
    await sheet.getByRole("button", { name: "Done" }).click();
    await expect(sheet).toHaveCount(0);
    const online = JSON.parse(stored[id].file);
    online.metadata = { tags: ["From the iPad"] };
    stored[id] = { file: JSON.stringify(online), etag: '"ipad"', savedAt: "2020-01-01T09:00:00+00:00" };
    network.down = false;

    // On the next pass this browser's save wins over the copy it fetched, and the iPad's copy is kept beside.
    await page.reload();
    await expect(titles(page)).toHaveCount(2);
    await expect(titles(page).filter({ hasText: /^Für Elise$/ })).toHaveCount(1);
    await expect(titles(page).filter({ hasText: /^Fur Elise, opening \(previous copy, .+\)$/ })).toHaveCount(1);
    await expect.poll(() => sent.filter(put => put.id === id && put.ifMatch === '"ipad"').length, { timeout: 15000 }).toBeGreaterThan(0);
    expect(JSON.parse(stored[id].file).metadata.title).toBe("Für Elise");
});

test("a piece beyond the free account's 300 stays here, and the library says so", async ({ page }) => {
    await signedIn(page, { quota: true });
    await page.goto("/");
    await page.locator(".lib-head").getByRole("button", { name: "Add piece" }).click();
    await page.getByRole("dialog").locator("input[type=file]").setInputFiles(elise);
    await expect(page).toHaveURL(/\/playback\?project=/);

    await page.goto("/");
    await expect(page.getByRole("status").filter({ hasText: "Pieces on this device only: 1. A free account keeps 300 pieces online; the others stay here." })).toBeVisible();
    await expect(titles(page)).toHaveText(["Fur Elise, opening"]);
});

// Local-first originals (version 0.3, Andrei 2026-11-11 21:20 UTC): only the descriptions of the originals go online. A
// recognised PDF with a recording, saved on the iPad: the PDF and the sound stayed there.
const ipadPdf = JSON.parse(readFileSync(fixture("Golden/v15-originals-online.motiff.json"), "utf8"));
for (const original of ipadPdf.originals) {
    original.held = [{ device: "ipad", at: "2026-11-12T01:03:00.0000000+00:00" }];
}
const ipadPdfFile = JSON.stringify(ipadPdf);
// The PDF the golden was made from (Projects/PieceOriginalTests.cs), byte for byte.
const nocturnePdf = Buffer.from("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n", "ascii");

test("a piece whose PDF is only on the iPad says so, and the same PDF picked here is linked to it", async ({ page }) => {
    const errors = pageErrors(page);
    const { stored } = await signedIn(page, { online: { [ipadPdf.id]: { file: ipadPdfFile, etag: '"e1"', savedAt: "2026-11-11T09:00:00+00:00" } } });

    await page.goto("/");
    await expect(titles(page)).toHaveText(["Nocturne"]);
    await expect(page.locator(".lib-table tbody .tag.held")).toHaveText("PDF: only on the iPad");

    await titles(page).first().click();
    await expect(page).toHaveURL(/\/playback\?project=/);
    await page.locator(".versions-label").click();
    const sheet = page.locator(".perf-sheet");
    const pdfCard = sheet.locator(".source-card", { hasText: "Nocturne.pdf" });
    await expect(pdfCard.locator(".held")).toHaveText("Only on the iPad");
    await expect(sheet.locator(".source-card", { hasText: "Evening take" }).locator(".held")).toHaveText("Only on the iPad");
    await expect(sheet.locator(".local-first")).toContainText("We don't keep your files");

    // Another PDF is not this original; the same one is, by its fingerprint, and the piece has its PDF again.
    await pdfCard.locator("input[type=file]").setInputFiles({ name: "Other.pdf", mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.4\nanother\n%%EOF\n", "ascii") });
    await expect(pdfCard.getByRole("status")).toHaveText("This is a different file. To keep it as another original, add it with Add a source.");
    await pdfCard.locator("input[type=file]").setInputFiles({ name: "Nocturne (1).pdf", mimeType: "application/pdf", buffer: nocturnePdf });
    await expect(pdfCard.getByRole("status")).toHaveText("The original is linked: it is on this device now.");
    await expect(pdfCard.locator(".held")).toHaveText("In the piece's file on this device");
    await expect(pdfCard.getByRole("button", { name: "Open PDF" })).toBeVisible();

    // The copy online says this browser holds the PDF now, and still holds no PDF itself.
    await expect.poll(() => JSON.parse(stored[ipadPdf.id].file).originals[0].held.map(mark => mark.device), { timeout: 15000 }).toEqual(["ipad", "web"]);
    expect(JSON.parse(stored[ipadPdf.id].file).document).toBeUndefined();
    expect(errors).toEqual([]);
});

// A piece that is only a PDF goes online without the PDF (version 0.5, 0026 C3 amended 2026-11-12): imported on the iPad.
const ipadPdfOnly = readFileSync(fixture("Golden/v15-pdf-online.motiff.json"), "utf8");
const ipadPdfOnlyId = JSON.parse(ipadPdfOnly).id;
// The PDF the golden describes (Projects/PdfPiecesOnlineTests.cs), byte for byte.
const nocturneOnly = Buffer.from("%PDF-1.4 Nocturne in E-flat, two pages", "ascii");

test("a piece that is only a PDF on the iPad is listed here, says where its PDF is, and takes only the same file", async ({ page }) => {
    const errors = pageErrors(page);
    // A browser without Chrome's file picker: the file picked is kept as a copy on this device.
    await page.addInitScript(() => {
        window.showOpenFilePicker = undefined;
    });
    const { stored } = await signedIn(page, { online: { [ipadPdfOnlyId]: { file: ipadPdfOnly, etag: '"e1"', savedAt: "2026-11-12T18:30:00+00:00" } } });

    await page.goto("/");
    await expect(titles(page)).toHaveText(["Nocturne Op. 9 No. 2"]);
    await expect(page.locator(".lib-table tbody .tag.held")).toHaveText("PDF: only on the iPad");

    await titles(page).first().click();
    const card = page.getByRole("dialog", { name: "The original is on the iPad" });
    await expect(card).toBeVisible();
    await card.locator("input[type=file]").setInputFiles({ name: "Other.pdf", mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.4\nanother\n%%EOF\n", "ascii") });
    await expect(card).toContainText("This is a different file.");
    await card.locator("input[type=file]").setInputFiles({ name: "Nocturne.pdf", mimeType: "application/pdf", buffer: nocturneOnly });
    await expect(card).toBeHidden();

    // The copy online says this browser holds the PDF now, and still holds no PDF itself.
    await expect.poll(() => JSON.parse(stored[ipadPdfOnlyId].file).originals[0].held.map(mark => mark.device), { timeout: 15000 }).toEqual(["ipad", "web"]);
    expect(JSON.parse(stored[ipadPdfOnlyId].file).document).toBeUndefined();
    expect(errors).toEqual([]);
});
