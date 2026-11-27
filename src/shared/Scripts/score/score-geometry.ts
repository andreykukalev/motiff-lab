// The score's geometry as one module (decision record 0018, sections 2 and 9; preparation item 11):
// where every page, system, bar, staff and note sits on screen, and the boxes every overlay is drawn
// from (phrase brackets, the selection band, rings round notes, Check flags, bookmarks, the pencil's
// bars) and the bar-to-page map. A4 pages and the strip are the same to it: a strip is one long page.
// The pages come read in page units from Verovio's SVG text (page-reader.ts); the view only says where
// each page sits on screen and at what size. Kept free of the DOM so Node can test it.

import type { PageGeometry, Rect, StaffBox } from "./page-reader.js";
import type { BarFrame } from "./pencil-marks.js";

/** Where a page sits on screen: the offset of its top left corner, and screen pixels per page unit. */
export interface PagePlacement {
    left: number;
    top: number;
    scale: number;
}

/** A bar placed on screen with its place in time. */
export interface ScoreBar extends Rect {
    id: string;
    /** Where the bar starts and ends, in ticks. */
    tick: number;
    end: number;
    page: number;
    /** The system across the whole score, from 0. */
    system: number;
    staves: StaffBox[];
    ink: Rect;
}

/** A note placed on screen. */
export interface ScoreNote extends Rect {
    id: string;
    page: number;
    bar: number;
    staff: number;
    cx: number;
    cy: number;
}

/** A system placed on screen. */
export interface ScoreSystem extends Rect {
    page: number;
    /** The indexes of its bars in the score's bars. */
    bars: number[];
}

/**
 * The part of a passage on one system: what a bracket, a band or a bookmark draws there. A passage
 * across a system break or a page edge has a run on each system it touches.
 */
export interface Run extends Rect {
    page: number;
    system: number;
    /** The run where the passage starts, and the one where it ends: a bracket's number goes on the first, a band's handles on both ends. */
    first: boolean;
    last: boolean;
}

/** The playhead at a moment: its x, the top and bottom of the staves it crosses, and the bar it is in. */
export interface Playhead {
    x: number;
    top: number;
    bottom: number;
    bar: ScoreBar;
}

/** A ring round a notehead, as the design kit draws fact highlights: a circle larger than the notehead. */
export interface Ring {
    cx: number;
    cy: number;
    diameter: number;
}

/** The kit's ring is 8 px wider than the notehead. */
export const RING_GROWTH = 8;

/** A bracket sits this far above the top staff of its bars, in screen pixels (design/kit/score.js). */
export const BRACKET_LIFT = 14;

/** A band reaches this far above and below the staves, in screen pixels (design/kit/score.js). */
export const BAND_PADDING = 8;

/**
 * The levels of overlapping bookmarks, from 0 under the others: each takes the lowest level free where it starts. The web
 * app's score view and the iPad's score page stack the bookmarks' brackets by them.
 */
export function bookmarkLevels(bookmarks: readonly { start: number; end: number }[]): number[] {
    const order = bookmarks.map((bookmark, index) => ({ ...bookmark, index })).sort((a, b) => a.start - b.start || b.end - a.end);
    const ends: number[] = [];
    const levels = new Array<number>(bookmarks.length).fill(0);
    for (const bookmark of order) {
        let level = ends.findIndex((end) => end <= bookmark.start);
        if (level < 0) {
            level = ends.length;
            ends.push(0);
        }
        ends[level] = bookmark.end;
        levels[bookmark.index] = level;
    }
    return levels;
}

/** The x of a tick in a bar, from its onsets and their x (see ScoreGeometry.playhead). */
export function placeInBar(bar: Rect & { tick: number; end: number }, points: readonly { tick: number; x: number }[], tick: number): number {
    const line = barLine(bar, points);
    if (tick <= line[0].tick) {
        return line[0].x;
    }
    for (let i = 1; i < line.length; i++) {
        const from = line[i - 1];
        const to = line[i];
        if (tick < to.tick) {
            return from.x + ((to.x - from.x) * (tick - from.tick)) / (to.tick - from.tick);
        }
    }
    return line[line.length - 1].x;
}

/**
 * The tick at an x in a bar: placeInBar the other way round, along the same line through the bar's onsets, for a playhead
 * dragged across it (#495 UX-08). Left of the line it is the bar's first moment, right of it the line's last; where the
 * line does not move on (onsets drawn at one x) it is the first tick there.
 */
export function tickInBar(bar: Rect & { tick: number; end: number }, points: readonly { tick: number; x: number }[], x: number): number {
    const line = barLine(bar, points);
    if (x <= line[0].x) {
        return line[0].tick;
    }
    for (let i = 1; i < line.length; i++) {
        const from = line[i - 1];
        const to = line[i];
        if (x < to.x) {
            return from.tick + ((to.tick - from.tick) * (x - from.x)) / (to.x - from.x);
        }
    }
    return line[line.length - 1].tick;
}

