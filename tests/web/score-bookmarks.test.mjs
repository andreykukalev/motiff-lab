// Tests for how bookmarks stack over the score (decision record 0018, section 5), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { bookmarkLevels } from "../../src/MotiffLab.Web/wwwroot/js/score/score-view.js";

test("bookmarks that do not overlap share the lowest level", () => {
    assert.deepEqual(bookmarkLevels([{ start: 0, end: 960 }, { start: 960, end: 1920 }]), [0, 0]);
    assert.deepEqual(bookmarkLevels([]), []);
});

test("overlapping bookmarks stack, each taking the lowest level free where it starts", () => {
    // Given in the player's order, not by place: the levels follow the score.
    const levels = bookmarkLevels([
        { start: 960, end: 2880 },
        { start: 0, end: 1920 },
        { start: 2400, end: 3840 },
        { start: 1920, end: 2400 },
    ]);
    assert.deepEqual(levels, [1, 0, 0, 0]);
    assert.deepEqual(bookmarkLevels([{ start: 0, end: 3840 }, { start: 0, end: 960 }, { start: 480, end: 960 }]), [0, 1, 2]);
});
