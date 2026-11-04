// Previous and next phrase from the keyboard (#126): Page Up and Page Down anywhere on the page, which
// is also what Bluetooth page-turner pedals send, so a player at the piano can step with a foot. Escape
// leaves phrase focus (0018 section 4); B bookmarks the selection (section 5). Keys typed into a field, or
// pressed with a modifier, are left alone.

/** The .NET side: PlaybackLab.StepAsync, -1 for the previous phrase and 1 for the next, LeaveFocusAsync and BookmarkFromKey. */
export interface StepTarget {
    invokeMethodAsync(method: "StepAsync", direction: number): Promise<void>;
    invokeMethodAsync(method: "LeaveFocusAsync" | "BookmarkFromKey"): Promise<void>;
}

type KeyPress = Pick<KeyboardEvent, "key" | "altKey" | "ctrlKey" | "metaKey" | "shiftKey" | "target">;

/** Whether a key belongs to what has the focus: a field, or a key pressed with a modifier. */
function ownedElsewhere(event: KeyPress): boolean {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
        return true;
    }
    const target = event.target as { tagName?: string; isContentEditable?: boolean } | null;
    const tag = target?.tagName?.toUpperCase();
    return target?.isContentEditable === true || tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
}

/** Whether the key leaves phrase focus: Escape, not typed into a field. */
export function leavesFocus(event: KeyPress): boolean {
    return event.key === "Escape" && !ownedElsewhere(event);
}

/** Whether the key bookmarks the selection: B, not typed into a field. */
export function bookmarks(event: KeyPress): boolean {
    return (event.key === "b" || event.key === "B") && !ownedElsewhere(event);
}

/** -1, 1, or null when the key is not a phrase step or belongs to what has the focus. */
export function stepFor(event: KeyPress): -1 | 1 | null {
    const direction = event.key === "PageUp" ? -1 : event.key === "PageDown" ? 1 : null;
    return direction === null || ownedElsewhere(event) ? null : direction;
}

export class PhraseKeys {
    private readonly onKeyDown = (event: KeyboardEvent): void => {
        if (leavesFocus(event)) {
            void this.target.invokeMethodAsync("LeaveFocusAsync");
            return;
        }
        if (bookmarks(event)) {
            void this.target.invokeMethodAsync("BookmarkFromKey");
            return;
        }
        const direction = stepFor(event);
        if (direction === null) {
            return;
        }
        // The page would scroll otherwise.
        event.preventDefault();
        void this.target.invokeMethodAsync("StepAsync", direction);
    };

    constructor(private readonly target: StepTarget) {
        document.addEventListener("keydown", this.onKeyDown);
    }

    dispose(): void {
        document.removeEventListener("keydown", this.onKeyDown);
    }
}

export function listen(target: StepTarget): PhraseKeys {
    return new PhraseKeys(target);
}
