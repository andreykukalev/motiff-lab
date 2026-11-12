// The sound of recordings added to a piece as sources (decision record 0024, scenario 2), kept on the device: one row
// per version, the recording as a mono WAV file, under "<project id>/<version id>". The project file never holds it,
// so it does not go online with the project; a database of its own keeps the projects database's version. Nothing
// here leaves the device; deleting a project deletes its recordings (ProjectStore.cs).
//
// A recording of several pieces (version 0.5) keeps the stretch that was heard under "<project id>/src_concert", and
// each piece split from it its own stretch of that sound under "<piece id>/src_sound", with its size, length and
// SHA-256 for the piece's file to describe (PieceParts.WithSound). Their times are those of the stretch heard.

import { SAMPLE_RATE, decodeMono, encodeWav, heldRecording, heldStretch } from "../import/recording.js";
import { wavOf } from "../import/sound.js";

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

/** Where the stretch of a recording of several pieces that was heard is kept, beside its project's versions. */
export const CONCERT = "src_concert";

/** Where a piece split from a recording keeps its own stretch of the sound (PieceParts.SoundId). */
export const SOUND = "src_sound";

/** The length of the header of the WAV files kept here, which this app writes itself (sound.ts). */
const HEADER = 44;

/** A piece's own sound as kept: the WAV file's size in bytes, its length in milliseconds and its SHA-256 in hex. */
export interface CutSound {
    bytes: number;
    milliseconds: number;
    sha256: string;
}

/** How far a stretch picked again may be from the length its piece's file gives, without a fingerprint: a second. */
export const LENGTH_SLACK_MILLISECONDS = 1000;

/** The first sample of a millisecond of sound at SAMPLE_RATE, within `length` samples. */
export function sampleOf(milliseconds: number, length: number): number {
    return Math.max(0, Math.min(length, Math.round((milliseconds * SAMPLE_RATE) / 1000)));
}

/** The SHA-256 of some bytes, in lower-case hex. */
export async function sha256Of(bytes: Uint8Array): Promise<string> {
    const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes as BufferSource));
    return Array.from(digest, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

/** What a piece's file says of a WAV file: its size, length and fingerprint. */
export async function describe(wav: Uint8Array): Promise<CutSound> {
    const samples = Math.max(0, (wav.length - HEADER) / 2);
    return { bytes: wav.length, milliseconds: Math.round((samples * 1000) / SAMPLE_RATE), sha256: await sha256Of(wav) };
}

/**
 * Whether a stretch cut again from a file picked again is the piece's own sound: by its fingerprint when the piece's file
 * gives one, else by its length, within a second.
 */
export function isSameSound(cut: CutSound, sha256: string | null, milliseconds: number | null): boolean {
    if (sha256) {
        return cut.sha256 === sha256;
    }
    return milliseconds === null || Math.abs(cut.milliseconds - milliseconds) <= LENGTH_SLACK_MILLISECONDS;
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

/**
 * Keeps the stretch of the recording held that was just heard, for a recording of several pieces; false when none is
 * held (the browser could not decode it) or it could not be kept.
 */
export async function keepHeldStretch(key: string, fromMilliseconds: number, toMilliseconds: number): Promise<boolean> {
    const sound = heldStretch(fromMilliseconds, toMilliseconds);
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
 * Keeps a piece's own sound, its stretch of the sound kept under `fromKey`, under `key`, and says what its file is to
 * describe; null when nothing is kept under `fromKey` or the stretch holds no sound. Only the stretch is read.
 */
export async function keepStretchOf(fromKey: string, key: string, fromMilliseconds: number, toMilliseconds: number): Promise<CutSound | null> {
    const whole = await openRecording(fromKey);
    if (whole === null) {
        return null;
    }
    const length = Math.max(0, Math.floor((whole.size - HEADER) / 2));
    const from = sampleOf(fromMilliseconds, length);
    const to = Math.max(from, sampleOf(toMilliseconds, length));
    if (to === from) {
        return null;
    }
    const pcm = await whole.slice(HEADER + from * 2, HEADER + to * 2).arrayBuffer();
    return keepCut(key, wavOf(new Int16Array(pcm), SAMPLE_RATE));
}

/**
 * Keeps a piece's own sound from the file of the recording picked again on another visit: its stretch, checked against
 * what the piece's file says of it (`isSameSound`); null when the browser cannot decode the file or the stretch is not
 * the piece's sound.
 */
export async function keepPickedStretch(
    input: HTMLInputElement,
    key: string,
    fromMilliseconds: number,
    toMilliseconds: number,
    sha256: string | null,
    milliseconds: number | null,
): Promise<CutSound | null> {
    const file = input.files?.[0];
    const mono = file ? await decodeMono(await file.arrayBuffer()) : null;
    if (mono === null) {
        return null;
    }
    const from = sampleOf(fromMilliseconds, mono.length);
    const to = Math.max(from, sampleOf(toMilliseconds, mono.length));
    const wav = encodeWav(mono.subarray(from, to), SAMPLE_RATE);
    const cut = await describe(wav);
    return to > from && isSameSound(cut, sha256, milliseconds) ? keepCut(key, wav, cut) : null;
}

async function keepCut(key: string, wav: Uint8Array, described?: CutSound): Promise<CutSound | null> {
    try {
        const cut = described ?? (await describe(wav));
        await keepRecording(key, new Blob([wav as BlobPart], { type: "audio/wav" }));
        return cut;
    } catch {
        return null;
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
