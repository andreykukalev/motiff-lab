// What the folder import keeps on this device (version 0.5, «Импорт папки»), never in a project and never sent: the folders
// the player chose (with Chrome's and Edge's read-only access to them, so the app can look through them again and read a
// file where it is), which folder each linked piece is in, the import under way, and the queue of pieces to recognise.

import { done, result } from "./idb.js";

const DATABASE = "motiff-lab-import";
const VERSION = 1;
const FOLDERS = "folders";
const LINKS = "links";
const STATE = "state";

/** A folder the player chose: its name, when it was looked through last, and the browser's access to it where there is one. */
export interface KeptFolder {
    id: string;
    name: string;
    addedAt: string;
    checkedAt: string | null;
    handle?: FileSystemDirectoryHandle;
}

let opening: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
    opening ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DATABASE, VERSION);
        request.onupgradeneeded = () => {
            const db = request.result;
            db.createObjectStore(FOLDERS, { keyPath: "id" });
            db.createObjectStore(LINKS);
            db.createObjectStore(STATE);
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => {
            opening = null;
            reject(request.error);
        };
    });
    return opening;
}

async function write(store: string, change: (objects: IDBObjectStore) => void): Promise<void> {
    const db = await open();
    const transaction = db.transaction(store, "readwrite");
    change(transaction.objectStore(store));
    await done(transaction);
}

export async function keepFolder(folder: KeptFolder): Promise<void> {
    await write(FOLDERS, (objects) => objects.put(folder));
}

export async function folder(id: string): Promise<KeptFolder | null> {
    const db = await open();
    return ((await result(db.transaction(FOLDERS).objectStore(FOLDERS).get(id))) as KeptFolder | undefined) ?? null;
}

/** The folders kept, the last added first. */
export async function folders(): Promise<KeptFolder[]> {
    const db = await open();
    const all = (await result(db.transaction(FOLDERS).objectStore(FOLDERS).getAll())) as KeptFolder[];
    return all.sort((a, b) => (a.addedAt < b.addedAt ? 1 : -1));
}

/** Remembers where a piece's file is on this device (its folder and path, as text), so the piece opens it there; null forgets it. */
export async function linkPiece(projectId: string, link: string | null): Promise<void> {
    await write(LINKS, (objects) => (link === null ? objects.delete(projectId) : objects.put(link, projectId)));
}

export async function linkedFolder(projectId: string): Promise<string | null> {
    const db = await open();
    const found = (await result(db.transaction(LINKS).objectStore(LINKS).get(projectId))) as string | undefined;
    return found ?? null;
}

/** Every piece linked on this device, as [project, link]. */
export async function links(): Promise<[string, string][]> {
    const db = await open();
    const store = db.transaction(LINKS).objectStore(LINKS);
    const [keys, values] = await Promise.all([result(store.getAllKeys()), result(store.getAll())]);
    return keys.map((key, index) => [String(key), String(values[index])]);
}

/** A piece of state kept as text (the import under way, the recognition queue); null when none. */
export async function loadState(key: string): Promise<string | null> {
    const db = await open();
    const found = (await result(db.transaction(STATE).objectStore(STATE).get(key))) as string | undefined;
    return found ?? null;
}

export async function saveState(key: string, value: string | null): Promise<void> {
    await write(STATE, (objects) => (value === null ? objects.delete(key) : objects.put(value, key)));
}
