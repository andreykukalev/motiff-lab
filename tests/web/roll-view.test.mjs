// Tests for what keeps the piano roll's zoom and view (#495 BUG-04, #511 sections 5 and 6; Scripts/roll/roll-view.ts), run
// in Node on the compiled output: the player's zoom changes only by the zoom controls and Fit, a selection never fits, a new
// size keeps the zoom, Fit's zoom stays afterwards, and playback carries the view along continuously, never by pages.
//   dotnet build src/MotiffLab.Web && node --test tests/web
import assert from "node:assert/strict";
import test from "node:test";
import {
    FOLLOW_AT,
    REVEAL_AT,
    chase,
    easing,
    fitFrame,
    followPlayhead,
    glideFrame,
    milliseconds,
    revealSelection,
    selectionShift,
    ticksIn,
    viewOnLoad,
} from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-view.js";
import { barsInView, octavesInView } from "../../src/MotiffLab.Web/wwwroot/js/roll/roll-zoom.js";

const zoomed = { scroll: 2880, pitchTop: 76, span: { start: 1440, end: 4320 } };
/** Bars of 3/8 at 480 per quarter (720 ticks), as in Für Elise. */
const bars = Array.from({ length: 40 }, (_, index) => ({ onset: index * 720, end: (index + 1) * 720 }));

test("the same selection sent again is neither moved nor changed", () => {
    assert.deepEqual(selectionShift({ start: 1440, end: 4320 }, 1440, 4320), { moved: false, changed: false });
});

test("a selection that starts elsewhere has moved, one that only ends elsewhere has changed", () => {
    assert.deepEqual(selectionShift({ start: 1440, end: 4320 }, 2880, 4320), { moved: true, changed: true });
    assert.deepEqual(selectionShift({ start: 1440, end: 4320 }, 1440, 5760), { moved: false, changed: true });
    assert.deepEqual(selectionShift(null, 1440, 4320), { moved: true, changed: true }, "the first selection is a new one");
});

test("the same piece's notes again keep the view and the selection's span; another piece starts at its beginning", () => {
    const kept = viewOnLoad(zoomed, true, false);
    assert.deepEqual(kept, zoomed);
    assert.notEqual(kept, zoomed, "a copy, not the view itself");
    assert.deepEqual(selectionShift(kept.span, 1440, 4320), { moved: false, changed: false });
    assert.deepEqual(viewOnLoad(zoomed, false, false), { scroll: 0, pitchTop: null, span: null });
    assert.deepEqual(viewOnLoad(zoomed, false, true), { scroll: 2880, pitchTop: 76, span: null });
});

test("a selection made, moved or changed in view leaves the view where it is", () => {
    const view = { scroll: 0, visible: 9 * 720, rows: { low: 52, high: 88 }, maxScroll: 30 * 720 };
    assert.equal(revealSelection({ ...view, span: { start: 720, end: 2880 }, pitches: { lowest: 60, highest: 76 } }), null);
    assert.equal(revealSelection({ ...view, span: { start: 8 * 720, end: 16 * 720 }, pitches: { lowest: 60, highest: 76 } }), null,
        "partly in view, as an edge dragged past the right of the roll");
    assert.equal(revealSelection({ ...view, span: { start: 720, end: 1440 }, pitches: null }), null);
});

test("a new selection wholly out of view is brought into it, its rows only when none of them shows, and nothing else", () => {
    const view = { scroll: 0, visible: 9 * 720, rows: { low: 52, high: 88 }, maxScroll: 30 * 720 };
    const far = revealSelection({ ...view, span: { start: 20 * 720, end: 24 * 720 }, pitches: { lowest: 60, highest: 76 } });
    assert.deepEqual(far, { scroll: Math.round(20 * 720 - 9 * 720 * REVEAL_AT), pitchTop: null });
    const low = revealSelection({ ...view, span: { start: 720, end: 1440 }, pitches: { lowest: 30, highest: 40 } });
    assert.equal(low?.scroll, 0, "in time, the left edge stays");
    assert.equal(low?.pitchTop, Math.round(35 + 37 / 2), "the rows centre on the selection's");
    assert.equal(Object.hasOwn(low ?? {}, "zoom"), false, "a selection never gives a zoom");
});

test("Fit lays the stretch across the width with margins, and its rows in the height", () => {
    const before = { zoom: 48, rowScale: 1, scroll: 0, pitchTop: 88 };
    const input = {
        start: 1440, end: 4320, pitches: { lowest: 60, highest: 76 }, width: 800, height: 400, margin: 16,
        zoomLimits: [6, 480], rowLimits: [5, 40], rowAt100: 22, before,
    };
    const fitted = fitFrame(input);
    assert.equal(Math.round(ticksIn(800, fitted.zoom)), Math.round(2880 + (2 * 16 * 480) / fitted.zoom), "the stretch and a margin either side");
    assert.ok(fitted.scroll < 1440 && fitted.scroll > 1440 - 480, "the margin left of the stretch");
    const rows = 76 - 60 + 1 + 2;
    assert.equal(fitted.rowScale, Math.floor(400 / rows) / 22);
    assert.ok(fitted.pitchTop >= 77, "the free row above shows");
    // On a roll twice as wide it fits again at twice the zoom: Fit reads the size it is now.
    assert.equal(fitFrame({ ...input, width: 1600 }).zoom > fitted.zoom * 1.9, true);
    // A stretch without notes leaves the rows.
    const empty = fitFrame({ ...input, pitches: null });
    assert.equal(empty.rowScale, 1);
    assert.equal(empty.pitchTop, 88);
});

