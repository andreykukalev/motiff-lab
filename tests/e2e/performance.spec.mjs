// Another version of the piece and the player's own version assembled from them (decision records 0022 and 0024). A
// MIDI file of a performance is added from More or the Sources sheet: one quiet card offers to compare, and the notes
// stay as they are. Comparing shows each source in its own bars, chosen from the playback pill; a selection in a version
// is carried into the notes through the carry sheet, every carry is a step of the journal that can be undone or returned
// with what leans on it, and only «Make it the notes» changes the piece. The performance is Für Elise's opening with three
// notes added in bar 5 and the left hand of bar 7 left out (fixtures/fur-elise-cover.mid, made from the public-domain
// score). Which is the base is only which was opened first: the score in MusicXML can be added over the cover opened as the piece.
import { expect, test } from "@playwright/test";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { pdf } from "./pdf-fixture.mjs";
import { closePopover, openMore, violations } from "./steps.mjs";

const score = fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Experiments/Fixtures/fur-elise-original.musicxml", import.meta.url));
const cover = fileURLToPath(new URL("fixtures/fur-elise-cover.mid", import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in scan server could.
test.use({ serviceWorkers: "block" });

/** A second of a 440 Hz tone as a WAV file, for the browser to take the sound out of; what is heard comes from the stand-in server. */
async function recording() {
    const rate = 44100;
    const bytes = Buffer.alloc(44 + rate * 2);
    bytes.write("RIFF", 0);
    bytes.writeUInt32LE(36 + rate * 2, 4);
    bytes.write("WAVEfmt ", 8);
    bytes.writeUInt32LE(16, 16);
    bytes.writeUInt16LE(1, 20);
    bytes.writeUInt16LE(1, 22);
    bytes.writeUInt32LE(rate, 24);
    bytes.writeUInt32LE(rate * 2, 28);
    bytes.writeUInt16LE(2, 32);
    bytes.writeUInt16LE(16, 34);
    bytes.write("data", 36);
    bytes.writeUInt32LE(rate * 2, 40);
    for (let i = 0; i < rate; i++) {
        bytes.writeInt16LE(Math.round(Math.sin((2 * Math.PI * 440 * i) / rate) * 12000), 44 + i * 2);
    }
    const path = join(await mkdtemp(join(tmpdir(), "recording-")), "Take 1 at the piano.wav");
    await writeFile(path, bytes);
    return path;
}

/** The piece from the score, opened. */
async function openScore(page) {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
}

/** The piece from the score, the cover added from More: the card that offers to compare is up. */
async function scoreWithCover(page) {
    await openScore(page);
    await (await openMore(page)).getByLabel(/Add another version/).setInputFiles(cover);
    const offer = page.locator(".assembly-offer");
    await expect(offer).toBeVisible();
    return offer;
}

/** A bar of the source shown, tapped on its top staff line, away from its notes. */
async function tapBar(page, index) {
    await page.locator("div.score g.measure").nth(index).locator("g.staff > path").first().dispatchEvent("click");
}

test("a version added from More is offered for comparing, and the notes stay as they are", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await openScore(page);
    // With one source there is no trace of the assembly.
    await expect(page.locator(".versions-label")).toHaveCount(0);
    await expect(page.locator("div.score g.note")).toHaveCount(53);

    await (await openMore(page)).getByLabel(/Add another version/).setInputFiles(cover);
    const offer = page.locator(".assembly-offer");
    await expect(offer.getByRole("heading")).toContainText("fur-elise-cover added");
    await expect(offer).toContainText("The notes have not changed");
    await expect(page.locator(".versions-label")).toHaveText("+1 version");
    await expect(page.locator("div.score g.note")).toHaveCount(53);
    expect(await violations(page)).toEqual([]);
    await offer.getByRole("button", { name: "Later" }).click();
    await expect(offer).toHaveCount(0);

    // «+1 version» opens the Sources: the notes and the version, which says in bars where it matches them.
    await page.locator(".versions-label").click();
    const sheet = page.locator(".perf-sheet");
    await expect(sheet.getByRole("heading", { name: "What to assemble from" })).toBeVisible();
    await expect(sheet.locator(".source-card")).toHaveCount(2);
    await expect(sheet.locator(".source-card").nth(1)).toContainText("fur-elise-cover");
    await expect(sheet.locator(".source-card").nth(1)).toContainText(/Matches in bars/);
    expect(await violations(page)).toEqual([]);
    expect(errors).toEqual([]);
});

test("comparing: a bar of the version is carried into the notes, undone, redone and returned from the journal", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const offer = await scoreWithCover(page);
    await offer.getByRole("button", { name: "Compare" }).click();

    // The pill holds a chip per source; the version is on the page.
    const pill = page.locator(".assembly-pill");
    await expect(pill.getByRole("radio")).toHaveText([/Notes/, /fur-elise-cover/]);
    await expect(pill.getByRole("radio", { name: /fur-elise-cover/ })).toHaveAttribute("aria-checked", "true");
    await expect(page.locator(".bar .shown-label")).toContainText("fur-elise-cover");

    // A bar of the version selected: the card says what it holds and where it is like the notes. The cover numbers its
    // bars from its first note, so the notes' bar 5 is its bar 6.
    await tapBar(page, 5);
    const card = page.locator(".assembly-card");
    await expect(card).toContainText("bar 6 of fur-elise-cover");
    await expect(card.locator(".similar")).toContainText("Like bar 5 of your notes");
    expect(await violations(page)).toEqual([]);

    // The carry sheet says what the carry will do in six lines, and the ghost is on the page.
    await card.getByRole("button", { name: "Into the notes…" }).click();
    const sheet = page.locator(".carry-sheet");
    await expect(sheet.getByRole("heading")).toContainText("from fur-elise-cover");
    await expect(sheet.locator(".sheet-line")).toHaveCount(6);
    await expect(sheet.getByRole("radio", { name: /replace .* bar 5/ })).toHaveAttribute("aria-checked", "true");
    await expect(page.locator(".score .assembly-ghost")).toHaveCount(1);
    await expect(page.locator(".bar .shown-label")).toContainText("Notes · ghost");
    expect(await violations(page)).toEqual([]);
    await sheet.getByRole("button", { name: "Carry" }).click();
    await expect(sheet).toHaveCount(0);
    await expect(page.locator(".merge-toast")).toContainText("Carry No. 1");
    await expect(page.locator(".bar .shown-label")).toContainText("Notes");
    await expect(page.locator(".score g.note.version-added")).toHaveCount(0);

    // Origin draws a line in the version's colour under what came from it.
    await page.locator(".bar").getByRole("button", { name: "Origin" }).click();
    await expect(page.locator(".score .origin-line.tone-plum").first()).toBeVisible();

    // ⌘Z and ⇧⌘Z step through the journal.
    const journalButton = page.locator(".bar").getByRole("button", { name: "Journal" });
    await page.keyboard.press("Control+z");
    await expect(page.locator(".score .origin-line")).toHaveCount(0);
    await page.keyboard.press("Control+Shift+z");
    await expect(page.locator(".score .origin-line").first()).toBeVisible();

    // The journal lists the carry; returning it shows first what goes with it.
    await journalButton.click();
    const journal = page.locator(".journal-sheet");
    await expect(journal.locator(".journal-row")).toHaveCount(1);
    await expect(journal.locator(".journal-row")).toContainText("No. 1 · ");
    await journal.getByRole("button", { name: "Return what was" }).click();
    await expect(journal.getByRole("heading")).toHaveText("Return what was: carry No. 1");
    await expect(journal.locator(".journal-row.going")).toHaveCount(1);
    expect(await violations(page)).toEqual([]);
    await journal.getByRole("button", { name: "Return No. 1" }).click();
    await expect(page.locator(".score .origin-line")).toHaveCount(0);
    await expect(page.locator(".bar").getByRole("button", { name: /Make it the notes/ })).toHaveCount(0);

    // Done folds the comparison away; nothing in the piece changed.
    await page.locator(".bar").getByRole("button", { name: "Done" }).click();
    await expect(page.locator(".assembly-pill")).toHaveCount(0);
    await expect(page.locator(".versions-label")).toHaveText("+1 version");
    expect(errors).toEqual([]);
});

