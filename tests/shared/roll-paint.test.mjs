// The roll's painting and its view, kept (docs/REFACTORING_AUDIT.md guard C6), run in Node on the compiled output over a
// fake page (roll-fake-dom.mjs). PianoRoll is driven as the iPad's roll.js drives it, on music-core's own answer for the
// example piece (tests/contracts/facade-answers/roll.json) and on small made-up pieces for the edges; each step writes down
// what the roll drew on its canvases, what it posted to its host, its zoom and view, its legend and its playhead's control.
// The steps must give what tests/shared/roll-paint.kept.json keeps, line for line: a change to the roll's viewport or
// painter that draws or posts anything else fails here. A deliberate change of what the roll draws writes the file again
// with ROLL_PAINT_WRITE=1 and shows the difference in its pull request.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import test from "node:test";
import { canvases, fakePage, pointer } from "./roll-fake-dom.mjs";

const KEPT = new URL("./roll-paint.kept.json", import.meta.url);
const WRITE = process.env.ROLL_PAINT_WRITE === "1";
const piece = JSON.parse(readFileSync(new URL("../contracts/facade-answers/roll.json", import.meta.url), "utf8")).answers[0].answer;

fakePage();
const { createPianoRoll, keptRollFrame, restoreRollFrame } = await import("../../src/shared/wwwroot/js/roll/piano-roll.js");
const { PRACTICE_EVENT } = await import("../../src/shared/wwwroot/js/layout/practice-view.js");

const GUTTER = 62;
const TICKS_PER_QUARTER = 480;
/** The frame every scenario starts from, as Swift gives back a frame kept on the device (#511 section 10). */
const START = { zoom: 48, rowScale: 1, scroll: 0, pitchTop: 80 };

/** A roll made as roll.js makes it, on a fake page of the size given, with the host's calls and the player it reads. */
function open({ width = 480, height = 320, frame = START } = {}) {
    const page = fakePage({ width, height });
    const posted = [];
    const player = { tick: 0, playing: "stopped" };
    const roll = createPianoRoll(page.host, { invokeMethodAsync: (method, ...args) => (posted.push([method, ...args]), Promise.resolve()) });
    roll.follow({ currentTick: () => player.tick, state: () => player.playing });
    roll.setTouchReading(true);
    roll.setAxisZoom(true);
    restoreRollFrame(frame);
    return { page, posted, player, roll };
}

/** Loads a piece as roll.js's `load` does: labels, the notes, their names, names on, the colouring. */
function load(roll, answer, labels = {}) {
    roll.setLabels(labels);
    roll.load({ ids: answer.ids, notes: answer.notes, bars: answer.bars, end: answer.end }, undefined, false);
    const names = answer.names;
    if (names !== undefined) {
        roll.setNames(names.notes, names.keyTicks, names.rows, names.short, names.shortRows, names.octaves);
    }
    roll.setNoteNames(true);
    roll.setColourBy("hand");
}

/** The ids of the example piece's notes that start in [start, end). */
function idsIn(answer, start, end) {
    return answer.ids.filter((_, index) => answer.notes[index * 7] >= start && answer.notes[index * 7] < end);
}

/** A digest of drawn lines, for the steps whose every line need not be read. */
function digest(lines) {
    return `${lines.length} lines, sha1 ${createHash("sha1").update(lines.join("\n")).digest("hex").slice(0, 16)}`;
}

/**
 * Runs a frame `ms` after the last and writes down what it gave: the canvases' lines (in full, or digested), what was posted since the last
 * step, the zoom and view kept, the legend's items, the playhead's control and the zoom bar's place.
 */
function step(state, name, { keys = false, overview = false, full = true, ms = 16 } = {}) {
    const drawn = canvases(state.page.host);
    for (const canvas of Object.values(drawn)) {
        canvas.log.length = 0;
    }
    state.page.frame(ms);
    const lines = (canvas) => (full ? [...canvas.log] : digest(canvas.log));
    const children = state.page.host.children;
    const legend = children.find((child) => child.className === "roll-legend");
    const playhead = children.find((child) => child.className === "roll-playhead");
    const scroller = children.find((child) => child.className === "roll-scroll");
    return {
        step: name,
        roll: lines(drawn["roll-notes"]),
        ...(keys ? { keys: lines(drawn["roll-keys"]) } : {}),
        ...(overview ? { overview: lines(drawn["roll-overview"]) } : {}),
        posted: state.posted.splice(0),
        frame: keptRollFrame(),
        scroller: `${scroller.value} of ${scroller.max}`,
        legend: legend.children.map((item) => item.text),
        playhead: [playhead.style.left, playhead.style.top, playhead.min, playhead.max, playhead.value, playhead.getAttribute("aria-valuetext")],
    };
}

/** Where a tick and a pitch are on the roll under the start frame's zoom, rows and view (no lanes: the notes under a bar row of 24). */
function at(tick, pitch, { header = 24, high = START.pitchTop, row = 22, scroll = 0, zoom = START.zoom } = {}) {
    return { x: GUTTER + ((tick - scroll) * zoom) / TICKS_PER_QUARTER + 4, y: header + (high - pitch) * row + row / 2 };
}

