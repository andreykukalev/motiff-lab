// The parts of a long recording already heard (0014), kept on the device until they are joined, so a hearing cut off by
// a closed tab or a lost connection goes on from the next part rather than from the start. A row is a part's notes as
// MIDI (base64), or none for a quiet part, with the engine that heard it, under a key naming the recording's sound, the
// stretch, the scan server and the part; music-core joins them, and they are deleted once joined. Rows older than a
// week are deleted when the store opens. Only notes are kept, never the sound, and nothing here leaves the device.

const DATABASE = "motiff-lab-heard-parts";
const VERSION = 1;
const PARTS = "parts";

/** How long a part waits to be joined before it is forgotten. */
export const KEEP_MILLISECONDS = 7 * 24 * 60 * 60 * 1000;

/** A part heard: its notes as a MIDI file in base64, or null when the server heard none; the engine; when. */
export interface StoredPart {
    midi: string | null;
    engine: string;
    at: number;
}

/** Where heard parts are kept: IndexedDB in the app, a map in tests. */
export interface PartStore {
    get(key: string): Promise<StoredPart | null>;
    put(key: string, part: StoredPart): Promise<void>;
    /** Forgets every part whose key starts with the prefix. */
    remove(prefix: string): Promise<void>;
}

let opening: Promise<IDBDatabase> | null = null;

function finished(transaction: IDBTransaction): Promise<void> {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onabort = () => reject(transaction.error);
        transaction.onerror = () => reject(transaction.error);
    });
}

/** Deletes the parts kept longer than KEEP_MILLISECONDS. */
async function prune(db: IDBDatabase, now: number): Promise<void> {
    const transaction = db.transaction(PARTS, "readwrite");
    const request = transaction.objectStore(PARTS).openCursor();
    request.onsuccess = () => {
        const cursor = request.result;
        if (!cursor) {
            return;
        }
        const part = cursor.value as StoredPart | undefined;
        if (!part || typeof part.at !== "number" || now - part.at > KEEP_MILLISECONDS) {
            cursor.delete();
        }
        cursor.continue();
    };
    await finished(transaction);
}

function open(): Promise<IDBDatabase> {
    opening ??= new Promise<IDBDatabase>((resolve, reject) => {
        const request = indexedDB.open(DATABASE, VERSION);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(PARTS)) {
                request.result.createObjectStore(PARTS);
            }
        };
        request.onsuccess = () => {
            const db = request.result;
            prune(db, Date.now()).catch(() => undefined).finally(() => resolve(db));
        };
        request.onerror = () => {
            opening = null;
            reject(request.error);
        };
    });
    return opening;
}

/** The parts kept in this browser's IndexedDB; a store that cannot open keeps nothing, and hearing goes on without it. */
export const browserParts: PartStore = {
    async get(key) {
        try {
            const db = await open();
            const request = db.transaction(PARTS).objectStore(PARTS).get(key);
            const part = await new Promise<unknown>((resolve, reject) => {
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
            return part && typeof part === "object" && "engine" in part ? (part as StoredPart) : null;
        } catch {
            return null;
        }
    },
    async put(key, part) {
        try {
            const db = await open();
            const transaction = db.transaction(PARTS, "readwrite");
            transaction.objectStore(PARTS).put(part, key);
            await finished(transaction);
        } catch {
            // Storage full or blocked: the part is still used now, only not kept for later.
        }
    },
    async remove(prefix) {
        try {
            const db = await open();
            const transaction = db.transaction(PARTS, "readwrite");
            transaction.objectStore(PARTS).delete(IDBKeyRange.bound(prefix, `${prefix}￿`));
            await finished(transaction);
        } catch {
            // Pruned after a week then.
        }
    },
};
