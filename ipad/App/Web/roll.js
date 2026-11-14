// The piano roll in the iPad app's drawer (0018 section 4). It is the web app's own roll (Scripts/roll/piano-roll.ts),
// which Blazor drives in the web app; here Swift drives it through window.roll, and what the player does on it goes to
// Swift as a message: bars dragged across or a bar number tapped, the selection cleared by Escape, Space pressed, the playhead
// dragged, nudged by a beat or a tap inside the loop or the selection to play from there, and with the note editor on (testing item 15) a note moved, added or taken out. The roll reads
// the player's position every frame; Swift sends it about 30 times a second (`position`). Since v0.5.2 (#479 item 23) the
// roll is also where notes are edited: Swift says which pointer edits (`input`), draws the zoom on each axis from what the
// roll reports (`zoom` both ways), and takes a drag across the bar numbers by its ticks, to select by beats.
import { createPianoRoll, keptRollFrame, restoreRollFrame } from "../roll/piano-roll.js";
import { rangeBars } from "../roll/roll-strip-gesture.js";
import { THEME_CHANGED } from "../layout/theme.js";
import { PRACTICE_ATTRIBUTE, PRACTICE_EVENT } from "../layout/practice-view.js";

const MODES = ["hand", "track", "voice"];
const NOTE_FIELDS = 7;
const BAR_FIELDS = 4;

function post(message) {
    window.webkit?.messageHandlers?.roll?.postMessage(message);
}

/** Where the player is, as Swift last said; the roll reads it every frame. */
const heard = { tick: 0, state: "stopped" };
const player = {
    currentTick: () => heard.tick,
    state: () => heard.state,
};

/** What the roll asks of its host, as Blazor's callbacks: passed on to Swift. */
const host = {
    invokeMethodAsync(method, ...args) {
        switch (method) {
            case "OnRollRange": {
                const [anchor, current, click, extend, how, gesture] = args;
                // By bars an edge moved goes to the nearer barline, a bar counting once half of it is covered (#521 3).
                const bars = rangeBars(numberedBars(), anchor, current, Boolean(click), how === "edit");
                if (bars !== undefined) {
                    // The ticks too: a drag across the bar numbers or a handle selects by beats where the score does (#479 item 23).
                    const from = Math.min(anchor, current);
                    const to = Math.max(anchor, current);
                    post({ range: { first: bars.first, last: bars.last, extend: Boolean(extend), click: Boolean(click), from, to, gesture } });
                }
                break;
            }
            case "OnRollZoom":
                // The bars and the octaves in view, and which steps are left, for the zoom controls Swift draws (#479 item 23).
                post({ zoom: args[0] });
                break;
            case "OnRollClear":
                // With the gesture that cleared it (#511 11): a hold and the drag after it are one step of the history.
                post({ clear: { gesture: args[0] } });
                break;
            case "OnRollGestureCancelled":
                post({ gestureCancelled: args[0] });
                break;
            case "OnRollHoldHint":
                post({ holdHint: true });
                break;
            case "OnRollSpace":
                post({ space: true });
                break;
            case "OnRollSeek": {
                const [tick, play] = args;
                post({ seek: { tick, play: Boolean(play) } });
                break;
            }
            case "OnRollDrag": {
                // A lane's playhead dragged by its handle (#495 UX-08): taken, moved to a tick, let go; Swift runs the model.
                const [phase, handle, tick] = args;
                post({ drag: { phase, handle, tick } });
                break;
            }
            case "OnRollNoteChange": {
                const [id, onset, pitch, length] = args;
                post({ edit: { kind: "change", id, onset, pitch, length } });
                break;
            }
            case "OnRollNoteAdd": {
                const [onset, pitch, length] = args;
                post({ edit: { kind: "add", onset, pitch, length } });
                break;
            }
            case "OnRollNoteRemove":
                post({ edit: { kind: "remove", id: args[0] } });
                break;
            default:
                break;
        }
        return Promise.resolve();
    },
};

