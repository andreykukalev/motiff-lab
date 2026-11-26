// Tests for the roll's painter (Scripts/roll/roll-painter.ts, W3.12), run in Node on the compiled output over the fake
// page's recording canvas (roll-fake-dom.mjs): what the notes canvas shows at the edges of the time in view.
import assert from "node:assert/strict";
import test from "node:test";
import { fakePage } from "./roll-fake-dom.mjs";

const page = fakePage();
const { RollModel } = await import("../../src/shared/wwwroot/js/roll/roll-model.js");
const { RollPainter } = await import("../../src/shared/wwwroot/js/roll/roll-painter.js");
const { palette, readPalette } = await import("../../src/shared/wwwroot/js/roll/roll-palette.js");
readPalette(page.host);

const GUTTER = 62;
const model = RollModel.fromPayload({ ids: ["a"], notes: [960, 480, 64, 0, 0, 2, 0], bars: [0, 1440, 480, 1, 1440, 1440, 480, 2, 2880, 1440, 480, 3], end: 4320 });
/** The view from tick 960, a quarter 48 pixels wide, on a roll 480 wide. */
const frame = { width: 480, height: 200, header: 24, low: 58, high: 70, row: 14, above: false, below: false, from: 960, to: 960 + 4180, zoom: 48, xOf: (tick) => GUTTER + ((tick - 960) * 48) / 480 };

function scene(band) {
    return {
        model, lane: () => 0, sounding: new Set(), chosen: new Set(), motif: null, filtered: null, highlight: null, variation: null,
        variationAudible: false, ghosts: [], phrases: [], checks: [], band, grips: true, play: null, tick: 0, noteNames: false,
        name: () => "", shortName: () => "", rowName: () => "", octaveName: () => ({ words: "", c: "" }),
        placed: (index) => ({ onset: model.notes[index].onset, pitch: model.notes[index].pitch, length: 480 }), picked: () => false,
    };
}

/** The accent's lines a pixel wide down the whole roll: the band's edges, by where they are across. */
function edgeLines(band) {
    const canvas = document.createElement("canvas");
    new RollPainter().paint(canvas, frame, scene(band));
    const lines = [];
    let colour = "";
    for (const line of canvas.log) {
        if (line.startsWith("fillStyle=")) {
            colour = JSON.parse(line.slice("fillStyle=".length));
        }
        const rect = /^fillRect (\S+) 0 1 200$/.exec(line);
        if (rect !== null && colour === palette.accent) {
            lines.push(Number(rect[1]));
        }
    }
    return lines;
}

test("the selection's band draws its edges where they are, both when both are in view", () => {
    assert.deepEqual(edgeLines({ start: 1440, end: 2880 }), [110, 253]);
});

test("a band running out of view draws no edge at the side of the view it runs past", () => {
    assert.deepEqual(edgeLines({ start: 480, end: 1440 }), [109], "begun before the view: only its end, no line at the gutter");
    assert.deepEqual(edgeLines({ start: 1200, end: 9000 }), [86], "running past the right: only its start, no line at the roll's edge");
    assert.deepEqual(edgeLines({ start: 0, end: 9000 }), [], "past both sides: no edge in view");
});
