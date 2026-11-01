// Pencil marks saved on the device (decision record 0015): one row per piece, its marks as the text
// pencil-marks.ts writes. A database of their own, so the projects database (project-store.ts) keeps
// its version. Since project file v12 a project keeps its marks itself (Projects/Marks.cs); this store
// holds the marks of a piece open without a project, and marks saved before v12 until the pencil layer
// takes them into their project. Nothing here leaves the device; deleting a project deletes what is
// left here for it (ProjectStore.cs).

const DATABASE = "motiff-lab-pencil";
const VERSION = 1;
const MARKS = "marks";

let opening: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
    opening ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DATABASE, VERSION);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(MARKS)) {
                request.result.createObjectStore(MARKS);
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

function finished(transaction: IDBTransaction): Promise<void> {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onabort = () => reject(transaction.error);
        transaction.onerror = () => reject(transaction.error);
    });
}

/** The marks saved for a piece, or null. */
export async function loadMarks(key: string): Promise<string | null> {
    const db = await open();
    const request = db.transaction(MARKS).objectStore(MARKS).get(key);
    const text = await new Promise<unknown>((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
    return typeof text === "string" ? text : null;
}

/** Saves a piece's marks, replacing what was saved; null forgets them. */
export async function saveMarks(key: string, text: string | null): Promise<void> {
    const db = await open();
    const transaction = db.transaction(MARKS, "readwrite");
    if (text === null) {
        transaction.objectStore(MARKS).delete(key);
    } else {
        transaction.objectStore(MARKS).put(text, key);
    }
    await finished(transaction);
}

export function deleteMarks(key: string): Promise<void> {
    return saveMarks(key, null);
}
