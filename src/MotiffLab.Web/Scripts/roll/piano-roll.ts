// The piano roll and keyboard views (S1-W2, S1-W3): notes on a bar and beat grid drawn on canvas,
// coloured and patterned by track or voice, selectable by pointer or keyboard, and a keyboard that
// lights the keys sounding at the playhead. Both redraw every animation frame from the player's
// currentTick (decision record 0007). Blazor imports this module through JS interop.

import {
    HAND_LEFT,
    HAND_NONE,
    HIGHEST_KEY,
    LOWEST_KEY,
    RollModel,
    isBlackKey,
    laneKey,
    pitchName,
    type ColourBy,
    type RollPayload,
} from "./roll-model.js";

interface DotNetObject {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** The part of the playback player (Scripts/playback/playback.ts) the views read every frame. */
interface PlayerHandle {
    currentTick(): number;
    state(): string;
    /** Strikes and releases a key by hand, over playback or without it. */
    noteOn?(pitch: number, velocity?: number): void;
    noteOff?(pitch: number): void;
}

/** A note of the variation drawn over the roll, with where it came from in the motif. */
interface VariationNote {
    onset: number;
    end: number;
    pitch: number;
    fromOnset: number;
    fromPitch: number;
}

/** Fields per note in setVariation's packed array: onset, end, pitch, the motif note's onset and pitch. */
const VARIATION_FIELDS = 5;

const TICKS_PER_QUARTER = 480;

/** Horizontal zoom, in pixels per quarter note. */
const DEFAULT_ZOOM = 48;
const MIN_ZOOM = 6;
const MAX_ZOOM = 480;

const GUTTER = 40;
// Tall enough to press and drag on for a bar selection (WCAG 2.2 target size, 2.5.8).
const HEADER = 24;
const ROLL_HEIGHT = 360;
const MIN_ROW = 5;
const MAX_ROW = 14;
const KEYBOARD_HEIGHT = 64;

/** While playing, the view pages on when the playhead passes this share of its width. */
const FOLLOW_RIGHT = 0.85;
const FOLLOW_LEFT = 0.15;

/** How close, in pixels, a press must be to an edge of the selection to drag that edge. */
const EDGE_GRAB = 6;

/** A press that moves less than this many pixels is a click, not a drag. */
const CLICK_SLOP = 4;

/**
 * A click outside the roll clears the selection unless it lands on a control or inside an element
 * marked data-keeps-selection, such as the panels that show or act on the selection.
 */
const KEEPS_SELECTION = "button, input, select, textarea, label, a, summary, [role=button], [data-keeps-selection]";

/**
 * Space plays and pauses from anywhere on the page, buttons and sliders included (Enter still
 * presses a button), except where Space types or ticks: text fields, lists, checkboxes and radios.
 */
const SPACE_OWNERS = "input:not([type=range]), select, textarea, [contenteditable]";

// Brand surface and ink (docs/02-brand-identity.md), and lane colours that hold 3:1 against the surface.
const SURFACE = "#1E242C";
const SURFACE_DARK = "#191E25";
const INK = "#F4F5F2";
const MUTED = "#B8C0CB";
const CHARCOAL = "#15191F";
const ACCENT = "#C8F169";
const BAR_LINE = "#8A93A0";
const BEAT_LINE = "#343C47";
const SELECTION_BAND = "rgba(200, 241, 105, 0.14)";
const SELECTION_HEADER = "rgba(200, 241, 105, 0.45)";
/** Notes outside the selected motif are drawn faint, so the motif stands out. */
const OUTSIDE_ALPHA = 0.3;
/** The range that plays, marked on the bar numbers, with a band where it runs past the selection. */
const PLAY_RANGE = INK;
const PLAY_RANGE_EXTENSION = "rgba(244, 245, 242, 0.08)";
/** Motif notes behind a variation are drawn this faint, so the variation's outlines read on top of them. */
const BEHIND_VARIATION_ALPHA = 0.4;
const VARIATION_FILL = "rgba(244, 245, 242, 0.28)";
/** Phrase starts (#18): a flag on the bar numbers and a faint line down the roll, dashed and blue where inferred. */
const PHRASE_MARK = INK;
const PHRASE_INFERRED = "#7CC4FA";
const PHRASE_LINE_ALPHA = 0.35;
/** How a phrase start was found, as setPhrases sends it. */
const PHRASE_INFERRED_KIND = 1;
const VARIATION_FILL_SOUNDING = "rgba(244, 245, 242, 0.75)";
/**
 * The places of a fact chosen on Understand (#25): a solid blue box round each of their notes, a faint
 * band over each place and a blue underline along the bottom, so they differ from the selection
 * (lime), the variation (white) and inferred marks (dashed) by line as well as colour.
 */
const HIGHLIGHT = "#7CC4FA";
const HIGHLIGHT_BAND = "rgba(124, 196, 250, 0.12)";
const HIGHLIGHT_UNDERLINE = 4;
const VARIATION_LINK = "rgba(244, 245, 242, 0.45)";
const LANE_COLOURS = ["#C8F169", "#6EC6FF", "#FF9E80", "#C5A3FF", "#F2D16B", "#5FE0C1"];

/** Patterns go with colours so that lanes differ by more than colour (WCAG 2.2 AA, 1.4.1). */
type PatternKind = "solid" | "rising" | "falling" | "horizontal" | "vertical" | "dots";
const LANE_PATTERNS: PatternKind[] = ["solid", "rising", "falling", "horizontal", "vertical", "dots"];

export class PianoRoll {
    private readonly host: HTMLElement;
    private readonly dotnet: DotNetObject;
    private readonly roll: HTMLCanvasElement;
    private readonly keys: HTMLCanvasElement;
    private readonly scroller: HTMLInputElement;
    private readonly legend: HTMLElement;
    private readonly resizeObserver: ResizeObserver;
    private readonly patterns = new Map<PatternKind, CanvasPattern | null>();
    private model: RollModel | null = null;
    private colourBy: ColourBy = "track";
    private laneIndex = new Map<number, number>();
    private zoom = DEFAULT_ZOOM;
    private scroll = 0;
    private selected = new Set<number>();
    private focusIndex: number | undefined;
    /** The motif selection (S1-M1, S1-M2): its range and its notes, or null for none. */
    private motif: { start: number; end: number; notes: Set<number>; alone: boolean } | null = null;
    /** A drag across the roll selecting a range: where it started and where the pointer is; edge when it moves an edge of the selection. */
    private drag: { pointer: number; anchorX: number; anchor: number; current: number; edge: boolean } | null = null;
    /** Indexes of the notes the Notes filter keeps, or null for all; the others are drawn faint and never light up. */
    private filtered: Set<number> | null = null;
    /** The range that plays for the selection [start, end), when it differs from what is selected; null for none. */
    private playRange: { start: number; end: number } | null = null;
    /** The variation of the motif, drawn as outlines over it, or null for none. */
    private variation: VariationNote[] | null = null;
    /** The places of the fact chosen on Understand (#25): their stretches of time and their notes, or null for none. */
    private highlight: { spans: { start: number; end: number }[]; notes: Set<number> } | null = null;
    /** Where phrases start and whether each start is inferred (#18). */
    private phrases: { tick: number; inferred: boolean }[] = [];
    /** Which sources are heard, so only notes being heard light up. */
    private originalAudible = true;
    private variationAudible = false;
    /** The key held on the keyboard by a pointer, so a drag across the keys plays each in turn. */
    private keysPress: { pointer: number; pitch: number | null } | null = null;

