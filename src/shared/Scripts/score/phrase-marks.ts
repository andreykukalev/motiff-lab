// Where the phrase marks stand over the score (the phrases spec, decision 1 and «Polish of option A», v1.2): at each phrase
// start a breath mark (a comma) right before the start note and the phrase's number over it, and at a form section's start
// its boxed letter before the number. They sit right above the notes, raised only over what is printed there; a number
// closer to the one before it than its width moves right, its comma staying at its note; each letter and number is a
// touch target of the kit's size, split halfway with a close neighbour's. In focus a motif's small comma (decision 2) is
// placed as a comma alone and takes the tap instead. Kept free of the DOM, so Node tests the rules; the iPad's page draws by
// them (ipad-page/page-phrases.ts).
import type { PagePhraseMark, PageSectionMark } from "./page-contract.js";
import type { Rect } from "./page-reader.js";

/** One mark to place, in the document's pixels: where its start is and how wide its parts are drawn (0 for a part it has not). */
export interface MarkSpot {
    /** The system it stands over, across the whole score. */
    system: number;
    /** The left edge of the start note's head. */
    noteLeft: number;
    /** The top line of the system's top staff there. */
    staffTop: number;
    /** The comma's width: 0 for a section that starts where no phrase does. */
    comma: number;
    /** The boxed letter's width: 0 where no section starts. */
    letter: number;
    /** The number's width: 0 for a section that starts where no phrase does. */
    number: number;
    /**
     * What of it takes a tap: its letter and number (the default, on the pages), its comma (a motif's, in focus) or nothing
     * (the card's phrase marks, only to read).
     */
    tap?: "parts" | "comma" | "none";
}

/** The marks' measures: the gap between their parts and above what they stand over, their height, a touch target's side. */
export interface MarkSizes {
    gap: number;
    height: number;
    target: number;
}

/** A mark placed: the top and bottom of its parts, the left of each part it has, and the targets of the parts that take a tap. */
export interface PlacedMark {
    top: number;
    bottom: number;
    comma: number | null;
    letter: number | null;
    number: number | null;
    letterTarget: Rect | null;
    numberTarget: Rect | null;
    commaTarget: Rect | null;
}

/** A target with the system it is on and its centre across, for splitting it with its neighbours. */
interface Target {
    rect: Rect;
    system: number;
    centre: number;
}

/**
 * Places every mark (`spots`, in any order; the answer in the same order): along each system its letter and number start
 * over the note, or right of the mark before them when that one reaches further; then the whole mark stands `gap` above
 * the top staff, or above the highest of the things printed on its system (`printed`) that its width covers.
 */
export function placeMarks(spots: readonly MarkSpot[], printed: (system: number) => readonly Rect[], sizes: MarkSizes): PlacedMark[] {
    const order = [...spots.keys()].sort((a, b) => spots[a].system - spots[b].system || spots[a].noteLeft - spots[b].noteLeft);
    const placed: PlacedMark[] = new Array<PlacedMark>(spots.length);
    let reach = -Infinity;
    let system = -1;
    for (const index of order) {
        const spot = spots[index];
        if (spot.system !== system) {
            system = spot.system;
            reach = -Infinity;
        }
        const { mark, right } = placeOne(spot, Math.max(spot.noteLeft, reach + sizes.gap), printed(spot.system), sizes);
        placed[index] = mark;
        reach = right;
    }
    splitTargets(placed.map((mark, index) => ({ mark, system: spots[index].system })));
    return placed;
}

/** One mark, its letter and number from `groupLeft`: where it stands, and how far right it reaches. */
function placeOne(spot: MarkSpot, groupLeft: number, printed: readonly Rect[], sizes: MarkSizes): { mark: PlacedMark; right: number } {
    const letter = spot.letter > 0 ? groupLeft : null;
    const number = spot.number > 0 ? groupLeft + (spot.letter > 0 ? spot.letter + sizes.gap : 0) : null;
    const right = number !== null ? number + spot.number : groupLeft + spot.letter;
    const comma = spot.comma > 0 ? spot.noteLeft - spot.comma - sizes.gap / 2 : null;
    const bottom = liftOver(spot.staffTop, printed, comma ?? groupLeft, right) - sizes.gap;
    const parts = (spot.tap ?? "parts") === "parts";
    const mark = {
        top: bottom - sizes.height,
        bottom,
        comma,
        letter,
        number,
        letterTarget: !parts || letter === null ? null : targetOver(letter, spot.letter, bottom + sizes.gap, sizes.target),
        numberTarget: !parts || number === null ? null : targetOver(number, spot.number, bottom + sizes.gap, sizes.target),
        commaTarget: spot.tap !== "comma" || comma === null ? null : targetOver(comma, spot.comma, bottom + sizes.gap, sizes.target),
    };
    return { mark, right };
}

