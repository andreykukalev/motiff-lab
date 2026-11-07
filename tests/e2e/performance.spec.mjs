// Another version compared with the piece (decision record 0022): a MIDI file of a performance added in the Sources sheet
// (testing item 16) opens its page there, with a verdict, a strip of bars and the way into a merge draft that changes nothing
// until applied (testing items 32 and 34); the score stays clean, its markers shown only with Extra information (item 33). The performance is Für Elise's opening with three notes
// added in bar 5 and the left hand of bar 7 left out (fixtures/fur-elise-cover.mid, made from the public-domain score). Which is the base is only which was opened first: the score
// in MusicXML can be added over the cover opened as the piece.
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

/** The piece from the score, the cover added over it in Sources: the drawer stays on the cover's page. */
async function scoreWithCover(page) {
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
    await (await openMore(page)).getByRole("button", { name: "Sources" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.getByRole("button", { name: "Add a source" }).click();
    await drawer.getByLabel("Add a MIDI or MusicXML file").setInputFiles(cover);
    await expect(drawer.getByRole("status")).toHaveText("Lined up: 50 of 53 notes found in the version, 2 bars differ.");
    return drawer;
}

test("a version opens its page, the score stays clean, and its markers show only with Extra information", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const drawer = await scoreWithCover(page);
    await expect(drawer.getByRole("heading", { name: "fur-elise-cover" })).toBeVisible();
    await expect(drawer.locator(".merge-verdict")).toContainText("fur-elise-cover plays 2 bars differently");
    await expect(drawer.getByRole("list", { name: "The bars of the piece" }).getByRole("listitem", { name: /^Bar 5:/ })).toHaveAccessibleName("Bar 5: +3 · added notes");
    expect(await violations(page)).toEqual([]);

    // Nothing of the version is drawn over the notes until Extra information is on.
    const markers = page.locator(".score button.perf-marker");
    await expect(markers).toHaveCount(0);
    await expect(page.locator(".score g.note.not-heard")).toHaveCount(0);
    const extra = page.locator(".bar").getByRole("button", { name: "Extra information", exact: true });
    await expect(extra).toHaveAttribute("aria-pressed", "false");
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

    // A marker tells how the bar differs and leads into the draft; nothing is taken from the score itself.
    await markers.nth(1).click();
    const pop = page.getByRole("dialog", { name: "Bar 7 · differs from «fur-elise-cover»" });
    await expect(pop).toContainText("−3 · simplified");
    await expect(pop.getByRole("button", { name: "As in the version" })).toBeVisible();
    expect(await violations(page)).toEqual([]);
    await pop.getByRole("button", { name: "Open in the draft" }).click();
    const draft = drawer.getByRole("region", { name: "Merge draft" });
    await expect(draft.locator(".merge-step")).toContainText("Bar 7");
    await expect(draft).toContainText("Decided 0 of 2");
    expect(errors).toEqual([]);
});

test("the draft changes nothing until applied, every step is undone and redone, and the merge is undone after", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const drawer = await scoreWithCover(page);
    await drawer.getByRole("button", { name: "Keep my notes" }).click();
    const draft = drawer.getByRole("region", { name: "Merge draft" });
    await expect(draft).toContainText("0 bars will change after Apply");
    await expect(draft.locator(".merge-step")).toContainText("Bar 5");

    // Bar 5 from the version: the result shows its three added notes in plum, the score is as before.
    await draft.getByRole("button", { name: "Take from the version" }).click();
    await expect(draft).toContainText("1 bar will change after Apply");
    await expect(draft.locator(".merge-state")).toHaveText("Now: from the version");
    const show = page.getByRole("group", { name: "Show:" });
    await expect(show.getByRole("button", { name: "Result", exact: true })).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".score g.note.version-added")).toHaveCount(3);
    await show.getByRole("button", { name: "Mine", exact: true }).click();
    await expect(page.locator(".score g.note.version-added")).toHaveCount(0);
    await show.getByRole("button", { name: "Result", exact: true }).click();
    expect(await violations(page)).toEqual([]);

    // Bar 7 too, then undone, redone, and the draft reset and the reset undone.
    await draft.getByRole("button", { name: "Next change" }).click();
    await expect(draft.locator(".merge-step")).toContainText("Bar 7");
    await draft.getByRole("button", { name: "Take from the version" }).click();
    await expect(draft).toContainText("2 bars will change after Apply");
    await draft.getByRole("button", { name: "Undo" }).click();
    await expect(draft).toContainText("1 bar will change after Apply");
    await draft.getByRole("button", { name: "Redo" }).click();
    await expect(draft).toContainText("2 bars will change after Apply");
    await draft.getByRole("button", { name: "Reset the draft" }).click();
    await expect(draft).toContainText("0 bars will change after Apply");
    await draft.getByRole("button", { name: "Undo" }).click();
    await expect(draft).toContainText("2 bars will change after Apply");

    // Applied: the page says what was merged, and so it stays after a reload, a thin line under the bars taken when asked.
    await draft.getByRole("button", { name: "Apply" }).click();
    await expect(page.locator(".merge-toast")).toContainText("Merge applied: 2 bars from the version");
    await expect(drawer.locator(".merge-applied")).toContainText(/Merge applied \d\d:\d\d · 2 bars/);
    await page.reload();
    await page.locator(".bar").getByRole("button", { name: "Extra information", exact: true }).click();
    await expect(page.locator(".score .taken-bar")).toHaveCount(2);
    await expect(page.locator(".score button.perf-marker")).toHaveCount(0);

    // Undo the merge: the notes are back, the choices kept to open again.
    await (await openMore(page)).getByRole("button", { name: "Sources" }).click();
    await drawer.getByRole("button", { name: /fur-elise-cover/ }).click();
    await drawer.getByRole("button", { name: "Undo the merge" }).click();
    await expect(page.locator(".merge-toast")).toContainText("Merge undone: your notes are back. The choices are kept.");
    await expect(page.locator(".score .taken-bar")).toHaveCount(0);
    await expect(page.locator(".score button.perf-marker")).toHaveCount(2);
    await drawer.getByRole("button", { name: "Open the draft again" }).click();
    await expect(draft).toContainText("Decided 2 of 2");
    await draft.getByRole("button", { name: "Cancel" }).click();

    // Removed, and put back from its toast.
    await drawer.getByRole("button", { name: "Remove the version from the piece…" }).click();
    await expect(page.locator(".merge-toast")).toContainText("Version «fur-elise-cover» removed");
    await expect(page.locator(".score button.perf-marker")).toHaveCount(0);
    await page.locator(".merge-toast").getByRole("button", { name: "Undo" }).click();
    await expect(page.locator(".score button.perf-marker")).toHaveCount(2);
    expect(errors).toEqual([]);
});

