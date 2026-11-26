// The roll's painter (W3.12): the notes canvas drawn from what the roll shows (NotesScene) where its viewport puts it
// (NotesFrame): the rows and the bar and beat grid with the bar numbers, phrase starts, the selection's band, the range that
// plays, the places of a fact, the notes in their lanes' colours and patterns with their names, the variation and a
// version's ghosts over them, the chosen notes' name tags, Check's flags, the keys down the left (roll-keys-draw.ts), and
// the playhead's line. It changes nothing but the canvas; what a press does is the roll's input, the gestures' own layers
// (place, box, lanes, the model's playheads) are drawn by their modules.

import { NAMED_ROW, drawGutter } from "./roll-keys-draw.js";
import { isBlackKey, nameFits, type RollModel, type RollNote } from "./roll-model.js";
import { laneColour, lanePattern, palette, patternTile, prepare, type PatternKind } from "./roll-palette.js";
import type { Span } from "./roll-playhead.js";
import { BAR_ROW, GUTTER, TICKS_PER_QUARTER } from "./roll-viewport.js";

/** A Check flag on the bar numbers: an amber square with "?" (design/kit/ds.css .flag). */
export const CHECK_FLAG = 14;
/** The playhead's handle under the bar numbers (Andrei, v0.2): drawn this wide and tall. */
const KNOB_WIDTH = 12;
const KNOB_HEIGHT = 16;
const NOTE_FONT = "bold 9px \"Source Sans 3\", system-ui, sans-serif";
/** Notes outside the selected motif are drawn faint, so the motif stands out. */
const OUTSIDE_ALPHA = 0.3;
/** Motif notes behind a variation are drawn this faint, so the variation's outlines read on top of them. */
const BEHIND_VARIATION_ALPHA = 0.4;
const PHRASE_LINE_ALPHA = 0.35;
const HIGHLIGHT_UNDERLINE = 4;

/** The name inside a note grows with the row, so it reads at every zoom. */
function noteFont(row: number): string {
    return `bold ${Math.min(13, Math.max(10, Math.round(row * 0.5)))}px "Source Sans 3", system-ui, sans-serif`;
}

/** Where the viewport puts the notes now: the canvas's size, the rows in view, the ticks in view and the zoom. */
export interface NotesFrame {
    width: number;
    height: number;
    /** Where the notes start, under the bar numbers (and the loop strip). */
    header: number;
    low: number;
    high: number;
    row: number;
    above: boolean;
    below: boolean;
    from: number;
    to: number;
    zoom: number;
    xOf(tick: number): number;
}

/** A note of the variation drawn over the roll, with where it came from in the motif. */
export interface VariationNote {
    onset: number;
    end: number;
    pitch: number;
    fromOnset: number;
    fromPitch: number;
}

/** A Check flag: the bar it flags, by its start and number. */
export interface CheckFlag {
    tick: number;
    bar: number;
    label: string;
}

/** What the roll shows, as the painter reads it; the roll keeps it and hands it over for each draw. */
export interface NotesScene {
    model: RollModel;
    /** A note's lane, by its index among the piece's lanes. */
    lane(note: RollNote): number;
    /** The notes sounding at the playhead that light up. */
    sounding: ReadonlySet<number>;
    /** The notes chosen one by one, ringed with their names in tags. */
    chosen: ReadonlySet<number>;
    motif: { end: number; notes: ReadonlySet<number> } | null;
    /** The notes the Notes filter keeps, or null for all. */
    filtered: ReadonlySet<number> | null;
    highlight: { spans: readonly Span[]; notes: ReadonlySet<number> } | null;
    variation: readonly VariationNote[] | null;
    variationAudible: boolean;
    ghosts: readonly { onset: number; end: number; pitch: number }[];
    phrases: readonly { tick: number; inferred: boolean }[];
    checks: readonly CheckFlag[];
    /** The selected range or the one being dragged, shaded; null for none (and in Expand). */
    band: Span | null;
    /** Whether the band's edges carry grips on the bar numbers. */
    grips: boolean;
    /** The range that plays as drawn, null where it is not (a drag, the lanes, Expand). */
    play: Span | null;
    /** The playhead's tick. */
    tick: number;
    /** Whether the notes carry their names. */
    noteNames: boolean;
    /** A note's whole name, and its name without the octave. */
    name(index: number): string;
    shortName(index: number): string;
    /** A row's name without its octave, spelled for the key in force at the left of the view (0018 section 4). */
    rowName(pitch: number): string;
    /** An octave's name in words for the band down the left (empty until sent), and the name of its C with the octave. */
    octaveName(octave: number): { words: string; c: string };
    /** Where a note is drawn: where the editor drags it, or where it is. */
    placed(index: number): { onset: number; pitch: number; length: number };
    /** Whether the editor has picked the note: it gets the accent's outline. */
    picked(index: number): boolean;
}