/**
 * The line a bar's time takes across it: from where its start would be at the pace of the notes after it (never left of
 * the bar), through the onsets and their x, to its right barline at its end. A bar with no notes is crossed evenly.
 */
function barLine(bar: Rect & { tick: number; end: number }, points: readonly { tick: number; x: number }[]): { tick: number; x: number }[] {
    const span = Number.isFinite(bar.end) ? bar.end - bar.tick : 0;
    if (points.length === 0) {
        return span > 0 ? [{ tick: bar.tick, x: bar.left }, { tick: bar.end, x: bar.right }] : [{ tick: bar.tick, x: bar.left }];
    }
    const first = points[0];
    const last = points[points.length - 1];
    let start = bar.left;
    if (first.tick > bar.tick && span > 0 && bar.end > first.tick) {
        start = Math.max(bar.left, first.x - ((first.tick - bar.tick) / (bar.end - first.tick)) * (bar.right - first.x));
    }
    const line: { tick: number; x: number }[] = [...(first.tick > bar.tick ? [{ tick: bar.tick, x: start }] : []), ...points];
    if (Number.isFinite(bar.end) && bar.end > last.tick) {
        line.push({ tick: bar.end, x: bar.right });
    }
    return line;
}

function place(rect: Rect, at: PagePlacement): Rect {
    return {
        left: at.left + rect.left * at.scale,
        top: at.top + rect.top * at.scale,
        right: at.left + rect.right * at.scale,
        bottom: at.top + rect.bottom * at.scale,
    };
}

/**
 * The whole score on screen: its pages read once per layout, placed where the view shows them, and the
 * bars put in time by the timeline's measure onsets. Every overlay is drawn from here, so it sits the same
 * on A4 pages and on the strip.
 */
export class ScoreGeometry {
    readonly bars: readonly ScoreBar[];
    readonly systems: readonly ScoreSystem[];
    readonly notes: readonly ScoreNote[];
    private readonly noteIndex: Map<string, ScoreNote>;
    /** Each bar's onsets with their x, worked out when the playhead first enters it. */
    private readonly points = new Map<number, { tick: number; x: number }[]>();

    /**
     * @param pages each page's geometry, in order.
     * @param placements where each page sits on screen, in the same order; a page without one is left out.
     * @param measureOnset where a measure starts, from the timeline; a measure it does not know is left out.
     * @param end where the piece ends, for the last bar's end.
     */
    constructor(pages: readonly PageGeometry[], placements: readonly PagePlacement[], measureOnset: (id: string) => number | undefined, end?: number) {
        const bars: ScoreBar[] = [];
        const systems: ScoreSystem[] = [];
        const notes: ScoreNote[] = [];
        pages.forEach((page, pageIndex) => {
            const at = placements[pageIndex];
            if (at === undefined) {
                return;
            }
            const barIndex = new Map<number, number>();
            for (const pageSystem of page.systems) {
                const system: ScoreSystem = { page: page.number, bars: [], ...place(pageSystem, at) };
                for (const index of pageSystem.bars) {
                    const pageBar = page.bars[index];
                    const tick = measureOnset(pageBar.id);
                    if (tick === undefined) {
                        continue;
                    }
                    barIndex.set(index, bars.length);
                    system.bars.push(bars.length);
                    bars.push({
                        id: pageBar.id,
                        tick,
                        end: Infinity,
                        page: page.number,
                        system: systems.length,
                        staves: pageBar.staves.map((staff) => ({ n: staff.n, space: staff.space * at.scale, ...place(staff, at) })),
                        ink: place(pageBar.ink, at),
                        ...place(pageBar, at),
                    });
                }
                if (system.bars.length > 0) {
                    systems.push(system);
                }
            }
            for (const note of page.notes) {
                const bar = barIndex.get(note.bar);
                if (bar !== undefined) {
                    notes.push({ id: note.id, page: page.number, bar, staff: note.staff, cx: at.left + note.cx * at.scale, cy: at.top + note.cy * at.scale, ...place(note, at) });
                }
            }
        });

        // A bar ends where the next one in time starts; the last where the piece ends.
        const inTime = [...bars.keys()].sort((a, b) => bars[a].tick - bars[b].tick);
        inTime.forEach((index, order) => {
            const next = inTime[order + 1];
            bars[index].end = next !== undefined ? bars[next].tick : (end ?? Infinity);
        });

        this.bars = bars;
        this.systems = systems;
        this.notes = notes;
        this.noteIndex = new Map(notes.map((note) => [note.id, note]));
    }

