// A folder of the player's notes (version 0.5, «Импорт папки»): choosing it, listing the notes files in it, reading their
// fingerprints and PDF details in a worker, and reading one file where it is. Chrome and Edge keep read-only access to the
// folder (File System Access, mode "read") so it can be looked through again and its files read in place; other browsers
// pick the folder with webkitdirectory and keep its files for this visit only. Nothing here writes, renames, moves or
// deletes a file or a folder, and nothing is sent anywhere.

import type { DetailsAsked, DetailsRead } from "./folder-worker.js";
import { folder, folders, keepFolder, type KeptFolder } from "../storage/import-store.js";

export { linkPiece, linkedFolder, links, loadState, saveState } from "../storage/import-store.js";

interface DotNetRef {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** A folder as the app answers it: its id, its name, and whether this browser keeps access to it. */
export interface ChosenFolder {
    id: string;
    name: string;
    kept: boolean;
    checkedAt: string | null;
}

export interface ListedFile {
    path: string;
    bytes: number;
}

/** The notes files found, and how many other files were passed over unread. */
export interface Listing {
    files: ListedFile[];
    others: number;
}

/** A folder picked with webkitdirectory: its files for this visit, by path inside it (composed). */
const visiting = new Map<string, Map<string, File>>();

type DirectoryPicker = (options: { mode: "read"; id?: string }) => Promise<FileSystemDirectoryHandle>;
type Permissioned = FileSystemHandle & {
    queryPermission?(options: { mode: "read" }): Promise<PermissionState>;
    requestPermission?(options: { mode: "read" }): Promise<PermissionState>;
};

function picker(): DirectoryPicker | null {
    const found = (globalThis as unknown as { showDirectoryPicker?: DirectoryPicker }).showDirectoryPicker;
    return typeof found === "function" ? found : null;
}

function newId(): string {
    return `f_${crypto.randomUUID().replaceAll("-", "")}`;
}

function answer(kept: KeptFolder): ChosenFolder {
    return { id: kept.id, name: kept.name, kept: kept.handle !== undefined, checkedAt: kept.checkedAt };
}

/** Whether this browser can keep access to a folder (Chrome, Edge); else the folder is picked again each visit. */
export function canKeepFolders(): boolean {
    return picker() !== null;
}

/** The folders chosen on this device, the last first. */
export async function keptFolders(): Promise<ChosenFolder[]> {
    return (await folders()).map(answer);
}

/**
 * Chrome and Edge: the browser's folder picker, read only. The same folder chosen again keeps its id, so its pieces stay
 * linked. Null when the player closed the picker.
 */
export async function chooseFolder(): Promise<ChosenFolder | null> {
    const pick = picker();
    if (pick === null) {
        return null;
    }
    let handle: FileSystemDirectoryHandle;
    try {
        handle = await pick({ mode: "read", id: "motiff-lab-notes" });
    } catch {
        return null;
    }
    for (const known of await folders()) {
        if (known.handle && (await known.handle.isSameEntry(handle))) {
            const again = { ...known, handle };
            await keepFolder(again);
            return answer(again);
        }
    }
    const kept: KeptFolder = { id: newId(), name: handle.name, addedAt: new Date().toISOString(), checkedAt: null, handle };
    await keepFolder(kept);
    return answer(kept);
}

/**
 * Other browsers: the files of the folder picked in a webkitdirectory input, kept for this visit. A folder of the same
 * name chosen before keeps its id. Null when nothing was picked.
 */
export async function takePicked(input: HTMLInputElement): Promise<ChosenFolder | null> {
    const files = [...(input.files ?? [])];
    if (files.length === 0) {
        return null;
    }
    const root = files[0].webkitRelativePath.split("/")[0] || "…";
    const known = (await folders()).find((each) => !each.handle && each.name === root);
    const kept: KeptFolder = known ?? { id: newId(), name: root, addedAt: new Date().toISOString(), checkedAt: null };
    const byPath = new Map<string, File>();
    for (const file of files) {
        const path = file.webkitRelativePath.split("/").slice(1).join("/");
        byPath.set(path.normalize("NFC"), file);
    }
    visiting.set(kept.id, byPath);
    await keepFolder(kept);
    input.value = "";
    return answer(kept);
}

/** Whether the app may read the folder now: "granted", "prompt" (one tap to allow again), "denied", or "none" (pick it again). */
export async function permission(id: string): Promise<string> {
    if (visiting.has(id)) {
        return "granted";
    }
    const kept = await folder(id);
    const handle = kept?.handle as Permissioned | undefined;
    if (!handle?.queryPermission) {
        return "none";
    }
    return handle.queryPermission({ mode: "read" });
}

/** Asks the browser again for read access to the whole folder; called from the player's tap. */
export async function askPermission(id: string): Promise<boolean> {
    const handle = (await folder(id))?.handle as Permissioned | undefined;
    try {
        return (await handle?.requestPermission?.({ mode: "read" })) === "granted";
    } catch {
        return false;
    }
}

/** Notes when it was last looked through. */
export async function markChecked(id: string): Promise<void> {
    const kept = await folder(id);
    if (kept) {
        await keepFolder({ ...kept, checkedAt: new Date().toISOString() });
    }
}

function isNotes(name: string, extensions: string[]): boolean {
    const dot = name.lastIndexOf(".");
    return dot > 0 && extensions.includes(name.slice(dot).toLowerCase());
}

/** Every file of a kept folder, depth first, skipping hidden names. */
async function* walk(directory: FileSystemDirectoryHandle, prefix: string): AsyncGenerator<[string, FileSystemFileHandle]> {
    const entries = (directory as unknown as { values(): AsyncIterable<FileSystemHandle> }).values();
    for await (const entry of entries) {
        if (entry.name.startsWith(".")) {
            continue;
        }
        const path = prefix ? `${prefix}/${entry.name}` : entry.name;
        if (entry.kind === "directory") {
            yield* walk(entry as FileSystemDirectoryHandle, path);
        } else {
            yield [path, entry as FileSystemFileHandle];
        }
    }
}

/**
 * The notes files in the folder (by extension, as the app adds them) with their sizes, in a stream: the page hears how many
 * so far. Other files are counted and never read. Throws "permission" when the browser no longer lets the app read it.
 */
export async function listFolder(id: string, extensions: string[], dotnet: DotNetRef): Promise<Listing> {
    const lower = extensions.map((extension) => extension.toLowerCase());
    const listing: Listing = { files: [], others: 0 };
    const seen = (): void => {
        if ((listing.files.length + listing.others) % 100 === 0) {
            void dotnet.invokeMethodAsync("OnListed", listing.files.length);
        }
    };
    const session = visiting.get(id);
    if (session) {
        for (const [path, file] of session) {
            if (path.split("/").some((name) => name.startsWith("."))) {
                continue;
            }
            if (isNotes(path, lower)) {
                listing.files.push({ path, bytes: file.size });
            } else {
                listing.others++;
            }
            seen();
        }
        return listing;
    }
    const kept = await folder(id);
    if (!kept?.handle || (await permission(id)) !== "granted") {
        throw new Error("permission");
    }
    for await (const [path, handle] of walk(kept.handle, "")) {
        if (isNotes(path, lower)) {
            listing.files.push({ path: path.normalize("NFC"), bytes: (await handle.getFile()).size });
        } else {
            listing.others++;
        }
        seen();
    }
    return listing;
}

/** A child by its name, or by the same name composed the other way (a Mac writes "ü" as "u" and a mark). */
async function child<T extends FileSystemHandle>(directory: FileSystemDirectoryHandle, name: string, kind: "file" | "directory"): Promise<T | null> {
    try {
        return (kind === "file" ? await directory.getFileHandle(name) : await directory.getDirectoryHandle(name)) as unknown as T;
    } catch {
        const entries = (directory as unknown as { values(): AsyncIterable<FileSystemHandle> }).values();
        for await (const entry of entries) {
            if (entry.kind === kind && entry.name.normalize("NFC") === name.normalize("NFC")) {
                return entry as T;
            }
        }
        return null;
    }
}

/** A file of the folder by its path inside it; null when it is not there. Throws "permission" without access. */
export async function fileAt(id: string, path: string): Promise<File | null> {
    const session = visiting.get(id);
    if (session) {
        return session.get(path.normalize("NFC")) ?? null;
    }
    const kept = await folder(id);
    if (!kept?.handle || (await permission(id)) !== "granted") {
        throw new Error("permission");
    }
    let directory: FileSystemDirectoryHandle | null = kept.handle;
    const names = path.split("/");
    for (const name of names.slice(0, -1)) {
        directory = directory ? await child<FileSystemDirectoryHandle>(directory, name, "directory") : null;
    }
    const handle = directory ? await child<FileSystemFileHandle>(directory, names.at(-1) ?? "", "file") : null;
    return handle ? handle.getFile() : null;
}

/** A file's bytes by its path in the folder, read where it is; null when it is not there. */
export async function readFile(id: string, path: string): Promise<Uint8Array | null> {
    const file = await fileAt(id, path);
    return file ? new Uint8Array(await file.arrayBuffer()) : null;
}

let stopped = false;

/** Stops the details being read, as when the sheet closes. */
export function stopDetails(): void {
    stopped = true;
}

/**
 * Reads the fingerprints and PDF details of the files asked, in two workers, and hands them to the page a few at a time
 * ("OnDetails"). Files are read whole, one per worker at a time.
 */
export async function readDetails(id: string, asked: { path: string; pdf: boolean }[], dotnet: DotNetRef): Promise<void> {
    stopped = false;
    const workers = [0, 1].map(() => new Worker(new URL("./folder-worker.js", import.meta.url), { type: "module" }));
    let pending: (DetailsRead & { path: string })[] = [];
    let last = Date.now();
    const flush = async (): Promise<void> => {
        if (pending.length > 0) {
            const batch = pending;
            pending = [];
            last = Date.now();
            await dotnet.invokeMethodAsync("OnDetails", batch);
        }
    };
    let next = 0;
    const run = async (worker: Worker): Promise<void> => {
        while (!stopped && next < asked.length) {
            const index = next++;
            const read = await readOne(worker, id, index, asked[index]);
            pending.push({ ...read, path: asked[index].path });
            if (pending.length >= 20 || Date.now() - last > 400) {
                await flush();
            }
        }
    };
    try {
        await Promise.all(workers.map(run));
        await flush();
    } finally {
        workers.forEach((worker) => worker.terminate());
    }
}

async function readOne(worker: Worker, id: string, index: number, asked: { path: string; pdf: boolean }): Promise<DetailsRead> {
    let file: File | null;
    try {
        file = await fileAt(id, asked.path);
    } catch {
        file = null;
    }
    if (file === null) {
        return { id: index, sha256: null, info: null, unreadable: true };
    }
    const message: DetailsAsked = { id: index, file, pdf: asked.pdf };
    return new Promise((resolve) => {
        worker.onmessage = (event: MessageEvent<DetailsRead>) => resolve(event.data);
        worker.onerror = () => resolve({ id: index, sha256: null, info: null, unreadable: true });
        worker.postMessage(message);
    });
}

/** Roughly how many bytes this site may still keep, for a copy's estimate; null when the browser does not say. */
export async function freeSpace(): Promise<number | null> {
    if (!navigator.storage?.estimate) {
        return null;
    }
    const { usage = 0, quota = 0 } = await navigator.storage.estimate();
    return quota > 0 ? Math.max(0, quota - usage) : null;
}