/** Where each Check flag in view is drawn: just inside the end of its bar on the bar numbers. */
export function checkPlaces(checks: readonly CheckFlag[], model: RollModel, frame: Pick<NotesFrame, "from" | "to" | "width" | "xOf">): { x: number; bar: number }[] {
    const places: { x: number; bar: number }[] = [];
    for (const flag of checks) {
        const end = model.bars.find((bar) => bar.onset === flag.tick)?.end ?? flag.tick;
        if (end < frame.from || flag.tick > frame.to) {
            continue;
        }
        const x = Math.min(frame.xOf(end), frame.width) - CHECK_FLAG - 3;
        if (x >= GUTTER) {
            places.push({ x, bar: flag.bar });
        }
    }
    return places;
}

/** Draws the roll's notes canvas; keeps the lanes' patterns, made again when the theme changes. */
export class RollPainter {
    private readonly patterns = new Map<PatternKind, CanvasPattern | null>();

    /** The theme changed: the patterns are made again in its pattern ink. */
    forgetPatterns(): void {
        this.patterns.clear();
    }

    /**
     * Draws the notes canvas: the rows, and with a piece the grid, the layers and the notes, the tags, the flags and the keys
     * down the left (the playhead is the roll's, drawn over it). Returns the context, to draw on over the notes.
     */
    paint(canvas: HTMLCanvasElement, frame: NotesFrame, scene: NotesScene | null): CanvasRenderingContext2D {
        const context = prepare(canvas, frame.width, frame.height);
        context.fillStyle = palette.surface;
        context.fillRect(0, 0, frame.width, frame.height);
        drawRows(context, frame);
        if (scene === null) {
            return context;
        }
        drawGrid(context, frame, scene.model);
        drawPhrases(context, frame, scene.phrases);
        drawBand(context, frame, scene.band, scene.grips);
        drawPlayRange(context, frame, scene.play, scene.motif?.end);
        drawHighlightBands(context, frame, scene.highlight?.spans ?? []);
        context.save();
        context.beginPath();
        context.rect(GUTTER, frame.header, frame.width - GUTTER, frame.height - frame.header);
        context.clip();
        context.font = noteFont(frame.row);
        const tags: { x: number; y: number; text: string }[] = [];
        for (const index of scene.model.inRange(frame.from, frame.to)) {
            this.drawNote(context, frame, scene, index, tags);
        }
        drawVariation(context, frame, scene);
        drawGhosts(context, frame, scene.ghosts);
        context.restore();
        drawTags(context, frame, tags);
        drawChecks(context, checkPlaces(scene.checks, scene.model, frame));
        drawGutter(context, frame, scene);
        return context;
    }