test("side by side, the notes follow the version from below, and Mapping shows the version against them bar by bar", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const offer = await scoreWithCover(page);
    await offer.getByRole("button", { name: "Compare" }).click();
    await tapBar(page, 5);
    await expect(page.locator(".assembly-card")).toContainText("bar 6 of fur-elise-cover");

    // The drawer shows the notes, read only, with the place the selection matches selected.
    await page.locator(".bar").getByRole("button", { name: "Side by side" }).click();
    const drawer = page.locator(".beside-drawer");
    await expect(drawer.getByRole("radio", { name: /Notes/ })).toHaveAttribute("aria-checked", "true");
    await expect(drawer.locator(".score g.note").first()).toBeVisible();
    await expect(drawer.locator(".score .sel-band").first()).toBeVisible();
    // The page above is the same: the version, its selection and its card.
    await expect(page.locator(".bar .shown-label")).toContainText("fur-elise-cover");
    expect(await violations(page)).toEqual([]);

    // Mapping: a cell per bar of the version; a tap selects that bar on the page.
    await drawer.getByRole("button", { name: "Mapping" }).click();
    const cells = drawer.locator(".match-map .cell");
    await expect(cells.first()).toBeVisible();
    expect(await cells.count()).toBeGreaterThan(8);
    await expect(drawer.locator(".match-map .cell.partly, .match-map .cell.differs").first()).toBeVisible();
    expect(await violations(page)).toEqual([]);
    await cells.nth(7).click();
    await expect(page.locator(".assembly-card")).toContainText("bar 8 of fur-elise-cover");

    await drawer.getByRole("button", { name: "Close side by side" }).click();
    await expect(drawer).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("only «Make it the notes» changes the piece; it is undone, and the notes before stay a source", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const offer = await scoreWithCover(page);
    await offer.getByRole("button", { name: "Compare" }).click();
    await tapBar(page, 5);
    await page.locator(".assembly-card").getByRole("button", { name: "Into the notes…" }).click();
    await page.locator(".carry-sheet").getByRole("button", { name: "Carry" }).click();
    await expect(page.locator(".versions-label")).toHaveCount(0);

    await page.locator(".bar").getByRole("button", { name: /Make it the notes/ }).click();
    const dialog = page.getByRole("dialog", { name: /Changes: bar 5/ });
    await expect(dialog.locator(".sheet-line.same")).toContainText("Bar 5 · from fur-elise-cover");
    expect(await violations(page)).toEqual([]);

    // Undone from the toast, the piece is as it was.
    await dialog.getByRole("button", { name: "Make it the notes" }).click();
    const toast = page.locator(".merge-toast");
    await expect(toast).toContainText("The notes are updated · 1 place");
    await expect(page.locator(".assembly-pill")).toHaveCount(0);
    await toast.getByRole("button", { name: "Undo" }).click();
    await expect(page.locator(".versions-label")).toHaveText("+1 version · 1 carry");

    // Applied again, it stays after a reload: the notes before the assembly are a source of their own.
    await page.locator(".versions-label").click();
    const sheet = page.locator(".perf-sheet");
    await sheet.locator(".source-card").first().getByRole("button", { name: "Open the assembly" }).click();
    await page.locator(".bar").getByRole("button", { name: /Make it the notes/ }).click();
    await page.getByRole("dialog", { name: /Changes:/ }).getByRole("button", { name: "Make it the notes" }).click();
    await expect(page.locator(".merge-toast")).toContainText("The notes are updated");
    await page.reload();
    await page.locator(".versions-label").click();
    await expect(sheet.locator(".source-card")).toHaveCount(3);
    await expect(sheet.locator(".source-card").nth(1)).toContainText("Notes before the assembly");
    await expect(sheet.locator(".check-head .caption")).toContainText("The notes were updated from the assembly");
    expect(errors).toEqual([]);
});

