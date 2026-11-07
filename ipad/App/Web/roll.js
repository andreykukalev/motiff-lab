// The piano roll in the iPad app's drawer (0018 section 4). It is the web app's own roll (Scripts/roll/piano-roll.ts),
// which Blazor drives in the web app; here Swift drives it through window.roll, and what the player does on it goes to
// Swift as a message: bars dragged across or a bar number tapped, the selection cleared, Space pressed, and with the note
// editor on (testing item 15) a note moved, added or taken out. The roll reads
// the player's position every frame; Swift sends it about 30 times a second (`position`).
import { createPianoRoll } from "../roll/piano-roll.js";
import { THEME_CHANGED } from "../layout/theme.js";

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
                const [anchor, current, click, extend] = args;
                const first = barAt(Math.min(anchor, current));
                const last = click ? first : barAt(Math.max(Math.min(anchor, current), Math.max(anchor, current) - 1));
                if (first !== undefined && last !== undefined) {
                    post({ range: { first, last, extend: Boolean(extend) } });
                }
                break;
            }
            case "OnRollClear":
                post({ clear: true });
                break;
            case "OnRollSpace":
                post({ space: true });
                break;
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

/** The number of the bar that holds a tick, the last bar for a tick at or past the end. */
function barAt(tick) {
    const bars = state.payload?.bars ?? [];
    let number;
    for (let at = 0; at + BAR_FIELDS <= bars.length; at += BAR_FIELDS) {
        if (bars[at] > tick && number !== undefined) {
            break;
        }
        number = bars[at + 3];
    }
    return number;
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
 * carry their names, and the appearance. Returns the colour scheme shown and those the piece allows.
 */
function load({ roll, labels, noteNames, dark, colourBy }) {
    theme(dark);
    view.setLabels(labels);
    state.payload = { ids: roll.ids, notes: roll.notes, bars: roll.bars, end: roll.end };
    view.load(state.payload);
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

/** Where the player is: its tick, and "playing", "paused" or "stopped". */
function position({ tick, playing }) {
    heard.tick = tick;
    heard.state = playing ? "playing" : "stopped";
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

/** In phrase focus the roll lays every selection across itself, as Fit does (testing item 12); off in the drawer. */
function fits(on) {
    view.setFitsSelection(Boolean(on));
}

/** The note editor on or off with its tool ("move", "add" or "delete"), and the note it acts on, such as one just added. */
function edit({ on, tool, picked }) {
    view.setEditing(Boolean(on), tool ?? "move");
    view.pickForEdit(on ? picked ?? null : null);
}

window.roll = { load, nextColour, colour, theme, position, selection, noteNames, highlight, fits, edit };
post("ready");
