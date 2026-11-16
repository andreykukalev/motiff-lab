// Tests for the full assistant's conversations kept on the device (#582 item 17.1), run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { newestFirst } from "../../src/MotiffLab.Web/wwwroot/js/storage/conversation-store.js";

test("the list of a piece's conversations shows the one added to last first, and only its title and when", () => {
    const rows = [
        { id: "c_a", title: "Older", updated: 1000, piece: "project:p_1", record: "{}" },
        { id: "c_c", title: "Newest", updated: 3000, piece: "project:p_1", record: "{}" },
        { id: "c_b", title: "Same time, later id", updated: 1000, piece: "project:p_1", record: "{}" },
    ];

    assert.deepEqual(newestFirst(rows), [
        { id: "c_c", title: "Newest", updated: 3000 },
        { id: "c_a", title: "Older", updated: 1000 },
        { id: "c_b", title: "Same time, later id", updated: 1000 },
    ]);
    assert.deepEqual(newestFirst([]), []);
});