    /** A note as a rounded bar in its lane's colour and pattern, its name or its tag, and the outlines of what it is. */
    private drawNote(context: CanvasRenderingContext2D, frame: NotesFrame, scene: NotesScene, index: number, tags: { x: number; y: number; text: string }[]): void {
        const note = scene.model.notes[index];
        if (note.pitch < frame.low || note.pitch > frame.high) {
            return;
        }
        const at = scene.placed(index);
        const box = {
            x: frame.xOf(at.onset),
            w: Math.max(2, (at.length * frame.zoom) / TICKS_PER_QUARTER - 1),
            y: frame.header + (frame.high - at.pitch) * frame.row + 1,
            h: Math.max(2, frame.row - 2),
        };
        const lane = scene.lane(note);
        const highlighted = scene.highlight?.notes.has(index) ?? false;
        context.globalAlpha = noteAlpha(scene, index, highlighted);
        // A note is a rounded bar, as in a DAW (testing item 11); its lane's colour and pattern tell the hand, the track or the voice.
        context.beginPath();
        context.roundRect(box.x, box.y, box.w, box.h, Math.min(3, box.w / 2, box.h / 2));
        context.fillStyle = laneColour(lane);
        context.fill();
        const pattern = this.pattern(lanePattern(lane), context);
        if (pattern !== null) {
            context.fillStyle = pattern;
            context.fill();
        }
        context.globalAlpha = 1;
        // Inferred values, the note or its hand, are marked by a dashed outline, with no letters on the note.
        if (note.noteInferred || note.handInferred) {
            context.setLineDash([3, 2]);
            context.strokeStyle = palette.ink;
            context.lineWidth = 1;
            context.strokeRect(box.x + 0.5, box.y + 0.5, box.w - 1, box.h - 1);
            context.setLineDash([]);
        }
        if (scene.chosen.has(index)) {
            tags.push({ x: box.x, y: box.y, text: scene.name(index) });
        } else if (scene.noteNames && frame.row >= NAMED_ROW) {
            drawNoteName(context, box, scene.shortName(index));
        }
        drawNoteOutlines(context, box, scene.chosen.has(index), scene.picked(index), highlighted);
    }

    private pattern(kind: PatternKind, context: CanvasRenderingContext2D): CanvasPattern | null {
        if (kind === "solid") {
            return null;
        }
        if (!this.patterns.has(kind)) {
            this.patterns.set(kind, context.createPattern(patternTile(kind), "repeat"));
        }
        return this.patterns.get(kind) ?? null;
    }
}

/** How strong a note is drawn: faint outside the selection or the filter (not a fact's), fainter behind a variation, full while sounding. */
function noteAlpha(scene: NotesScene, index: number, highlighted: boolean): number {
    const motif = scene.motif;
    // A highlighted note outside the selection stays at full strength, as the fact points to it.
    const outside = !highlighted && ((motif !== null && !motif.notes.has(index)) || (scene.filtered !== null && !scene.filtered.has(index)));
    if (outside) {
        return OUTSIDE_ALPHA;
    }
    if (scene.variation !== null && motif !== null && motif.notes.has(index)) {
        return BEHIND_VARIATION_ALPHA;
    }
    return scene.sounding.has(index) ? 1 : 0.82;
}

/**
 * The note's name without its octave (0018 section 4, on by default; testing items 11 to 13), spelled for the key, inside the
 * note where it fits; the octave is on the band down the left.
 */
function drawNoteName(context: CanvasRenderingContext2D, box: { x: number; y: number; w: number; h: number }, text: string): void {
    if (!nameFits(box.w, context.measureText(text).width)) {
        return;
    }
    context.fillStyle = palette.onNote;
    context.textAlign = "left";
    context.textBaseline = "middle";
    context.fillText(text, box.x + 4, box.y + box.h / 2 + 0.5, box.w - 6);
}

/**
 * A chosen note's thick light outline with a dark edge, readable on every lane colour; the note the editor picked in the
 * accent, as everything the player points at (0018); a fact's note in a solid blue box with a dark edge, outside the others.
 */
function drawNoteOutlines(context: CanvasRenderingContext2D, box: { x: number; y: number; w: number; h: number }, chosen: boolean, picked: boolean, highlighted: boolean): void {
    const { x, y, w, h } = box;
    if (chosen) {
        context.strokeStyle = palette.charcoal;
        context.lineWidth = 4;
        context.strokeRect(x - 1, y - 1, w + 2, h + 2);
        context.strokeStyle = palette.ink;
        context.lineWidth = 2;
        context.strokeRect(x - 1, y - 1, w + 2, h + 2);
    }
    if (picked) {
        context.strokeStyle = palette.accent;
        context.lineWidth = 2;
        context.strokeRect(x - 1.5, y - 1.5, w + 3, h + 3);
    }
    if (highlighted) {
        context.strokeStyle = palette.charcoal;
        context.lineWidth = 5;
        context.strokeRect(x - 3, y - 3, w + 6, h + 6);
        context.strokeStyle = palette.highlight;
        context.lineWidth = 2;
        context.strokeRect(x - 3, y - 3, w + 6, h + 6);
    }
}

