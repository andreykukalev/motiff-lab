// The roll's colours, patterns and measures (0018): the design kit's tokens, read when the roll draws so it follows the
// theme. The right hand is cobalt and the left teal, the hand colours of the kit; other lanes take the plum and the user
// inks, each with its pattern so lanes differ by more than colour (WCAG 2.2 AA, 1.4.1). One palette for the page: the
// painter, the keys, the legend and the strips read it; only readPalette writes it.

import { mix, token, withAlpha } from "../layout/tokens.js";
import { easing, milliseconds } from "./roll-view.js";

/** The roll's colours, as readPalette last read them from the tokens. */
export interface RollPalette {
    surface: string;
    surfaceDark: string;
    ink: string;
    muted: string;
    /** A halo round outlines drawn on the notes, so they read on every lane colour. */
    charcoal: string;
    paper: string;
    /** Letters and dots on a note or a lit key. */
    onNote: string;
    accent: string;
    barLine: string;
    beatLine: string;
    selectionBand: string;
    selectionHeader: string;
    keyWhite: string;
    keyBlack: string;
    checkFill: string;
    /** The range that plays, marked on the bar numbers, with a band where it runs past the selection. */
    playRange: string;
    playRangeExtension: string;
    variationFill: string;
    variationFillSounding: string;
    variationLink: string;
    ghostLine: string;
    /** Phrase starts (#18): a flag on the bar numbers and a faint line down the roll, dashed where inferred. */
    phraseMark: string;
    phraseInferred: string;
    /** The places of a fact chosen in Insights (#25): a solid cobalt box round each note, a faint band and an underline. */
    highlight: string;
    highlightBand: string;
    patternInk: string;
    /** The strips of the iPad's roll (roll-strips.ts): the whole-piece track and its muted words. */
    stripLine: string;
    stripMuted: string;
    /** The lanes' colours in order: cobalt, teal, plum, then the user inks. */
    lanes: string[];
}

/** The measures and the motion the roll takes from the tokens, with what it uses before they are read. */
export interface RollMeasures {
    /** What a finger needs (`--h-touch`), in pixels. */
    touch: number;
    /** The playhead handle's corners (`--r-xs`), in pixels. */
    knobRadius: number;
    /** The free room either side of what Fit lays across the roll (`--s-4`), in pixels. */
    fitMargin: number;
    /** Fit and a selection brought into view (`--t-slow`), in ms. */
    glideMs: number;
    /** Following the playhead (`--t-base`), in ms. */
    followMs: number;
    /** How far back a press still finds a playhead that moves on while playing (#533 4): the kit's slow time, in ms. */
    reachBackMs: number;
    /** The kit's easing (`--ease`). */
    ease: (share: number) => number;
}

export const palette: RollPalette = {
    surface: "",
    surfaceDark: "",
    ink: "",
    muted: "",
    charcoal: "",
    paper: "",
    onNote: "",
    accent: "",
    barLine: "",
    beatLine: "",
    selectionBand: "",
    selectionHeader: "",
    keyWhite: "",
    keyBlack: "",
    checkFill: "",
    playRange: "",
    playRangeExtension: "",
    variationFill: "",
    variationFillSounding: "",
    variationLink: "",
    ghostLine: "",
    phraseMark: "",
    phraseInferred: "",
    highlight: "",
    highlightBand: "",
    patternInk: "",
    stripLine: "",
    stripMuted: "",
    lanes: [],
};

export const measures: RollMeasures = {
    touch: 44,
    knobRadius: 4,
    fitMargin: 16,
    glideMs: 320,
    followMs: 200,
    reachBackMs: 320,
    ease: (share) => share,
};

/** The tokens the palette was last read from, so it is read again, and patterns made again, only when the theme changes. */
let paletteKey = "";

/** Reads the palette and the measures from the tokens; true when they changed since the last read. */
export function readPalette(element: Element): boolean {
    const t = (name: string): string => token(name, element);
    const key = ["surface", "ink", "accent", "teal", "plum", "notation", "paper"].map(t).join("|");
    if (key === paletteKey) {
        return false;
    }
    paletteKey = key;
    readColours(t);
    measures.touch = Number.parseFloat(t("h-touch")) || measures.touch;
    measures.knobRadius = Number.parseFloat(t("r-xs")) || measures.knobRadius;
    measures.fitMargin = Number.parseFloat(t("s-4")) || measures.fitMargin;
    measures.glideMs = milliseconds(t("t-slow"), measures.glideMs);
    measures.reachBackMs = measures.glideMs;
    measures.followMs = milliseconds(t("t-base"), measures.followMs);
    measures.ease = easing(t("ease"));
    return true;
}