test("Fit's zoom stays: a new selection, playback and a new size afterwards never change it", () => {
    const fitted = fitFrame({
        start: 0, end: 2880, pitches: { lowest: 60, highest: 76 }, width: 800, height: 400, margin: 16,
        zoomLimits: [6, 480], rowLimits: [5, 40], rowAt100: 22, before: { zoom: 48, rowScale: 1, scroll: 0, pitchTop: 88 },
    });
    let frame = { ...fitted };
    const visible = ticksIn(800, frame.zoom);
    const reveal = revealSelection({ scroll: frame.scroll, visible, rows: { low: 50, high: frame.pitchTop }, span: { start: 20000, end: 22000 }, pitches: null, maxScroll: 40000 });
    frame = { ...frame, scroll: reveal?.scroll ?? frame.scroll };
    for (let tick = 20000; tick < 30000; tick += 16) {
        const follow = followPlayhead({ scroll: frame.scroll, visible, tick, playing: true, jumped: false, end: 40000, maxScroll: 40000 });
        frame = { ...frame, scroll: follow?.scroll ?? frame.scroll };
    }
    assert.equal(frame.zoom, fitted.zoom);
    assert.equal(frame.rowScale, fitted.rowScale);
    // A new size shows more or fewer bars at the same zoom: the counts follow the size, the zoom does not.
    const narrow = barsInView(bars, 0, ticksIn(800, frame.zoom));
    const wide = barsInView(bars, 0, ticksIn(1600, frame.zoom));
    assert.ok(wide > narrow, "the drawer made wider shows more bars");
    const row = Math.round(22 * frame.rowScale);
    assert.ok(octavesInView(Math.floor(800 / row)) > octavesInView(Math.floor(400 / row)), "the drawer made taller shows more octaves");
});

test("playing, the view stays until the playhead passes FOLLOW_AT of the width, then moves with it, never by a page", () => {
    const visible = 4000;
    const base = { scroll: 0, visible, playing: true, jumped: false, end: 100000, maxScroll: 100000 };
    assert.equal(followPlayhead({ ...base, tick: visible * FOLLOW_AT - 1 }), null);
    let scroll = 0;
    let previous = 0;
    for (let tick = 0; tick < 20000; tick += 16) {
        const follow = followPlayhead({ ...base, scroll, tick });
        if (follow !== null) {
            assert.equal(follow.kind, "glue");
            assert.ok(follow.scroll - scroll <= 16 + 1e-9, "the view moves no further than the playhead did");
            scroll = follow.scroll;
        }
        const at = (tick - scroll) / visible;
        assert.ok(at <= FOLLOW_AT + 1e-9, "the playhead never passes its place");
        assert.ok(scroll >= previous, "the view only goes on");
        previous = scroll;
    }
    assert.ok(scroll > 10000, "the view went along");
});

test("a loop in view stays still, and one longer than the view goes no further than its end", () => {
    const base = { visible: 4000, playing: true, jumped: false, maxScroll: 100000 };
    assert.equal(followPlayhead({ ...base, scroll: 0, tick: 3800, end: 3900 }), null, "the loop's end is in view");
    const along = followPlayhead({ ...base, scroll: 0, tick: 3900, end: 5000 });
    assert.deepEqual(along, { kind: "glue", scroll: 1000 }, "only as far as its end at the right edge");
});

test("a playhead out of view after a jump is brought back once; stopped and in view, nothing moves", () => {
    const base = { scroll: 10000, visible: 4000, end: 100000, maxScroll: 100000 };
    assert.deepEqual(followPlayhead({ ...base, tick: 2000, playing: true, jumped: true }), { kind: "reveal", scroll: Math.round(2000 - 4000 * REVEAL_AT) });
    assert.deepEqual(followPlayhead({ ...base, tick: 30000, playing: false, jumped: true }), { kind: "reveal", scroll: Math.round(30000 - 4000 * REVEAL_AT) },
        "a seek while stopped");
    assert.equal(followPlayhead({ ...base, tick: 12000, playing: false, jumped: true }), null, "a seek in view");
    assert.equal(followPlayhead({ ...base, tick: 30000, playing: false, jumped: false }), null);
});

test("the view eases to where following takes it, at once with less motion", () => {
    const first = chase(0, 1000, 16, 67);
    assert.ok(first > 0 && first < 1000);
    assert.ok(chase(first, 1000, 16, 67) > first);
    assert.ok(Math.abs(chase(0, 1000, 2000, 67) - 1000) < 1e-6);
    assert.equal(chase(0, 1000, 16, 0), 1000);
});

test("a glide goes from one frame to the other, the zoom by ratio, on the kit's curve", () => {
    const from = { zoom: 20, rowScale: 1, scroll: 0, pitchTop: 80 };
    const to = { zoom: 80, rowScale: 0.5, scroll: 4000, pitchTop: 70 };
    assert.deepEqual(glideFrame(from, to, 0), from);
    assert.deepEqual(glideFrame(from, to, 1), to);
    assert.equal(glideFrame(from, to, 0.5).zoom, 40, "halfway in ratio");
    const ease = easing("cubic-bezier(.2,.8,.2,1)");
    assert.equal(Math.round(ease(0) * 1000), 0);
    assert.equal(Math.round(ease(1) * 1000), 1000);
    assert.ok(ease(0.25) > 0.5, "an ease-out: most of the way early");
    assert.equal(easing("nonsense")(0.3), 0.3);
    assert.equal(milliseconds("320ms", 0), 320);
    assert.equal(milliseconds("0.2s", 0), 200);
    assert.equal(milliseconds("", 99), 99);
});