    /** The page each bar is on, by the tick where it starts: the same map as the PDF's on A4 pages (0017). */
    barToPage(): Map<number, number> {
        const map = new Map<number, number>();
        for (const bar of this.bars) {
            if (!map.has(bar.tick)) {
                map.set(bar.tick, bar.page);
            }
        }
        return map;
    }

    /** The page that holds a tick, or undefined before the first bar. */
    pageOf(tick: number): number | undefined {
        return this.barAt(tick)?.page;
    }

    /** The bar that holds a tick: starts at or before it and ends after it. */
    barAt(tick: number): ScoreBar | undefined {
        return this.bars.find((bar) => bar.tick <= tick && tick < bar.end);
    }

    /** The bars a passage [start, end) touches, in order on the page. */
    barsIn(start: number, end: number): ScoreBar[] {
        return this.bars.filter((bar) => bar.tick < end && bar.end > start);
    }

    /**
     * A passage [start, end) system by system: on each system it touches, the box from its first bar's left
     * edge to its last bar's right edge, over the staves (or only the staves numbered in `staves`).
     */
    runs(start: number, end: number, staves?: readonly number[]): Run[] {
        const bySystem = new Map<number, ScoreBar[]>();
        for (const bar of this.barsIn(start, end)) {
            const list = bySystem.get(bar.system) ?? [];
            list.push(bar);
            bySystem.set(bar.system, list);
        }
        const runs: Run[] = [];
        for (const [system, bars] of [...bySystem.entries()].sort((a, b) => a[0] - b[0])) {
            const lines = bars.flatMap((bar) => bar.staves.filter((staff) => staves === undefined || staves.includes(staff.n)));
            const from = lines.length > 0 ? lines : bars;
            runs.push({
                page: bars[0].page,
                system,
                left: Math.min(...bars.map((bar) => bar.left)),
                right: Math.max(...bars.map((bar) => bar.right)),
                top: Math.min(...from.map((box) => box.top)),
                bottom: Math.max(...from.map((box) => box.bottom)),
                first: false,
                last: false,
            });
        }
        if (runs.length > 0) {
            runs[0].first = true;
            runs[runs.length - 1].last = true;
        }
        return runs;
    }

    /** A phrase bracket over a passage: a line `lift` pixels above the top staff on each system, inset 2 px at each end as the kit draws it. */
    bracket(start: number, end: number, lift = BRACKET_LIFT): Run[] {
        return this.runs(start, end).map((run) => ({ ...run, left: run.left + 2, right: run.right - 2, top: run.top - lift, bottom: run.top - lift }));
    }

    /** The selection band over a passage: the staves of each system it touches, `padding` pixels above and below. */
    band(start: number, end: number, padding = BAND_PADDING, staves?: readonly number[]): Run[] {
        return this.runs(start, end, staves).map((run) => ({ ...run, top: run.top - padding, bottom: run.bottom + padding }));
    }

    /**
     * The bar under a point, as a drag across the score picks it: on the system nearest the point, the bar
     * that holds it across, or the nearest one when the point is beside the system.
     */
    barNear(x: number, y: number): ScoreBar | undefined {
        const bars = this.systemNear(x, y);
        if (bars.length === 0) {
            return undefined;
        }
        return bars.find((bar) => x >= bar.left && x < bar.right) ?? (x < bars[0].left ? bars[0] : bars[bars.length - 1]);
    }

    /**
     * The bar a selection's edge goes to when its handle is let go at a point: on the system nearest the point,
     * the bar whose left barline (for the start) or right barline (for the end) is nearest across, so a bar counts once half
     * of it or more is covered (#521 3); at exactly half it counts (the bars run left to right, so the end takes the later).
     */
    edgeNear(x: number, y: number, edge: "start" | "end"): ScoreBar | undefined {
        let best: ScoreBar | undefined;
        let distance = Infinity;
        for (const bar of this.systemNear(x, y)) {
            const away = Math.abs(x - (edge === "start" ? bar.left : bar.right));
            if (away < distance || (edge === "end" && away === distance)) {
                distance = away;
                best = bar;
            }
        }
        return best;
    }

    /** The bars, left to right, of the system nearest a point; none when nothing is engraved. */
    private systemNear(x: number, y: number): ScoreBar[] {
        let nearest: ScoreSystem | undefined;
        let distance = Infinity;
        for (const system of this.systems) {
            const across = x < system.left ? system.left - x : x > system.right ? x - system.right : 0;
            const down = y < system.top ? system.top - y : y > system.bottom ? y - system.bottom : 0;
            const away = Math.hypot(across, down);
            if (away < distance) {
                distance = away;
                nearest = system;
            }
        }
        return nearest === undefined ? [] : nearest.bars.map((index) => this.bars[index]);
    }

