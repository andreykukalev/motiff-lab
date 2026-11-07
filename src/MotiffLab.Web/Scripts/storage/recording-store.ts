// The sound of recordings added to a piece as sources (decision record 0024, scenario 2), kept on the device: one row
// per version, the recording as a mono WAV file, under "<project id>/<version id>". The project file never holds it,
// so it does not go online with the project; a database of its own keeps the projects database's version. Nothing
// here leaves the device; deleting a project deletes its recordings (ProjectStore.cs).

import { SAMPLE_RATE, decodeMono, encodeWav, heldRecording } from "../import/recording.js";

const DATABASE = "motiff-lab-recordings";
const VERSION = 1;
const SOUNDS = "sounds";

let opening: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
    opening ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DATABASE, VERSION);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(SOUNDS)) {
                request.result.createObjectStore(SOUNDS);
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

/** The key a version's recording is kept under. */
export function recordingKey(projectId: string, versionId: string): string {
    return `${projectId}/${versionId}`;
}

/** Keeps a recording's sound under a key, replacing what was kept. */
export async function keepRecording(key: string, sound: Blob): Promise<void> {
    const db = await open();
    const transaction = db.transaction(SOUNDS, "readwrite");
    transaction.objectStore(SOUNDS).put(sound, key);
    await finished(transaction);
}

/** Keeps the recording the transcription has just heard; false when none is held (the browser could not decode it). */
export async function keepHeld(key: string): Promise<boolean> {
    const sound = heldRecording();
    if (sound === null) {
        return false;
    }
    try {
        await keepRecording(key, sound);
        return true;
    } catch {
        return false;
    }
}

/**
 * Keeps the sound of the recording picked in `input` again, for a version whose sound is not on this device (another
 * device, or added before the sound was kept); false when the browser cannot decode it.
 */
export async function keepPicked(input: HTMLInputElement, key: string): Promise<boolean> {
    const file = input.files?.[0];
    const mono = file ? await decodeMono(await file.arrayBuffer()) : null;
    if (mono === null) {
        return false;
    }
    try {
        await keepRecording(key, new Blob([encodeWav(mono, SAMPLE_RATE) as BlobPart], { type: "audio/wav" }));
        return true;
    } catch {
        return false;
    }
}

/** The sound kept under a key, or null. */
export async function openRecording(key: string): Promise<Blob | null> {
    try {
        const db = await open();
        const request = db.transaction(SOUNDS).objectStore(SOUNDS).get(key);
        const sound = await new Promise<unknown>((resolve, reject) => {
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        return sound instanceof Blob ? sound : null;
    } catch {
        return null;
    }
}

/** Whether a sound is kept under a key. */
export async function hasRecording(key: string): Promise<boolean> {
    return (await openRecording(key)) !== null;
}

/** Forgets the sounds of a project, or of one of its versions when the key names it. */
export async function deleteRecordings(prefix: string): Promise<void> {
    const db = await open();
    const transaction = db.transaction(SOUNDS, "readwrite");
    const store = transaction.objectStore(SOUNDS);
    const range = prefix.includes("/") ? IDBKeyRange.only(prefix) : IDBKeyRange.bound(`${prefix}/`, `${prefix}/￿`);
    store.delete(range);
    await finished(transaction);
}