/** Pitch rows, black-key rows darker, a line under each C. */
function drawRows(context: CanvasRenderingContext2D, frame: NotesFrame): void {
    for (let pitch = frame.low; pitch <= frame.high; pitch++) {
        const y = frame.header + (frame.high - pitch) * frame.row;
        if (isBlackKey(pitch)) {
            context.fillStyle = palette.surfaceDark;
            context.fillRect(GUTTER, y, frame.width - GUTTER, frame.row);
        }
        if (pitch % 12 === 0) {
            context.fillStyle = palette.beatLine;
            context.fillRect(GUTTER, y + frame.row - 1, frame.width - GUTTER, 1);
        }
    }
}

/** Beat lines faint, bar lines strong and numbered where the numbers do not run into each other. */
function drawGrid(context: CanvasRenderingContext2D, frame: NotesFrame, model: RollModel): void {
    let lastLabel = -Infinity;
    context.font = "11px \"Source Sans 3\", system-ui, sans-serif";
    context.textBaseline = "middle";
    context.textAlign = "left";
    for (const line of model.grid(frame.from, frame.to)) {
        const x = Math.round(frame.xOf(line.tick));
        context.fillStyle = line.bar === null ? palette.beatLine : palette.barLine;
        context.fillRect(x, line.bar === null ? frame.header : 0, 1, frame.height);
        if (line.bar !== null && x - lastLabel >= 28) {
            context.fillStyle = palette.ink;
            context.fillText(String(line.bar), x + 3, BAR_ROW / 2);
            lastLabel = x;
        }
    }
}

/** Phrase starts: a flag on the bar numbers and a faint line down the roll; inferred ones dashed and muted. */
function drawPhrases(context: CanvasRenderingContext2D, frame: NotesFrame, phrases: NotesScene["phrases"]): void {
    for (const phrase of phrases) {
        if (phrase.tick < frame.from || phrase.tick > frame.to) {
            continue;
        }
        const x = Math.round(frame.xOf(phrase.tick)) + 0.5;
        const colour = phrase.inferred ? palette.phraseInferred : palette.phraseMark;
        context.strokeStyle = colour;
        context.lineWidth = 1;
        context.setLineDash(phrase.inferred ? [4, 3] : []);
        context.globalAlpha = PHRASE_LINE_ALPHA;
        context.beginPath();
        context.moveTo(x, frame.header);
        context.lineTo(x, frame.height);
        context.stroke();
        context.globalAlpha = 1;
        context.lineWidth = 2;
        context.beginPath();
        context.moveTo(x, 0);
        context.lineTo(x, BAR_ROW);
        context.stroke();
        context.setLineDash([]);
        context.fillStyle = colour;
        context.beginPath();
        context.moveTo(x, 0);
        context.lineTo(x + 7, 4);
        context.lineTo(x, 8);
        context.closePath();
        context.fill();
    }
}

/** A stretch's left and right across the roll, clipped to the time in view; null when none of it is in view. */
function clipped(frame: NotesFrame, span: Span): { left: number; right: number } | null {
    const left = Math.max(GUTTER, frame.xOf(span.start));
    const right = Math.min(frame.width, frame.xOf(span.end));
    return right > left ? { left, right } : null;
}

/**
 * The selected range, or the range being dragged, shaded and marked on the bar numbers, its edges in the accent where they
 * are in view (a band running out of view has no edge at the side it runs past, as the place rule's handles have none);
 * grips on the bar numbers show that the edges can be dragged (under the place rule the handles at the edges' middles do,
 * v0.6.4).
 */
