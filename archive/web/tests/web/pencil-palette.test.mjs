// The archived web app's pen palette draws its thickness samples from the shared pen sizes (#533 section 9); the sizes
// themselves: tests/shared/pencil-marks.test.mjs. Not run while the web is archived.
//   node --test archive/web/tests/web

import { test } from "node:test";
import assert from "node:assert/strict";

test("the web palette's thickness samples are drawn from the same sizes (#533 section 9)", async () => {
    const { readFile } = await import("node:fs/promises");
    const { HIGHLIGHTER_SIZES, PEN_SIZES } = await import("../../../../src/shared/wwwroot/js/score/pencil-marks.js");
    const razor = await readFile(new URL("../../src/MotiffLab.Web/Score/PenPalette.razor", import.meta.url), "utf8");
    const list = (name) => JSON.parse(razor.match(new RegExp(`double\\[\\] ${name} = (\\[[^\\]]*\\])`))[1]);
    assert.deepEqual(list("s_penSizes"), [...PEN_SIZES]);
    assert.deepEqual(list("s_highlighterSizes"), [...HIGHLIGHTER_SIZES]);
});
