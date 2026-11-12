// The pieces this browser split from a collection (the «Пьесы в файле» experiment, version 0.4): which projects were
// made from which, the playlist they joined, and where each was in the collection (its bars, and for a recording the
// stretch of the sound in milliseconds), so the collection can say «Разделено на N пьес» and a piece where it came from.
// For a recording this is the part's reference to its stretch of the collection's sound: no sound is copied. The project
// file does not say where a part was until the format step adds "partOf"; until then it is kept here, in local storage,
// beside the projects. Nothing here leaves the device.

const KEY = "motiff-lab:piece-parts";

/** One piece made from the collection: its project, title, the collection's bars it took and, for a recording, its stretch. */
export interface PartLink {
    id: string;
    title: string;
    first: number | null;
    last: number | null;
    from: number | null;
    to: number | null;
}

/** A collection's split: the collection's title, the playlist its pieces joined and the pieces, in order. */
export interface SplitLink {
    title: string;
    playlist: string;
    parts: PartLink[];
}

/** Where a piece came from: the collection it was split from, by project id, and its place there. */
export interface PartOrigin {
    collection: string;
    title: string;
    playlist: string;
    part: PartLink;
}

function numberOrNull(value: unknown): number | null {
    return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function partLinkOf(value: unknown): PartLink | null {
    if (typeof value !== "object" || value === null) {
        return null;
    }
    const part = value as Record<string, unknown>;
    if (typeof part.id !== "string" || part.id === "") {
        return null;
    }
    return {
        id: part.id,
        title: typeof part.title === "string" ? part.title : "",
        first: numberOrNull(part.first),
        last: numberOrNull(part.last),
        from: numberOrNull(part.from),
        to: numberOrNull(part.to),
    };
}

function splitLinkOf(value: unknown): SplitLink | null {
    if (typeof value !== "object" || value === null) {
        return null;
    }
    const link = value as Record<string, unknown>;
    if (typeof link.playlist !== "string" || !Array.isArray(link.parts)) {
        return null;
    }
    const parts = link.parts.map(partLinkOf).filter((part): part is PartLink => part !== null);
    return parts.length > 0 ? { title: typeof link.title === "string" ? link.title : "", playlist: link.playlist, parts } : null;
}

/** The splits kept, by the collection's project id, from what local storage holds; anything it cannot read is left out. */
export function parseSplits(text: string | null): Record<string, SplitLink> {
    let kept: unknown;
    try {
        kept = JSON.parse(text ?? "{}");
    } catch {
        return {};
    }
    const splits: Record<string, SplitLink> = {};
    if (typeof kept !== "object" || kept === null || Array.isArray(kept)) {
        return splits;
    }
    for (const [id, value] of Object.entries(kept)) {
        const link = splitLinkOf(value);
        if (link !== null) {
            splits[id] = link;
        }
    }
    return splits;
}

/** The splits with the collection's split put in place of any kept before; a split with no pieces forgets it. */
export function withSplit(splits: Record<string, SplitLink>, collectionId: string, link: SplitLink | null): Record<string, SplitLink> {
    const next = { ...splits };
    const checked = splitLinkOf(link);
    if (checked === null) {
        delete next[collectionId];
    } else {
        next[collectionId] = checked;
    }
    return next;
}

function read(): Record<string, SplitLink> {
    try {
        return parseSplits(localStorage.getItem(KEY));
    } catch {
        return {};
    }
}

/** The collection's split as this browser kept it, or null. */
export function splitOf(collectionId: string): SplitLink | null {
    return read()[collectionId] ?? null;
}

/** Where the piece came from, from the splits kept: its collection and its place there; null for a piece not split from one. */
export function originIn(splits: Record<string, SplitLink>, partId: string): PartOrigin | null {
    for (const [collection, link] of Object.entries(splits)) {
        const part = link.parts.find((each) => each.id === partId);
        if (part !== undefined) {
            return { collection, title: link.title, playlist: link.playlist, part };
        }
    }
    return null;
}

/** Where the piece came from, as this browser kept it, or null. */
export function partOf(partId: string): PartOrigin | null {
    return originIn(read(), partId);
}

/** Keeps the collection's split; blocked storage keeps nothing, and the pieces are in the library all the same. */
export function keepSplit(collectionId: string, link: SplitLink): void {
    try {
        localStorage.setItem(KEY, JSON.stringify(withSplit(read(), collectionId, link)));
    } catch {
        // Private browsing or blocked storage: the collection then does not say it was split.
    }
}
