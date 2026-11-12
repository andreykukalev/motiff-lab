// Local-first originals (version 0.3): the fingerprint of a file the player picks, to link it again to the same original
// of a piece; which recordings this browser itself holds, so it takes back only its own mark when one is gone (another
// browser may hold the same piece's sound); and the one request that the browser keep this site's storage. Nothing here
// leaves the device.

import { linkedPieces } from "./import-store.js";
import { hasRecording, keepPicked } from "./recording-store.js";

const HELD = "motiff-lab:originals-here";
const PERSIST_ASKED = "motiff-lab:persist-asked";

/** The SHA-256 of some bytes as 64 lower-case hex digits, as the project file keeps it. */
export async function sha256(bytes: ArrayBuffer): Promise<string> {
    const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
    return Array.from(digest, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

/** The largest file fingerprinted: it is read whole into memory. A longer recording is linked again by the player's word. */
export const MOST_FINGERPRINTED = 300 * 1024 * 1024;

/** The fingerprint of the file picked in `input`, or null when none is picked, it is too large or it cannot be read. */
export async function fingerprintPicked(input: HTMLInputElement): Promise<string | null> {
    const file = input.files?.[0];
    if (!file || file.size > MOST_FINGERPRINTED) {
        return null;
    }
    try {
        return await sha256(await file.arrayBuffer());
    } catch {
        return null;
    }
}

function readHeld(): Set<string> {
    try {
        const kept: unknown = JSON.parse(localStorage.getItem(HELD) ?? "[]");
        return new Set(Array.isArray(kept) ? kept.filter((key): key is string => typeof key === "string") : []);
    } catch {
        return new Set();
    }
}

function writeHeld(held: Set<string>): void {
    try {
        localStorage.setItem(HELD, JSON.stringify([...held].sort()));
    } catch {
        // Blocked storage: this browser then never takes back a mark it cannot tell is its own.
    }
}

/** The keys ("<project>/<original>") of the recordings this browser said it holds. */
export function heldHere(): string[] {
    return [...readHeld()];
}

/** What heldHere says, and the PDFs this browser reads where the player keeps them (version 0.5, «Импорт папки»). */
export async function heldHereWithLinks(): Promise<string[]> {
    return [...readHeld(), ...(await linkedPieces()).map((projectId) => `${projectId}/src_pdf`)];
}

/** Remembers that this browser holds a recording under the key, or that it no longer does. */
export function rememberHeld(key: string, held: boolean): void {
    const all = readHeld();
    if (held === all.has(key)) {
        return;
    }
    if (held) {
        all.add(key);
    } else {
        all.delete(key);
    }
    writeHeld(all);
}

/**
 * Whether the recording under the key is here: "here", "gone" when this browser held it and it is no longer kept (the
 * browser cleared it), or "never" when this browser never held it.
 */
export async function recordingHere(key: string): Promise<"here" | "gone" | "never"> {
    if (await hasRecording(key)) {
        return "here";
    }
    return readHeld().has(key) ? "gone" : "never";
}

/** Keeps the sound of the recording picked in `input` under the key, and remembers this browser holds it. */
export async function keepRelinked(input: HTMLInputElement, key: string): Promise<boolean> {
    const kept = await keepPicked(input, key);
    if (kept) {
        rememberHeld(key, true);
    }
    return kept;
}

/**
 * Asks the browser once to keep this site's storage, so it does not clear the originals kept here (Safari clears a
 * site's storage after seven days without a visit). Answers whether storage is kept, or null when the browser cannot
 * say or it was asked before.
 */
export async function persistOnce(): Promise<boolean | null> {
    try {
        if (localStorage.getItem(PERSIST_ASKED) !== null || !navigator.storage?.persist) {
            return null;
        }
        localStorage.setItem(PERSIST_ASKED, new Date().toISOString());
        return await navigator.storage.persist();
    } catch {
        return null;
    }
}