    /** Keys struck by hand on the roll or the keyboard, lit while held. */
    private readonly live = new Map<number, number>();
    /** Key x positions from the last keyboard draw, for finding the key under the pointer. */
    private keyLayout: { positions: Map<number, number>; whiteWidth: number; blackWidth: number; blackHeight: number } | null = null;
    /** A press on the notes area that becomes a click on a note or the grid, or a range drag once it moves. */
    private press: { pointer: number; x: number; tick: number; pitch: number; add: boolean; sounding: number | null } | null = null;
    /** Where a press outside the roll started, so that a drag (such as selecting text) is not a click. */
    private outsidePress: { x: number; y: number; target: Element } | null = null;
    private player: PlayerHandle | null = null;
    private tickNow = 0;
    private dirty = true;
    private frame = 0;
    private disposed = false;

    constructor(host: HTMLElement, dotnet: DotNetObject) {
        this.host = host;
        this.dotnet = dotnet;

        this.roll = document.createElement("canvas");
        this.roll.className = "roll-notes";
        this.roll.tabIndex = 0;
        this.roll.setAttribute("role", "application");
        this.roll.setAttribute(
            "aria-label",
            "Piano roll. Drag anywhere across the roll to select a range, drag an edge of the selection to move it, click a bar number to select that bar, Shift-click a bar number to extend the selection to it. Click a note to hear and select it, Shift-click to add to the selection; click the pitch names or the keyboard to hear a key. Click an empty place or press Escape to clear the selection. Space plays the selection on a loop, or the piece, and pauses. Right and left square brackets select the next or previous bar, with Shift they extend the selection by it. Arrow keys move the note selection, plus and minus zoom.",
        );

        this.scroller = document.createElement("input");
        this.scroller.type = "range";
        this.scroller.className = "roll-scroll";
        this.scroller.min = "0";
        this.scroller.step = "1";
        this.scroller.setAttribute("aria-label", "Scroll the piano roll");

        this.keys = document.createElement("canvas");
        this.keys.className = "roll-keys";
        this.keys.setAttribute("role", "img");
        this.keys.setAttribute("aria-label", "Keyboard: the keys of the notes being heard are lit and marked with a dot. Click or drag across the keys to play them, also while the piece plays.");

        this.legend = document.createElement("div");
        this.legend.className = "roll-legend";

        this.host.replaceChildren(this.roll, this.scroller, this.keys, this.legend);

        this.roll.addEventListener("pointerdown", this.onPointerDown);
        this.roll.addEventListener("pointermove", this.onPointerMove);
        this.roll.addEventListener("pointerup", this.onPointerUp);
        this.roll.addEventListener("pointercancel", this.onPointerCancel);
        this.roll.addEventListener("keydown", this.onKeyDown);
        this.roll.addEventListener("wheel", this.onWheel, { passive: false });
        this.keys.addEventListener("pointerdown", this.onKeysDown);
        this.keys.addEventListener("pointermove", this.onKeysMove);
        this.keys.addEventListener("pointerup", this.onKeysUp);
        this.keys.addEventListener("pointercancel", this.onKeysUp);
        this.scroller.addEventListener("input", this.onScroll);
        document.addEventListener("pointerdown", this.onDocumentPointerDown);
        document.addEventListener("pointerup", this.onDocumentPointerUp);
        document.addEventListener("keydown", this.onDocumentKeyDown);
        this.resizeObserver = new ResizeObserver(() => this.invalidate());
        this.resizeObserver.observe(host);
        this.frame = requestAnimationFrame(this.tick);
    }

    /** Shows a piece, replacing what was shown. The selection is cleared. */
    load(payload: RollPayload): void {
        this.model = RollModel.fromPayload(payload);
        this.selected = new Set();
        this.focusIndex = undefined;
        this.motif = null;
        this.playRange = null;
        this.filtered = null;
        this.variation = null;
        this.highlight = null;
        this.drag = null;
        this.press = null;
        this.scroll = 0;
        this.updateLanes();
    }

    setColourBy(by: ColourBy): void {
        this.colourBy = by;
        this.updateLanes();
    }

