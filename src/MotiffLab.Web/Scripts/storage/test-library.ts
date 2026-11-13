// «Очистить тестовую библиотеку» on the web test site (Andrei, 2026-11-13; infra/README.md, "A fresh testing cycle"): before
// a new testing cycle the test site starts from an empty library in this browser. It empties the app's own stores of the
// library, every IndexedDB database below, and the library's entries in local storage, then the page loads again. The
// interface language, the theme, the settings, the sign-in (MSAL's own entries), the scan server address and the day's
// count of hosted pages stay. Nothing outside the browser's storage for this site is touched: the player's files and
// folders are only ever read. It refuses to run anywhere but on a test site, so the main address never loses a piece.

import { done } from "./idb.js";
import { isTestSite } from "../layout/test-site.js";

/** The app's IndexedDB databases: projects, pencil marks, recordings, the parts of a recording heard, the folder import. */
export const LIBRARY_DATABASES = ["motiff-lab", "motiff-lab-pencil", "motiff-lab-recordings", "motiff-lab-heard-parts", "motiff-lab-import"];

const PREFIX = "motiff-lab:";
/** Local storage entries of the library itself: the sync records and the originals kept on this device. */
const LIBRARY_KEYS = new Set([PREFIX + "ml-sync", PREFIX + "originals-here"]);
/** Entries kept by piece: corrections off, the PDF page and the metronome where the piece was left. */
const LIBRARY_PREFIXES = [PREFIX + "edits.off.", PREFIX + "pdf.page.", PREFIX + "pdf.metronome."];

/** Whether a local storage entry belongs to the library (and goes with it), rather than to the device. */
export function isLibraryKey(key: string): boolean {
    return LIBRARY_KEYS.has(key) || LIBRARY_PREFIXES.some((prefix) => key.startsWith(prefix));
}

/** Where the app runs, for Settings · About: "test" on the web test site, "local" on this computer, else "main". */
export function siteKind(hostname: string = typeof location !== "undefined" ? location.hostname : ""): "test" | "local" | "main" {
    if (isTestSite(hostname)) {
        return "test";
    }
    return ["localhost", "127.0.0.1", "[::1]", ""].includes(hostname.toLowerCase()) ? "local" : "main";
}

/** True on the web test site (the Static Web App's environment "develop"), the only place the clear runs. */
export function isTestSiteHere(): boolean {
    return siteKind() === "test";
}

/** Empties every store of the database, if it exists; a database that is not there is not made. */
function emptyDatabase(name: string): Promise<void> {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(name);
        // Not there yet: abort, so it is not created empty at version 1 under the store that makes it.
        request.onupgradeneeded = () => request.transaction?.abort();
        request.onerror = () => (request.error?.name === "AbortError" ? resolve() : reject(request.error));
        request.onsuccess = () => {
            const db = request.result;
            const stores = [...db.objectStoreNames];
            if (stores.length === 0) {
                db.close();
                resolve();
                return;
            }
            const transaction = db.transaction(stores, "readwrite");
            for (const store of stores) {
                transaction.objectStore(store).clear();
            }
            done(transaction).then(() => resolve(), reject).finally(() => db.close());
        };
    });
}

/** Empties the library in this browser; throws on any address but a test site's. Returns how many entries went. */
export async function clearTestLibrary(): Promise<number> {
    if (!isTestSiteHere()) {
        throw new Error("The library is cleared only on the test site.");
    }
    for (const name of LIBRARY_DATABASES) {
        await emptyDatabase(name);
    }
    const keys: string[] = [];
    for (let index = 0; index < localStorage.length; index++) {
        const key = localStorage.key(index);
        if (key !== null && isLibraryKey(key)) {
            keys.push(key);
        }
    }
    for (const key of keys) {
        localStorage.removeItem(key);
    }
    return keys.length;
}

/** Loads the page again, so nothing of the old library stays in memory. */
export function reloadPage(): void {
    location.reload();
}