const state = { payload: null, modes: [], colourBy: "hand" };
const view = createPianoRoll(document.getElementById("roll"), host);
view.follow(player);
// The iPad reads the roll by touch (#338): a finger moves through the music, two fingers zoom the time, a tap never clears
// the selection or sounds a note. The zoom is the player's (#511 section 5): a selection, a turn of the iPad, the drawer's
// height and Expand leave it, and only Fit lays the selection across the roll. A finger on the playhead drags it, and a tap
// inside the loop or the selection plays from there (v0.2).
view.setTouchReading(true);
// The zoom on each axis (v0.5.2, #479 item 23): Swift draws "− 9 bars +" with Fit and "+ 3 octaves −" from what the roll
// reports, and a pinch zooms the axis it spreads along while two fingers move the roll.
view.setAxisZoom(true);

/** The piece's bars in time order, each its [onset, end) in ticks and its number, from the packed bars music-core sent. */
function numberedBars() {
    const bars = state.payload?.bars ?? [];
    const numbered = [];
    for (let at = 0; at + BAR_FIELDS <= bars.length; at += BAR_FIELDS) {
        numbered.push({ onset: bars[at], end: bars[at] + bars[at + 1], number: bars[at + 3] });
    }
    return numbered;
}

/** The colour schemes that tell notes apart in this piece: those with two lanes or more. */
function modesOf(payload) {
    return MODES.filter((mode) => {
        const lanes = new Set();
        for (let at = 0; at + NOTE_FIELDS <= payload.notes.length; at += NOTE_FIELDS) {
            const track = payload.notes[at + 3];
            lanes.add(mode === "hand" ? payload.notes[at + 5] : mode === "track" ? track : track * 1000 + payload.notes[at + 4]);
        }
        return lanes.size > 1;
    });
}

/**
 * Shows a piece: the roll's data from music-core (`roll`), its labels in the interface's language, whether the notes
 * carry their names, and the appearance. With `keep`, the same piece's notes again (a note edited, a version taken in, another
 * naming): the zoom, the stretch in view and the selection stay as they are (#495 BUG-04). With `frame`, the zoom and the
 * view the player left the roll at before PDF | Notes (#511 section 10, `frame()`), which the roll starts from. Returns the
 * colour scheme shown and those the piece allows.
 */
function load({ roll, labels, noteNames, dark, colourBy, keep, frame }) {
    if (frame) {
        restoreRollFrame(frame);
    }
    theme(dark);
    view.setLabels(labels);
    state.payload = { ids: roll.ids, notes: roll.notes, bars: roll.bars, end: roll.end };
    view.load(state.payload, undefined, Boolean(keep));
    view.setNames(roll.names.notes, roll.names.keyTicks, roll.names.rows, roll.names.short, roll.names.shortRows, roll.names.octaves);
    view.setNoteNames(Boolean(noteNames));
    state.modes = modesOf(state.payload);
    // The colouring the player chose in Settings, when this piece tells its notes apart that way.
    state.colourBy = state.modes.includes(colourBy) ? colourBy : state.modes.includes("hand") ? "hand" : state.modes[0] ?? "hand";
    view.setColourBy(state.colourBy);
    return { colourBy: state.colourBy, modes: state.modes };
}

/** The next colour scheme that tells notes apart, as the web app's colour chip does. */
function nextColour() {
    if (state.modes.length > 1) {
        state.colourBy = state.modes[(state.modes.indexOf(state.colourBy) + 1) % state.modes.length];
        view.setColourBy(state.colourBy);
    }
    return state.colourBy;
}

function theme(dark) {
    document.documentElement.toggleAttribute("data-theme", false);
    if (dark) {
        document.documentElement.setAttribute("data-theme", "dark");
    }
    document.dispatchEvent(new Event(THEME_CHANGED));
}

/**
 * Where the player is: its tick, and "playing", "paused" or "stopped"; with the lanes (v0.5.2) also the place written as
 * bar.beat for the whole-piece strip and whether the selection loops (Цикл).
 */