    /** Zooms in (factor above 1) or out, keeping the playhead, or else the left edge, where it is. */
    zoomBy(factor: number, anchorX?: number): void {
        const width = this.timeWidth();
        const anchor = anchorX ?? Math.min(Math.max(this.xOf(this.tickNow) - GUTTER, 0), width);
        const anchorTick = this.scroll + (anchor * TICKS_PER_QUARTER) / this.zoom;
        this.zoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, this.zoom * factor));
        this.scrollTo(anchorTick - (anchor * TICKS_PER_QUARTER) / this.zoom);
    }

    /** Selects the notes with these ids, for a selection made elsewhere. Unknown ids are ignored. */
    setSelection(ids: string[]): void {
        const model = this.model;
        if (model === null) {
            return;
        }
        this.selected = new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
        this.invalidate();
    }

    /**
     * Shows the motif selection: the range [start, end) shaded, its notes at full strength and
     * the other notes faint. When only the motif's notes play (alone), only they light up as
     * they sound. Unknown ids are ignored.
     */
    setMotif(start: number, end: number, ids: string[], alone: boolean): void {
        const model = this.model;
        if (model === null) {
            return;
        }
        const notes = new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
        this.motif = { start, end, notes, alone };
        this.drag = null;
        this.invalidate();
    }

    clearMotif(): void {
        this.motif = null;
        this.drag = null;
        this.invalidate();
    }

    /**
     * Shows the range that plays for the selection: a line over the bar numbers, a band where it runs
     * past the selection and a dashed line where it ends, when that differs from the selection's end.
     */
    setPlayRange(start: number, end: number): void {
        this.playRange = { start, end };
        this.invalidate();
    }

    clearPlayRange(): void {
        this.playRange = null;
        this.invalidate();
    }

    /** Marks where phrases start: packed as pairs of tick and kind (0 from the file, 1 inferred, 2 the player's). */
    setPhrases(packed: number[]): void {
        const phrases: { tick: number; inferred: boolean }[] = [];
        for (let at = 0; at + 2 <= packed.length; at += 2) {
            phrases.push({ tick: packed[at], inferred: packed[at + 1] === PHRASE_INFERRED_KIND });
        }
        this.phrases = phrases;
        this.invalidate();
    }

    /**
     * Highlights the places a chosen fact names: spans packed as pairs of start and end, and the ids
     * of their notes. The first place is scrolled into view. Empty clears the highlight; unknown ids are ignored.
     */
    setHighlight(packed: number[], ids: string[]): void {
        const model = this.model;
        const spans: { start: number; end: number }[] = [];
        for (let at = 0; at + 2 <= packed.length; at += 2) {
            spans.push({ start: packed[at], end: packed[at + 1] });
        }
        if (model === null || spans.length === 0) {
            this.highlight = null;
        } else {
            const notes = new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
            this.highlight = { spans, notes };
            if (this.player?.state() !== "playing") {
                this.keepInView(Math.min(...spans.map((span) => span.start)));
            }
        }
        this.renderLegend();
        this.invalidate();
    }

    /** Keeps only the notes with these ids at full strength, for the Notes filter; null keeps all. */
    setFilter(ids: string[] | null): void {
        const model = this.model;
        if (model === null) {
            return;
        }
        this.filtered = ids === null ? null : new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
        this.invalidate();
    }

    /**
     * Draws a variation over the motif (V1 and later): each note as an outline where it lands, with
     * a line from the motif note it came from when it moved. Packed as VARIATION_FIELDS per note.
     */
    setVariation(packed: number[]): void {
        const notes: VariationNote[] = [];
        for (let at = 0; at + VARIATION_FIELDS <= packed.length; at += VARIATION_FIELDS) {
            notes.push({ onset: packed[at], end: packed[at + 1], pitch: packed[at + 2], fromOnset: packed[at + 3], fromPitch: packed[at + 4] });
        }
        this.variation = notes;
        this.renderLegend();
        this.invalidate();
    }

    clearVariation(): void {
        this.variation = null;
        this.renderLegend();
        this.invalidate();
    }

    /** Which sources are being heard, so only their notes light up as they sound. */
    setAudible(original: boolean, variation: boolean): void {
        this.originalAudible = original;
        this.variationAudible = variation;
        this.invalidate();
    }

    /** Follows the player every animation frame, until called with null. */
    follow(player: PlayerHandle | null): void {
        this.player = player;
        this.invalidate();
    }

    dispose(): void {
        this.disposed = true;
        cancelAnimationFrame(this.frame);
        this.resizeObserver.disconnect();
        this.roll.removeEventListener("pointerdown", this.onPointerDown);
        this.roll.removeEventListener("pointermove", this.onPointerMove);
        this.roll.removeEventListener("pointerup", this.onPointerUp);
        this.roll.removeEventListener("pointercancel", this.onPointerCancel);
        this.roll.removeEventListener("keydown", this.onKeyDown);
        this.roll.removeEventListener("wheel", this.onWheel);
        this.keys.removeEventListener("pointerdown", this.onKeysDown);
        this.keys.removeEventListener("pointermove", this.onKeysMove);
        this.keys.removeEventListener("pointerup", this.onKeysUp);
        this.keys.removeEventListener("pointercancel", this.onKeysUp);
        this.releaseAllLive();
        this.scroller.removeEventListener("input", this.onScroll);
        document.removeEventListener("pointerdown", this.onDocumentPointerDown);
        document.removeEventListener("pointerup", this.onDocumentPointerUp);
        document.removeEventListener("keydown", this.onDocumentKeyDown);
        this.host.replaceChildren();
    }

    private readonly tick = (): void => {
        if (this.disposed) {
            return;
        }
        const player = this.player;
        if (player !== null) {
            const now = player.currentTick();
            if (now !== this.tickNow) {
                const jumped = Math.abs(now - this.tickNow) > TICKS_PER_QUARTER;
                this.tickNow = now;
                this.dirty = true;
                if (player.state() !== "stopped" || jumped) {
                    this.keepInView(now);
                }
            }
        }
        if (this.dirty) {
            this.dirty = false;
            this.draw();
        }
        this.frame = requestAnimationFrame(this.tick);
    };

    private invalidate(): void {
        this.dirty = true;
    }

    private updateLanes(): void {
        const lanes = this.model?.lanes(this.colourBy) ?? [];
        this.laneIndex = new Map(lanes.map((lane, index) => [lane.key, index]));
        this.renderLegend();
        this.invalidate();
    }

    private timeWidth(): number {
        return Math.max(1, this.host.clientWidth - GUTTER);
    }

    private xOf(tick: number): number {
        return GUTTER + ((tick - this.scroll) * this.zoom) / TICKS_PER_QUARTER;
    }

    private visibleTicks(): number {
        return (this.timeWidth() * TICKS_PER_QUARTER) / this.zoom;
    }

    private maxScroll(): number {
        return Math.max(0, (this.model?.end ?? 0) - this.visibleTicks() * 0.5);
    }

    private scrollTo(tick: number): void {
        this.scroll = Math.round(Math.min(this.maxScroll(), Math.max(0, tick)));
        this.invalidate();
    }

    /**
     * Selects the bar after or before the selection (the first bar when there is none), or with
     * extend adds it to the selection: a click or Shift-click on its number, by keyboard (WCAG 2.1.1).
     */
    private stepBar(model: RollModel, step: 1 | -1, extend: boolean): void {
        const motif = this.motif;
        let index = 0;
        if (motif !== null) {
            const edge = step > 0 ? motif.end - 1 : motif.start;
            index = model.bars.findIndex((bar) => bar.onset <= edge && edge < bar.end) + step;
        }
        const bar = model.bars[index];
        if (bar === undefined) {
            return;
        }
        this.keepInView(bar.onset);
        void this.dotnet.invokeMethodAsync("OnRollRange", bar.onset, bar.onset, true, extend && motif !== null);
    }

    private keepInView(tick: number): void {
        const visible = this.visibleTicks();
        const offset = tick - this.scroll;
        if (offset < 0 || offset > visible * FOLLOW_RIGHT) {
            this.scrollTo(tick - visible * FOLLOW_LEFT);
        }
    }

    /** Rows from one below the lowest note to one above the highest. */
    private pitchRange(): { low: number; high: number; row: number } {
        const model = this.model;
        const low = Math.max(0, (model?.lowest ?? 60) - 1);
        const high = Math.min(127, (model?.highest ?? 72) + 1);
        const row = Math.min(MAX_ROW, Math.max(MIN_ROW, Math.floor(ROLL_HEIGHT / (high - low + 1))));
        return { low, high, row };
    }

    private draw(): void {
        const width = this.host.clientWidth;
        if (width === 0) {
            return;
        }
        const { low, high, row } = this.pitchRange();
        this.drawRoll(width, HEADER + (high - low + 1) * row, low, high, row);
        this.drawKeyboard(width);

        this.scroller.max = String(this.maxScroll());
        this.scroller.value = String(this.scroll);
    }

    private drawRoll(width: number, height: number, low: number, high: number, row: number): void {
        const context = prepare(this.roll, width, height);
        const model = this.model;
        context.fillStyle = SURFACE;
        context.fillRect(0, 0, width, height);

        // Pitch rows, black-key rows darker, a line under each C.
        for (let pitch = low; pitch <= high; pitch++) {
            const y = HEADER + (high - pitch) * row;
            if (isBlackKey(pitch)) {
                context.fillStyle = SURFACE_DARK;
                context.fillRect(GUTTER, y, width - GUTTER, row);
            }
            if (pitch % 12 === 0) {
                context.fillStyle = BEAT_LINE;
                context.fillRect(GUTTER, y + row - 1, width - GUTTER, 1);
                if (row >= 7) {
                    context.fillStyle = MUTED;
                    context.font = "10px system-ui, sans-serif";
                    context.textBaseline = "middle";
                    context.textAlign = "left";
                    context.fillText(pitchName(pitch), 4, y + row / 2);
                }
            }
        }
        if (model === null) {
            return;
        }

        const from = this.scroll;
        const to = this.scroll + this.visibleTicks();

        // Beat lines faint, bar lines strong and numbered.
        let lastLabel = -Infinity;
        context.font = "11px system-ui, sans-serif";
        context.textBaseline = "middle";
        context.textAlign = "left";
        for (const line of model.grid(from, to)) {
            const x = Math.round(this.xOf(line.tick));
            context.fillStyle = line.bar === null ? BEAT_LINE : BAR_LINE;
            context.fillRect(x, line.bar === null ? HEADER : 0, 1, height);
            if (line.bar !== null && x - lastLabel >= 28) {
                context.fillStyle = INK;
                context.fillText(String(line.bar), x + 3, HEADER / 2);
                lastLabel = x;
            }
        }

        // Phrase starts: a flag on the bar numbers and a faint line down the roll; inferred ones dashed and blue.
        for (const phrase of this.phrases) {
            if (phrase.tick < from || phrase.tick > to) {
                continue;
            }
            const x = Math.round(this.xOf(phrase.tick)) + 0.5;
            const colour = phrase.inferred ? PHRASE_INFERRED : PHRASE_MARK;
            context.strokeStyle = colour;
            context.lineWidth = 1;
            context.setLineDash(phrase.inferred ? [4, 3] : []);
            context.globalAlpha = PHRASE_LINE_ALPHA;
            context.beginPath();
            context.moveTo(x, HEADER);
            context.lineTo(x, height);
            context.stroke();
            context.globalAlpha = 1;
            context.lineWidth = 2;
            context.beginPath();
            context.moveTo(x, 0);
            context.lineTo(x, HEADER);
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

        // The selected range, or the range being dragged, shaded and marked on the bar numbers.
        const band = this.drag !== null ? { start: Math.min(this.drag.anchor, this.drag.current), end: Math.max(this.drag.anchor, this.drag.current) } : this.motif;
        if (band !== null) {
            const left = Math.max(GUTTER, this.xOf(band.start));
            const right = Math.min(width, this.xOf(band.end));
            if (right > left) {
                context.fillStyle = SELECTION_BAND;
                context.fillRect(left, HEADER, right - left, height - HEADER);
                context.fillStyle = SELECTION_HEADER;
                context.fillRect(left, HEADER - 4, right - left, 4);
                context.fillStyle = ACCENT;
                context.fillRect(Math.round(left), 0, 1, height);
                context.fillRect(Math.round(right) - 1, 0, 1, height);
                // Grips on the bar numbers show that the edges can be dragged.
                if (this.drag === null) {
                    if (left > GUTTER) {
                        context.fillRect(Math.round(left), 0, 4, HEADER);
                    }
                    if (right < width) {
                        context.fillRect(Math.round(right) - 4, 0, 4, HEADER);
                    }
                }
            }
        }

        // The range that plays, which a variation can make longer or shorter than the selection.
        const play = this.drag === null ? this.playRange : null;
        if (play !== null) {
            const left = Math.max(GUTTER, this.xOf(play.start));
            const right = Math.min(width, this.xOf(play.end));
            if (right > left) {
                context.fillStyle = PLAY_RANGE;
                context.fillRect(left, 0, right - left, 3);
                const selectionEnd = this.motif?.end ?? play.end;
                if (play.end > selectionEnd) {
                    const from = Math.max(GUTTER, this.xOf(selectionEnd));
                    context.fillStyle = PLAY_RANGE_EXTENSION;
                    context.fillRect(from, HEADER, right - from, height - HEADER);
                }
                if (play.end !== selectionEnd && right < width) {
                    context.strokeStyle = PLAY_RANGE;
                    context.setLineDash([4, 4]);
                    context.beginPath();
                    context.moveTo(Math.round(right) - 0.5, 0);
                    context.lineTo(Math.round(right) - 0.5, height);
                    context.stroke();
                    context.setLineDash([]);
                }
            }
        }

        // The places of the chosen fact: a faint band and an underline along the bottom.
        for (const span of this.highlight?.spans ?? []) {
            const left = Math.max(GUTTER, this.xOf(span.start));
            const right = Math.min(width, this.xOf(span.end));
            if (right > left) {
                context.fillStyle = HIGHLIGHT_BAND;
                context.fillRect(left, HEADER, right - left, height - HEADER);
                context.fillStyle = HIGHLIGHT;
                context.fillRect(left, height - HIGHLIGHT_UNDERLINE, Math.max(2, right - left - 1), HIGHLIGHT_UNDERLINE);
            }
        }

        context.save();
        context.beginPath();
        context.rect(GUTTER, HEADER, width - GUTTER, height - HEADER);
        context.clip();
        const sounding = new Set(this.soundingNow(model));
        for (const index of model.inRange(from, to)) {
            const note = model.notes[index];
            if (note.pitch < low || note.pitch > high) {
                continue;
            }
            const x = this.xOf(note.onset);
            const w = Math.max(2, ((note.end - note.onset) * this.zoom) / TICKS_PER_QUARTER - 1);
            const y = HEADER + (high - note.pitch) * row + 1;
            const h = Math.max(2, row - 2);
            const lane = this.laneIndex.get(laneKey(note, this.colourBy)) ?? 0;

            const highlighted = this.highlight?.notes.has(index) ?? false;
            // A highlighted note outside the selection stays at full strength, as the fact points to it.
            const outside = !highlighted && ((this.motif !== null && !this.motif.notes.has(index)) || (this.filtered !== null && !this.filtered.has(index)));
            const behind = this.variation !== null && this.motif !== null && this.motif.notes.has(index);
            context.globalAlpha = outside ? OUTSIDE_ALPHA : behind ? BEHIND_VARIATION_ALPHA : sounding.has(index) ? 1 : 0.82;
            context.fillStyle = LANE_COLOURS[lane % LANE_COLOURS.length];
            context.fillRect(x, y, w, h);
            const pattern = this.pattern(LANE_PATTERNS[lane % LANE_PATTERNS.length], context);
            if (pattern !== null) {
                context.fillStyle = pattern;
                context.fillRect(x, y, w, h);
            }
            context.globalAlpha = 1;

            // Inferred values are marked by a dashed outline, and an inferred hand by a question mark.
            if (note.noteInferred || note.handInferred) {
                context.setLineDash([3, 2]);
                context.strokeStyle = INK;
                context.lineWidth = 1;
                context.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
                context.setLineDash([]);
            }
            if (note.hand !== HAND_NONE && row >= 10 && w >= 18) {
                context.fillStyle = CHARCOAL;
                context.font = "bold 9px system-ui, sans-serif";
                context.textAlign = "left";
                context.fillText(`${note.hand === HAND_LEFT ? "L" : "R"}${note.handInferred ? "?" : ""}`, x + 3, y + h / 2);
            }
            // Selected notes get a thick light outline with a dark edge, readable on every lane colour.
            if (this.selected.has(index)) {
                context.strokeStyle = CHARCOAL;
                context.lineWidth = 4;
                context.strokeRect(x - 1, y - 1, w + 2, h + 2);
                context.strokeStyle = INK;
                context.lineWidth = 2;
                context.strokeRect(x - 1, y - 1, w + 2, h + 2);
            }
            // Highlighted notes get a solid blue box with a dark edge, outside the selection's outline.
            if (highlighted) {
                context.strokeStyle = CHARCOAL;
                context.lineWidth = 5;
                context.strokeRect(x - 3, y - 3, w + 6, h + 6);
                context.strokeStyle = HIGHLIGHT;
                context.lineWidth = 2;
                context.strokeRect(x - 3, y - 3, w + 6, h + 6);
            }
        }
        this.drawVariation(context, from, to, high, low, row);
        context.restore();

        // Playhead.
        const playhead = this.xOf(this.tickNow);
        if (playhead >= GUTTER && playhead <= width) {
            context.fillStyle = ACCENT;
            context.fillRect(Math.round(playhead) - 1, 0, 2, height);
        }
    }

    /** The variation as outlines where its notes land, each linked to the motif note it came from when it moved. */
    private drawVariation(context: CanvasRenderingContext2D, from: number, to: number, high: number, low: number, row: number): void {
        const variation = this.variation;
        if (variation === null) {
            return;
        }
        const centre = (pitch: number): number => HEADER + (high - pitch) * row + row / 2;
        for (const note of variation) {
            if (note.end < from || note.onset > to || note.pitch < low || note.pitch > high) {
                continue;
            }
            const x = this.xOf(note.onset);
            const w = Math.max(2, ((note.end - note.onset) * this.zoom) / TICKS_PER_QUARTER - 1);
            const y = HEADER + (high - note.pitch) * row + 1;
            const h = Math.max(2, row - 2);
            if (note.onset !== note.fromOnset || note.pitch !== note.fromPitch) {
                context.strokeStyle = VARIATION_LINK;
                context.lineWidth = 1;
                context.beginPath();
                context.moveTo(this.xOf(note.fromOnset), centre(note.fromPitch));
                context.lineTo(x, centre(note.pitch));
                context.stroke();
            }
            const sounding = this.variationAudible && note.onset <= this.tickNow && note.end > this.tickNow;
            context.fillStyle = sounding ? VARIATION_FILL_SOUNDING : VARIATION_FILL;
            context.fillRect(x, y, w, h);
            context.strokeStyle = INK;
            context.lineWidth = 2;
            context.strokeRect(x + 1, y + 1, w - 2, h - 2);
        }
    }

    private drawKeyboard(width: number): void {
        const context = prepare(this.keys, width, KEYBOARD_HEIGHT);
        context.fillStyle = SURFACE;
        context.fillRect(0, 0, width, KEYBOARD_HEIGHT);

        // Which lane lights each sounding key: the note that started last at that pitch.
        const lit = new Map<number, number>();
        const model = this.model;
        if (model !== null) {
            for (const index of this.soundingNow(model)) {
                const note = model.notes[index];
                lit.set(note.pitch, this.laneIndex.get(laneKey(note, this.colourBy)) ?? 0);
            }
        }
        // Variation notes and keys struck by hand light in the ink colour, so they read apart from the piece's lanes.
        const struck = new Set<number>(this.live.keys());
        if (this.variation !== null && this.variationAudible) {
            for (const note of this.variation) {
                if (note.onset <= this.tickNow && note.end > this.tickNow) {
                    struck.add(note.pitch);
                }
            }
        }

        let whites = 0;
        for (let pitch = LOWEST_KEY; pitch <= HIGHEST_KEY; pitch++) {
            whites += isBlackKey(pitch) ? 0 : 1;
        }
        const whiteWidth = width / whites;
        const blackWidth = whiteWidth * 0.6;
        const blackHeight = KEYBOARD_HEIGHT * 0.62;

        const positions = new Map<number, number>();
        let white = 0;
        for (let pitch = LOWEST_KEY; pitch <= HIGHEST_KEY; pitch++) {
            if (isBlackKey(pitch)) {
                positions.set(pitch, white * whiteWidth - blackWidth / 2);
            } else {
                positions.set(pitch, white * whiteWidth);
                white++;
            }
        }

        const drawKey = (pitch: number, black: boolean): void => {
            const x = positions.get(pitch) ?? 0;
            const w = black ? blackWidth : whiteWidth;
            const h = black ? blackHeight : KEYBOARD_HEIGHT;
            const lane = lit.get(pitch);
            const byHand = struck.has(pitch);
            context.fillStyle = byHand ? MUTED : lane !== undefined ? LANE_COLOURS[lane % LANE_COLOURS.length] : black ? CHARCOAL : INK;
            context.fillRect(x + 0.5, 0, w - 1, h);
            if (!black) {
                context.fillStyle = BAR_LINE;
                context.fillRect(x, 0, 1, h);
            }
            // A dot as well as the colour, so a lit key reads without colour vision.
            if (lane !== undefined || byHand) {
                const radius = Math.max(2, Math.min(w * 0.28, 5));
                context.fillStyle = CHARCOAL;
                context.beginPath();
                context.arc(x + w / 2, h - radius - 4, radius, 0, Math.PI * 2);
                context.fill();
            }
        };
        this.keyLayout = { positions, whiteWidth, blackWidth, blackHeight };
        for (let pitch = LOWEST_KEY; pitch <= HIGHEST_KEY; pitch++) {
            if (!isBlackKey(pitch)) {
                drawKey(pitch, false);
            }
        }
        for (let pitch = LOWEST_KEY; pitch <= HIGHEST_KEY; pitch++) {
            if (isBlackKey(pitch)) {
                drawKey(pitch, true);
            }
        }
        // Middle C, so the keyboard can be read without counting.
        const middleC = positions.get(60);
        if (middleC !== undefined && whiteWidth >= 8) {
            context.fillStyle = MUTED;
            context.font = "9px system-ui, sans-serif";
            context.textAlign = "center";
            context.textBaseline = "alphabetic";
            context.fillText("C4", middleC + whiteWidth / 2, KEYBOARD_HEIGHT - 2);
        }
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

    private renderLegend(): void {
        const model = this.model;
        const items: HTMLElement[] = [];
        for (const lane of model?.lanes(this.colourBy) ?? []) {
            const index = this.laneIndex.get(lane.key) ?? 0;
            items.push(legendItem(swatch(LANE_COLOURS[index % LANE_COLOURS.length], LANE_PATTERNS[index % LANE_PATTERNS.length], false), lane.label));
        }
        if (model !== null && model.notes.length > 0) {
            items.push(legendItem(null, "Drag across the roll to select · drag its edges to adjust · click an empty place to clear · click a note or key to hear it"));
        }
        if (this.variation !== null) {
            items.push(legendItem(variationSwatch(), "Outlined: the variation, linked to the notes it moved from"));
        }
        if (this.highlight !== null) {
            items.push(legendItem(highlightSwatch(), "Blue box and underline: the places of the fact chosen on Understand"));
        }
        if (model?.notes.some((note) => note.noteInferred || note.handInferred)) {
            items.push(legendItem(swatch(SURFACE, "solid", true), "Dashed outline: inferred, not in the file"));
        }
        if (model?.notes.some((note) => note.hand !== HAND_NONE)) {
            const inferred = model.notes.some((note) => note.handInferred);
            items.push(legendItem(null, inferred ? "L, R: hand from the file · L?, R?: hand inferred" : "L, R: hand from the file"));
        }
        this.legend.replaceChildren(...items);
    }

    private selectIndex(index: number | undefined, add: boolean): void {
        if (index === undefined) {
            if (!add) {
                this.selected = new Set();
            }
        } else if (add) {
            if (!this.selected.delete(index)) {
                this.selected.add(index);
            }
        } else {
            this.selected = new Set([index]);
        }
        this.focusIndex = index ?? this.focusIndex;
        this.invalidate();
        const model = this.model;
        if (model !== null) {
            const ids = [...this.selected].sort((a, b) => a - b).map((i) => model.notes[i].id);
            void this.dotnet.invokeMethodAsync("OnRollSelection", ids);
        }
    }

    private readonly onPointerDown = (event: PointerEvent): void => {
        const model = this.model;
        if (model === null) {
            return;
        }
        const box = this.roll.getBoundingClientRect();
        const x = event.clientX - box.left;
        const y = event.clientY - box.top;
        const { high, row } = this.pitchRange();
        const pitch = high - Math.floor((y - HEADER) / row);
        this.roll.setPointerCapture(event.pointerId);
        // No text selection while dragging; focus by hand so the keys (Escape, arrows) reach the roll.
        event.preventDefault();
        this.roll.focus({ preventScroll: true });
        if (x < GUTTER) {
            // The pitch names play their row's key, as the keyboard does.
            if (y >= HEADER) {
                this.press = { pointer: event.pointerId, x, tick: -1, pitch, add: false, sounding: this.strike(pitch) };
            }
            return;
        }
        const tick = this.tickAtX(x);
        // A press on an edge of the selection drags that edge; the other edge stays where it is.
        const edge = this.edgeAt(x);
        if (edge !== null && this.motif !== null) {
            const anchor = edge === "start" ? this.motif.end - 1 : this.motif.start;
            this.drag = { pointer: event.pointerId, anchorX: Number.NaN, anchor, current: tick, edge: true };
            this.invalidate();
            return;
        }
        if (y < HEADER) {
            // Shift-click on a bar number extends the selection to that bar.
            if (event.shiftKey && this.motif !== null) {
                void this.dotnet.invokeMethodAsync("OnRollRange", Math.round(tick), Math.round(tick), true, true);
                return;
            }
            this.drag = { pointer: event.pointerId, anchorX: x, anchor: tick, current: tick, edge: false };
            this.invalidate();
            return;
        }
        // A note sounds as soon as it is pressed, over playback too.
        const index = model.noteAt(tick, pitch);
        const sounding = index !== undefined ? this.strike(model.notes[index].pitch) : null;
        this.press = { pointer: event.pointerId, x, tick, pitch, add: event.shiftKey || event.ctrlKey || event.metaKey, sounding };
    };

    private readonly onPointerMove = (event: PointerEvent): void => {
        const box = this.roll.getBoundingClientRect();
        const x = event.clientX - box.left;
        const press = this.press;
        if (press !== null && event.pointerId === press.pointer) {
            if (press.tick < 0) {
                // Dragging down the pitch names plays each key in turn.
                const { high, row } = this.pitchRange();
                const pitch = high - Math.floor((event.clientY - box.top - HEADER) / row);
                if (pitch !== press.pitch) {
                    this.release(press.sounding);
                    press.pitch = pitch;
                    press.sounding = this.strike(pitch);
                }
                return;
            }
            // A press on the notes area that moves becomes a range drag, as on the bar numbers.
            if (Math.abs(x - press.x) >= CLICK_SLOP) {
                this.release(press.sounding);
                this.drag = { pointer: press.pointer, anchorX: press.x, anchor: press.tick, current: press.tick, edge: false };
                this.press = null;
            }
        }
        if (this.drag === null || event.pointerId !== this.drag.pointer) {
            this.roll.style.cursor = x >= GUTTER && this.edgeAt(x) !== null ? "ew-resize" : "";
            return;
        }
        this.drag.current = this.tickAtX(Math.max(GUTTER, x));
        // Dragging past either edge scrolls the roll along.
        if (x > this.host.clientWidth - 8 || x < GUTTER + 8) {
            this.keepInView(this.drag.current);
        }
        this.invalidate();
    };

    /**
     * Ends a press. A drag sends its range; music-core snaps it to beats and replies with setMotif.
     * A click on a note selects it; a click on an empty place clears the selection.
     */
    private readonly onPointerUp = (event: PointerEvent): void => {
        const press = this.press;
        if (press !== null && event.pointerId === press.pointer) {
            this.press = null;
            this.release(press.sounding);
            if (press.tick < 0) {
                return;
            }
            const model = this.model;
            const index = model?.noteAt(press.tick, press.pitch);
            if (index !== undefined || press.add) {
                this.selectIndex(index, press.add);
            } else {
                this.clearAll();
            }
            return;
        }
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointer) {
            return;
        }
        const box = this.roll.getBoundingClientRect();
        const x = event.clientX - box.left;
        const click = !drag.edge && Math.abs(x - drag.anchorX) < CLICK_SLOP;
        // An end edge let go on a beat line ends there, not a beat later: music-core rounds an end up to the next beat.
        const current = drag.edge && drag.current > drag.anchor ? Math.max(drag.anchor, drag.current - 1) : drag.current;
        void this.dotnet.invokeMethodAsync("OnRollRange", Math.round(drag.anchor), Math.round(current), click, false);
        this.drag = null;
        this.invalidate();
    };

    private readonly onPointerCancel = (): void => {
        this.release(this.press?.sounding ?? null);
        this.drag = null;
        this.press = null;
        this.invalidate();
    };

    /** Which edge of the selection is under x, if any. */
    private edgeAt(x: number): "start" | "end" | null {
        const motif = this.motif;
        if (motif === null) {
            return null;
        }
        if (Math.abs(x - this.xOf(motif.end)) <= EDGE_GRAB) {
            return "end";
        }
        return Math.abs(x - this.xOf(motif.start)) <= EDGE_GRAB ? "start" : null;
    }

    /** Strikes a key by hand and lights it; returns the pitch, to release later. */
    private strike(pitch: number): number | null {
        if (pitch < 0 || pitch > 127 || this.player?.noteOn === undefined) {
            return null;
        }
        this.player.noteOn(pitch);
        this.live.set(pitch, (this.live.get(pitch) ?? 0) + 1);
        this.invalidate();
        return pitch;
    }

    private release(pitch: number | null): void {
        if (pitch === null) {
            return;
        }
        const held = (this.live.get(pitch) ?? 1) - 1;
        if (held > 0) {
            this.live.set(pitch, held);
        } else {
            this.live.delete(pitch);
            this.player?.noteOff?.(pitch);
        }
        this.invalidate();
    }

    private releaseAllLive(): void {
        for (const pitch of this.live.keys()) {
            this.player?.noteOff?.(pitch);
        }
        this.live.clear();
    }

    /** The key under a point on the keyboard: black keys first, as they sit on top. */
    private keyAt(x: number, y: number): number | null {
        const layout = this.keyLayout;
        if (layout === null) {
            return null;
        }
        if (y < layout.blackHeight) {
            for (const [pitch, left] of layout.positions) {
                if (isBlackKey(pitch) && x >= left && x < left + layout.blackWidth) {
                    return pitch;
                }
            }
        }
        for (const [pitch, left] of layout.positions) {
            if (!isBlackKey(pitch) && x >= left && x < left + layout.whiteWidth) {
                return pitch;
            }
        }
        return null;
    }

    private readonly onKeysDown = (event: PointerEvent): void => {
        const box = this.keys.getBoundingClientRect();
        this.keys.setPointerCapture(event.pointerId);
        event.preventDefault();
        this.keysPress = { pointer: event.pointerId, pitch: this.strike(this.keyAt(event.clientX - box.left, event.clientY - box.top) ?? -1) };
    };

    private readonly onKeysMove = (event: PointerEvent): void => {
        const press = this.keysPress;
        if (press === null || event.pointerId !== press.pointer) {
            return;
        }
        const box = this.keys.getBoundingClientRect();
        const pitch = this.keyAt(event.clientX - box.left, event.clientY - box.top);
        if (pitch !== press.pitch) {
            this.release(press.pitch);
            press.pitch = this.strike(pitch ?? -1);
        }
    };

    private readonly onKeysUp = (event: PointerEvent): void => {
        const press = this.keysPress;
        if (press === null || event.pointerId !== press.pointer) {
            return;
        }
        this.release(press.pitch);
        this.keysPress = null;
    };

    /** Clears the selected notes and asks the page to clear the motif selection, when there is any. */
    private clearAll(): void {
        if (this.selected.size > 0) {
            this.selectIndex(undefined, false);
        }
        if (this.motif !== null) {
            void this.dotnet.invokeMethodAsync("OnRollClear");
        }
    }

    private readonly onDocumentPointerDown = (event: PointerEvent): void => {
        const target = event.target;
        this.outsidePress =
            target instanceof Element && !this.host.contains(target) && target.closest(KEEPS_SELECTION) === null
                ? { x: event.clientX, y: event.clientY, target }
                : null;
    };

    /** A click on the page outside the roll, away from any control, clears the selection. */
    /** Space asks the page to play or pause: the selection on a loop when there is one, else the piece. */
    private readonly onDocumentKeyDown = (event: KeyboardEvent): void => {
        if (event.key !== " " || event.repeat || event.ctrlKey || event.altKey || event.metaKey || this.model === null) {
            return;
        }
        const target = event.target;
        if (target instanceof Element && target.closest(SPACE_OWNERS) !== null) {
            return;
        }
        event.preventDefault();
        void this.dotnet.invokeMethodAsync("OnRollSpace");
    };

    private readonly onDocumentPointerUp = (event: PointerEvent): void => {
        const press = this.outsidePress;
        this.outsidePress = null;
        if (press === null || Math.hypot(event.clientX - press.x, event.clientY - press.y) >= CLICK_SLOP) {
            return;
        }
        if (this.host.isConnected && this.host.offsetParent !== null) {
            this.clearAll();
        }
    };

    /** The notes being heard: those at the playhead, only the motif's when it plays alone. */
    /** The piece's notes being heard: none while only the variation plays, the motif's when it plays alone, and only those the Notes filter keeps. */
    private soundingNow(model: RollModel): number[] {
        if (!this.originalAudible) {
            return [];
        }
        const motif = this.motif;
        const filtered = this.filtered;
        return model
            .soundingAt(this.tickNow)
            .filter((index) => (motif === null || !motif.alone || motif.notes.has(index)) && (filtered === null || filtered.has(index)));
    }

    private tickAtX(x: number): number {
        return Math.max(0, this.scroll + ((x - GUTTER) * TICKS_PER_QUARTER) / this.zoom);
    }

    private readonly onKeyDown = (event: KeyboardEvent): void => {
        const model = this.model;
        if (model === null) {
            return;
        }
        // By key position, so Shift (which turns them into braces) and other layouts keep working.
        if (event.code === "BracketRight" || event.code === "BracketLeft") {
            this.stepBar(model, event.code === "BracketRight" ? 1 : -1, event.shiftKey);
            event.preventDefault();
            return;
        }
        switch (event.key) {
            case "ArrowRight":
            case "ArrowLeft": {
                const next = model.step(this.focusIndex, event.key === "ArrowRight" ? 1 : -1);
                this.selectIndex(next, event.shiftKey);
                if (next !== undefined) {
                    this.keepInView(model.notes[next].onset);
                }
                break;
            }
            case "Escape":
                this.clearAll();
                break;
            case "+":
            case "=":
                this.zoomBy(1.5);
                break;
            case "-":
                this.zoomBy(1 / 1.5);
                break;
            default:
                return;
        }
        event.preventDefault();
    };

    private readonly onWheel = (event: WheelEvent): void => {
        // Ctrl (or a pinch) zooms around the pointer; a sideways or Shift scroll moves through time.
        // A plain vertical scroll is left to the page.
        if (event.ctrlKey) {
            const box = this.roll.getBoundingClientRect();
            this.zoomBy(event.deltaY < 0 ? 1.25 : 0.8, Math.max(0, event.clientX - box.left - GUTTER));
        } else {
            const delta = event.shiftKey ? event.deltaY : event.deltaX;
            if (delta === 0) {
                return;
            }
            this.scrollTo(this.scroll + (delta * TICKS_PER_QUARTER) / this.zoom);
        }
        event.preventDefault();
    };

    private readonly onScroll = (): void => {
        this.scrollTo(Number(this.scroller.value));
    };
}