test("Compare versions shows mine, the version and the result for a change, taken by key or by group", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const drawer = await scoreWithCover(page);
    await drawer.getByRole("button", { name: /Compare versions/ }).click();
    const window = page.getByRole("dialog", { name: "Compare versions" });
    await expect(window.locator(".merge-view")).toHaveCount(3);
    await expect(window.locator(".merge-view.version .phrase-card-score g.note")).not.toHaveCount(0);
    await expect(window.locator(".merge-step")).toContainText("Bar 5");
    expect(await violations(page)).toEqual([]);

    // V takes the bar from the version, → goes to the next, M keeps mine there.
    await window.press("v");
    await expect(window.locator(".merge-state")).toHaveText("Now: from the version");
    await window.press("ArrowRight");
    await expect(window.locator(".merge-step")).toContainText("Bar 7");
    await window.press("m");
    await expect(window.locator(".merge-state")).toHaveText("Now: mine");
    await expect(window).toContainText("Decided 2 of 2");
    await window.getByRole("button", { name: "Reset the bar" }).click();
    await expect(window).toContainText("Decided 1 of 2");

    // A group at once, then what is shown and the size of the notes.
    await window.getByRole("button", { name: "Take the version for the whole group" }).first().click();
    await window.getByRole("radio", { name: "the whole phrase" }).click();
    await expect(window.getByRole("radio", { name: "the whole phrase" })).toHaveAttribute("aria-checked", "true");
    await window.getByRole("button", { name: "Larger notes" }).click();
    await expect(window.getByRole("group", { name: "Note size" })).toContainText("110 %");

    await window.getByRole("button", { name: "Apply", exact: true }).click();
    await expect(window).toHaveCount(0);
    await expect(drawer.locator(".merge-applied")).toBeVisible();
    expect(errors).toEqual([]);
});

