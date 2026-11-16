// Writes the fixtures the page scenarios open (tests/contracts/page-scenarios/fixtures, README.md there): the example
// piece as the iPad app gives it to its pages. music-core's notation (fixture.cs) is engraved by Verovio 6.3.0, the version
// the app builds (src/ipad/Verovio/build.sh), with the app's A4 options (Engraver.a4(evenBars: false): A4_LAYOUT and
// NOTE_SPACING of Scripts/score/score-page.ts, which tests/shared/score-pdf.test.mjs keeps equal to Engraver.swift), with
// the beat lines of music-core's playback score the app always sends it (SelectionTools.setBeatLines); the roll gets
// music-core's `roll` answer. Run after `dotnet build src/shared`, and only to change a fixture, which changes
// what the scenarios see: `node tests/pages/fixture.mjs`.
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const repo = fileURLToPath(new URL("../../", import.meta.url));
const shared = `${repo}src/shared/`;
const out = `${repo}tests/contracts/page-scenarios/fixtures/`;

/** The pages kept: the first, a whole page with its systems spread to its margins (VIEW-03), is enough for every scenario. */
const PAGES = 1;

const { A4_LAYOUT, NOTE_SPACING } = await import(`${shared}wwwroot/js/score/score-page.js`);
const { default: createVerovioModule } = await import(`${shared}wwwroot/lib/verovio/verovio-module.mjs`);
const { VerovioToolkit } = await import(`${shared}wwwroot/lib/verovio/verovio.mjs`);

const asked = execFileSync("dotnet", ["run", `${repo}tests/pages/fixture.cs`, "--", `${shared}Playback/FurElise.musicxml`, "Für Elise"], { encoding: "utf8", maxBuffer: 1 << 26 });
const { mei, beats, roll } = JSON.parse(asked);
const toolkit = new VerovioToolkit(await createVerovioModule());
// The seed gives the elements music-core names no id (the page's own, a system's) the same ids each time.
toolkit.setOptions({ ...A4_LAYOUT, ...NOTE_SPACING, xmlIdSeed: 1 });
if (!toolkit.loadData(mei)) {
    throw new Error(`Verovio could not lay out the notation: ${toolkit.getLog()}`);
}
// Without the indentation, which no reader of the page uses: a third of the file.
const pages = Array.from({ length: Math.min(PAGES, toolkit.getPageCount()) }, (_, index) => toolkit.renderToSVG(index + 1).replace(/\n\s+/g, "\n"));
const timemap = toolkit.renderToTimemap({ includeRests: true, includeMeasures: true });
const made = "node tests/pages/fixture.mjs: Für Elise (src/shared/Playback/FurElise.musicxml) as the iPad app gives it to its pages";
writeFileSync(`${out}fur-elise-score.json`, `${JSON.stringify({ made, pages, timemap, beats })}\n`);
writeFileSync(`${out}fur-elise-roll.json`, `${JSON.stringify({ made, roll })}\n`);
console.log(`${pages.length} pages of ${toolkit.getPageCount()}, ${timemap.length} timemap entries, ${roll.ids.length} notes on the roll`);