    /** The engraved note with an element id, if it is on a page. */
    note(id: string): ScoreNote | undefined {
        return this.noteIndex.get(id);
    }

    /** A ring round a note: centred on its notehead, `growth` pixels wider than it. */
    ring(id: string, growth = RING_GROWTH): Ring | undefined {
        const note = this.noteIndex.get(id);
        if (note === undefined) {
            return undefined;
        }
        return { cx: note.cx, cy: note.cy, diameter: Math.max(note.right - note.left, note.bottom - note.top) + growth };
    }

    /**
     * Where the playhead stands at a tick (testing item 4, 0018 "Playback"): a line across the staves of the bar that
     * holds it, at the place the bar's notes give that moment. Between two onsets it moves evenly from one note to the
     * next; before the bar's first note it comes from the left at the same pace, and after the last it runs on to the
     * barline. A bar with no notes is crossed evenly. `onsetOf` gives a note's onset, from the timeline.
     */
    playhead(tick: number, onsetOf: (id: string) => number | undefined): Playhead | undefined {
        const index = this.bars.findIndex((bar) => bar.tick <= tick && tick < bar.end);
        if (index < 0) {
            return undefined;
        }
        const bar = this.bars[index];
        const top = bar.staves.length > 0 ? Math.min(...bar.staves.map((staff) => staff.top)) : bar.top;
        const bottom = bar.staves.length > 0 ? Math.max(...bar.staves.map((staff) => staff.bottom)) : bar.bottom;
        const points = this.timePoints(index, onsetOf);
        return { x: placeInBar(bar, points, tick), top, bottom, bar };
    }

    /** The tick at an x across a bar, along its onsets as the playhead crosses it (tickInBar). */
    tickAt(bar: ScoreBar, x: number, onsetOf: (id: string) => number | undefined): number {
        const index = this.bars.indexOf(bar);
        return tickInBar(bar, index < 0 ? [] : this.timePoints(index, onsetOf), x);
    }

    /** The starts of a bar's engraved notes, in time order: where a dragged playhead may stand on a note (#582 item 12). */
    onsets(bar: ScoreBar, onsetOf: (id: string) => number | undefined): number[] {
        const index = this.bars.indexOf(bar);
        return index < 0 ? [] : this.timePoints(index, onsetOf).map((point) => point.tick);
    }

    /** The bar's onsets with the x of their notes (the middle of the noteheads that start together), in time order. */
    private timePoints(index: number, onsetOf: (id: string) => number | undefined): { tick: number; x: number }[] {
        const known = this.points.get(index);
        if (known !== undefined) {
            return known;
        }
        const byOnset = new Map<number, number[]>();
        for (const note of this.notes) {
            const onset = note.bar === index ? onsetOf(note.id) : undefined;
            if (onset !== undefined) {
                const list = byOnset.get(onset) ?? [];
                list.push(note.cx);
                byOnset.set(onset, list);
            }
        }
        const points = [...byOnset.entries()]
            .map(([onset, xs]) => ({ tick: onset, x: xs.reduce((sum, x) => sum + x, 0) / xs.length }))
            .sort((a, b) => a.tick - b.tick);
        this.points.set(index, points);
        return points;
    }

    /** The lowest ink of a system, its notes and their stems included: what the playhead's track runs under (#479 item 19). */
    lineInkBottom(system: number): number {
        const bars = this.systems[system]?.bars ?? [];
        return bars.reduce((lowest, index) => Math.max(lowest, this.bars[index].ink.bottom, this.bars[index].bottom), Number.NEGATIVE_INFINITY);
    }

    /** Where a mark on a bar goes, such as a Check flag or a margin note's marker: the top right corner of its top staff. */
    flag(tick: number): { x: number; y: number } | undefined {
        const bar = this.barAt(tick);
        return bar === undefined ? undefined : { x: bar.right, y: bar.staves[0]?.top ?? bar.top };
    }

    /** The bars as the pencil layer anchors marks to them (pencil-marks.ts, 0015), each with the edges of its line for strokes in the margins. */
    barFrames(): BarFrame[] {
        const edges = this.systems.map((system) => ({
            left: Math.min(...system.bars.map((index) => this.bars[index].left)),
            right: Math.max(...system.bars.map((index) => this.bars[index].right)),
        }));
        return this.bars.map((bar) => ({
            tick: bar.tick,
            left: bar.left,
            right: bar.right,
            top: bar.ink.top,
            bottom: bar.ink.bottom,
            staffTop: bar.staves[0]?.top ?? bar.top,
            space: bar.staves[0]?.space ?? (bar.bottom - bar.top) / 8,
            line: bar.system,
            lineLeft: edges[bar.system]?.left ?? bar.left,
            lineRight: edges[bar.system]?.right ?? bar.right,
        }));
    }
}