function drawBand(context: CanvasRenderingContext2D, frame: NotesFrame, band: Span | null, grips: boolean): void {
    const edges = band === null ? null : clipped(frame, band);
    if (band === null || edges === null) {
        return;
    }
    const { left, right } = edges;
    const { header, height, width } = frame;
    context.fillStyle = palette.selectionBand;
    context.fillRect(left, header, right - left, height - header);
    context.fillStyle = palette.selectionHeader;
    context.fillRect(left, BAR_ROW - 4, right - left, 4);
    context.fillStyle = palette.accent;
    if (frame.xOf(band.start) >= GUTTER) {
        context.fillRect(Math.round(left), 0, 1, height);
    }
    if (frame.xOf(band.end) <= width) {
        context.fillRect(Math.round(right) - 1, 0, 1, height);
    }
    if (grips && left > GUTTER) {
        context.fillRect(Math.round(left), 0, 4, BAR_ROW);
    }
    if (grips && right < width) {
        context.fillRect(Math.round(right) - 4, 0, 4, BAR_ROW);
    }
}

/** The range that plays, which a variation can make longer or shorter than the selection: a line over the bar numbers, a band past it. */
function drawPlayRange(context: CanvasRenderingContext2D, frame: NotesFrame, play: Span | null, motifEnd: number | undefined): void {
    const edges = play === null ? null : clipped(frame, play);
    if (play === null || edges === null) {
        return;
    }
    const { left, right } = edges;
    const { header, height, width } = frame;
    context.fillStyle = palette.playRange;
    context.fillRect(left, 0, right - left, 3);
    const selectionEnd = motifEnd ?? play.end;
    if (play.end > selectionEnd) {
        const from = Math.max(GUTTER, frame.xOf(selectionEnd));
        context.fillStyle = palette.playRangeExtension;
        context.fillRect(from, header, right - from, height - header);
    }
    if (play.end !== selectionEnd && right < width) {
        context.strokeStyle = palette.playRange;
        context.setLineDash([4, 4]);
        context.beginPath();
        context.moveTo(Math.round(right) - 0.5, 0);
        context.lineTo(Math.round(right) - 0.5, height);
        context.stroke();
        context.setLineDash([]);
    }
}

/** The places of the chosen fact: a faint band and an underline along the bottom. */
function drawHighlightBands(context: CanvasRenderingContext2D, frame: NotesFrame, spans: readonly Span[]): void {
    for (const span of spans) {
        const edges = clipped(frame, span);
        if (edges === null) {
            continue;
        }
        const { left, right } = edges;
        context.fillStyle = palette.highlightBand;
        context.fillRect(left, frame.header, right - left, frame.height - frame.header);
        context.fillStyle = palette.highlight;
        context.fillRect(left, frame.height - HIGHLIGHT_UNDERLINE, Math.max(2, right - left - 1), HIGHLIGHT_UNDERLINE);
    }
}

/** The variation as outlines where its notes land, each linked to the motif note it came from when it moved. */
function drawVariation(context: CanvasRenderingContext2D, frame: NotesFrame, scene: NotesScene): void {
    const variation = scene.variation;
    if (variation === null) {
        return;
    }
    const { header, high, row } = frame;
    const centre = (pitch: number): number => header + (high - pitch) * row + row / 2;
    for (const note of variation) {
        if (note.end < frame.from || note.onset > frame.to || note.pitch < frame.low || note.pitch > high) {
            continue;
        }
        const x = frame.xOf(note.onset);
        const w = Math.max(2, ((note.end - note.onset) * frame.zoom) / TICKS_PER_QUARTER - 1);
        const y = header + (high - note.pitch) * row + 1;
        const h = Math.max(2, row - 2);
        if (note.onset !== note.fromOnset || note.pitch !== note.fromPitch) {
            context.strokeStyle = palette.variationLink;
            context.lineWidth = 1;
            context.beginPath();
            context.moveTo(frame.xOf(note.fromOnset), centre(note.fromPitch));
            context.lineTo(x, centre(note.pitch));
            context.stroke();
        }
        const sounding = scene.variationAudible && note.onset <= scene.tick && note.end > scene.tick;
        context.fillStyle = sounding ? palette.variationFillSounding : palette.variationFill;
        context.fillRect(x, y, w, h);
        context.strokeStyle = palette.ink;
        context.lineWidth = 2;
        context.strokeRect(x + 1, y + 1, w - 2, h - 2);
    }
}

