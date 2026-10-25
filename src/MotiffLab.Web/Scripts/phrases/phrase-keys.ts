// Previous and next phrase from the keyboard (#126): Page Up and Page Down anywhere on the page, which
// is also what Bluetooth page-turner pedals send, so a player at the piano can step with a foot.
// Keys typed into a field, or pressed with a modifier, are left alone.

/** The .NET side: PhraseStrip.StepAsync, -1 for the previous phrase and 1 for the next. */
export interface StepTarget {
    invokeMethodAsync(method: "StepAsync", direction: number): Promise<void>;
}

/** -1, 1, or null when the key is not a phrase step or belongs to what has the focus. */
export function stepFor(event: Pick<KeyboardEvent, "key" | "altKey" | "ctrlKey" | "metaKey" | "shiftKey" | "target">): -1 | 1 | null {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
        return null;
    }
    const direction = event.key === "PageUp" ? -1 : event.key === "PageDown" ? 1 : null;
    if (direction === null) {
        return null;
    }
    const target = event.target as { tagName?: string; isContentEditable?: boolean } | null;
    const tag = target?.tagName?.toUpperCase();
    if (target?.isContentEditable || tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") {
        return null;
    }
    return direction;
}

export class PhraseKeys {
    private readonly onKeyDown = (event: KeyboardEvent): void => {
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
