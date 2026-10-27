// Preferences kept on the device in local storage (0012): the interface language, how the workspace
// is laid out (Score, Roll or Both, the divider) and whether Focus mode was on. None of it is part of
// a project, and nothing here leaves the device.

const PREFIX = "motiff-lab:";

/** The stored value, or null when there is none or storage is blocked. */
export function getPreference(key: string): string | null {
    try {
        return localStorage.getItem(PREFIX + key);
    } catch {
        return null;
    }
}

/** Stores a value; null forgets it. Storage that is blocked is left alone. */
export function setPreference(key: string, value: string | null): void {
    try {
        if (value === null) {
            localStorage.removeItem(PREFIX + key);
        } else {
            localStorage.setItem(PREFIX + key, value);
        }
    } catch {
        // Private browsing or blocked storage: the preference lasts for this visit only.
    }
}

/** Marks the document with the interface language, so assistive technology and hyphenation follow it. */
export function applyLanguage(tag: string): void {
    document.documentElement.lang = tag;
}

/** The share of the canvas the score takes in Both mode, kept between 0.2 and 0.8; NaN or a blank is the default. */
export function clampSplit(value: string | number | null | undefined, fallback = 0.6): number {
    const number = typeof value === "number" ? value : Number(value);
    if (value === null || value === undefined || value === "" || Number.isNaN(number)) {
        return fallback;
    }
    return Math.min(0.8, Math.max(0.2, number));
}