function readColours(t: (name: string) => string): void {
    const p = palette;
    p.surface = t("surface");
    p.ink = t("ink");
    p.surfaceDark = mix(p.surface, p.ink, 0.04);
    p.muted = t("ink-2");
    p.charcoal = t("surface-2");
    p.paper = t("paper");
    p.stripLine = t("hairline-strong");
    p.stripMuted = t("ink-3");
    p.onNote = t("on-accent");
    p.accent = t("accent");
    p.barLine = mix(p.surface, p.ink, 0.3);
    p.beatLine = mix(p.surface, p.ink, 0.12);
    p.selectionBand = t("accent-tint");
    p.selectionHeader = withAlpha(p.accent, 0.45);
    p.keyWhite = t("paper");
    p.keyBlack = t("notation");
    p.checkFill = t("warning-fill");
    p.playRange = p.ink;
    p.playRangeExtension = withAlpha(p.ink, 0.08);
    const plum = t("plum");
    p.variationFill = withAlpha(plum, 0.28);
    p.variationFillSounding = withAlpha(plum, 0.75);
    p.variationLink = withAlpha(plum, 0.45);
    p.ghostLine = plum;
    p.phraseMark = p.ink;
    p.phraseInferred = p.muted;
    p.highlight = p.accent;
    p.highlightBand = withAlpha(p.accent, 0.12);
    p.patternInk = withAlpha(p.onNote, 0.6);
    p.lanes = [p.accent, t("teal"), plum, t("ink-carmine"), t("ink-ochre"), t("ink-forest")];
}

/** The patterns that go with the lanes' colours, in the same order. */
export type PatternKind = "solid" | "rising" | "falling" | "horizontal" | "vertical" | "dots";
const LANE_PATTERNS: PatternKind[] = ["solid", "rising", "falling", "horizontal", "vertical", "dots"];

/** A lane's colour, by its index among the piece's lanes. */
export function laneColour(lane: number): string {
    return palette.lanes[lane % palette.lanes.length];
}

/** A lane's pattern, by its index among the piece's lanes. */
export function lanePattern(lane: number): PatternKind {
    return LANE_PATTERNS[lane % LANE_PATTERNS.length];
}

/** Sizes a canvas for the device's pixel ratio and returns a context drawing in CSS pixels. */
export function prepare(canvas: HTMLCanvasElement, width: number, height: number): CanvasRenderingContext2D {
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

/** A tile of a pattern, drawn in the pattern ink, to repeat over a note or a swatch. */
export function patternTile(kind: PatternKind): HTMLCanvasElement {
    const size = 6;
    const tile = document.createElement("canvas");
    tile.width = size;
    tile.height = size;
    const context = tile.getContext("2d")!;
    context.strokeStyle = palette.patternInk;
    context.fillStyle = palette.patternInk;
    context.lineWidth = 1.5;
    context.beginPath();
    if (kind === "dots") {
        context.arc(size / 2, size / 2, 1.2, 0, Math.PI * 2);
        context.fill();
        return tile;
    }
    for (const [fromX, fromY, toX, toY] of TILE_LINES[kind]) {
        context.moveTo(fromX, fromY);
        context.lineTo(toX, toY);
    }
    if (kind !== "solid") {
        context.stroke();
    }
    return tile;
}

/** The lines of each pattern's 6-pixel tile, the diagonals with their corners so they join across tiles. */
const TILE_LINES: Record<PatternKind, [number, number, number, number][]> = {
    solid: [],
    rising: [[0, 6, 6, 0], [-1, 1, 1, -1], [5, 7, 7, 5]],
    falling: [[0, 0, 6, 6], [5, -1, 7, 1], [-1, 5, 1, 7]],
    horizontal: [[0, 3, 6, 3]],
    vertical: [[3, 0, 3, 6]],
    dots: [],
};
