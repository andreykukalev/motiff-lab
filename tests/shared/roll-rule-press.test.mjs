import assert from "node:assert/strict";
import { test } from "node:test";
import { RollRulePress } from "../../src/shared/wwwroot/js/roll/roll-rule-press.js";

/** A press under the modes over a roll in Edit notes with one note under every point; what it asked of the editor. */
function editNotes() {
    const asked = [];
    const host = {
        view: { scroll: 0, pitchRange: () => ({ high: 72 }), tickAtX: (x) => x, pitchAt: (y) => y },
        editor: { editing: false, pickBox: () => asked.push("box"), pickNote: () => asked.push("note") },
        model: () => ({ noteAt: () => 0 }),
        motif: () => null,
        mode: () => "edit",
        lanes: () => false,
        header: () => 0,
        post: (name) => asked.push(name),
        changed: () => {},
    };
    const router = { navigates: () => false, waitForHold: () => {}, beginRange: () => {}, tapStrip: () => {} };
    return { press: new RollRulePress(host, router), asked };
}

const down = (pointerId) => ({ pointerId, pointerType: "mouse", shiftKey: false });

test("a box whose press was dropped mid-drag is not picked by the next click", () => {
    const { press, asked } = editNotes();
    press.begin(down(1), 10, 10, 10, 60, true);
    press.move(1, 60, 40);
    assert.notEqual(press.boxDrawn(), null);
    press.drop();
    press.begin(down(2), 100, 20, 100, 60);
    assert.equal(press.boxDrawn(), null, "the new press draws no box");
    press.end(2);
    assert.deepEqual(asked, ["note"], "the click opens the editor on its note");
});

test("a box drawn and let go picks its notes", () => {
    const { press, asked } = editNotes();
    press.begin(down(1), 10, 10, 10, 60, true);
    press.move(1, 60, 40);
    press.end(1);
    assert.deepEqual(asked, ["box"]);
    assert.equal(press.boxDrawn(), null);
});
