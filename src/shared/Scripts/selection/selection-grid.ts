// The grid a selection's edges stop at on the iPad, the same on the score and the roll (v0.6.4, Andrei 2026-11-15
// 19:53Z and 19:55Z): one selection, no «По тактам / По долям», every sixteenth note measured from each bar's start, so an
// odd meter or a pickup bar keeps its lines on its own sixteenths; a tuplet (a triplet or any other group) is one block and
// never split, so the grid has no line inside a group, only its start and its end. An edge dragged goes to the nearest of
// these lines (snapEdge), and a rectangle drawn takes the lines outward (drawnRange, rectPassage), so a group is wholly in
// or wholly out. Pure; the score (score/selection-place-layer.ts) and the roll (roll/roll-place-gesture.ts) take their lines here.

/** A sixteenth note at 480 ticks per quarter. */
export const SIXTEENTH = 120;

/** A stretch of time in ticks, [start, end): a bar, or a tuplet group from its first note's start to its last note's end. */
export interface GridSpan {
    start: number;
    end: number;
}

/**
 * The lines in time order: each bar's start and every sixteenth after it inside the bar, each bar's end, and of the tuplet
 * groups their start and end in place of the lines inside them. A bar with no known end (Infinity) gives only its start.
 */
export function gridLines(bars: readonly GridSpan[], groups: readonly GridSpan[] = [], step = SIXTEENTH): number[] {
    const lines = new Set<number>();
    for (const bar of bars) {
        lines.add(bar.start);
        if (!Number.isFinite(bar.end)) {
            continue;
        }
        for (let tick = bar.start + step; tick < bar.end; tick += step) {
            lines.add(tick);
        }
        lines.add(bar.end);
    }
    const blocks = groups.filter((group) => group.end > group.start);
    for (const group of blocks) {
        for (const line of [...lines]) {
            if (line > group.start && line < group.end) {
                lines.delete(line);
            }
        }
    }
    for (const group of blocks) {
        lines.add(group.start);
        lines.add(group.end);
    }
    return [...lines].sort((a, b) => a - b);
}

/**
 * The tuplet groups as one block each: from each group's earliest start to its latest end (the starts and ends of the notes
 * and rests in it), groups that overlap (a tuplet inside a tuplet) merged into one; groups with no known time left out.
 */
export function tupletBlocks(groups: readonly (readonly GridSpan[])[]): GridSpan[] {
    const spans = groups
        .map((members) => members.filter((member) => Number.isFinite(member.start) && Number.isFinite(member.end)))
        .filter((members) => members.length > 0)
        .map((members) => ({ start: Math.min(...members.map((member) => member.start)), end: Math.max(...members.map((member) => member.end)) }))
        .sort((a, b) => a.start - b.start);
    const merged: GridSpan[] = [];
    for (const span of spans) {
        const last = merged[merged.length - 1];
        if (last !== undefined && span.start < last.end) {
            last.end = Math.max(last.end, span.end);
        } else {
            merged.push({ ...span });
        }
    }
    return merged;
}
