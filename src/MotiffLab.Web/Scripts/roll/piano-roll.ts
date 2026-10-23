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
}

const TICKS_PER_QUARTER = 480;

/** Horizontal zoom, in pixels per quarter note. */
const DEFAULT_ZOOM = 48;
const MIN_ZOOM = 6;
const MAX_ZOOM = 480;

const GUTTER = 40;
// Tall enough to press and drag on for a bar selection (WCAG 2.2 target size, 2.5.8).
const HEADER = 24;
const ROLL_HEIGHT = 300;
const MIN_ROW = 5;
const MAX_ROW = 14;
const KEYBOARD_HEIGHT = 64;

/** While playing, the view pages on when the playhead passes this share of its width. */
const FOLLOW_RIGHT = 0.85;
const FOLLOW_LEFT = 0.15;

/** A press on the bar numbers that moves less than this many pixels is a click on one bar. */
const CLICK_SLOP = 4;

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
    /** A drag across the bar numbers in progress: where it started and where the pointer is. */
    private drag: { pointer: number; anchorX: number; anchor: number; current: number } | null = null;
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
            "Piano roll. Drag across the bar numbers to select bars, or click a bar number to select that bar. Click a note to select it, Shift-click to add to the selection. Arrow keys move the selection, Escape clears it, plus and minus zoom.",
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
        this.keys.setAttribute("aria-label", "Keyboard: the keys of the notes being heard are lit and marked with a dot.");

        this.legend = document.createElement("div");
        this.legend.className = "roll-legend";

        this.host.replaceChildren(this.roll, this.scroller, this.keys, this.legend);

        this.roll.addEventListener("pointerdown", this.onPointerDown);
        this.roll.addEventListener("pointermove", this.onPointerMove);
        this.roll.addEventListener("pointerup", this.onPointerUp);
        this.roll.addEventListener("pointercancel", this.onPointerCancel);
        this.roll.addEventListener("keydown", this.onKeyDown);
        this.roll.addEventListener("wheel", this.onWheel, { passive: false });
        this.scroller.addEventListener("input", this.onScroll);
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
        this.drag = null;
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
        this.scroller.removeEventListener("input", this.onScroll);
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

            const outside = this.motif !== null && !this.motif.notes.has(index);
            context.globalAlpha = outside ? OUTSIDE_ALPHA : sounding.has(index) ? 1 : 0.82;
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
        }
        context.restore();

        // Playhead.
        const playhead = this.xOf(this.tickNow);
        if (playhead >= GUTTER && playhead <= width) {
            context.fillStyle = ACCENT;
            context.fillRect(Math.round(playhead) - 1, 0, 2, height);
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
            context.fillStyle = lane !== undefined ? LANE_COLOURS[lane % LANE_COLOURS.length] : black ? CHARCOAL : INK;
            context.fillRect(x + 0.5, 0, w - 1, h);
            if (!black) {
                context.fillStyle = BAR_LINE;
                context.fillRect(x, 0, 1, h);
            }
            // A dot as well as the colour, so a lit key reads without colour vision.
            if (lane !== undefined) {
                const radius = Math.max(2, Math.min(w * 0.28, 5));
                context.fillStyle = CHARCOAL;
                context.beginPath();
                context.arc(x + w / 2, h - radius - 4, radius, 0, Math.PI * 2);
                context.fill();
            }
        };
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
            items.push(legendItem(null, "Drag across the bar numbers to select bars"));
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
        if (x < GUTTER) {
            return;
        }
        const tick = this.tickAtX(x);
        if (y < HEADER) {
            this.roll.setPointerCapture(event.pointerId);
            this.drag = { pointer: event.pointerId, anchorX: x, anchor: tick, current: tick };
            this.invalidate();
            event.preventDefault();
            return;
        }
        const { high, row } = this.pitchRange();
        const pitch = high - Math.floor((y - HEADER) / row);
        this.selectIndex(model.noteAt(tick, pitch), event.shiftKey || event.ctrlKey || event.metaKey);
    };

    private readonly onPointerMove = (event: PointerEvent): void => {
        if (this.drag === null || event.pointerId !== this.drag.pointer) {
            return;
        }
        const box = this.roll.getBoundingClientRect();
        const x = event.clientX - box.left;
        this.drag.current = this.tickAtX(Math.max(GUTTER, x));
        // Dragging past either edge scrolls the roll along.
        if (x > this.host.clientWidth - 8 || x < GUTTER + 8) {
            this.keepInView(this.drag.current);
        }
        this.invalidate();
    };

    /** Ends a drag across the bar numbers; music-core snaps the range to beats and replies with setMotif. */
    private readonly onPointerUp = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointer) {
            return;
        }
        const box = this.roll.getBoundingClientRect();
        const x = event.clientX - box.left;
        const click = Math.abs(x - drag.anchorX) < CLICK_SLOP;
        void this.dotnet.invokeMethodAsync("OnRollRange", Math.round(drag.anchor), Math.round(drag.current), click);
        this.drag = null;
        this.invalidate();
    };

    private readonly onPointerCancel = (): void => {
        this.drag = null;
        this.invalidate();
    };

    /** The notes being heard: those at the playhead, only the motif's when it plays alone. */
    private soundingNow(model: RollModel): number[] {
        const motif = this.motif;
        const sounding = model.soundingAt(this.tickNow);
        return motif !== null && motif.alone ? sounding.filter((index) => motif.notes.has(index)) : sounding;
    }

    private tickAtX(x: number): number {
        return Math.max(0, this.scroll + ((x - GUTTER) * TICKS_PER_QUARTER) / this.zoom);
    }

    private readonly onKeyDown = (event: KeyboardEvent): void => {
        const model = this.model;
        if (model === null) {
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
                this.selectIndex(undefined, false);
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
