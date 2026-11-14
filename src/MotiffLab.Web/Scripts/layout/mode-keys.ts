// The keys of the modes (the experiment Two playheads and modes, Settings · Experimental; product spec interaction-modes):
// H Navigate, S Select, E Edit notes and P Pen, by the key's place on the keyboard, so a Russian layout works as well;
// Escape removes the selection; Space plays the selection when there is one, else the piece, as the roll's Space does (the
// roll takes it first while it is open). Keys typed into a field or pressed with a modifier are left alone, and nothing is
// taken while the experiment is off.

import type { InteractionMode } from "../score/score-modes.js";

/** The .NET side: PlaybackLab.OnModeKeyAsync, OnEscapeKeyAsync and OnSpaceKeyAsync. */
export interface ModeTarget {
    invokeMethodAsync(method: "OnModeKeyAsync", mode: InteractionMode): Promise<void>;
    invokeMethodAsync(method: "OnEscapeKeyAsync" | "OnSpaceKeyAsync"): Promise<void>;
    invokeMethodAsync(method: "OnUndoKeyAsync", redo: boolean): Promise<void>;
}

type KeyPress = Pick<KeyboardEvent, "code" | "key" | "altKey" | "ctrlKey" | "metaKey" | "shiftKey" | "target" | "defaultPrevented">;

const BY_CODE: Readonly<Record<string, InteractionMode>> = { KeyH: "navigate", KeyS: "select", KeyE: "edit", KeyP: "pen" };

/** What types or ticks with a key, and what Escape closes by itself: the key is theirs. */
const KEY_OWNERS = "input, select, textarea, [contenteditable], [role=dialog]";

/** Whether the key belongs to what has the focus: a field or a dialog, or a key pressed with a modifier. */
function ownedElsewhere(event: KeyPress): boolean {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.defaultPrevented) {
        return true;
    }
    const target = event.target as { closest?: (selector: string) => unknown } | null;
    return typeof target?.closest === "function" && target.closest(KEY_OWNERS) !== null;
}

/** The mode a key chooses, or null. */
export function modeFor(event: KeyPress): InteractionMode | null {
    return ownedElsewhere(event) ? null : (BY_CODE[event.code] ?? null);
}

/** Whether the key removes the selection: Escape, outside a field or a dialog. */
export function removesSelection(event: KeyPress): boolean {
    return event.key === "Escape" && !ownedElsewhere(event);
}

/** Where Space types or ticks, or a dialog has it: the key is theirs. A button or a slider leaves it to Play, as on the roll. */
const SPACE_OWNERS = "input:not([type=range]), select, textarea, [contenteditable], [role=dialog]";

/** Whether the key plays or pauses: Space, outside a field or a dialog, with no modifier but Shift, and not taken already. */
export function playsOrPauses(event: KeyPress): boolean {
    if (event.key !== " " || event.altKey || event.ctrlKey || event.metaKey || event.defaultPrevented) {
        return false;
    }
    const target = event.target as { closest?: (selector: string) => unknown } | null;
    return typeof target?.closest !== "function" || target.closest(SPACE_OWNERS) === null;
}

/**
 * The selection's Undo by keyboard (#511 11): ⌘Z or Ctrl+Z, and Redo by ⇧⌘Z, Ctrl+Shift+Z or Ctrl+Y, by the key's place so
 * a Russian layout works; outside a field or a dialog, whose Undo is its own, and not taken already. Null for another key.
 */
export function undoFor(event: KeyPress): "undo" | "redo" | null {
    if (!(event.metaKey || event.ctrlKey) || event.altKey || event.defaultPrevented) {
        return null;
    }
    const target = event.target as { closest?: (selector: string) => unknown } | null;
    if (typeof target?.closest === "function" && target.closest(KEY_OWNERS) !== null) {
        return null;
    }
    if (event.code === "KeyZ") {
        return event.shiftKey ? "redo" : "undo";
    }
    return event.code === "KeyY" && event.ctrlKey && !event.shiftKey ? "redo" : null;
}

export class ModeKeys {
    private on = false;

    private readonly onKeyDown = (event: KeyboardEvent): void => {
        const undo = undoFor(event);
        if (undo !== null) {
            // Undo works with the experiment off too: the page decides whose it is (the assembly's, the selection's).
            event.preventDefault();
            void this.target.invokeMethodAsync("OnUndoKeyAsync", undo === "redo");
            return;
        }
        if (!this.on || event.repeat) {
            return;
        }
        const mode = modeFor(event);
        if (mode !== null) {
            event.preventDefault();
            void this.target.invokeMethodAsync("OnModeKeyAsync", mode);
        } else if (removesSelection(event)) {
            void this.target.invokeMethodAsync("OnEscapeKeyAsync");
        } else if (playsOrPauses(event)) {
            event.preventDefault();
            void this.target.invokeMethodAsync("OnSpaceKeyAsync");
        }
    };

    constructor(private readonly target: ModeTarget) {
        // On the window, so a key the roll or a field took first (and prevented) is seen as taken.
        window.addEventListener("keydown", this.onKeyDown);
    }

    /** The keys work only while the experiment is on. */
    setOn(on: boolean): void {
        this.on = on;
    }

    dispose(): void {
        window.removeEventListener("keydown", this.onKeyDown);
    }
}

export function listen(target: ModeTarget): ModeKeys {
    return new ModeKeys(target);
}
