// The roll's press table (Scripts/roll/roll-press.ts, architecture blueprint S2): what a press on the roll begins, in the order
// an approved gesture outranks the next. Run in Node on the compiled modules:   dotnet build src/shared && node --test tests/shared

import assert from "node:assert/strict";
import { test } from "node:test";

import { rollPressFor } from "../../src/shared/wwwroot/js/roll/roll-press.js";

/** The facts under a press: nothing holds unless named; each asked fact is noted in `asked`, in the order asked. */
function facts(held = {}, asked = []) {
    const fact = (name) => () => {
        asked.push(name);
        return held[name] === true;
    };
    return {
        gutter: held.gutter === true,
        header: held.header === true,
        placeEdge: fact("placeEdge"),
        playhead: fact("playhead"),
        places: fact("places"),
        pans: fact("pans"),
        edge: fact("edge"),
        editing: held.editing === true,
        edits: held.edits === true,
        rules: fact("rules"),
    };
}

const press = (held) => rollPressFor(facts(held));

test("each approved press begins its own gesture", () => {
    const rows = [
        // The pitch names sound their key; the corner above them does nothing.
        [{ gutter: true }, "keys"],
        [{ gutter: true, header: true }, "none"],
        [{ gutter: true, playhead: true, places: true }, "keys"],
        // Select by the place of the touch, over the notes and the bar numbers alike (#601, SEL-02, SEL-03).
        [{ places: true }, "place"],
        [{ places: true, header: true }, "place"],
        // An edge of the selection wins over a playhead in its reach (v0.6.4, as on the score's page).
        [{ placeEdge: true, playhead: true, places: true }, "place"],
        // R7, PLAY-02: a playhead taken by its handle or its line, while playing too, ahead of a new selection.
        [{ playhead: true, places: true }, "playhead"],
        [{ playhead: true, pans: true }, "playhead"],
        [{ playhead: true, header: true }, "playhead"],
        // A finger moves the roll in touch reading; the old rule's edge drag; the strips; the loop lane under the editor.
        [{ pans: true, edge: true }, "pan"],
        [{ edge: true, header: true }, "edge"],
        [{ header: true }, "barNumbers"],
        [{ header: true, editing: true }, "loopLane"],
        [{ header: true, editing: true, edits: true }, "loopLane"],
        // The note editor's press; the modes' press (gestureFor: R8, a range from outside the selection in Select); a note.
        [{ editing: true, edits: true, rules: true }, "edit"],
        [{ rules: true }, "rule"],
        [{ editing: true, rules: false }, "note"],
        [{}, "note"],
    ];
    for (const [held, expected] of rows) {
        assert.equal(press(held), expected, JSON.stringify(held));
    }
});

test("a press asks only the facts the table reaches, in its order", () => {
    const keys = [];
    rollPressFor(facts({ gutter: true }, keys));
    assert.deepEqual(keys, [], "the pitch names test nothing under the press");
    const edge = [];
    rollPressFor(facts({ placeEdge: true, playhead: true }, edge));
    assert.deepEqual(edge, ["placeEdge"], "an edge by the place rule: no playhead is looked for");
    const note = [];
    rollPressFor(facts({}, note));
    assert.deepEqual(note, ["placeEdge", "playhead", "places", "pans", "edge", "rules"]);
    const strips = [];
    rollPressFor(facts({ header: true }, strips));
    assert.deepEqual(strips, ["placeEdge", "playhead", "places", "pans", "edge"], "the strips ask no rule");
});
