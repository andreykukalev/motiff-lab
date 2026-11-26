// The roll's keys (0018 section 4, W3.12): down the left of the notes a band naming each octave once, in words, then a key per
// row with its name (the names inside the notes and on the keys leave the octave out); under the roll the keyboard, whose
// keys light in their lane's colour while a note on them sounds. Drawing and the keyboard's layout only: a press on a key
// is the roll's input, which finds the key through keyAt.

import { HIGHEST_KEY, LOWEST_KEY, isBlackKey, type RollModel, type RollNote } from "./roll-model.js";
import { laneColour, palette, prepare } from "./roll-palette.js";
import { GUTTER } from "./roll-viewport.js";

/** The band naming the octaves, at the left edge of the gutter. */
const OCTAVE_BAND = 18;
/** Names are written on the keys and inside the notes only on rows this tall or taller (else on each C only). */
export const NAMED_ROW = 11;
const KEYBOARD_HEIGHT = 64;

/** The rows the gutter draws keys for, and where. */
interface GutterFrame {
    header: number;
    low: number;
    high: number;
    row: number;
    above: boolean;
    below: boolean;
}

/** What the gutter reads of what the roll shows. */
interface GutterScene {
    model: RollModel;
    lane(note: RollNote): number;
    sounding: ReadonlySet<number>;
    chosen: ReadonlySet<number>;
    rowName(pitch: number): string;
    octaveName(octave: number): { words: string; c: string };
}

/** Where the keyboard's keys lie, from its last draw, for finding the key under a pointer. */
export interface KeyLayout {
    positions: Map<number, number>;
    whiteWidth: number;
    blackWidth: number;
    blackHeight: number;
}

/** Which lane lights each sounding key: the note that started last at that pitch. */
export function litKeys(model: RollModel, sounding: Iterable<number>, lane: (note: RollNote) => number): Map<number, number> {
    const lit = new Map<number, number>();
    for (const index of sounding) {
        const note = model.notes[index];
        lit.set(note.pitch, lane(note));
    }
    return lit;
}

/**
 * The keys down the left (0018 section 4): a key per row, white or black, with its name on it when the rows are tall enough
 * and on each C when not; a key lights in its lane's colour while a note on it sounds, and shows the pointing colour while
 * a chosen note is on it. A small arrow at the right of the keys where rows go on above or below the view.
 */
export function drawGutter(context: CanvasRenderingContext2D, frame: GutterFrame, scene: GutterScene): void {
    const { header, low, high, row } = frame;
    const lit = litKeys(scene.model, scene.sounding, (note) => scene.lane(note));
    const chosen = new Set([...scene.chosen].map((index) => scene.model.notes[index]?.pitch));
    const length = (high - low + 1) * row;
    context.fillStyle = palette.surface;
    context.fillRect(0, header, GUTTER, length);
    drawOctaves(context, frame, scene);
    context.font = `${Math.min(12, Math.max(9, row - 6))}px "Source Sans 3", system-ui, sans-serif`;
    context.textAlign = "left";
    context.textBaseline = "middle";
    for (let pitch = low; pitch <= high; pitch++) {
        drawRowKey(context, frame, pitch, lit.get(pitch), chosen.has(pitch), scene.rowName(pitch));
    }
    context.fillStyle = palette.barLine;
    context.fillRect(GUTTER - 3, header, 1, length);
    context.fillStyle = palette.muted;
    const arrow = (tipY: number, direction: 1 | -1): void => {
        context.beginPath();
        context.moveTo(GUTTER - 9, tipY);
        context.lineTo(GUTTER - 13, tipY + 5 * direction);
        context.lineTo(GUTTER - 5, tipY + 5 * direction);
        context.closePath();
        context.fill();
    };
    if (frame.above) {
        arrow(header + 2, 1);
    }
    if (frame.below) {
        arrow(header + length - 2, -1);
    }
}

/** A row's key: lit, pointed at, black or white, a line under each C and F, and its name (thin rows name only their Cs). */
function drawRowKey(context: CanvasRenderingContext2D, frame: GutterFrame, pitch: number, lane: number | undefined, pointed: boolean, name: string): void {
    const { row } = frame;
    const y = frame.header + (frame.high - pitch) * row;
    const black = isBlackKey(pitch);
    const lit = lane !== undefined;
    context.fillStyle = lit ? laneColour(lane) : keyFill(pointed, black, palette.selectionBand, palette.keyBlack, palette.keyWhite);
    context.fillRect(OCTAVE_BAND, y, GUTTER - 3 - OCTAVE_BAND, row);
    context.fillStyle = palette.barLine;
    context.fillRect(OCTAVE_BAND, y + row - 1, GUTTER - 3 - OCTAVE_BAND, pitch % 12 === 0 || pitch % 12 === 5 ? 1 : 0);
    context.fillStyle = lit ? palette.onNote : keyFill(pointed, black, palette.accent, palette.keyWhite, palette.ink);
    if (row >= NAMED_ROW || pitch % 12 === 0) {
        context.fillText(name, OCTAVE_BAND + 4, y + row / 2, GUTTER - 9 - OCTAVE_BAND);
    }
}

/** A colour by what an unlit key is: pointed at, else black, else white. */
function keyFill(pointed: boolean, black: boolean, ifPointed: string, ifBlack: string, ifWhite: string): string {
    if (pointed) {
        return ifPointed;
    }
    return black ? ifBlack : ifWhite;
}

