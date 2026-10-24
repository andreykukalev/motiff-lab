// Tests for the project store's pure helpers (S1-P1), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { NEARLY_FULL, isNearlyFull, sortSummaries } from "../../src/MotiffLab.Web/wwwroot/js/storage/project-store.js";

test("storage is nearly full from 80% of a known quota", () => {
    assert.equal(NEARLY_FULL, 0.8);
    assert.equal(isNearlyFull(79, 100), false);
    assert.equal(isNearlyFull(80, 100), true);
    assert.equal(isNearlyFull(5, 0), false);
});

test("projects list most recently opened first, then by name", () => {
    const row = (fileName, lastOpened) => ({ id: fileName, fileName, motifCount: 0, variationCount: 0, lastOpened });
    const sorted = sortSummaries([
        row("b.mid", "2026-10-22T10:00:00.0000000+00:00"),
        row("c.mid", "2026-10-23T10:00:00.0000000+00:00"),
        row("a.mid", "2026-10-22T10:00:00.0000000+00:00"),
    ]);
    assert.deepEqual(sorted.map((s) => s.fileName), ["c.mid", "a.mid", "b.mid"]);
});
