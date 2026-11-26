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
const { nextGestureId } = await import("../../src/shared/wwwroot/js/score/selection-gesture.js");
/** The page's own prefix of the gesture names (a random one per page load), kept as "gesture" so the steps compare across runs. */
const REALM = new RegExp(`"${nextGestureId().split("-")[0]}-`, "g");

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
        posted: JSON.parse(JSON.stringify(state.posted.splice(0)).replace(REALM, '"gesture-')),
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

/** The notes canvas's pointer events, as the browser dispatches them: down, move, up, a cancel and a capture lost. */
function hands(state) {
    const notes = canvases(state.page.host)["roll-notes"];
    const send = (type) => (id, kind, place, more = {}) => notes.dispatch(type, pointer(id, kind, place.x, place.y, more));
    return { down: send("pointerdown"), move: send("pointermove"), up: send("pointerup"), cancel: send("pointercancel"), lost: send("lostpointercapture") };
}

/** A key pressed on the notes canvas. */
function key(state, name, more = {}) {
    canvases(state.page.host)["roll-notes"].dispatch("keydown", { key: name, code: "", shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, preventDefault() {}, ...more });
}

/** The iPad's rules as roll.js sets them: with a stylus the stylus edits and selects and a finger moves; without, a finger does. */
function ipad(roll, mode, { stylus = true, place = true } = {}) {
    roll.setPointerRules(stylus ? { touch: false, pen: true, scrollFingers: 1, penSelects: true } : { touch: true, pen: true, scrollFingers: 2, penSelects: true });
    roll.setRules(mode);
    roll.setPlaceSelection(place, stylus);
}

