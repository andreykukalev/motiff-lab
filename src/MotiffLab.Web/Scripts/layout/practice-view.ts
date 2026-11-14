// Practice view (#533 11): one view state that keeps the page clean while the player plays, in the web's workspace and the
// iPad's score and roll alike. Entered and left by one action (the bar's «Практика», the selection menu's Hide, F on the
// web), it hides the selection's menu, the transport pill, the blue playhead of the selection, the roll's loop strip and
// the hints of the selection and of playback; the notes, the selection's band and the black playhead of the piece stay,
// and the black playhead stays to drag and a tap still moves it (Andrei, 16:08 UTC: it reads as part of the score). A tap
// on the page shows Play/Pause and «Выйти из практики» for a few seconds, as a video player does. Nothing else changes:
// playback, the loop, the selection and the position go on as they are, so leaving shows them as they are now. The
// state belongs to the view: it is not saved in the project file nor in the selection's history.

/** What the page shows: the normal workspace, practice view, or practice view with its controls shown for a while. */
export type PracticeShown = "off" | "on" | "reveal";

/** How long a tap shows the controls in practice view, as a video player's do. */
export const REVEAL_MS = 3000;

/** How far a pointer may move between down and up and still be a tap, not a drag (a selection, the playhead), in pixels. */
export const TAP_SLOP = 10;

/** The attribute on the workspace (the web's `main`, the iPad's page body) that the stylesheets and the roll read. */
export const PRACTICE_ATTRIBUTE = "data-practice";

/** Sent on the document whenever practice view begins, ends, or shows or hides its controls; the roll draws again. */
export const PRACTICE_EVENT = "motiff-practice";

/** Practice view on or off, and until when its controls show (a time in milliseconds; 0 for not shown). */
export interface PracticeState {
    on: boolean;
    revealUntil: number;
}

/** The normal workspace. */
export const NORMAL: PracticeState = { on: false, revealUntil: 0 };

/**
 * What changes the view: entering or leaving it (the bar, the menu's Hide, the controls' Leave, F or Escape), a tap on the
 * page (it shows the controls), and a touch on the shown controls (they stay a while longer).
 */
export type PracticeEvent = "enter" | "leave" | "toggle" | "tap" | "touch";

/** The view after `event` at the time `now`, in milliseconds. */
export function practiceStep(state: PracticeState, event: PracticeEvent, now: number): PracticeState {
    switch (event) {
        case "enter":
            return { on: true, revealUntil: 0 };
        case "leave":
            return NORMAL;
        case "toggle":
            return state.on ? NORMAL : { on: true, revealUntil: 0 };
        case "tap":
            return state.on ? { on: true, revealUntil: now + REVEAL_MS } : state;
        case "touch":
            return state.on && now < state.revealUntil ? { on: true, revealUntil: now + REVEAL_MS } : state;
        default:
            return state;
    }
}

/** What the page shows at `now`. */
export function practiceShown(state: PracticeState, now: number): PracticeShown {
    if (!state.on) {
        return "off";
    }
    return now < state.revealUntil ? "reveal" : "on";
}

/** How long, from `now`, until the controls hide again; 0 when they are not shown. */
export function revealLeft(state: PracticeState, now: number): number {
    return state.on ? Math.max(0, state.revealUntil - now) : 0;
}

/** Which of the elements over the music the page shows. */
export interface PracticeChrome {
    /** The selection's menu (its popup). */
    menu: boolean;
    /** The transport pill, with its popovers. */
    transport: boolean;
    /** The blue playhead of the selection, on the score and the roll. */
    selectionHead: boolean;
    /** The black playhead of the piece, on the score and the roll: shown and taken in practice view too. */
    pieceHead: boolean;
    /** The roll's loop strip under the bar numbers (the iPad's). */
    loopStrip: boolean;
    /** The hints of the selection and of playback (the selection cleared, how to hold, back to playback). */
    hints: boolean;
    /** Practice view's own Play/Pause and Leave. */
    controls: boolean;
}

/** What shows in each state; the selection's band and the notes always do. */
export function practiceChrome(shown: PracticeShown): PracticeChrome {
    const normal = shown === "off";
    return { menu: normal, transport: normal, selectionHead: normal, pieceHead: true, loopStrip: normal, hints: normal, controls: shown === "reveal" };
}

/** The playheads as drawn on the roll in this state: in practice view without the blue one of the selection. */
export function practiced<T extends { blue: number | null }>(places: T, shown: PracticeShown): T {
    return practiceChrome(shown).selectionHead || places.blue === null ? places : { ...places, blue: null };
}

/** Whether a press let go at `up` after going down at `down` is a tap: it moved no further than a tap may. */
export function isTap(down: { x: number; y: number }, up: { x: number; y: number }): boolean {
    return Math.hypot(up.x - down.x, up.y - down.y) <= TAP_SLOP;
}