/** Sizes a canvas for the device's pixel ratio and returns a context drawing in CSS pixels. */
function prepare(canvas: HTMLCanvasElement, width: number, height: number): CanvasRenderingContext2D {
    const ratio = window.devicePixelRatio || 1;
    const pixelWidth = Math.round(width * ratio);
    const pixelHeight = Math.round(height * ratio);
    if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
        canvas.width = pixelWidth;
        canvas.height = pixelHeight;
        canvas.style.width = `${width}px`;
        canvas.style.height = `${height}px`;
    }
    const context = canvas.getContext("2d")!;
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    return context;
}

function patternTile(kind: PatternKind): HTMLCanvasElement {
    const size = 6;
    const tile = document.createElement("canvas");
    tile.width = size;
    tile.height = size;
    const context = tile.getContext("2d")!;
    context.strokeStyle = "rgba(21, 25, 31, 0.6)";
    context.fillStyle = "rgba(21, 25, 31, 0.6)";
    context.lineWidth = 1.5;
    context.beginPath();
    switch (kind) {
        case "rising":
            context.moveTo(0, size);
            context.lineTo(size, 0);
            context.moveTo(-1, 1);
            context.lineTo(1, -1);
            context.moveTo(size - 1, size + 1);
            context.lineTo(size + 1, size - 1);
            break;
        case "falling":
            context.moveTo(0, 0);
            context.lineTo(size, size);
            context.moveTo(size - 1, -1);
            context.lineTo(size + 1, 1);
            context.moveTo(-1, size - 1);
            context.lineTo(1, size + 1);
            break;
        case "horizontal":
            context.moveTo(0, size / 2);
            context.lineTo(size, size / 2);
            break;
        case "vertical":
            context.moveTo(size / 2, 0);
            context.lineTo(size / 2, size);
            break;
        case "dots":
            context.arc(size / 2, size / 2, 1.2, 0, Math.PI * 2);
            context.fill();
            return tile;
        case "solid":
            return tile;
    }
    context.stroke();
    return tile;
}

