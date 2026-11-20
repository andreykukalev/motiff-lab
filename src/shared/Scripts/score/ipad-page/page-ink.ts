// The pencil's marks on the iPad's score page (#479): strokes kept against their bars and drawn exactly as the web app
// did (Scripts/score/pencil-marks.ts). Swift draws the live stroke with the same outline and grain (InkInput.swift); when it
// ends, Swift hands its points here to be kept against its bar (`anchor`), saves the stroke through music-core
// (`ink.add`), and gives the marks back to be drawn (`draw`). The marks are filled with the grain of Web/grain.png
// (score.html, #479 item 7); the eraser cuts the marks it passes over into what is left of them as it moves (`rub`,
// BUG-05), and Swift saves what it did when it lifts (`rubEnd`).
import { DEFAULT_WIDTH, HIGHLIGHTER_SIZES, NO_RUBBING, PEN_SIZES, type PencilMark, type Rubbing, anchorMark, outlinePath, placeMark, rubAlong, rubbedMarks, touches } from "../pencil-marks.js";
import { mergedSpans, rubbedColour } from "../colour-bars.js";
import type { PageCommands, PagePoint, TickRange } from "../page-contract.js";
import { state } from "./page-state.js";
import { gridCell } from "./page-colouring.js";

const SVG = "http://www.w3.org/2000/svg";

/** The eraser takes marks within this many pixels of its path, as in the web app. */
const ERASER_REACH = 10;

/** The piece's marks, as music-core answers them (`marks.ink`). */
let marks: PencilMark[] = [];
let nextId = 0;
/** The press of the eraser now (BUG-05): what it has cut since it came down, shown at once (`rubbedMarks`). */
let rubbing: Rubbing = NO_RUBBING;
/** What the eraser took the colour from since it pressed (colour-bars.ts rubbedColour), for `rubEnd`. */
let uncolouring: TickRange[] = [];

/**
 * Keeps a stroke, its points in the document's pixels, against the bar it starts in, at the thickness chosen (0 to 2);
 * an empty object when there is no bar (WebKit's async calls do not take a null back).
 */
export function anchor({ points, tool, colour, width = DEFAULT_WIDTH }: Parameters<PageCommands["anchor"]>[0]): ReturnType<PageCommands["anchor"]> {
    const id = newMarkId();
    // The thickness chosen in the palette, thin to thick, as the web app's pencil layer sizes its marks.
    const size = (tool === "highlighter" ? HIGHLIGHTER_SIZES : PEN_SIZES)[width] ?? (tool === "highlighter" ? HIGHLIGHTER_SIZES : PEN_SIZES)[DEFAULT_WIDTH];
    return anchorMark(state.frames, points, { id, tool, colour: tool === "highlighter" ? "amber" : colour, size }) ?? {};
}

/** The marks an eraser path, in the document's pixels, passes over: their ids. */
export function erase({ points }: Parameters<PageCommands["erase"]>[0]): string[] {
    const ids: string[] = [];
    for (const mark of marks) {
        const place = placeMark(mark, state.byTick);
        if (place !== null && points.some((point) => touches(place.points, place.radius, point.x, point.y, ERASER_REACH))) {
            ids.push(mark.id);
        }
    }
    return ids;
}

function newMarkId(): string {
    nextId += 1;
    return `m${Date.now().toString(36)}${nextId.toString(36)}`;
}

/** The eraser cuts along `points` from where it was, in the document's pixels; nothing else changes. */
function rubOn(from: Rubbing, points: readonly PagePoint[]): { rubbing: Rubbing; cut: boolean } {
    return rubAlong(from, marks, points, { frames: state.frames, byTick: state.byTick, reach: ERASER_REACH, newId: newMarkId });
}

/**
 * The eraser moved on to these points, in the document's pixels (BUG-05): the marks under it go at once, as a real
 * eraser leaves a line in pieces (#479 item 7), and the page is drawn again where it took something. Answers how many
 * marks the page shows.
 */
export function rub({ points }: Parameters<PageCommands["rub"]>[0]): number {
    // The colour under it goes when it lifts, in the cells of the grid it passed (colour-bars.ts), but not where it is hidden.
    if (!document.body.classList.contains("hide-notes")) {
        uncolouring.push(...rubbedColour(points ?? [], state.painted, gridCell));
    }
    const moved = rubOn(rubbing, points ?? []);
    rubbing = moved.rubbing;
    if (moved.cut) {
        redraw();
    }
    return rubbedMarks(rubbing, marks).length;
}

/**
 * The eraser lifted: what the press did, for Swift to save as one step: the saved marks it cut, whole (for Undo), and the
 * pieces left of them, each kept against its bar. The page keeps showing them so until music-core's answer is drawn. With
 * them the stretches of highlights it took the colour from (`uncoloured`, merged, in ticks), which stay until Swift's answer.
 */
export function rubEnd(): ReturnType<PageCommands["rubEnd"]> {
    const done = rubbing;
    const uncoloured = mergedSpans(uncolouring);
    rubbing = NO_RUBBING;
    uncolouring = [];
    marks = rubbedMarks(done, marks);
    return { removed: done.removed, added: done.pieces, uncoloured };
}

/**
 * The eraser's whole path at once, in the document's pixels: the marks it passes over, by id, and what is left of each,
 * as one press would leave them. Nothing on the page changes.
 */
export function eraseAlongPath({ points }: Parameters<PageCommands["eraseAlong"]>[0]): ReturnType<PageCommands["eraseAlong"]> {
    const done = rubOn(NO_RUBBING, points ?? []).rubbing;
    return { removed: done.removed.map((mark) => mark.id), added: done.pieces };
}

/** Draws the piece's marks, as music-core answers them (`marks.ink`). */
export function draw(drawn: Parameters<PageCommands["draw"]>[0]): number {
    marks = drawn.marks ?? [];
    redraw();
    return marks.length;
}

/** Draws the marks again over the pages where they are now. */
export function redraw(): void {
    const ink = document.getElementById("ink") as Element;
    const shown = rubbedMarks(rubbing, marks);
    ink.setAttribute("width", String(document.documentElement.scrollWidth));
    ink.setAttribute("height", String(document.documentElement.scrollHeight));
    // The highlighter's strokes are one layer under the pencil's, opaque inside it and translucent as a whole, so where
    // strokes overlap the amber is as light as anywhere else (#479 item 9).
    const highlights = document.createElementNS(SVG, "g");
    highlights.setAttribute("class", "highlights");
    ink.replaceChildren(highlights);
    for (const mark of shown) {
        const place = placeMark(mark, state.byTick);
        if (place === null) {
            continue;
        }
        const path = document.createElementNS(SVG, "path");
        path.setAttribute("class", `mark ${mark.tool} ${mark.colour}`);
        path.setAttribute("d", outlinePath(place.points, place.radius, mark.tool === "pen"));
        path.dataset.id = mark.id;
        (mark.tool === "highlighter" ? highlights : ink).append(path);
    }
}
