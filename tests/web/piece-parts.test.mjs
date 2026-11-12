// Tests for the pieces a browser split from a collection (the «Пьесы в файле» experiment, v0.4), kept in local storage
// beside the projects until the format step keeps them in the projects. Run in Node on the compiled module:
//   dotnet build src/MotiffLab.Web && node --test tests/web

import assert from "node:assert/strict";
import { test } from "node:test";

import { keepSplit, originIn, parseSplits, partOf, splitOf, withSplit } from "../../src/MotiffLab.Web/wwwroot/js/storage/piece-parts.js";

const elise = { id: "p_1", title: "Für Elise", first: 0, last: 105, from: null, to: null };
const ode = { id: "p_2", title: "Ode to Joy", first: 106, last: 121, from: 750000, to: 1180000 };

test("a kept split reads back as it was written", () => {
    const kept = withSplit({}, "p_book", { title: "Two pieces", playlist: "Two pieces", parts: [elise, ode] });
    assert.deepEqual(parseSplits(JSON.stringify(kept)), { p_book: { title: "Two pieces", playlist: "Two pieces", parts: [elise, ode] } });
});

test("what local storage holds that is not a split is left out, piece by piece", () => {
    assert.deepEqual(parseSplits(null), {});
    assert.deepEqual(parseSplits("not json"), {});
    assert.deepEqual(parseSplits("[1, 2]"), {});
    const text = JSON.stringify({
        p_book: { playlist: "Book", parts: [{ id: "p_1", title: 7, first: "1", last: 4, from: Number.NaN }, { title: "no id" }, null] },
        p_none: { playlist: "Empty", parts: [] },
        p_bad: { parts: [elise] },
    });
    assert.deepEqual(parseSplits(text), {
        p_book: { title: "", playlist: "Book", parts: [{ id: "p_1", title: "", first: null, last: 4, from: null, to: null }] },
    });
});

test("a new split of a collection takes the place of the one kept, and one with no pieces forgets it", () => {
    const first = withSplit({}, "p_book", { playlist: "A", parts: [elise] });
    const other = withSplit(first, "p_other", { playlist: "B", parts: [ode] });
    const again = withSplit(other, "p_book", { playlist: "C", parts: [ode] });
    assert.deepEqual(Object.keys(again).sort(), ["p_book", "p_other"]);
    assert.equal(again.p_book.playlist, "C");
    assert.deepEqual(first, { p_book: { title: "", playlist: "A", parts: [elise] } }, "the splits given are not changed");
    assert.deepEqual(Object.keys(withSplit(again, "p_book", { playlist: "C", parts: [] })), ["p_other"]);
    assert.deepEqual(Object.keys(withSplit(again, "p_other", null)), ["p_book"]);
});

test("a piece finds the collection it was split from and its place there, a recording's stretch with it", () => {
    const splits = withSplit(withSplit({}, "p_book", { title: "Concert", playlist: "Concert 12 May", parts: [elise, ode] }), "p_other", { title: "B", playlist: "B", parts: [{ ...elise, id: "p_3" }] });
    assert.deepEqual(originIn(splits, "p_2"), { collection: "p_book", title: "Concert", playlist: "Concert 12 May", part: ode });
    assert.equal(originIn(splits, "p_3")?.collection, "p_other");
    assert.equal(originIn(splits, "p_book"), null, "the collection itself was not split from anything");
    assert.equal(originIn({}, "p_2"), null);
});

test("blocked storage keeps nothing and does not throw", () => {
    // Node has no localStorage: reads give nothing and writes are swallowed.
    assert.equal(splitOf("p_book"), null);
    assert.equal(partOf("p_2"), null);
    assert.doesNotThrow(() => keepSplit("p_book", { playlist: "A", parts: [elise] }));
});