function swatch(colour: string, kind: PatternKind, dashed: boolean): HTMLCanvasElement {
    const width = 28;
    const height = 12;
    const canvas = document.createElement("canvas");
    canvas.setAttribute("aria-hidden", "true");
    const context = prepare(canvas, width, height);
    context.fillStyle = colour;
    context.fillRect(0, 0, width, height);
    if (kind !== "solid") {
        const pattern = context.createPattern(patternTile(kind), "repeat");
        if (pattern !== null) {
            context.fillStyle = pattern;
            context.fillRect(0, 0, width, height);
        }
    }
    if (dashed) {
        context.setLineDash([3, 2]);
        context.strokeStyle = INK;
        context.strokeRect(0.5, 0.5, width - 1, height - 1);
    }
    return canvas;
}

function variationSwatch(): HTMLCanvasElement {
    const canvas = swatch(SURFACE, "solid", false);
    const context = canvas.getContext("2d")!;
    context.fillStyle = VARIATION_FILL;
    context.fillRect(0, 0, 28, 12);
    context.strokeStyle = INK;
    context.lineWidth = 2;
    context.strokeRect(1, 1, 26, 10);
    return canvas;
}

function highlightSwatch(): HTMLCanvasElement {
    const canvas = swatch(SURFACE, "solid", false);
    const context = canvas.getContext("2d")!;
    context.fillStyle = HIGHLIGHT_BAND;
    context.fillRect(0, 0, 28, 12);
    context.strokeStyle = HIGHLIGHT;
    context.lineWidth = 2;
    context.strokeRect(1, 1, 26, 7);
    context.fillStyle = HIGHLIGHT;
    context.fillRect(0, 10, 28, 2);
    return canvas;
}

function legendItem(mark: HTMLElement | null, label: string): HTMLElement {
    const item = document.createElement("span");
    item.className = "roll-legend-item";
    if (mark !== null) {
        item.append(mark);
    }
    item.append(document.createTextNode(label));
    return item;
}

/** Entry point for Blazor. */
export function createPianoRoll(host: HTMLElement, dotnet: DotNetObject): PianoRoll {
    return new PianoRoll(host, dotnet);
}
