// Transcribing a recording (decision record 0014): Import takes the sound out of a recording in the browser,
// sends only that sound to the scan server, and opens the MIDI it hears with every note marked as detected.
// The scan server is played by the test: it answers with what Basic Pitch heard in a synthesised melody.
// Runs in every engine, since decoding the recording is the browser's own work.
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const fixtures = "../MotiffLab.MusicCore.Tests/Import/Transcription/Fixtures/";
const heard = fileURLToPath(new URL(`${fixtures}basic-pitch-melody.mid`, import.meta.url));
const furElise = fileURLToPath(new URL(`${fixtures}transkun-fur-elise.mid`, import.meta.url));
const server = "http://localhost:8765";

// The app's service worker would answer the page's requests before the test's stand-in server could.
test.use({ serviceWorkers: "block" });

/** A second of a 440 Hz tone in stereo at 44.1 kHz, as a WAV file the browser decodes and mixes down. */
async function recording() {
    const rate = 44100;
    const frames = rate;
    const bytes = Buffer.alloc(44 + frames * 4);
    bytes.write("RIFF", 0);
    bytes.writeUInt32LE(36 + frames * 4, 4);
    bytes.write("WAVEfmt ", 8);
    bytes.writeUInt32LE(16, 16);
    bytes.writeUInt16LE(1, 20);
    bytes.writeUInt16LE(2, 22);
    bytes.writeUInt32LE(rate, 24);
    bytes.writeUInt32LE(rate * 4, 28);
    bytes.writeUInt16LE(4, 32);
    bytes.writeUInt16LE(16, 34);
    bytes.write("data", 36);
    bytes.writeUInt32LE(frames * 4, 40);
    for (let i = 0; i < frames; i++) {
        const sample = Math.round(Math.sin((2 * Math.PI * 440 * i) / rate) * 12000);
        bytes.writeInt16LE(sample, 44 + i * 4);
        bytes.writeInt16LE(sample, 46 + i * 4);
    }
    const path = join(tmpdir(), "Take 1 at the piano.wav");
    await writeFile(path, bytes);
    return path;
}

/** Plays the scan server, answering with the MIDI file given; returns the uploads it received. */
async function scanServer(page, answer = heard) {
    const uploads = [];
    const midi = await readFile(answer);
    const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Expose-Headers": "X-Engine, X-Pages, X-Seconds, X-Cut" };
    await page.route(`${server}/health`, route => route.fulfill({
        json: { engine: "homr", version: "0.7.0", maxPages: 8, transcribe: { engine: "basic-pitch", version: "0.4.0", maxMegabytes: 200, maxSeconds: 600 } },
        headers: cors,
    }));
    await page.route(`${server}/transcribe`, route => {
        uploads.push(route.request().postDataBuffer() ?? Buffer.alloc(0));
        return route.fulfill({ body: midi, contentType: "audio/midi", headers: { ...cors, "X-Engine": "basic-pitch 0.4.0", "X-Seconds": "1.0", "X-Cut": "false" } });
    });
    return uploads;
}

async function violations(page) {
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
    return results.violations.map(v => `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.slice(0, 5).map(n => n.target.join(" ")).join("\n  ")}`);
}

test("transcription is off until Settings has a scan server", async ({ page }) => {
    await page.goto("/import/recording");
    await expect(page.getByText("Set up the scan server in Settings")).toBeVisible();
    await expect(page.getByLabel("Choose an audio or video recording to transcribe")).toHaveCount(0);
});

test("a recording's sound is sent as mono audio, and its notes open marked as heard", async ({ page }) => {
    const uploads = await scanServer(page);
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();

    await page.goto("/import/recording");
    await page.getByLabel("Choose an audio or video recording to transcribe").setInputFiles(await recording());
    await expect(page.getByText("Transcribed by basic-pitch 0.4.0")).toBeVisible();
    await expect(page.locator(".report .summary")).toContainText("7 notes");
    await expect(page.getByRole("heading", { name: "Check the transcription" })).toBeVisible();

    // Only the sound went, as 16-bit mono at 22,050 Hz, and not under the file's name.
    expect(uploads).toHaveLength(1);
    const body = uploads[0];
    expect(body.includes(Buffer.from("Take 1"))).toBe(false);
    const wav = body.indexOf(Buffer.from("RIFF"));
    expect(wav).toBeGreaterThan(0);
    expect(body.toString("latin1", wav + 8, wav + 12)).toBe("WAVE");
    expect(body.readUInt16LE(wav + 22)).toBe(1);
    expect(body.readUInt32LE(wav + 24)).toBe(22050);
    expect(await violations(page)).toEqual([]);

    await page.getByText("Open the piece").click();
    await expect(page).toHaveURL(/playback/);
    await page.goto("/");
    await page.getByRole("link", { name: "Take 1 at the piano.mid" }).first().click();
    await expect(page.getByText("heard in a recording")).toBeVisible();
});

test("a transcription is written in bars and a tempo worked out from its notes, which the player can change", async ({ page }) => {
    await scanServer(page, furElise);
    await page.goto("/settings");
    await page.getByRole("button", { name: "Save and check" }).click();
    await expect(page.getByText("Connected: the scan server runs homr 0.7.0")).toBeVisible();

    await page.goto("/import/recording");
    await page.getByLabel("Choose an audio or video recording to transcribe").setInputFiles(await recording());
    const bars = page.getByRole("region", { name: "Bars and tempo" });
    await expect(bars.getByText("3/8 with a quarter at 72 a minute")).toBeVisible();
    await expect(bars.getByText("The piece starts with a pickup.")).toBeVisible();
    await expect(page.getByText("were worked out from the notes", { exact: false })).toBeVisible();
    expect(await violations(page)).toEqual([]);

    // Another time signature lays out the same notes again; twice the tempo halves the 16th.
    await bars.getByLabel("Time signature").selectOption("6/8");
    await expect(bars.getByText("6/8 with a quarter at 72 a minute")).toBeVisible();
    await bars.getByRole("button", { name: "Twice the tempo" }).click();
    await expect(bars.getByText("6/8 with a quarter at 144 a minute")).toBeVisible();

    // Off, the engine's 4/4 at 120 comes back.
    await bars.getByLabel("Write the notes in bars worked out from the playing").uncheck();
    await expect(bars.getByText("The notes stay in the bars the engine wrote", { exact: false })).toBeVisible();
    await expect(page.getByText("were worked out from the notes", { exact: false })).toHaveCount(0);
    await bars.getByLabel("Write the notes in bars worked out from the playing").check();
    await bars.getByLabel("Time signature").selectOption("3/8");
    await bars.getByRole("button", { name: "Half the tempo" }).click();
    await expect(bars.getByText("3/8 with a quarter at 72 a minute")).toBeVisible();

    // The workspace writes the notes in those bars, with short gaps held.
    await page.getByText("Open the piece").click();
    await expect(page).toHaveURL(/playback/);
    await expect(page.getByText("shown held to the next note", { exact: false })).toBeVisible();
    await page.goto("/");
    await page.getByRole("link", { name: "Take 1 at the piano.mid" }).first().click();
    await expect(page.getByText("heard in a recording")).toBeVisible();
});