test("a marker leads into the comparison at its bar, and the version can be removed and put back", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const offer = await scoreWithCover(page);
    await offer.getByRole("button", { name: "Later" }).click();

    // Nothing of the version is drawn over the notes until Extra information is on.
    const markers = page.locator(".score button.perf-marker");
    await expect(markers).toHaveCount(0);
    const extra = page.locator(".bar").getByRole("button", { name: "Extra information", exact: true });
    await extra.click();
    await expect(markers).toHaveText(["+3 · added notes", "−3 · simplified"]);
    await expect(markers.nth(1)).toHaveAccessibleName("Bar 7 in fur-elise-cover: −3 · simplified");
    await expect(page.locator(".score g.note.not-heard")).toHaveCount(3);

    // Each kind can be left out from the list under the chevron.
    await page.locator(".bar").getByRole("button", { name: "Choose what extra information shows" }).click();
    const list = page.locator(".layers-popover");
    await list.getByRole("checkbox", { name: "Differences from versions" }).click();
    await expect(markers).toHaveCount(0);
    await list.getByRole("checkbox", { name: "Differences from versions" }).click();
    await expect(markers).toHaveCount(2);
    await closePopover(page);

    await markers.nth(1).click();
    const pop = page.getByRole("dialog", { name: "Bar 7 · differs from «fur-elise-cover»" });
    await expect(pop).toContainText("−3 · simplified");
    expect(await violations(page)).toEqual([]);
    await pop.getByRole("button", { name: "Compare" }).click();
    await expect(page.locator(".assembly-pill")).toBeVisible();
    // The cover numbers its bars from its first note, so the notes' bar 7 is its bar 8.
    await expect(page.locator(".assembly-card")).toContainText("bar 8 of fur-elise-cover");
    await expect(page.locator(".assembly-card .similar")).toContainText("Like bar 7 of your notes");
    await page.locator(".bar").getByRole("button", { name: "Done" }).click();

    // Removed from its card in Sources, and put back from the toast.
    await page.locator(".versions-label").click();
    const sheet = page.locator(".perf-sheet");
    await sheet.locator(".source-card").nth(1).getByRole("button", { name: "More" }).click();
    await sheet.getByRole("button", { name: "Remove the version from the piece…" }).click();
    await expect(page.locator(".merge-toast")).toContainText("Version «fur-elise-cover» removed");
    await expect(page.locator(".versions-label")).toHaveCount(0);
    await page.locator(".merge-toast").getByRole("button", { name: "Undo" }).click();
    await expect(page.locator(".versions-label")).toHaveText("+1 version");
    expect(errors).toEqual([]);
});