/** The state an element is in: the nearest workspace's attribute, "off" outside one. */
export function shownAt(element: Element): PracticeShown {
    const value = element.closest(`[${PRACTICE_ATTRIBUTE}]`)?.getAttribute(PRACTICE_ATTRIBUTE);
    return value === "on" || value === "reveal" ? value : "off";
}

type KeyPress = Pick<KeyboardEvent, "code" | "key" | "altKey" | "ctrlKey" | "metaKey" | "shiftKey" | "target" | "defaultPrevented" | "repeat">;

/** Where the key types, ticks or belongs to a dialog: it is theirs. */
const KEY_OWNERS = "input, select, textarea, [contenteditable], [role=dialog]";

/**
 * The web's keys (by the key's place, so a Russian layout works): F enters and leaves practice view, as a video player's
 * full screen; Escape leaves it, before it would clear the selection. Null for any other key, one with a modifier, one
 * typed into a field or a dialog, or one taken already.
 */
export function practiceKey(event: KeyPress, on: boolean): "toggle" | "leave" | null {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.defaultPrevented || event.repeat) {
        return null;
    }
    const target = event.target as { closest?: (selector: string) => unknown } | null;
    if (typeof target?.closest === "function" && target.closest(KEY_OWNERS) !== null) {
        return null;
    }
    if (event.code === "KeyF") {
        return "toggle";
    }
    return on && event.key === "Escape" ? "leave" : null;
}

/** The .NET side: PlaybackLab.OnPracticeKeyAsync, with whether the key leaves (Escape) or enters and leaves (F). */
export interface PracticeTarget {
    invokeMethodAsync(method: "OnPracticeKeyAsync", leave: boolean): Promise<void>;
}

/** Where a tap shows the controls: on the score. The controls themselves keep them up a while longer. */
const PAGE = ".score";
const CONTROLS = ".practice-controls";

/**
 * Practice view on the web's workspace: the attribute the stylesheet and the roll read, the time the controls show for,
 * the taps on the score that show them (the tap still does what it does, a playhead placed, a sheet put away) and the keys.
 */
export class PracticeView {
    private state: PracticeState = NORMAL;
    private timer = 0;
    private down: { pointer: number; x: number; y: number } | null = null;

    constructor(
        private readonly workspace: HTMLElement,
        private readonly target: PracticeTarget | null,
    ) {
        workspace.addEventListener("pointerdown", this.onDown, true);
        workspace.addEventListener("pointerup", this.onUp, true);
        // Before the keys of the modes, so Escape leaves practice view rather than clearing the selection.
        window.addEventListener("keydown", this.onKey, true);
        this.show();
    }

    /** Practice view on or off, as the page decided (its button, the menu's Hide, the controls' Leave, a key). */
    set(on: boolean): void {
        if (on !== this.state.on) {
            this.step(on ? "enter" : "leave");
        }
    }

    dispose(): void {
        clearTimeout(this.timer);
        this.workspace.removeEventListener("pointerdown", this.onDown, true);
        this.workspace.removeEventListener("pointerup", this.onUp, true);
        window.removeEventListener("keydown", this.onKey, true);
        this.workspace.removeAttribute(PRACTICE_ATTRIBUTE);
        document.dispatchEvent(new Event(PRACTICE_EVENT));
    }

    private step(event: PracticeEvent): void {
        const now = performance.now();
        this.state = practiceStep(this.state, event, now);
        clearTimeout(this.timer);
        const left = revealLeft(this.state, now);
        if (left > 0) {
            this.timer = window.setTimeout(() => this.show(), left);
        }
        this.show();
    }

    private show(): void {
        const shown = practiceShown(this.state, performance.now());
        if (this.workspace.getAttribute(PRACTICE_ATTRIBUTE) !== shown) {
            this.workspace.setAttribute(PRACTICE_ATTRIBUTE, shown);
            document.dispatchEvent(new Event(PRACTICE_EVENT));
        }
    }

    private readonly onDown = (event: PointerEvent): void => {
        if (!this.state.on) {
            return;
        }
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest(CONTROLS) != null) {
            this.step("touch");
            return;
        }
        this.down = target?.closest(PAGE) != null ? { pointer: event.pointerId, x: event.clientX, y: event.clientY } : null;
    };

    private readonly onUp = (event: PointerEvent): void => {
        const down = this.down;
        this.down = null;
        if (down !== null && down.pointer === event.pointerId && isTap(down, { x: event.clientX, y: event.clientY })) {
            this.step("tap");
        }
    };

    private readonly onKey = (event: KeyboardEvent): void => {
        const key = practiceKey(event, this.state.on);
        if (key === null || this.target === null) {
            return;
        }
        event.preventDefault();
        void this.target.invokeMethodAsync("OnPracticeKeyAsync", key === "leave");
    };
}

export function attach(workspace: HTMLElement, target: PracticeTarget | null): PracticeView {
    return new PracticeView(workspace, target);
}
