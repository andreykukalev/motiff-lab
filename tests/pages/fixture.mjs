// Writes the fixtures the page scenarios open (tests/contracts/page-scenarios/fixtures, README.md there): the example
// piece as the iPad app gives it to its pages. music-core's notation (fixture.cs) is engraved by Verovio 6.3.0, the version
// the app builds (src/ipad/Verovio/build.sh), with the app's A4 options (Engraver.a4(evenBars: false): A4_LAYOUT and
// NOTE_SPACING of Scripts/score/score-page.ts, which tests/shared/score-pdf.test.mjs keeps equal to Engraver.swift), with
// the beat lines of music-core's playback score the app always sends it (SelectionTools.setBeatLines); the roll gets
// music-core's `roll` answer. Expand's card gets bars 5–20 engraved on their own with the card's options at the card's
// width on the scenarios' page (Engraver.engraveCard with `cardLayout` of Scripts/score/phrase-card.ts). Run after
// `dotnet build src/shared`, and only to change a fixture, which changes what the scenarios see:
// `node tests/pages/fixture.mjs`. With `--check` it writes nothing and fails when a committed
// fixture is not what it makes now (run.mjs runs it beside every run): a change to the engraving options, music-core's
// notation or Verovio reaches the scenarios only through a fixture remade and reviewed. The output is the same on every run.
import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const repo = fileURLToPath(new URL("../../", import.meta.url));
const shared = `${repo}src/shared/`;
const out = `${repo}tests/contracts/page-scenarios/fixtures/`;

/** The pages kept: the first, a whole page with its systems spread to its margins (VIEW-03), is enough for every scenario. */
const PAGES = 1;
/**
 * The card's width in CSS pixels on a page of 1024 × 1366, the scenarios' default: what score.js `focus` measures and
 * answers as `cardLayout(964)` (a scenario on the card expects that answer, so a card of another width fails it).
 */
const CARD_WIDTH = 964;

const { A4_LAYOUT, NOTE_SPACING } = await import(`${shared}wwwroot/js/score/score-page.js`);
const { cardLayout } = await import(`${shared}wwwroot/js/score/phrase-card.js`);
const { default: createVerovioModule } = await import(`${shared}wwwroot/lib/verovio/verovio-module.mjs`);
const { VerovioToolkit } = await import(`${shared}wwwroot/lib/verovio/verovio.mjs`);

const asked = (() => {
    try {
        return execFileSync("dotnet", ["run", `${repo}tests/pages/fixture.cs`, "--", `${shared}Playback/FurElise.musicxml`, "Für Elise"], { encoding: "utf8", maxBuffer: 1 << 26 });
    } catch (error) {
        if (error.code !== "ENOENT") {
            throw error;
        }
        console.log("fixture.mjs needs the dotnet command of the .NET SDK global.json names on PATH, to run fixture.cs: install it (https://dot.net), or put its folder on PATH with DOTNET_ROOT, as `export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet`.");
        return process.exit(1);
    }
})();
const { mei, card, beats, roll } = JSON.parse(asked);
const toolkit = new VerovioToolkit(await createVerovioModule());
// The seed gives the elements music-core names no id (the page's own, a system's) the same ids each time.
toolkit.setOptions({ ...A4_LAYOUT, ...NOTE_SPACING, xmlIdSeed: 1 });
if (!toolkit.loadData(mei)) {
    throw new Error(`Verovio could not lay out the notation: ${toolkit.getLog()}`);
}
// Without the indentation, which no reader of the page uses: a third of the file.
const pageCount = toolkit.getPageCount();
const pages = Array.from({ length: Math.min(PAGES, pageCount) }, (_, index) => toolkit.renderToSVG(index + 1).replace(/\n\s+/g, "\n"));
const timemap = toolkit.renderToTimemap({ includeRests: true, includeMeasures: true });
toolkit.setOptions({ ...cardLayout(CARD_WIDTH), xmlIdSeed: 1 });
if (!toolkit.loadData(card)) {
    throw new Error(`Verovio could not lay out the card's bars: ${toolkit.getLog()}`);
}
const cardPages = Array.from({ length: toolkit.getPageCount() }, (_, index) => toolkit.renderToSVG(index + 1).replace(/\n\s+/g, "\n"));
const cardTimemap = toolkit.renderToTimemap({ includeRests: true, includeMeasures: true });
const made = "node tests/pages/fixture.mjs: Für Elise (src/shared/Playback/FurElise.musicxml) as the iPad app gives it to its pages";
const files = {
    "fur-elise-score.json": `${JSON.stringify({ made, pages, timemap, beats, card: { pages: cardPages, timemap: cardTimemap } })}\n`,
    "fur-elise-roll.json": `${JSON.stringify({ made, roll })}\n`,
};
if (process.argv.includes("--check")) {
    const stale = Object.entries(files).filter(([name, text]) => readFileSync(`${out}${name}`, "utf8") !== text);
    for (const [name, text] of stale) {
        const committed = readFileSync(`${out}${name}`, "utf8");
        let at = 0;
        while (committed[at] === text[at]) {
            at++;
        }
        const around = (value) => JSON.stringify(value.slice(Math.max(0, at - 30), at + 30));
        console.log(`fixtures/${name} is not what fixture.mjs makes from the code now: from character ${at}, committed ${around(committed)}, made ${around(text)}.`);
    }
    if (stale.length > 0) {
        console.log("Remake them with `node tests/pages/fixture.mjs` after `dotnet build src/shared`, and review what moved (tests/contracts/page-scenarios/README.md).");
        process.exitCode = 1;
    }
} else {
    for (const [name, text] of Object.entries(files)) {
        writeFileSync(`${out}${name}`, text);
    }
    console.log(`${pages.length} pages of ${pageCount}, ${timemap.length} timemap entries, ${roll.ids.length} notes on the roll, ${cardPages.length} card pages`);
}