/**
 * The octaves down the left (testing item 13): a band per octave, its name written up it once ("octave 4", "первая
 * октава"), or the name of its C ("C4", "до¹") where only a few of its rows are in view, or nothing where even that does
 * not fit.
 */
function drawOctaves(context: CanvasRenderingContext2D, frame: GutterFrame, scene: Pick<GutterScene, "octaveName">): void {
    const { low, high, row } = frame;
    context.save();
    context.font = `10px "Source Sans 3", system-ui, sans-serif`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    for (let octave = Math.floor(low / 12); octave <= Math.floor(high / 12); octave++) {
        const bottom = Math.max(low, octave * 12);
        const top = Math.min(high, octave * 12 + 11);
        const y = frame.header + (high - top) * row;
        const length = (top - bottom + 1) * row;
        context.fillStyle = octave % 2 === 0 ? palette.surfaceDark : palette.surface;
        context.fillRect(0, y, OCTAVE_BAND - 2, length);
        context.fillStyle = palette.barLine;
        context.fillRect(0, y + length - 1, OCTAVE_BAND - 2, bottom === octave * 12 ? 1 : 0);
        const { words, c } = scene.octaveName(octave);
        const text = octaveLabel(context, words, c, length);
        if (text === "") {
            continue;
        }
        context.save();
        context.translate((OCTAVE_BAND - 2) / 2, y + length / 2);
        context.rotate(-Math.PI / 2);
        context.fillStyle = palette.muted;
        context.fillText(text, 0, 0.5);
        context.restore();
    }
    context.restore();
}

/** What an octave's band says in its length: its name in words, else its C's name, else nothing. */
function octaveLabel(context: CanvasRenderingContext2D, words: string, c: string, length: number): string {
    if (words !== "" && context.measureText(words).width + 8 <= length) {
        return words;
    }
    return context.measureText(c).width + 6 <= length ? c : "";
}

/** The keyboard's keys across a width: the white keys side by side, the black ones between them. */
function keyLayout(width: number): KeyLayout {
    let whites = 0;
    for (let pitch = LOWEST_KEY; pitch <= HIGHEST_KEY; pitch++) {
        whites += isBlackKey(pitch) ? 0 : 1;
    }
    const whiteWidth = width / whites;
    const blackWidth = whiteWidth * 0.6;
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
    return { positions, whiteWidth, blackWidth, blackHeight: KEYBOARD_HEIGHT * 0.62 };
}

/**
 * The keyboard under the roll: a key lights in its lane's colour while a note on it sounds (`lit`, pitch to lane), and in
 * the muted ink while struck by hand or sounding in the variation (`struck`), with a dot as well as the colour, so a lit
 * key reads without colour vision; middle C named. Returns where the keys lie.
 */
export function drawKeyboard(canvas: HTMLCanvasElement, width: number, lit: ReadonlyMap<number, number>, struck: ReadonlySet<number>, middleC: string): KeyLayout {
    const context = prepare(canvas, width, KEYBOARD_HEIGHT);
    context.fillStyle = palette.surface;
    context.fillRect(0, 0, width, KEYBOARD_HEIGHT);
    const layout = keyLayout(width);
    for (const black of [false, true]) {
        for (let pitch = LOWEST_KEY; pitch <= HIGHEST_KEY; pitch++) {
            if (isBlackKey(pitch) === black) {
                drawKey(context, layout, pitch, lit.get(pitch), struck.has(pitch));
            }
        }
    }
    const middle = layout.positions.get(60);
    if (middle !== undefined && layout.whiteWidth >= 8) {
        context.fillStyle = palette.muted;
        context.font = "9px \"Source Sans 3\", system-ui, sans-serif";
        context.textAlign = "center";
        context.textBaseline = "alphabetic";
        context.fillText(middleC, middle + layout.whiteWidth / 2, KEYBOARD_HEIGHT - 2);
    }
    return layout;
}

function drawKey(context: CanvasRenderingContext2D, layout: KeyLayout, pitch: number, lane: number | undefined, byHand: boolean): void {
    const black = isBlackKey(pitch);
    const x = layout.positions.get(pitch) ?? 0;
    const w = black ? layout.blackWidth : layout.whiteWidth;
    const h = black ? layout.blackHeight : KEYBOARD_HEIGHT;
    if (byHand) {
        context.fillStyle = palette.muted;
    } else if (lane !== undefined) {
        context.fillStyle = laneColour(lane);
    } else {
        context.fillStyle = black ? palette.keyBlack : palette.keyWhite;
    }
    context.fillRect(x + 0.5, 0, w - 1, h);
    if (!black) {
        context.fillStyle = palette.barLine;
        context.fillRect(x, 0, 1, h);
    }
    if (lane !== undefined || byHand) {
        const radius = Math.max(2, Math.min(w * 0.28, 5));
        context.fillStyle = palette.onNote;
        context.beginPath();
        context.arc(x + w / 2, h - radius - 4, radius, 0, Math.PI * 2);
        context.fill();
    }
}

/** The key under a point on the keyboard: black keys first, as they sit on top; null for none. */
export function keyAt(layout: KeyLayout | null, x: number, y: number): number | null {
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
