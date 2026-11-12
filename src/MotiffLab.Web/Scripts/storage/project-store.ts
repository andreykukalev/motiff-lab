// Projects saved on the device (S1-P1): a small IndexedDB wrapper. Each project is stored as its
// project file (ProjectFile.cs) next to a summary row for the library (0018 section 9), so listing never
// loads the source files, and a cover: the first page as engraved, with where its pages, phrases and bars
// start, saved when the score was last shown. Nothing here leaves the device.

export { deleteMarks } from "./pencil-store.js";
export { deleteRecordings } from "./recording-store.js";

const DATABASE = "motiff-lab";
const VERSION = 2;
const PROJECTS = "projects";
const SUMMARIES = "summaries";
const COVERS = "covers";

/** Above this share of the browser's quota the projects screen suggests exporting projects. */
export const NEARLY_FULL = 0.8;

/** A row on the projects screen; lastOpened is an ISO 8601 time. */
export interface ProjectSummary {
    id: string;
    fileName: string;
    motifCount: number;
    variationCount: number;
    lastOpened: string;
    /** Work sessions and kept ideas (0012); rows saved by earlier versions have neither. */
    resumeCount?: number;
    ideaCount?: number;
    /** Where the piece came from, the bars Check asks about and the latest resume point (0018); rows saved earlier have none. */
    origin?: string;
    checkBars?: number;
    latest?: { id: string; name: string | null; at: number; from: number | null; to: number | null; lastActive: string } | null;
    /** The title, composer, tags and playlists the card shows, each with where it comes from (version 0.2); rows saved earlier have none. */
    title?: string | null;
    titleFrom?: string | null;
    composer?: string | null;
    composerFrom?: string | null;
    tags?: string[] | null;
    playlists?: string[] | null;
    /** The piece's originals and where their bytes are (version 0.3); rows saved earlier have none. */
    originals?: { id: string; kind: string; kept: string; inFile: boolean; held: string[] | null }[] | null;
}

/**
 * A piece's cover in the library (0018 section 9), as the score view saves it: the first page's SVG, the title
 * and lines it is headed with, the number of pages, and the ticks where each page, phrase and bar starts, so
 * the card can say "p. 2 · phrase 5" for any resume point without opening the piece.
 */
export interface Cover {
    id: string;
    svg: string;
    title: string | null;
    lines: string[];
    pages: number;
    pageStarts: number[];
    phraseStarts: number[];
    barStarts: number[];
    firstBar: number;
}

/** A cover as the library reads it: the page as an image address in place of its SVG. */
export type CoverRow = Omit<Cover, "svg"> & { url: string };

let coverUrls: string[] = [];

export interface StorageUse {
    usage: number;
    quota: number;
    nearlyFull: boolean;
}

/** True when usage is at or above NEARLY_FULL of a known, non-zero quota. */
export function isNearlyFull(usage: number, quota: number): boolean {
    return quota > 0 && usage / quota >= NEARLY_FULL;
}

/** Most recently opened first, then by file name, so the order is stable. */
export function sortSummaries(summaries: ProjectSummary[]): ProjectSummary[] {
    return [...summaries].sort((a, b) =>
        a.lastOpened === b.lastOpened ? a.fileName.localeCompare(b.fileName) : a.lastOpened < b.lastOpened ? 1 : -1);
}

let opening: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
    opening ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DATABASE, VERSION);
        request.onupgradeneeded = () => {
            const db = request.result;
            if (!db.objectStoreNames.contains(PROJECTS)) {
                db.createObjectStore(PROJECTS);
            }
            if (!db.objectStoreNames.contains(SUMMARIES)) {
                db.createObjectStore(SUMMARIES, { keyPath: "id" });
            }
            if (!db.objectStoreNames.contains(COVERS)) {
                db.createObjectStore(COVERS, { keyPath: "id" });
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => {
            opening = null;
            reject(request.error);
        };
    });
    return opening;
}

function done(transaction: IDBTransaction): Promise<void> {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onabort = () => reject(transaction.error);
        transaction.onerror = () => reject(transaction.error);
    });
}

function result<T>(request: IDBRequest<T>): Promise<T> {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

/** Saves a project file and its summary in one transaction, replacing any earlier save. */
export async function saveProject(id: string, file: string, summary: ProjectSummary): Promise<void> {
    const db = await open();
    const transaction = db.transaction([PROJECTS, SUMMARIES], "readwrite");
    transaction.objectStore(PROJECTS).put(file, id);
    transaction.objectStore(SUMMARIES).put(summary);
    await done(transaction);
}

/** Writes a project's summary row again, leaving its file as it is: rows saved before they kept the title, composer and tags. */
export async function saveSummary(summary: ProjectSummary): Promise<void> {
    const db = await open();
    const transaction = db.transaction(SUMMARIES, "readwrite");
    transaction.objectStore(SUMMARIES).put(summary);
    await done(transaction);
}

/** The project file saved under id, or null. */
export async function loadProject(id: string): Promise<string | null> {
    const db = await open();
    const file = await result(db.transaction(PROJECTS).objectStore(PROJECTS).get(id));
    return typeof file === "string" ? file : null;
}

export async function listProjects(): Promise<ProjectSummary[]> {
    const db = await open();
    const summaries = await result(db.transaction(SUMMARIES).objectStore(SUMMARIES).getAll());
    return sortSummaries(summaries as ProjectSummary[]);
}

export async function deleteProject(id: string): Promise<void> {
    const db = await open();
    const transaction = db.transaction([PROJECTS, SUMMARIES, COVERS], "readwrite");
    transaction.objectStore(PROJECTS).delete(id);
    transaction.objectStore(SUMMARIES).delete(id);
    transaction.objectStore(COVERS).delete(id);
    await done(transaction);
}

/** Keeps a piece's cover, replacing the one saved before. */
export async function saveCover(cover: Cover): Promise<void> {
    const db = await open();
    const transaction = db.transaction(COVERS, "readwrite");
    transaction.objectStore(COVERS).put(cover);
    await done(transaction);
}

/** The covers saved, each page as an image address valid until the next call. */
export async function listCovers(): Promise<CoverRow[]> {
    const db = await open();
    const covers = (await result(db.transaction(COVERS).objectStore(COVERS).getAll())) as Cover[];
    releaseCovers();
    return covers.map(({ svg, ...cover }) => {
        const url = URL.createObjectURL(new Blob([svg], { type: "image/svg+xml" }));
        coverUrls.push(url);
        return { ...cover, url };
    });
}

/** Lets go of the image addresses the last listing made. */
export function releaseCovers(): void {
    for (const url of coverUrls) {
        URL.revokeObjectURL(url);
    }
    coverUrls = [];
}

/** How much of the browser's storage this site uses, or null when the browser does not say. */
export async function storageUse(): Promise<StorageUse | null> {
    if (!navigator.storage?.estimate) {
        return null;
    }
    const { usage = 0, quota = 0 } = await navigator.storage.estimate();
    return { usage, quota, nearlyFull: isNearlyFull(usage, quota) };
}

/** Hands the player a text file to save, for project export. */
export function downloadText(fileName: string, text: string): void {
    const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 0);
}

/** Hands the player a binary file to save, such as a MIDI export. */
export function downloadBytes(fileName: string, data: Uint8Array, contentType: string): void {
    const url = URL.createObjectURL(new Blob([data as BlobPart], { type: contentType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 0);
}
