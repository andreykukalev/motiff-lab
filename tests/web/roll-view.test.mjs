// Tests for what keeps the piano roll's view (#495 BUG-04; Scripts/roll/roll-view.ts), run in Node on the compiled output:
// the notes coming back after an edit, the same selection sent again, and Expand turning fitting on again leave the zoom
// and the stretch in view as the player set them; only a new selection, or the player, moves them.
//   dotnet build src/MotiffLab.Web && node --test tests/web
import assert from "node:assert/strict";
import test from "node:test";
import { fitsWhenTurned, selectionShift, viewOnLoad } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-view.js";

const zoomed = { scroll: 2880, pitchTop: 76, fitted: false, span: { start: 1440, end: 4320 } };

test("the same selection sent again is neither moved nor changed, so it fits nothing", () => {
    assert.deepEqual(selectionShift({ start: 1440, end: 4320 }, 1440, 4320), { moved: false, changed: false });
});

test("a selection that starts elsewhere has moved, one that only ends elsewhere has changed", () => {
    assert.deepEqual(selectionShift({ start: 1440, end: 4320 }, 2880, 4320), { moved: true, changed: true });
    assert.deepEqual(selectionShift({ start: 1440, end: 4320 }, 1440, 5760), { moved: false, changed: true });
    assert.deepEqual(selectionShift(null, 1440, 4320), { moved: true, changed: true }, "the first selection is a new one");
});

test("the same piece's notes again keep the zoom's view and the selection's span", () => {
    const kept = viewOnLoad(zoomed, true, false);
    assert.deepEqual(kept, zoomed);
    assert.notEqual(kept, zoomed, "a copy, not the view itself");
    // So the selection sent back after the notes is the one shown before: nothing is fitted again.
    assert.deepEqual(selectionShift(kept.span, 1440, 4320), { moved: false, changed: false });
});

test("another piece starts at its beginning; the editor keeps its place as before", () => {
    assert.deepEqual(viewOnLoad(zoomed, false, false), { scroll: 0, pitchTop: null, fitted: false, span: null });
    assert.deepEqual(viewOnLoad(zoomed, false, true), { scroll: 2880, pitchTop: 76, fitted: false, span: null });
});

test("a view as Fit made it stays Fit's when the notes come back, so a new size fits it again", () => {
    assert.equal(viewOnLoad({ ...zoomed, fitted: true }, true, false).fitted, true);
});

test("fitting every selection fits at once only when it turns on, and never when the roll is read by touch", () => {
    assert.equal(fitsWhenTurned(false, true, false), true, "the web's roll in focus fits its phrase");
    assert.equal(fitsWhenTurned(true, true, false), false, "sent again after the notes come back: the zoom stays");
    assert.equal(fitsWhenTurned(true, false, false), false);
    assert.equal(fitsWhenTurned(false, true, true), false, "the iPad's Expand keeps the zoom set in the drawer");
});
