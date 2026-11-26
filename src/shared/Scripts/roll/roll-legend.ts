// The roll's legend (the audit's W3.12, second part): a swatch and its words for each lane as the colouring chosen orders
// them, and for the marks on the roll now (a variation, a fact's places, Check flags, notes or hands inferred). PianoRoll
// says what shows; this draws it in the theme's colours (roll-palette.ts) and owns the legend's element.

import { laneColour, lanePattern, palette, patternTile, prepare, type PatternKind } from "./roll-palette.js";
import type { RollLabels } from "./roll-model.js";

/** What the legend lists: the lanes by their index and label, and which of the other marks the roll shows now. */
export interface LegendContent {
    readonly lanes: readonly { index: number; label: string }[];
    readonly variation: boolean;
    readonly highlight: boolean;
    readonly checks: boolean;
    readonly inferred: boolean;
}

/** The legend's words. */
export type LegendWords = Pick<RollLabels, "legendVariation" | "legendHighlight" | "legendCheck" | "legendInferred">;

/** The legend under the roll. */
export class RollLegend {
    readonly element: HTMLElement;

    constructor() {
        this.element = document.createElement("div");
        this.element.className = "roll-legend";
    }

    /** Replaces the legend's items by those of what the roll shows now, in the palette as it is read. */
    show(content: LegendContent, words: LegendWords): void {
        const items: HTMLElement[] = content.lanes.map((lane) => legendItem(swatch(laneColour(lane.index), lanePattern(lane.index), false), lane.label));
        if (content.variation) {
            items.push(legendItem(variationSwatch(), words.legendVariation));
        }
        if (content.highlight) {
            items.push(legendItem(highlightSwatch(), words.legendHighlight));
        }
        if (content.checks) {
            items.push(legendItem(checkSwatch(), words.legendCheck));
        }
        if (content.inferred) {
            items.push(legendItem(swatch(palette.surface, "solid", true), words.legendInferred));
        }
        this.element.replaceChildren(...items);
    }
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
        context.strokeStyle = palette.ink;
        context.strokeRect(0.5, 0.5, width - 1, height - 1);
    }
    return canvas;
}

function variationSwatch(): HTMLCanvasElement {
    const canvas = swatch(palette.surface, "solid", false);
    const context = canvas.getContext("2d")!;
    context.fillStyle = palette.variationFill;
    context.fillRect(0, 0, 28, 12);
    context.strokeStyle = palette.ink;
    context.lineWidth = 2;
    context.strokeRect(1, 1, 26, 10);
    return canvas;
}

function highlightSwatch(): HTMLCanvasElement {
    const canvas = swatch(palette.surface, "solid", false);
    const context = canvas.getContext("2d")!;
    context.fillStyle = palette.highlightBand;
    context.fillRect(0, 0, 28, 12);
    context.strokeStyle = palette.highlight;
    context.lineWidth = 2;
    context.strokeRect(1, 1, 26, 7);
    context.fillStyle = palette.highlight;
    context.fillRect(0, 10, 28, 2);
    return canvas;
}

function checkSwatch(): HTMLCanvasElement {
    const canvas = swatch(palette.surface, "solid", false);
    const context = canvas.getContext("2d")!;
    context.clearRect(0, 0, 28, 12);
    context.fillStyle = palette.checkFill;
    context.beginPath();
    context.roundRect(8, 0, 12, 12, 2);
    context.fill();
    context.fillStyle = palette.keyBlack;
    context.font = "bold 9px \"Source Sans 3\", system-ui, sans-serif";
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText("?", 14, 6.5);
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