/** The top of what a mark from `left` to `right` stands over: the staff's top line, or anything printed higher across it. */
export function liftOver(staffTop: number, printed: readonly Rect[], left: number, right: number): number {
    let top = staffTop;
    for (const box of printed) {
        if (box.right > left && box.left < right && box.top < top) {
            top = box.top;
        }
    }
    return top;
}

/** A touch target `side` wide and high, centred across a part `width` wide at `left`, standing on `base`. */
function targetOver(left: number, width: number, base: number, side: number): Rect {
    const centre = left + width / 2;
    return { left: centre - side / 2, right: centre + side / 2, top: base - side, bottom: base };
}

/**
 * Close targets share the room between them (the spec, refinement 2): two on one system that overlap meet halfway between
 * their centres, so each part is still pressed where it is drawn.
 */
function splitTargets(marks: readonly { mark: PlacedMark; system: number }[]): void {
    const targets: Target[] = [];
    for (const { mark, system } of marks) {
        for (const rect of [mark.letterTarget, mark.numberTarget, mark.commaTarget]) {
            if (rect !== null) {
                targets.push({ rect, system, centre: (rect.left + rect.right) / 2 });
            }
        }
    }
    targets.sort((a, b) => a.system - b.system || a.centre - b.centre);
    targets.forEach((target, index) => {
        const next = targets[index + 1];
        if (next === undefined || next.system !== target.system || !overlap(target.rect, next.rect)) {
            return;
        }
        const half = (target.centre + next.centre) / 2;
        target.rect.right = Math.min(target.rect.right, half);
        next.rect.left = Math.max(next.rect.left, half);
    });
}

function overlap(a: Rect, b: Rect): boolean {
    return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
}

/** Which of the starts (in time order) the moment `tick` is in: the last at or before it; -1 before the first. */
export function phraseAt(starts: readonly number[], tick: number): number {
    let low = 0;
    let high = starts.length;
    while (low < high) {
        const middle = (low + high) >> 1;
        if (starts[middle] <= tick) {
            low = middle + 1;
        } else {
            high = middle;
        }
    }
    return low - 1;
}

/**
 * A start on the score: a phrase's, a section's, or both; or where a start the player moved was found (`ghost`); in focus
 * also a motif's (`motif`: its phrase's number, its place in it from 1, and its label).
 */
export interface MarkStart {
    tick: number;
    phrase?: PagePhraseMark;
    section?: PageSectionMark;
    ghost?: true;
    motif?: { of: number; number: number; label: string };
}

/**
 * The starts of the phrases (in time order) and the sections, one for each tick, in time order: where moved starts were
 * found among them, and with `motifs` (in focus) each motif's at its own tick, at its phrase's start or on a ghost.
 */
export function markStarts(phrases: readonly PagePhraseMark[], sections: readonly PageSectionMark[], motifs: boolean): MarkStart[] {
    const byTick = new Map<number, MarkStart>();
    for (const phrase of phrases) {
        byTick.set(phrase.tick, { tick: phrase.tick, phrase });
    }
    for (const section of sections) {
        byTick.set(section.tick, { ...(byTick.get(section.tick) ?? { tick: section.tick }), section });
    }
    for (const found of phrases.flatMap((phrase) => (phrase.found === undefined ? [] : [phrase.found]))) {
        if (!byTick.has(found)) {
            byTick.set(found, { tick: found, ghost: true });
        }
    }
    for (const phrase of motifs ? phrases : []) {
        (phrase.motifs ?? []).forEach(({ tick, label }, index) => {
            byTick.set(tick, { ...(byTick.get(tick) ?? { tick }), motif: { of: phrase.number, number: index + 1, label } });
        });
    }
    return [...byTick.values()].sort((a, b) => a.tick - b.tick);
}

/**
 * The kind of a start's comma: a phrase's, else a moved start's faint ghost (a motif starting there takes its tap), else a
 * motif's small one (in focus); null for a section alone.
 */
export function commaOf(start: MarkStart): string | null {
    if (start.phrase !== undefined) {
        return "comma";
    }
    if (start.ghost === true) {
        return "comma ghost";
    }
    return start.motif === undefined ? null : "comma motif";
}