test("the official score in MusicXML is added over notes from a performance", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(cover);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);

    await (await openMore(page)).getByRole("button", { name: "Sources" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.getByRole("button", { name: "Add a source" }).click();
    await drawer.getByLabel("Add a MIDI or MusicXML file").setInputFiles(score);
    await expect(drawer.getByRole("status")).toContainText("Lined up:");
    // The mirror of the first test: what the cover added is now left out of the score, and the left hand it left out is added.
    await page.locator(".bar").getByRole("button", { name: "Extra information", exact: true }).click();
    await expect(page.locator(".score button.perf-marker")).toHaveText(["−3 · simplified", "+3 · added notes"]);
    await expect(drawer.getByLabel("Name")).toHaveValue("fur-elise-original");
    expect(errors).toEqual([]);
});

test("in focus, the other version stands above the phrase, is heard, and opens in the draft", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    const drawer = await scoreWithCover(page);
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
    await focusBar.getByRole("button", { name: "Open in the draft" }).click();
    await expect(page.locator(".bar.focus-bar")).toHaveCount(0);
    await expect(drawer.getByRole("region", { name: "Merge draft" }).locator(".merge-step")).toContainText("Bar 5");
    expect(errors).toEqual([]);
});

test("a recording is added as another version: its sound is heard on the scan server and its notes lined up", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    // The scan server is played by the test: it answers with what Transkun heard in a recording of Für Elise.
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

    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
    await (await openMore(page)).getByRole("button", { name: "Sources" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.getByRole("button", { name: "Add a source" }).click();
    const fromRecording = drawer.getByRole("region", { name: "From a recording" });
    await expect(fromRecording).toContainText("The recording's sound, up to 7 minutes, goes to localhost and is deleted once heard.");
    expect(await violations(page)).toEqual([]);

    await fromRecording.getByLabel("Choose an audio or video recording to add as a version").setInputFiles(await recording());
    // The whole piece was played: its opening, all that is printed here, agrees note for note.
    await expect(drawer.getByRole("status").first()).toHaveText("Lined up: 53 of 53 notes found in the version, 0 bars differ.");
    expect(uploads).toHaveLength(1);
    // Only the sound went, not the file under its name.
    expect(uploads[0].includes(Buffer.from("Take 1"))).toBe(false);
    await expect(drawer.getByLabel("Name")).toHaveValue("Take 1 at the piano");
    await expect(drawer.locator(".merge-verdict")).toHaveText("Take 1 at the piano plays every bar as your notes have it.");
    expect(errors).toEqual([]);
});

test("the Sources sheet names the base, takes the version at once when asked and keeps a PDF inside the piece", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
    await (await openMore(page)).getByRole("button", { name: "Sources" }).click();
    const drawer = page.locator(".perf-sheet");
    await expect(drawer.getByRole("heading", { name: "My version" })).toBeVisible();
    await expect(drawer.locator(".sources-summary")).toHaveText("Base: the notes of the file");

    await drawer.getByRole("button", { name: "Add a source" }).click();
    await drawer.getByLabel("Add a MIDI or MusicXML file").setInputFiles(cover);
    await expect(drawer.getByRole("status")).toContainText("Lined up:");

    // Apply at once, without a draft: the bars come in, and the toast undoes it.
    await drawer.getByRole("checkbox", { name: "Apply at once, without a draft" }).check();
    await drawer.getByRole("button", { name: /Take the version wherever it differs/ }).click();
    await expect(drawer.getByRole("region", { name: "Merge draft" })).toHaveCount(0);
    await expect(drawer.locator(".merge-applied")).toContainText("· 2 bars");
    await page.locator(".merge-toast").getByRole("button", { name: "Undo" }).click();
    await expect(drawer.locator(".merge-applied")).toHaveCount(0);

    await drawer.getByRole("button", { name: "Back to Sources" }).click();
    await expect(drawer.getByRole("button", { name: /fur-elise-cover/ })).toContainText("Performance");

    // A PDF kept inside the piece: its row opens the "PDF" view of this same piece.
    await drawer.getByRole("button", { name: "Add a source" }).click();
    await drawer.getByLabel("Add a PDF").setInputFiles({ name: "Für Elise.pdf", mimeType: "application/pdf", buffer: pdf(2) });
    await expect(drawer.getByRole("status")).toHaveText("The PDF is kept in the piece.");
    await expect(drawer.locator(".source-row", { hasText: "PDF kept in the piece" })).toBeVisible();
    const url = page.url();
    await drawer.getByRole("button", { name: "Open PDF" }).click();
    await expect(page).toHaveURL(new RegExp(`/pdf\\?project=${new URL(url).searchParams.get("project")}&page=1`));
    await expect(page.locator(".pdf-page")).toHaveCount(2);
    expect(errors).toEqual([]);
});