/** Another version's notes in the bar the player writes in a merge (testing item 34): dashed plum outlines. */
function drawGhosts(context: CanvasRenderingContext2D, frame: NotesFrame, ghosts: NotesScene["ghosts"]): void {
    if (ghosts.length === 0) {
        return;
    }
    context.save();
    context.setLineDash([4, 3]);
    context.strokeStyle = palette.ghostLine;
    context.lineWidth = 1.5;
    for (const note of ghosts) {
        if (note.end < frame.from || note.onset > frame.to || note.pitch < frame.low || note.pitch > frame.high) {
            continue;
        }
        const x = frame.xOf(note.onset);
        const w = Math.max(4, ((note.end - note.onset) * frame.zoom) / TICKS_PER_QUARTER - 1);
        const y = frame.header + (frame.high - note.pitch) * frame.row + 1;
        context.strokeRect(x + 1, y + 1, w - 2, Math.max(2, frame.row - 2) - 2);
    }
    context.restore();
}

/** The chosen notes' names, in a tag above each, over the notes round them, kept inside the roll. */
function drawTags(context: CanvasRenderingContext2D, frame: NotesFrame, tags: readonly { x: number; y: number; text: string }[]): void {
    context.font = NOTE_FONT;
    context.textAlign = "left";
    context.textBaseline = "middle";
    for (const tag of tags) {
        const tagWidth = context.measureText(tag.text).width + 8;
        const left = Math.min(Math.max(GUTTER + 1, tag.x - 1), frame.width - tagWidth - 1);
        const top = Math.max(frame.header + 1, tag.y - 17);
        context.fillStyle = palette.ink;
        context.beginPath();
        context.roundRect(left, top, tagWidth, 14, 3);
        context.fill();
        context.fillStyle = palette.surface;
        context.fillText(tag.text, left + 4, top + 7.5);
    }
}

/** The bars Check flags on the bar numbers: an amber square with "?" at the right of each flagged bar's number cell. */
function drawChecks(context: CanvasRenderingContext2D, places: readonly { x: number }[]): void {
    context.font = "bold 10px \"Source Sans 3\", system-ui, sans-serif";
    context.textAlign = "center";
    context.textBaseline = "middle";
    for (const flag of places) {
        context.fillStyle = palette.checkFill;
        context.beginPath();
        context.roundRect(flag.x, (BAR_ROW - CHECK_FLAG) / 2, CHECK_FLAG, CHECK_FLAG, 2);
        context.fill();
        context.fillStyle = palette.keyBlack;
        context.fillText("?", flag.x + CHECK_FLAG / 2, BAR_ROW / 2 + 0.5);
    }
}

/**
 * The playhead: the kit's 2 px accent line, with its handle hanging under the bar numbers for a finger to drag, ringed
 * while the keyboard is on it; nothing when it is out of view.
 */
export function drawPlayheadLine(context: CanvasRenderingContext2D, frame: Pick<NotesFrame, "width" | "height" | "header">, at: number, focused: boolean, radius: number): void {
    if (at < GUTTER || at > frame.width) {
        return;
    }
    const x = Math.round(at);
    context.fillStyle = palette.accent;
    context.fillRect(x - 1, 0, 2, frame.height);
    context.beginPath();
    context.roundRect(x - KNOB_WIDTH / 2, frame.header, KNOB_WIDTH, KNOB_HEIGHT, [0, 0, radius, radius]);
    context.fill();
    if (focused) {
        context.strokeStyle = palette.accent;
        context.lineWidth = 2;
        context.strokeRect(x - KNOB_WIDTH / 2 - 3, frame.header - 3, KNOB_WIDTH + 6, KNOB_HEIGHT + 6);
    }
}