test("the official score in MusicXML is added over notes from a performance", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(cover);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);

    await (await openMore(page)).getByLabel(/Add another version/).setInputFiles(score);
    await page.locator(".assembly-offer").getByRole("button", { name: "Later" }).click();
    // The mirror of the first test: what the cover added is now left out of the score, and the left hand it left out is added.
    await page.locator(".bar").getByRole("button", { name: "Extra information", exact: true }).click();
    await expect(page.locator(".score button.perf-marker")).toHaveText(["−3 · simplified", "+3 · added notes"]);
    await page.locator(".versions-label").click();
    await expect(page.locator(".perf-sheet .source-card").nth(1)).toContainText("fur-elise-original");
    expect(errors).toEqual([]);
});

test("in focus, the other version stands above the phrase, is heard, and leads into the comparison", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await (await scoreWithCover(page)).getByRole("button", { name: "Later" }).click();
    await page.locator(".bar").getByRole("button", { name: "Extra information", exact: true }).click();
    const markers = page.locator(".score button.perf-marker");

    // Bar 5 picked from its marker, then lifted into focus: the cover's bar stands above it.
    await markers.first().click();
    await expect(page.getByRole("dialog", { name: /^Bar 5 · differs/ })).toBeVisible();
    await page.locator(".ask-pop-backdrop").click();
    await expect(page.locator(".selection-status")).toContainText("bar 5");
    await page.locator(".ctx .menu").getByRole("button", { name: "Focus" }).click();
    await expect(page.locator(".focus-card")).toContainText("As in fur-elise-cover");
    const focusBar = page.locator(".bar.focus-bar");
    // The phrase is heard as in the notes, as in the version, or one after the other.
    const listen = focusBar.getByRole("radiogroup", { name: "What you hear" });
    for (const choice of ["Version", "Alternate", "Notes"]) {
        await listen.getByRole("radio", { name: choice }).click();
        await expect(listen.getByRole("radio", { name: choice })).toHaveAttribute("aria-checked", "true");
    }
    expect(await violations(page)).toEqual([]);
    await focusBar.getByRole("button", { name: "Compare" }).click();
    await expect(page.locator(".bar.focus-bar")).toHaveCount(0);
    await expect(page.locator(".assembly-pill")).toBeVisible();
    await expect(page.locator(".assembly-card")).toContainText("fur-elise-cover");
    expect(errors).toEqual([]);
});

/** The stand-in scan server, set in Settings: it answers with what Transkun heard in a recording of Für Elise. */
async function standInScanServer(page) {
    const heard = await readFile(fileURLToPath(new URL("../MotiffLab.MusicCore.Tests/Import/Transcription/Fixtures/transkun-fur-elise.mid", import.meta.url)));
    const uploads = [];
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Seconds, X-Cut" };
    await page.route(`${server}/health`, route => route.fulfill({
        json: { engine: "homr", version: "0.7.0", maxPages: 8, transcribe: { engine: "transkun", version: "2.0.1", maxMegabytes: 200, maxSeconds: 600 } },
        headers: cors,
    }));
    await page.route(`${server}/transcribe`, route => {
        uploads.push(route.request().postDataBuffer() ?? Buffer.alloc(0));
        return route.fulfill({ body: heard, contentType: "audio/midi", headers: { ...cors, "X-Engine": "transkun 2.0.1", "X-Seconds": "1.0", "X-Cut": "false" } });
    });
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();
    return uploads;
}

