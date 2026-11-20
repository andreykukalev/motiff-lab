// The pen's colour bars on the iPad's score page (finding 31, Scripts/score/colour-bars.ts): while Swift's rules name the
// ink they colour in (`input.colour`), the pointers that take the pen (`input.<pointer>.colour`) colour exactly as Select
// selects, by the same place rule with no selection, so every press is a new passage: a drag draws the dashed rectangle
// and colours what it covers on the selection's grid, a tap colours the bar under it or the beats round a note head. What
// it colours goes to Swift as `coloured`, which keeps it as music-core's highlights; the selection never changes.
import { PlaceLayer } from "../selection-place-layer.js";
import { keyLines } from "../selection-beats.js";
import { noteUnit, type Point } from "../colour-bars.js";
import type { TickRange } from "../page-contract.js";
import { pointerRules, post, state } from "./page-state.js";
import { noteIdOf, scoreView, selection, selectionGrid } from "./page-selection.js";
import { highlightBand, highlightRuns } from "./page-marks.js";

/** The colour bars' place layer, made by `installColouring`. */
export let colouring: PlaceLayer;

/** Adds the colour bars' place layer to the page, after the selection's. */
export function installColouring(): void {
    colouring = new PlaceLayer(document.body, {
        geometry: () => state.geometry,
        beats: selection.beats,
        on: () => typeof state.input?.colour === "string" && !state.focus,
        rule: (pointerType) => (pointerRules(pointerType)?.colour === true ? "now" : "none"),
        selection: () => null,
        show: (range) => showColouring(range),
        chosen: (range) => coloured(range),
        picked: (id) => colourNote(id),
        cleared: () => {},
        zoom: () => {},
        view: () => scoreView(),
        grid: (geometry) => selectionGrid(geometry),
        edges: false,
    });
}

/** A passage coloured: to Swift, and the next press reads no passage as there (the layer has no selection). */
function coloured(range: TickRange): void {
    colouring.settle();
    post({ coloured: { start: range.start, end: range.end } });
}

/** A note head tapped with the colour bars: the beats round the note, as Select's tap on a note takes them. */
function colourNote(id: string): void {
    const geometry = state.geometry;
    const spans = state.timeline?.spans ?? [];
    const note = spans.find((span) => span.id === noteIdOf(id)) ?? spans.find((span) => span.id === id);
    if (geometry === null || note === undefined) {
        return;
    }
    const unit = noteUnit(note.onset, note.end, state.beats !== null ? keyLines(state.beats, geometry.bars) : selectionGrid(geometry));
    if (unit !== undefined) {
        coloured(unit);
    }
}

/** What a colouring gesture would colour, in the ink chosen, while it is drawn; null for nothing. */
function showColouring(range: TickRange | null): void {
    for (const old of document.querySelectorAll("body > .hl-preview")) {
        old.remove();
    }
    const ink = state.input?.colour;
    if (range === null || state.geometry === null || typeof ink !== "string") {
        return;
    }
    for (const run of highlightRuns(state.geometry, range.start, range.end)) {
        highlightBand(run, `hl hl-preview ink-${ink}`);
    }
}

/** The cell of the selection's grid under a point, on the bar nearest it, where the eraser takes colour. */
export function gridCell({ x, y }: Point): TickRange | undefined {
    const geometry = state.geometry;
    return geometry === null ? undefined : selection.beats.near(geometry, selectionGrid(geometry), x, y);
}