/** A point on the bar numbers above a tick (no lanes). */
function onBars(tick) {
    return { x: at(tick, 0).x, y: 12 };
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
    /** Navigate on the iPad (the press table, roll-press.ts): a finger's tap, drag and hold on the notes; a tap and the stylus on the bar numbers. */
    pressNavigate() {
        const state = open();
        const { roll } = state;
        load(roll, piece);
        ipad(roll, "navigate");
        roll.setMotif(960, 1680, idsIn(piece, 960, 1680), false);
        const hand = hands(state);
        const steps = [step(state, "Navigate, bar 2 selected", { full: false })];
        hand.down(1, "touch", at(1200, 72));
        hand.up(1, "touch", at(1200, 72));
        steps.push(step(state, "a finger's tap inside the selection plays from there", { full: false }));
        hand.down(2, "touch", at(2500, 72));
        hand.up(2, "touch", at(2500, 72));
        hand.down(2, "touch", at(2500, 72));
        hand.up(2, "touch", at(2500, 72));
        steps.push(step(state, "two fast taps outside it", { full: false }));
        const from = at(1500, 74);
        hand.down(3, "touch", from);
        hand.move(3, "touch", { x: from.x - 40, y: from.y + 30 });
        hand.move(3, "touch", { x: from.x - 90, y: from.y + 60 });
        steps.push(step(state, "a finger drags the roll along and down", { full: false }));
        hand.up(3, "touch", { x: from.x - 90, y: from.y + 60 });
        hand.down(4, "touch", at(1600, 72));
        steps.push(step(state, "held still: the black playhead goes there", { full: false, ms: 500 }));
        hand.move(4, "touch", at(1920, 72));
        steps.push(step(state, "moved on: it scrubs", { full: false }));
        hand.up(4, "touch", at(1920, 72));
        steps.push(step(state, "let go", { full: false }));
        hand.down(5, "touch", onBars(1800));
        hand.up(5, "touch", onBars(1800));
        steps.push(step(state, "a tap on the bar numbers takes the playhead to the beat", { full: false }));
        hand.down(6, "pen", onBars(2400));
        hand.move(6, "pen", { x: onBars(2400).x - 60, y: 12 });
        hand.up(6, "pen", { x: onBars(2400).x - 60, y: 12 });
        steps.push(step(state, "the stylus drags the bar numbers: the roll moves, nothing is selected", { full: false }));
        return steps;
    },

    /** Select by place with the stylus: a tap outside, a double tap, a span drawn, a finger beside, the lasso, picked notes moved. */
    pressSelect() {
        const state = open();
        const { roll } = state;
        load(roll, piece);
        ipad(roll, "select");
        roll.setMotif(960, 1680, idsIn(piece, 960, 1680), false);
        const hand = hands(state);
        const steps = [step(state, "Select by place, bar 2 selected", { full: false })];
        hand.down(1, "pen", at(2500, 79));
        hand.up(1, "pen", at(2500, 79));
        steps.push(step(state, "a tap outside the selection selects its bar", { full: false }));
        roll.setMotif(2400, 3120, idsIn(piece, 2400, 3120), false);
        steps.push(step(state, "the host's answer", { full: false }));
        hand.down(3, "pen", at(3400, 79));
        hand.move(3, "pen", at(3700, 79));
        hand.move(3, "pen", at(4000, 79));
        steps.push(step(state, "a span drawn outside it", { full: false }));
        hand.up(3, "pen", at(4000, 79));
        steps.push(step(state, "let go: the span sent and held", { full: false }));
        steps.push(step(state, "no answer: the span goes back", { full: false, ms: 2600 }));
        hand.down(4, "touch", at(3000, 72));
        hand.move(4, "touch", at(2800, 72));
        hand.up(4, "touch", at(2800, 72));
        steps.push(step(state, "a finger beside the stylus moves the roll", { full: false }));
        roll.setPickShape("lasso");
        // The finger moved the view 200 ticks on.
        const moved = { scroll: 200 };
        const ring = [at(900, 78, moved), at(1300, 78, moved), at(1300, 67, moved), at(900, 67, moved), at(900, 78, moved)];
        hand.down(5, "pen", ring[0]);
        for (const point of ring.slice(1)) {
            hand.move(5, "pen", point);
        }
        steps.push(step(state, "a lasso drawn round notes", { full: false }));
        hand.up(5, "pen", ring.at(-1));
        steps.push(step(state, "let go: its notes picked", { full: false }));
        roll.setMotif(960, 2400, ["n_0010", "n_0015", "n_0017"], false);
        roll.setPicked(true);
        hand.down(6, "pen", at(1700, 71, moved));
        hand.move(6, "pen", at(1940, 71, moved));
        hand.move(6, "pen", at(2180, 73, moved));
        steps.push(step(state, "picked notes dragged later and up", { full: false }));
        hand.up(6, "pen", at(2180, 73, moved));
        steps.push(step(state, "let go: the move sent", { full: false }));
        roll.setMotif(2400, 3120, idsIn(piece, 2400, 3120), false);
        for (let tap = 0; tap < 2; tap++) {
            hand.down(2, "pen", at(2600, 72));
            hand.up(2, "pen", at(2600, 72));
        }
        steps.push(step(state, "a double tap inside it: Fit", { full: false, ms: 400 }));
        roll.zoomOutToOverview();
        steps.push(step(state, "back to the overview", { full: false, ms: 400 }));
        return steps;
    },

    /** Two fingers in Select without a stylus: a span begun by one finger goes when a second comes; a finger whose end was lost. */
    pressTwoFingers() {
        const state = open();
        const { roll } = state;
        load(roll, piece);
        ipad(roll, "select", { stylus: false });
        const hand = hands(state);
        hand.down(1, "touch", at(2500, 72), { isPrimary: true });
        hand.move(1, "touch", at(2900, 72));
        const steps = [step(state, "one finger draws a span", { full: false })];
        hand.down(2, "touch", at(3500, 74));
        hand.move(2, "touch", at(3900, 74));
        steps.push(step(state, "a second finger: a pinch, the span gone", { full: false }));
        hand.up(2, "touch", at(3900, 74));
        hand.up(1, "touch", at(2900, 72));
        steps.push(step(state, "both lifted: nothing selected", { full: false }));
        hand.down(10, "touch", at(2000, 72), { isPrimary: true });
        hand.down(11, "touch", at(2600, 72), { isPrimary: true });
        hand.up(11, "touch", at(2600, 72));
        steps.push(step(state, "a first finger again after one whose end was lost: a tap", { full: false }));
        return steps;
    },

    /** The playheads taken by hand with the lanes: the black one down its line, the blue one by its handle, while playing, at the edge. */
    pressPlayhead() {
        const state = open();
        const { roll, player } = state;
        load(roll, piece);
        ipad(roll, "select");
        roll.setLanes(true);
        roll.setPlace("1.1", false);
        roll.setMotif(960, 1680, idsIn(piece, 960, 1680), false);
        player.tick = 480;
        const hand = hands(state);
        const lane = { header: 52 };
        const steps = [step(state, "lanes, stopped in bar 1", { full: false })];
        hand.down(1, "touch", { x: at(480, 72, lane).x - 4, y: at(480, 72, lane).y });
        hand.move(1, "touch", at(1200, 72, lane));
        steps.push(step(state, "the black playhead taken down its line and dragged", { full: false }));
        hand.up(1, "touch", at(1200, 72, lane));
        steps.push(step(state, "let go", { full: false }));
        hand.down(2, "pen", { x: at(960, 0).x - 4, y: 38 });
        hand.move(2, "pen", { x: at(1440, 0).x, y: 38 });
        hand.up(2, "pen", { x: at(1440, 0).x, y: 38 });
        steps.push(step(state, "the stylus on the selection's start in the loop strip takes its edge", { full: false }));
        roll.setMotif(960, 1680, idsIn(piece, 960, 1680), false);
        hand.down(9, "touch", { x: at(960, 0).x - 4, y: 38 });
        hand.move(9, "touch", { x: at(1440, 0).x, y: 38 });
        hand.up(9, "touch", { x: at(1440, 0).x, y: 38 });
        steps.push(step(state, "a finger there takes the edge by the lanes' rule", { full: false }));
        player.tick = 1300;
        player.playing = "playing";
        steps.push(step(state, "playing", { full: false }));
        player.tick = 1340;
        hand.down(3, "touch", { x: at(1300, 72, lane).x - 4, y: at(1300, 72, lane).y });
        hand.move(3, "touch", { x: 470, y: at(1300, 72, lane).y });
        steps.push(step(state, "taken where it was drawn, dragged to the right edge", { full: false }));
        steps.push(step(state, "held at the edge: the view goes on", { full: false, ms: 200 }));
        hand.up(3, "touch", { x: 470, y: at(1300, 72, lane).y });
        steps.push(step(state, "let go: it plays on from there", { full: false }));
        return steps;
    },

    /** The editor's tools: a length dragged, a box of notes, Delete, Add, Remove, a finger's tap that waits, an edit while playing, the limits. */
    pressEditor() {
        const state = open();
        const { roll, player } = state;
        load(roll, piece);
        ipad(roll, "edit");
        roll.setEditing(true, "move");
        const hand = hands(state);
        const end = { x: at(600, 76).x - 6, y: at(600, 76).y };
        hand.down(1, "pen", end);
        hand.move(1, "pen", { x: end.x + 24, y: end.y });
        const steps = [step(state, "a note's end dragged longer", { full: false })];
        hand.up(1, "pen", { x: end.x + 24, y: end.y });
        steps.push(step(state, "let go: the length sent", { full: false }));
        hand.down(2, "pen", at(1300, 79));
        hand.move(2, "pen", at(1500, 74));
        hand.move(2, "pen", at(1700, 68));
        steps.push(step(state, "a box drawn on empty space", { full: false }));
        hand.up(2, "pen", at(1700, 68));
        steps.push(step(state, "let go: its notes picked", { full: false }));
        key(state, "Delete");
        steps.push(step(state, "Delete takes them out", { full: false }));
        roll.setEditing(true, "add");
        hand.down(3, "pen", at(2000, 79));
        hand.up(3, "pen", at(2000, 79));
        steps.push(step(state, "Add: a tap on empty space adds a note", { full: false }));
        roll.setEditing(true, "delete");
        hand.down(4, "pen", at(1680, 71));
        hand.up(4, "pen", at(1680, 71));
        steps.push(step(state, "Remove: a tap on a note takes it out", { full: false }));
        ipad(roll, "edit", { stylus: false });
        roll.setEditing(true, "add");
        hand.down(5, "touch", at(2200, 78));
        steps.push(step(state, "a finger's Add tap waits to be lifted", { full: false }));
        hand.up(5, "touch", at(2200, 78));
        steps.push(step(state, "lifted: the note added", { full: false }));
        hand.down(6, "touch", at(2200, 78));
        hand.down(7, "touch", at(2700, 78));
        hand.up(7, "touch", at(2700, 78));
        hand.up(6, "touch", at(2200, 78));
        steps.push(step(state, "a second finger: no note added", { full: false }));
        ipad(roll, "edit");
        roll.setEditing(true, "move");
        player.tick = 600;
        player.playing = "playing";
        hand.down(8, "pen", { x: at(720, 74).x - 2, y: at(720, 74).y });
        hand.move(8, "pen", { x: 10, y: at(720, 74).y + 200 });
        steps.push(step(state, "playing, a note dragged out past the left and the bottom", { full: false }));
        hand.up(8, "pen", { x: 10, y: at(720, 74).y + 200 });
        steps.push(step(state, "let go: the note at the start", { full: false }));
        player.playing = "stopped";
        load(roll, { ids: ["hi", "lo"], notes: [0, 120, 127, 0, 0, 2, 0, 480, 120, 0, 0, 0, 2, 0], bars: [0, 1920, 480, 1], end: 1920 });
        roll.setEditing(true, "move");
        roll.pickForEdit("hi");
        key(state, "ArrowUp");
        key(state, "ArrowLeft");
        key(state, "ArrowLeft", { shiftKey: true });
        roll.pickForEdit("lo");
        key(state, "ArrowDown");
        key(state, "ArrowRight");
        steps.push(step(state, "the keys at the limits: the highest and lowest note, the first beat, the shortest length", { full: false }));
        return steps;
    },

    /** Presses let go without an end: a cancel, a capture lost, a change of tool, a turn, a drag that leaves the roll. */
    pressCancel() {
        const state = open();
        const { roll, page } = state;
        load(roll, piece);
        ipad(roll, "select");
        roll.setMotif(960, 1680, idsIn(piece, 960, 1680), false);
        const hand = hands(state);
        hand.down(1, "pen", at(2500, 72));
        hand.move(1, "pen", at(3000, 72));
        hand.cancel(1, "pen", at(3000, 72));
        const steps = [step(state, "a span drawn, then cancelled by the system", { full: false })];
        hand.down(2, "pen", at(2500, 72));
        hand.move(2, "pen", at(3000, 72));
        hand.lost(2, "pen", at(3000, 72));
        hand.up(2, "pen", at(3000, 72));
        steps.push(step(state, "a span drawn, its capture lost, then let go", { full: false }));
        hand.down(3, "pen", at(2500, 72));
        hand.move(3, "pen", at(3000, 72));
        roll.setRules("navigate");
        hand.up(3, "pen", at(3000, 72));
        steps.push(step(state, "the tool changed mid-drag", { full: false }));
        roll.setRules("select");
        hand.down(4, "pen", at(2500, 72));
        hand.move(4, "pen", at(2800, 72));
        page.resize(700, 400);
        hand.move(4, "pen", at(3100, 72));
        hand.up(4, "pen", at(3100, 72));
        steps.push(step(state, "a turn mid-drag", { full: false }));
        hand.down(5, "pen", at(2500, 72));
        hand.move(5, "pen", { x: 900, y: -40 });
        steps.push(step(state, "a drag out past the right and the top", { full: false }));
        hand.up(5, "pen", { x: 900, y: -40 });
        steps.push(step(state, "let go out there", { full: false }));
        roll.setRules("edit");
        roll.setEditing(true, "move");
        hand.down(6, "pen", at(480, 76));
        hand.move(6, "pen", at(960, 77));
        hand.cancel(6, "pen", at(960, 77));
        steps.push(step(state, "a note dragged in the editor, then cancelled: nothing sent", { full: false }));
        return steps;
    },

    /** The presses before the modes and without the place rule (the web's mouse): a note, a range, an edge, the strips, the keys. */
    pressMouse() {
        const state = open();
        const { roll } = state;
        roll.setTouchReading(false);
        load(roll, piece);
        roll.setChecks([{ tick: 2400, bar: 4, label: "?" }]);
        const hand = hands(state);
        hand.down(1, "mouse", at(960, 69));
        hand.up(1, "mouse", at(960, 69));
        const steps = [step(state, "a click on a note selects it", { full: false })];
        hand.down(2, "mouse", at(1300, 79));
        hand.up(2, "mouse", at(1300, 79));
        steps.push(step(state, "a click on empty space clears", { full: false }));
        hand.down(3, "mouse", at(2400, 72));
        hand.move(3, "mouse", at(2800, 72));
        hand.move(3, "mouse", at(3200, 72));
        steps.push(step(state, "a range dragged", { full: false }));
        hand.up(3, "mouse", at(3200, 72));
        roll.setMotif(2400, 3120, idsIn(piece, 2400, 3120), false);
        steps.push(step(state, "let go: the range sent, the host's answer", { full: false }));
        hand.down(4, "mouse", { x: at(3120, 72).x - 4, y: at(3120, 72).y });
        hand.move(4, "mouse", at(3600, 72));
        hand.up(4, "mouse", at(3600, 72));
        steps.push(step(state, "the end edge dragged", { full: false }));
        hand.down(5, "mouse", { x: at(3120, 0).x - 14, y: 12 });
        hand.up(5, "mouse", { x: at(3120, 0).x - 14, y: 12 });
        steps.push(step(state, "a click on the Check flag", { full: false }));
        hand.down(6, "mouse", onBars(1000));
        hand.move(6, "mouse", onBars(1800));
        hand.up(6, "mouse", onBars(1800));
        steps.push(step(state, "bars dragged on the bar numbers", { full: false }));
        hand.down(7, "mouse", { x: 30, y: at(0, 74).y });
        hand.move(7, "mouse", { x: 30, y: at(0, 70).y });
        hand.up(7, "mouse", { x: 30, y: at(0, 70).y });
        steps.push(step(state, "the pitch names: a key and down the rows", { full: false }));
        roll.setRules("select");
        hand.down(8, "mouse", at(4000, 72));
        hand.move(8, "mouse", at(4400, 72));
        hand.up(8, "mouse", at(4400, 72));
        steps.push(step(state, "Select: a range from outside the selection", { full: false }));
        roll.setRules("navigate");
        hand.down(9, "mouse", at(2000, 72));
        hand.move(9, "mouse", at(1800, 72));
        hand.up(9, "mouse", at(1800, 72));
        hand.down(10, "mouse", at(2000, 72));
        hand.up(10, "mouse", at(2000, 72));
        steps.push(step(state, "Navigate: the mouse moves the roll, a click places the playhead", { full: false }));
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
