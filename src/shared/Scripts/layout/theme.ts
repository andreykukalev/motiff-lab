// The theme (0018 section 13, design/kit/tokens.css): Daylight, Night stand, or Night stand with the Slate page, or Auto,
// which follows the device's light or dark appearance. The choice is the setting appearance.theme, kept on the device.
// tokens.css holds the colours: Daylight on :root, Night stand on [data-theme="dark"], the Slate page on [data-page="slate"].
// index.html applies the stored choice before the app starts, so the first frame is already in the right colours.

import { getPreference, setPreference } from "../storage/preferences.js";

/** Sent on the document after the theme changes, for what draws its own colours (the piano roll's canvas). */
export const THEME_CHANGED = "motiff:theme";

/** The preference the choice is kept under: the id of the setting in music-core's SettingsCatalogue. */
export const THEME_PREFERENCE = "appearance.theme";

export type ThemeChoice = "auto" | "daylight" | "night" | "slate";

const CHOICES: readonly ThemeChoice[] = ["auto", "daylight", "night", "slate"];

/** The choice as read from storage: an unknown or missing value is Auto. */
export function readChoice(value: string | null | undefined): ThemeChoice {
    return CHOICES.includes(value as ThemeChoice) ? (value as ThemeChoice) : "auto";
}

/** The document's attributes for a choice: data-theme "dark" for Night stand, data-page "slate" for the Slate page. */
export function attributesFor(choice: ThemeChoice, deviceIsDark: boolean): { theme: "light" | "dark"; page: "slate" | null } {
    const dark = choice === "night" || choice === "slate" || (choice === "auto" && deviceIsDark);
    return { theme: dark ? "dark" : "light", page: choice === "slate" ? "slate" : null };
}

let current: ThemeChoice = "auto";
let watching: MediaQueryList | null = null;

function deviceIsDark(): boolean {
    return typeof matchMedia === "function" && matchMedia("(prefers-color-scheme: dark)").matches;
}

function paint(): void {
    const { theme, page } = attributesFor(current, deviceIsDark());
    const root = document.documentElement;
    const changed = root.dataset.theme !== theme || (root.dataset.page ?? null) !== page;
    root.dataset.theme = theme;
    if (page === null) {
        delete root.dataset.page;
    } else {
        root.dataset.page = page;
    }
    // The browser's own bars take the desk's colour.
    const desk = getComputedStyle(root).getPropertyValue("--desk").trim();
    const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
    if (meta !== null && desk !== "") {
        meta.content = desk;
    }
    if (changed) {
        document.dispatchEvent(new CustomEvent(THEME_CHANGED, { detail: { theme, page } }));
    }
}

const onDeviceChange = (): void => paint();

/** Applies a choice and keeps it; with Auto, the theme follows the device from then on. Returns the choice applied. */
export function applyTheme(choice: string | null | undefined, keep = true): ThemeChoice {
    current = readChoice(choice);
    if (keep) {
        setPreference(THEME_PREFERENCE, current === "auto" ? null : current);
    }
    if (watching === null && typeof matchMedia === "function") {
        watching = matchMedia("(prefers-color-scheme: dark)");
        watching.addEventListener("change", onDeviceChange);
    }
    paint();
    return current;
}

/** The stored choice (Auto when none), applied: the app calls this once it has started. */
export function applyStoredTheme(): ThemeChoice {
    return applyTheme(getPreference(THEME_PREFERENCE), false);
}

/** The choice in use. */
export function themeChoice(): ThemeChoice {
    return current;
}