/** The piece from the score, a recording added to it from the Sources sheet: the card that offers to compare is up. */
async function scoreWithRecording(page) {
    const uploads = await standInScanServer(page);
    await openScore(page);
    await (await openMore(page)).getByRole("button", { name: "Sources" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.getByRole("button", { name: "Add a source" }).click();
    const fromRecording = drawer.getByRole("region", { name: "From a recording" });
    await expect(fromRecording).toContainText("The recording's sound, however long, goes to localhost part by part and is deleted once heard.");
    expect(await violations(page)).toEqual([]);
    await fromRecording.getByLabel("Choose an audio or video recording to add as a version").setInputFiles(await recording());
    // The whole piece was played: its opening, all that is printed here, agrees note for note.
    await expect(page.locator(".assembly-offer")).toContainText("Matches in bars");
    return uploads;
}

test("a recording is added as another version: its sound is heard on the scan server and its notes lined up", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const uploads = await scoreWithRecording(page);
    expect(uploads).toHaveLength(1);
    // Only the sound went, not the file under its name.
    expect(uploads[0].includes(Buffer.from("Take 1"))).toBe(false);
    await expect(page.locator(".assembly-offer").getByRole("heading")).toContainText("Take 1 at the piano added");
    expect(errors).toEqual([]);
});

test("a recording as a source plays its own sound over its waveform, and its fragment keeps the notes it agrees with", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await scoreWithRecording(page);
    await page.locator(".assembly-offer").getByRole("button", { name: "Compare" }).click();
    const pill = page.locator(".assembly-pill");
    await pill.getByRole("radio", { name: /Take 1 at the piano/ }).click();

    // Its sound stayed on this device: the strip over the pill shows its waveform, and moving along it moves playing.
    const wave = page.getByRole("group", { name: "The recording's waveform" });
    await expect(wave).toBeVisible();
    await expect(wave.locator("path")).toHaveAttribute("d", /^M/);
    await expect(wave.getByRole("slider", { name: "Where in the recording" })).toBeVisible();

    // A bar of the recording selected: the card says when it is heard and that the recording itself plays.
    await tapBar(page, 2);
    const card = page.locator(".assembly-card");
    await expect(card).toContainText(/\d+:\d\d–\d+:\d\d/);
    await expect(card).toContainText("The recording itself plays, not the piano.");
    await expect(wave.locator(".picked")).toBeVisible();
    expect(await violations(page)).toEqual([]);

    // Into the notes: the sheet first asks how to get its notes, keeping the notes and adding what is played otherwise.
    await card.getByRole("button", { name: "Into the notes…" }).click();
    const sheet = page.locator(".carry-sheet");
    await expect(sheet.locator(".sheet-line").first()).toContainText("How to get the notes");
    const how = sheet.getByRole("radiogroup", { name: "How to get the notes" });
    await expect(how.getByRole("radio", { name: "keep your notes and add what the performer plays otherwise" })).toHaveAttribute("aria-checked", "true");
    await expect(sheet.getByRole("button", { name: "Recording" })).toBeVisible();
    expect(await violations(page)).toEqual([]);
    await how.getByRole("radio", { name: "take the notes as the recording was heard" }).click();
    await expect(how.getByRole("radio", { name: "take the notes as the recording was heard" })).toHaveAttribute("aria-checked", "true");
    await how.getByRole("radio", { name: /keep your notes/ }).click();
    await sheet.getByRole("button", { name: "Carry" }).click();
    await expect(page.locator(".merge-toast")).toContainText("No. 1");
    expect(errors).toEqual([]);
});

test("the Sources sheet keeps a PDF inside the piece and opens it", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await openScore(page);
    await (await openMore(page)).getByRole("button", { name: "Sources" }).click();
    const drawer = page.locator(".perf-sheet");
    await expect(drawer.locator(".source-card")).toHaveCount(1);
    await drawer.getByRole("button", { name: "Add a source" }).click();
    await drawer.getByLabel("Add a PDF").setInputFiles({ name: "Für Elise.pdf", mimeType: "application/pdf", buffer: pdf(2) });
    await expect(drawer.getByRole("status")).toHaveText("The PDF is kept in the piece.");
    await expect(drawer.locator(".source-card", { hasText: "PDF kept in the piece" })).toBeVisible();
    const url = page.url();
    await drawer.getByRole("button", { name: "Open PDF" }).click();
    await expect(page).toHaveURL(new RegExp(`/pdf\\?project=${new URL(url).searchParams.get("project")}&page=1`));
    await expect(page.locator(".pdf-page")).toHaveCount(2);
    expect(errors).toEqual([]);
});
