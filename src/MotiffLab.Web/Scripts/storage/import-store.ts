// What the folder import keeps on this device (version 0.5, «Импорт папки»), never in a project and never sent: the folders
// the player chose (with Chrome's and Edge's read-only access to them, so the app can look through them again and read a
// file where it is), which folder each linked piece is in, the import under way, and the queue of pieces to recognise.

import { done, result } from "./idb.js";

const DATABASE = "motiff-lab-import";
const VERSION = 2;
const FOLDERS = "folders";
const LINKS = "links";
const STATE = "state";
/** Version 2: an original the player linked by picking the file itself, by piece: Chrome's read-only handle, else a copy. */
const ORIGINALS = "originals";

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
            for (const store of [FOLDERS, LINKS, STATE, ORIGINALS]) {
                if (!db.objectStoreNames.contains(store)) {
                    db.createObjectStore(store, store === FOLDERS ? { keyPath: "id" } : undefined);
                }
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


/** A piece's original as the player picked it: Chrome's handle to the file (read only), or a copy of it in other browsers. */
export type PickedOriginal = FileSystemFileHandle | Blob;

export async function keepOriginal(projectId: string, original: PickedOriginal | null): Promise<void> {
    await write(ORIGINALS, (objects) => (original === null ? objects.delete(projectId) : objects.put(original, projectId)));
}

export async function pickedOriginal(projectId: string): Promise<PickedOriginal | null> {
    const db = await open();
    return ((await result(db.transaction(ORIGINALS).objectStore(ORIGINALS).get(projectId))) as PickedOriginal | undefined) ?? null;
}

/**
 * The pieces whose PDF this browser reads where the player keeps it (a folder or a file picked), for the library's badges;
 * none, without creating the database, when no folder was ever added here.
 */
export async function linkedPieces(): Promise<string[]> {
    const known = await indexedDB.databases?.().catch(() => []);
    if (!known?.some((each) => each.name === DATABASE)) {
        return [];
    }
    const db = await open();
    const transaction = db.transaction([LINKS, ORIGINALS]);
    const [linked, picked] = await Promise.all([result(transaction.objectStore(LINKS).getAllKeys()), result(transaction.objectStore(ORIGINALS).getAllKeys())]);
    return [...new Set([...linked, ...picked].map(String))];
}
