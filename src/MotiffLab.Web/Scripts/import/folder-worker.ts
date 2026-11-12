// The folder import's worker (version 0.5, «Импорт папки»): reads one file of the player's folder at a time, off the page's
// thread, and answers its SHA-256 and, for a PDF, what it says about itself (pdf-info.ts). The file is only read.

import { pdfInfo, type PdfInfo } from "./pdf-info.js";

/** A file to read: its fingerprint when asked, and a PDF's details. */
export interface DetailsAsked {
    id: number;
    file: File;
    pdf: boolean;
}

export interface DetailsRead {
    id: number;
    sha256: string | null;
    info: PdfInfo | null;
    /** The file could not be read: not downloaded, no permission, gone. */
    unreadable: boolean;
}

/** The largest file fingerprinted: it is read whole into memory. */
const MOST_READ = 300 * 1024 * 1024;

async function sha256(data: ArrayBuffer): Promise<string> {
    const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", data));
    return Array.from(digest, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

/** Reads one file: its fingerprint, and a PDF's pages, title, author and lock. */
export async function readDetails(asked: DetailsAsked): Promise<DetailsRead> {
    if (asked.file.size > MOST_READ) {
        return { id: asked.id, sha256: null, info: null, unreadable: false };
    }
    try {
        const data = await asked.file.arrayBuffer();
        return {
            id: asked.id,
            sha256: await sha256(data),
            info: asked.pdf ? await pdfInfo(new Uint8Array(data)) : null,
            unreadable: false,
        };
    } catch {
        return { id: asked.id, sha256: null, info: null, unreadable: true };
    }
}

interface WorkerScope {
    onmessage: ((event: MessageEvent<DetailsAsked>) => void) | null;
    postMessage(message: DetailsRead): void;
}

// Only in a worker: Node's tests import readDetails without starting it.
const scope = globalThis as unknown as WorkerScope & { WorkerGlobalScope?: unknown };
if (typeof scope.WorkerGlobalScope !== "undefined") {
    scope.onmessage = (event) => {
        void readDetails(event.data).then((read) => scope.postMessage(read));
    };
}
