// Tests for the one rule of what a held playhead sounds (#582 items 12 and 13, scrubStrikes in
// Scripts/playback/playback-processor.ts) on the case file the iPad's engine runs too
// (tests/ipad/MotiffLabTests/Golden/scrub.json, PlaybackTests.swift), in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { SCRUB_GROUPS, scrubStrikes } from "../../src/MotiffLab.Web/wwwroot/js/playback/playback-processor.js";

const root = new URL("../../", import.meta.url);
const { groups, cases } = JSON.parse(readFileSync(new URL("tests/ipad/MotiffLabTests/Golden/scrub.json", root), "utf8"));

test("the case file strikes at most as many starts a move as the engine does", () => {
    assert.equal(groups, SCRUB_GROUPS);
    assert.ok(cases.length >= 10);
});

for (const one of cases) {
    test(one.name, () => {
        const strikes = scrubStrikes(one.onsets, one.from, one.to, one.opening, groups);
        assert.deepEqual(strikes.map((strike) => strike.onset), one.strikes.map((strike) => strike.onset));
        strikes.forEach((strike, index) => assert.ok(Math.abs(strike.at - one.strikes[index].at) < 1e-9, `${strike.onset}: how far through the move`));
    });
}