const scenarios = {
    /** The drawer opened on the example piece (a pickup bar 0, 3/8), then every layer the roll draws over the notes. */
    drawer() {
        const state = open();
        const { roll, player } = state;
        load(roll, piece);
        const steps = [step(state, "opened at the start", { keys: true })];
        roll.setMotif(240, 1680, idsIn(piece, 240, 1680), false);
        roll.setPlayRange(240, 2400);
        roll.setPhrases([240, 0, 1680, 1]);
        roll.setChecks([{ tick: 960, bar: 2, label: "?" }]);
        roll.setHighlight([1680, 2400], ["n_0016", "n_0017"]);
        roll.setVariation([960, 1200, 71, 960, 69, 1080, 1200, 52, 1080, 52]);
        roll.setGhosts([1800, 1920, 60]);
        player.tick = 700;
        player.playing = "playing";
        steps.push(step(state, "selection, loop, phrases, a Check flag, a fact, a variation and a ghost, playing", { keys: true }));
        roll.setSelection(["n_0005", "n_0010"]);
        steps.push(step(state, "two notes chosen, their names in tags"));
        roll.clearVariation();
        roll.clearPlayRange();
        roll.clearMotif();
        roll.setHighlight([], []);
        player.playing = "stopped";
        steps.push(step(state, "the layers cleared, stopped"));
        return steps;
    },

    /** The iPad under the modes: Select by place with the stylus, the lanes (loop strip and the whole piece), then practice view. */
    lanes() {
        const state = open();
        const { roll, player, page } = state;
        load(roll, piece);
        roll.setPointerRules({ touch: false, pen: true, scrollFingers: 1, penSelects: true });
        roll.setRules("select");
        roll.setPlaceSelection(true, true);
        roll.setLanes(true);
        roll.setPlace("2.1", true);
        roll.setMotif(960, 2400, idsIn(piece, 960, 2400), false);
        roll.setPlayRange(960, 2400);
        player.tick = 1200;
        player.playing = "playing";
        const steps = [step(state, "lanes, Select by place, looping inside the selection", { overview: true })];
        page.practice("on");
        document.dispatchEvent({ type: PRACTICE_EVENT });
        roll.setPlace("2.1", true);
        steps.push(step(state, "practice view: the black playhead alone", { overview: true }));
        return steps;
    },

    /** The note editor: a note picked, a note dragged a quarter later and a row up, then let go. */
    editor() {
        const state = open();
        const { roll, page } = state;
        load(roll, piece);
        roll.setPointerRules({ touch: true, pen: true, scrollFingers: 2, penSelects: true });
        roll.setEditing(true, "move");
        roll.pickForEdit("n_0003");
        const steps = [step(state, "editing, a note picked")];
        const notes = canvases(page.host)["roll-notes"];
        const from = at(480, 76);
        notes.dispatch("pointerdown", pointer(1, "pen", from.x, from.y));
        notes.dispatch("pointermove", pointer(1, "pen", from.x + 48, from.y - 22));
        steps.push(step(state, "the note dragged two eighths later and a row up"));
        notes.dispatch("pointerup", pointer(1, "pen", from.x + 48, from.y - 22));
        steps.push(step(state, "let go: the edit sent"));
        return steps;
    },

    /** The zoom and the view: each zoom step, Fit's glide, Zoom Out, a pan, a pinch, the wheel, Expand's passage and a turn. */
    viewport() {
        const state = open();
        const { roll, page } = state;
        load(roll, piece);
        const steps = [];
        const take = (name, ms) => steps.push(step(state, name, { full: false, ms }));
        take("opened");
        roll.zoomTime(1);
        take("time zoomed in a step");
        roll.zoomTime(-1);
        roll.zoomTime(-1);
        take("time zoomed out two steps");
        roll.zoomPitch(1);
        take("pitch zoomed in a step");
        roll.zoomPitch(-1);
        roll.zoomPitch(-1);
        take("pitch zoomed out two steps");
        roll.setMotif(2400, 4560, idsIn(piece, 2400, 4560), false);
        take("a selection partly in view");
        roll.fit();
        take("Fit begins its glide");
        take("Fit's glide half way", 100);
        take("Fit laid the selection across the roll", 400);
        roll.zoomOutToOverview();
        take("Zoom Out: the overview round the playhead", 400);
        const notes = canvases(page.host)["roll-notes"];
        notes.dispatch("pointerdown", pointer(2, "touch", 300, 150));
        notes.dispatch("pointermove", pointer(2, "touch", 250, 120));
        notes.dispatch("pointermove", pointer(2, "touch", 200, 90));
        take("a finger moves the roll");
        notes.dispatch("pointerup", pointer(2, "touch", 200, 90));
        notes.dispatch("pointerdown", pointer(3, "touch", 200, 150));
        notes.dispatch("pointerdown", pointer(4, "touch", 300, 160));
        notes.dispatch("pointermove", pointer(4, "touch", 380, 170));
        take("two fingers spread across: the time zooms in");
        notes.dispatch("pointermove", pointer(4, "touch", 380, 260));
        take("then up and down: the rows zoom");
        notes.dispatch("pointerup", pointer(4, "touch", 380, 260));
        notes.dispatch("pointerup", pointer(3, "touch", 200, 150));
        notes.dispatch("wheel", { ctrlKey: true, shiftKey: false, deltaX: 0, deltaY: -40, clientX: 200, clientY: 100, preventDefault() {} });
        take("the wheel with Ctrl zooms in round the pointer");
        notes.dispatch("wheel", { ctrlKey: false, shiftKey: false, deltaX: 120, deltaY: 0, clientX: 200, clientY: 100, preventDefault() {} });
        take("the wheel sideways moves through the time");
        roll.setHighlight([30000, 30720], []);
        take("a fact's place far away, brought into view");
        roll.setHighlight([], []);
        roll.setPassage({ start: 960, end: 3120 });
        take("Expand bound to bars 2 to 4");
        page.resize(700);
        take("a turn: the roll wider, the passage laid across it again");
        roll.setPassage(null);
        take("Expand let go: the zoom and the view before it");
        return steps;
    },

    /** Playback carrying the view along, a jump to the last bar, and the zoom at its limits, on the example piece. */
    playback() {
        const state = open();
        const { roll, player, page } = state;
        load(roll, piece);
        const steps = [];
        player.playing = "playing";
        for (let tick = 0; tick <= 4800; tick += 240) {
            player.tick = tick;
            page.frame();
        }
        steps.push(step(state, "playing on past 70 % of the width: the view goes along", { full: false }));
        player.tick = 74900;
        page.frame();
        steps.push(step(state, "a jump to the last bar, eased to", { full: false, ms: 400 }));
        page.resize(320, 400);
        steps.push(step(state, "a turn while playing at the end", { full: false }));
        for (let count = 0; count < 12; count++) {
            roll.zoomTime(-1);
        }
        steps.push(step(state, "zoomed out to the least zoom", { full: false }));
        for (let count = 0; count < 40; count++) {
            roll.zoomTime(1);
            roll.zoomPitch(1);
        }
        steps.push(step(state, "zoomed in to the greatest zoom", { full: false }));
        return steps;
    },

    /** The edges: an empty piece, one bar of two inferred notes in Russian, and 320 bars with the playhead at the last. */
    edges() {
        const state = open();
        const { roll, player, page } = state;
        load(roll, { ids: [], notes: [], bars: [], end: 0 });
        const steps = [step(state, "an empty piece")];
        const russian = { rightHand: "Правая рука", leftHand: "Левая рука", noHand: "Без руки", legendInferred: "Пунктир: выведено, нет в файле (нота или её рука)" };
        load(roll, { ids: ["a", "b"], notes: [0, 960, 64, 0, 0, 2, 1, 960, 960, 48, 0, 0, 1, 2], bars: [0, 1920, 480, 1], end: 1920 }, russian);
        roll.setMotif(0, 1920, ["a", "b"], false);
        steps.push(step(state, "one bar, two inferred notes, selected, in Russian"));
        const ids = [];
        const notes = [];
        const bars = [];
        for (let bar = 0; bar < 320; bar++) {
            ids.push(`n${bar}`);
            notes.push(bar * 1920, 960, 60 + (bar % 12), 0, 0, 2, 0);
            bars.push(bar * 1920, 1920, 480, bar + 1);
        }
        load(roll, { ids, notes, bars, end: 320 * 1920 });
        player.tick = 319 * 1920 + 960;
        player.playing = "playing";
        page.frame();
        steps.push(step(state, "320 bars, playing in the last bar", { full: false, ms: 400 }));
        roll.setMotif(318 * 1920, 320 * 1920, ["n318", "n319"], false);
        steps.push(step(state, "the last two bars selected, the playhead in them", { full: false }));
        return steps;
    },
};

const kept = WRITE ? {} : JSON.parse(readFileSync(KEPT, "utf8"));
for (const [name, run] of Object.entries(scenarios)) {
    test(`the roll draws and posts what it kept: ${name}`, () => {
        const got = run();
        if (WRITE) {
            kept[name] = got;
            return;
        }
        assert.ok(kept[name] !== undefined, `no kept steps for ${name}: write them with ROLL_PAINT_WRITE=1`);
        assert.equal(got.length, kept[name].length, "the number of steps");
        for (const [index, taken] of got.entries()) {
            assert.deepEqual(JSON.parse(JSON.stringify(taken)), kept[name][index], `step ${index}: ${taken.step}`);
        }
    });
}

test.after(() => {
    if (WRITE) {
        writeFileSync(KEPT, `${JSON.stringify(kept, null, 1)}\n`);
    }
});
