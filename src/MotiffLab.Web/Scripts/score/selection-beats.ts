// Selection by beats (#349, an experiment the iPad turns on through `beats`): the selection layer's drag and handles
// snap to the beat lines music-core gives rather than to barlines, so a passage can start and end mid-bar. The band then
// starts and ends at the beats' notes, with a small tick at each beat inside it. The beat lines are music-core's (ticks
// at 480 per quarter, from the meter); nothing here works out a beat.

import type { Run, ScoreBar, ScoreGeometry } from "./score-geometry.js";

/** A band that starts or ends on a beat inside a bar begins this far left of that beat's notes, in pixels. */
export const BEAT_INSET = 6;

export const BEAT_CLASS = "sel-beat";

/** A beat: from one beat line to the next, in ticks. */
export interface Beat {
    start: number;
    end: number;
}

/** What selecting by beats needs from the score view: the beat lines, and where a tick sounds across its bar. */
export interface BeatSource {
    /** The beat lines in ticks, in time order; null or empty while the selection takes whole bars. */
    beats?(): readonly number[] | null;
    /** Where a tick sounds across its bar on screen (the playhead's place). */
    tickX?(tick: number): number | undefined;
}

/** The beat lines of a bar in time order: its start, the lines inside it, and its end when it is known. */
export function barLines(lines: readonly number[], bar: { tick: number; end: number }): number[] {
    const inside = lines.filter((line) => line > bar.tick && line < bar.end);
    return Number.isFinite(bar.end) ? [bar.tick, ...inside, bar.end] : [bar.tick, ...inside];
}

/** The beat a point across a bar falls in: from the last of the bar's lines left of the point to the next one. */
export function beatAt(lines: readonly number[], xOf: (line: number) => number, x: number): Beat | undefined {
    if (lines.length < 2) {
        return undefined;
    }
    let index = 0;
    for (let i = 1; i < lines.length - 1; i++) {
        if (xOf(lines[i]) <= x) {
            index = i;
        }
    }
    return { start: lines[index], end: lines[index + 1] };
}

/** Of the lines given, the one nearest a point across. */
export function nearestLine(lines: readonly number[], xOf: (line: number) => number, x: number): number | undefined {
    let best: number | undefined;
    let distance = Infinity;
    for (const line of lines) {
        const away = Math.abs(xOf(line) - x);
        if (away < distance) {
            distance = away;
            best = line;
        }
    }
    return best;
}

/**
 * A passage with one edge moved a line on (`step` 1) or back (-1) among `lines` (in time order), never onto or past the
 * other edge; undefined when it cannot move.
 */
export function stepEdge(lines: readonly number[], span: Beat, edge: "start" | "end", step: number): Beat | undefined {
    const at = span[edge];
    const next = step > 0 ? lines.find((line) => line > at) : [...lines].reverse().find((line) => line < at);
    if (next === undefined || (edge === "start" ? next >= span.end : next <= span.start)) {
        return undefined;
    }
    return { ...span, [edge]: next };
}

/** Two beats as one passage, whichever comes first: from the earlier start to the later end. */
export function spanOf(a: Beat, b: Beat): Beat {
    return { start: Math.min(a.start, b.start), end: Math.max(a.end, b.end) };
}

/** The beat lines and the barlines together, in time order: where the arrow keys move an edge to. */
export function keyLines(lines: readonly number[], bars: readonly ScoreBar[]): number[] {
    const barlines = bars.flatMap((bar) => (Number.isFinite(bar.end) ? [bar.tick, bar.end] : [bar.tick]));
    return [...new Set([...lines, ...barlines])].sort((a, b) => a - b);
}

/** The beats as the score shows them: where each line is across, the beat under a point, and the band cut to beats. */
export class ScoreBeats {
    constructor(private readonly source: BeatSource) {}

    /** The beat lines while selecting by beats, or null while the selection takes whole bars. */
    lines(): readonly number[] | null {
        const lines = this.source.beats?.() ?? null;
        return lines !== null && lines.length > 0 && this.source.tickX !== undefined ? lines : null;
    }

    /** Where a beat line of a bar is across: the barline at the bar's start and end, else just before the beat's notes. */
    lineX(bar: ScoreBar, line: number): number {
        if (line <= bar.tick) {
            return bar.left;
        }
        if (line >= bar.end) {
            return bar.right;
        }
        const x = this.source.tickX?.(line);
        return x === undefined ? bar.left + ((bar.right - bar.left) * (line - bar.tick)) / (bar.end - bar.tick) : Math.max(bar.left, x - BEAT_INSET);
    }

    /** The beat under a point, on the bar nearest it. */
    near(geometry: ScoreGeometry, lines: readonly number[], x: number, y: number): Beat | undefined {
        const bar = geometry.barNear(x, y);
        return bar === undefined ? undefined : beatAt(barLines(lines, bar), (line) => this.lineX(bar, line), x);
    }

    /** The beat line nearest a point, for a handle: on the bar nearest it. */
    edgeNear(geometry: ScoreGeometry, lines: readonly number[], x: number, y: number): number | undefined {
        const bar = geometry.barNear(x, y);
        return bar === undefined ? undefined : nearestLine(barLines(lines, bar), (tick) => this.lineX(bar, tick), x);
    }

    /** The band's runs cut to the beats where the passage starts and ends inside a bar, and a tick for each beat inside it. */
    cut(geometry: ScoreGeometry, runs: Run[], start: number, end: number): HTMLElement[] {
        const lines = this.lines();
        if (lines === null || runs.length === 0) {
            return [];
        }
        const first = geometry.barAt(start);
        if (first !== undefined && start > first.tick) {
            runs[0].left = this.lineX(first, start);
        }
        const last = geometry.barAt(end - 1);
        if (last !== undefined && end < last.end) {
            runs[runs.length - 1].right = this.lineX(last, end);
        }
        const ticks: HTMLElement[] = [];
        for (const line of lines) {
            const bar = line > start && line < end ? geometry.barAt(line) : undefined;
            const run = bar === undefined ? undefined : runs.find((candidate) => candidate.system === bar.system);
            if (bar === undefined || run === undefined || line === bar.tick) {
                continue;
            }
            const tick = document.createElement("div");
            tick.className = BEAT_CLASS;
            tick.setAttribute("aria-hidden", "true");
            tick.style.left = `${this.lineX(bar, line)}px`;
            tick.style.top = `${run.top}px`;
            ticks.push(tick);
        }
        return ticks;
    }
}