function position({ tick, playing, label, looping }) {
    heard.tick = tick;
    heard.state = playing ? "playing" : "stopped";
    view.setPlace(label ?? "", Boolean(looping));
}

/**
 * The lanes of version 0.5.2 (#479 item 25), on under the modes outside phrase focus: one position, ink outside the
 * selection and cobalt inside, the loop strip under the bar numbers and the whole piece over the roll.
 */
function lanes(on) {
    view.setLanes(Boolean(on));
}

/** Practice view (#533 11): the roll draws the black playhead alone, without the loop strip and the blue one (piano-roll.ts). */
function practice(on) {
    document.body.setAttribute(PRACTICE_ATTRIBUTE, on ? "on" : "off");
    document.dispatchEvent(new Event(PRACTICE_EVENT));
}

/** The selection, its span shaded and its notes lit, or none; and the span that loops, or none. */
function selection({ selected, loop }) {
    if (selected) {
        view.setMotif(selected.start, selected.end, selected.ids ?? [], false);
    } else {
        view.clearMotif();
    }
    if (loop) {
        view.setPlayRange(loop.start, loop.end);
    } else {
        view.clearPlayRange();
    }
}

/** The places of the fact chosen in Insights, each { onset, end, noteIds }, lit as the web app's roll lights them; none clears them. */
function highlight({ places }) {
    const chosen = places ?? [];
    view.setHighlight(chosen.flatMap((place) => [place.onset, place.end]), [...new Set(chosen.flatMap((place) => place.noteIds ?? []))]);
}

function noteNames(on) {
    view.setNoteNames(Boolean(on));
}

/** The colouring chosen in Settings, when the piece allows it; answers the colouring shown. */
function colour(by) {
    if (state.modes.includes(by)) {
        state.colourBy = by;
        view.setColourBy(by);
    }
    return state.colourBy;
}

/** The note editor on or off with its tool ("move", "add" or "delete"), and the note it acts on, such as one just added. */
function edit({ on, tool, picked }) {
    view.setEditing(Boolean(on), tool ?? "move");
    view.pickForEdit(on ? picked ?? null : null);
}

/**
 * Who edits and who moves the roll (#479 item 23, InputRules.swift `RollInput`): whether a finger ("touch") and the Pencil
 * ("pen") edit while the editor is on, how many fingers move the roll, whether the Pencil changes the selection (not
 * while the Pen is on, #511 4.1), and the bar's tool under the modes ("navigate", "select" or "pen"; empty before them):
 * on the roll too only Select changes the selection, and in Navigate a long press puts the playhead there (#543 2–4).
 */
function input({ touch, pen, scrollFingers, penSelects, mode }) {
    view.setPointerRules({ touch: Boolean(touch), pen: Boolean(pen), scrollFingers: Number(scrollFingers) || 1, penSelects: penSelects !== false });
    view.setRules(mode || null);
}

/**
 * A step of the zoom from Swift's controls (#479 item 23): "time" or "pitch", +1 in or −1 out; "fit" is Fit, and "overview"
 * Zoom Out to the general overview, the selection kept (#521 2.2).
 */
function zoom({ axis, direction }) {
    const step = direction > 0 ? 1 : -1;
    if (axis === "time") {
        view.zoomTime(step);
    } else if (axis === "pitch") {
        view.zoomPitch(step);
    } else if (axis === "fit") {
        view.fit();
    } else if (axis === "overview") {
        view.zoomOutToOverview();
    }
}

/**
 * Expand (#521 19): the roll bound to the passage it shows, { start, end } in ticks, laid across it, moving only within
 * it and changing no selection; null lets it go, back to the zoom and the view before (#521 17).
 */
function passage(span) {
    view.setPassage(span ? { start: span.start, end: span.end } : null);
}

/** The zoom and the view the roll shows now, for Swift to keep while the PDF is shown (#511 section 10); null before a load. */
function frame() {
    return keptRollFrame();
}

window.roll = { load, nextColour, colour, theme, position, selection, noteNames, highlight, edit, lanes, practice, input, zoom, frame, passage };
post("ready");
