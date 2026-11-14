// Where a view of the piece is scrolled to, kept so that PDF | Notes brings each view back as the player left it (#511
// section 10): not as pixels, which a new layout (another width, the score engraved again after the switch, a turn of the
// iPad) would send elsewhere, but as a place in what the view shows and a share of it. On the notes the place is a system,
// named by the tick of its first bar; on a PDF it is a page, by its number. The same rules serve the web's score and PDF
// views and the iPad's score page (Web/score.js); the iPad's PDF keeps them in Swift (ViewStates.swift), with the same tests.

/** A stretch of the view from top to bottom, in the view's content pixels: a system of the notes, or a page of a PDF. */
export interface AnchorSpan {
    /** What names it across layouts: the tick its first bar starts at, or the page's number. */
    key: number;
    top: number;
    bottom: number;
}

/** A place in the view: the span named by `key`, and how far into its height the place lies (0 its top, 1 its bottom). */
export interface ViewAnchor {
    key: number;
    fraction: number;
}

/**
 * The place a line of the view lies at: the span it crosses, or the next one below when it lies in the gap between two
 * (a negative share, so the gap is kept), or the last span when it lies below them all. Null when there are no spans.
 */
export function anchorAt(spans: readonly AnchorSpan[], y: number): ViewAnchor | null {
    const ordered = inOrder(spans);
    if (ordered.length === 0 || !Number.isFinite(y)) {
        return null;
    }
    const span = ordered.find((candidate) => candidate.bottom > y) ?? ordered[ordered.length - 1];
    const height = Math.max(1, span.bottom - span.top);
    return { key: span.key, fraction: (y - span.top) / height };
}

/**
 * Where the line of a place lies in the view now: in the span of the same key, or, laid out anew, the last span that starts
 * at or before it (the system that now holds the bar), at the same share of its height. Null when there are no spans.
 */
export function lineOf(spans: readonly AnchorSpan[], anchor: ViewAnchor): number | null {
    const ordered = inOrder(spans);
    if (ordered.length === 0 || !Number.isFinite(anchor.key) || !Number.isFinite(anchor.fraction)) {
        return null;
    }
    let span = ordered.find((candidate) => candidate.key === anchor.key);
    if (span === undefined) {
        const before = ordered.filter((candidate) => candidate.key <= anchor.key);
        span = before.length > 0 ? before.reduce((latest, candidate) => (candidate.key > latest.key ? candidate : latest)) : ordered[0];
    }
    return span.top + anchor.fraction * Math.max(1, span.bottom - span.top);
}

/** A place read back from storage or from the other side of the bridge: both numbers, or null. */
export function anchorOf(value: unknown): ViewAnchor | null {
    if (typeof value !== "object" || value === null) {
        return null;
    }
    const { key, fraction } = value as { key?: unknown; fraction?: unknown };
    return typeof key === "number" && typeof fraction === "number" && Number.isFinite(key) && Number.isFinite(fraction) ? { key, fraction } : null;
}

/** The systems of the notes as spans, each named by the earliest tick of its bars. */
export function systemSpans(systems: readonly { top: number; bottom: number; bars: readonly number[] }[], bars: readonly { tick: number }[]): AnchorSpan[] {
    const spans: AnchorSpan[] = [];
    for (const system of systems) {
        const ticks = system.bars.map((index) => bars[index]?.tick).filter((tick): tick is number => tick !== undefined);
        if (ticks.length > 0) {
            spans.push({ key: Math.min(...ticks), top: system.top, bottom: system.bottom });
        }
    }
    return spans;
}

/** The spans from the top down; side by side (two pages to a row), the one named first. */
function inOrder(spans: readonly AnchorSpan[]): AnchorSpan[] {
    return spans.filter((span) => Number.isFinite(span.top) && Number.isFinite(span.bottom)).sort((a, b) => a.top - b.top || a.key - b.key);
}
