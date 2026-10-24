// Projects saved on the device (S1-P1): a small IndexedDB wrapper. Each project is stored as its
// project file (ProjectFile.cs) next to a summary row for the projects screen (N1), so listing never
// loads the source files. Nothing here leaves the device.

const DATABASE = "motiff-lab";
const VERSION = 1;
const PROJECTS = "projects";
const SUMMARIES = "summaries";

/** Above this share of the browser's quota the projects screen suggests exporting projects. */
export const NEARLY_FULL = 0.8;

/** A row on the projects screen; lastOpened is an ISO 8601 time. */
export interface ProjectSummary {
    id: string;
    fileName: string;
    motifCount: number;
    variationCount: number;
    lastOpened: string;
}

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
    const transaction = db.transaction([PROJECTS, SUMMARIES], "readwrite");
    transaction.objectStore(PROJECTS).delete(id);
    transaction.objectStore(SUMMARIES).delete(id);
    await done(transaction);
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
