// Another version compared with the piece (decision record 0022, sections 1, 4 and 5): a MIDI file of a performance added in
// the Other versions drawer, a plum marker above each bar it plays differently, the printed notes it did not play tinted, a bar
// taken into the score and restored, all kept with the project. The performance is Für Elise's opening with three notes
// added in bar 5 and the left hand of bar 7 left out (fixtures/fur-elise-cover.mid, made from the public-domain score). Which is the base is only which was opened first: the score
// in MusicXML can be added over the cover opened as the piece.
import { expect, test } from "@playwright/test";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
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

test("a MIDI performance marks the bars it plays differently, and a bar is taken into the score and restored", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);

    const more = page.locator(".bar").getByRole("button", { name: "More" });
    await more.click();
    await page.locator(".more-popover").getByRole("button", { name: "Add a version" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.locator("input[type=file]").setInputFiles(cover);
    await expect(drawer.getByRole("status")).toHaveText("Lined up: 50 of 53 notes found in the version, 2 bars differ.");

    const markers = page.locator(".score button.perf-marker");
    await expect(markers).toHaveText(["+3 · added notes", "−3 · simplified"]);
    await expect(markers.nth(1)).toHaveAccessibleName("Bar 7 in fur-elise-cover: −3 · simplified");
    await expect(page.locator(".score g.note.not-heard")).toHaveCount(3);
    expect(await violations(page)).toEqual([]);

    // The comparison layer: the cover on small staves above every system, the three notes it adds in plum.
    await (await openMore(page)).getByRole("button", { name: "Layers" }).click();
    const versionLayer = page.locator(".layers-popover").getByRole("switch", { name: "Other version above the staves" });
    await expect(versionLayer).not.toBeChecked();
    await versionLayer.click();
    await expect(page.locator(".score g.note.version-added")).toHaveCount(3);
    await expect(page.locator(".score .score-page svg").first()).toContainText("fur-elise-cover");
    await expect(markers).toHaveText(["+3 · added notes", "−3 · simplified"]);
    await closePopover(page);
    expect(await violations(page)).toEqual([]);
    await (await openMore(page)).getByRole("button", { name: "Layers" }).click();
    await versionLayer.click();
    await expect(page.locator(".score g.note.version-added")).toHaveCount(0);
    await closePopover(page);
    await page.locator(".bar").getByRole("button", { name: "More" }).click();
    await page.locator(".more-popover").getByRole("button", { name: "Other versions" }).click();

    // A marker selects its bar in the drawer.
    await markers.nth(1).click();
    await expect(drawer.locator(".check-entry.on")).toContainText("Bar 7");

    // Bar 5 taken: its marker goes, it is listed as taken, and so it stays after a reload.
    await drawer.locator(".check-entry", { hasText: "Bar 5" }).getByRole("button", { name: "Take into the score" }).click();
    await expect(markers).toHaveText(["−3 · simplified"]);
    await expect(drawer.locator(".resolved .check-entry")).toContainText("Bar 5");
    await page.reload();
    await expect(page.locator(".score button.perf-marker")).toHaveText(["−3 · simplified"]);

    // Restore these notes: the marker comes back. Not a difference: bar 7's marker goes.
    await page.locator(".bar").getByRole("button", { name: "More" }).click();
    await page.locator(".more-popover").getByRole("button", { name: "Other versions" }).click();
    await drawer.getByRole("button", { name: "Restore these notes" }).click();
    await expect(markers).toHaveText(["+3 · added notes", "−3 · simplified"]);
    await drawer.locator(".check-entry", { hasText: "Bar 7" }).getByRole("button", { name: "Not a difference" }).click();
    await expect(markers).toHaveText(["+3 · added notes"]);
    await expect(page.locator(".score g.note.not-heard")).toHaveCount(0);

    // Removed, asked once: no markers are left.
    await drawer.getByRole("button", { name: "Remove the version" }).click();
    await drawer.getByRole("button", { name: "Remove it for good" }).click();
    await expect(markers).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("the official score in MusicXML is added over notes from a performance", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(cover);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);

    await page.locator(".bar").getByRole("button", { name: "More" }).click();
    await page.locator(".more-popover").getByRole("button", { name: "Add a version" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.locator("input[type=file]").setInputFiles(score);
    await expect(drawer.getByRole("status")).toContainText("Lined up:");
    // The mirror of the first test: what the cover added is now left out of the score, and the left hand it left out is added.
    await expect(page.locator(".score button.perf-marker")).toHaveText(["−3 · simplified", "+3 · added notes"]);
    await expect(drawer.getByLabel("Name")).toHaveValue("fur-elise-original");
    expect(errors).toEqual([]);
});

test("in focus, the other version stands above the phrase, is heard, and its bars are taken into the score at once", async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("/");
    await page.getByRole("button", { name: "Add piece" }).click();
    await page.getByLabel("Choose a MIDI, MusicXML or PDF file").setInputFiles(score);
    await expect(page).toHaveURL(/\/playback\?project=p_[0-9a-f]+/);
    await (await openMore(page)).getByRole("button", { name: "Add a version" }).click();
    const drawer = page.locator(".perf-sheet");
    await drawer.locator("input[type=file]").setInputFiles(cover);
    await expect(drawer.getByRole("status")).toContainText("Lined up:");
    const markers = page.locator(".score button.perf-marker");

    // Bar 5 picked from its marker, then lifted into focus: the cover's bar stands above it.
    await markers.first().click();
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
    await focusBar.getByRole("button", { name: "Take into the score" }).click();
    await expect(focusBar.getByRole("button", { name: "Restore these notes" })).toBeVisible();
    await expect(focusBar.getByRole("button", { name: "Take into the score" })).toHaveCount(0);
    expect(await violations(page)).toEqual([]);

    await focusBar.getByRole("button", { name: "Restore these notes" }).click();
    await expect(focusBar.getByRole("button", { name: "Take into the score" })).toBeVisible();
    await focusBar.getByRole("button", { name: "Back to page" }).click();
    await expect(markers).toHaveText(["+3 · added notes", "−3 · simplified"]);
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
    await (await openMore(page)).getByRole("button", { name: "Add a version" }).click();
    const drawer = page.locator(".perf-sheet");
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

    // On the comparison layer, the notes Transkun likely heard wrongly are left out, counted, and shown grey when asked.
    await (await openMore(page)).getByRole("button", { name: "Layers" }).click();
    const layers = page.locator(".layers-popover");
    await layers.getByRole("switch", { name: "Other version above the staves" }).click();
    const artefacts = layers.getByRole("switch", { name: /^Likely artefacts of the hearing \(\d+\)$/ });
    await expect(artefacts).not.toBeChecked();
    const count = Number((await artefacts.locator("xpath=..").innerText()).match(/\((\d+)\)/)[1]);
    expect(count).toBeGreaterThan(0);
    await expect(page.locator(".score g.note.version-artefact")).toHaveCount(0);
    await artefacts.click();
    await expect(page.locator(".score g.note.version-artefact")).toHaveCount(count);
    expect(await violations(page)).toEqual([]);
    expect(errors).toEqual([]);
});
