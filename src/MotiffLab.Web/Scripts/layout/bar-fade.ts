// The bar's fade (decision record 0018, section 2; design/kit/ds.css .bar): the bar over the score fades
// about two seconds after the last touch, key or mouse move and comes back with the next one. It stays
// while the pointer is over it, while it holds the keyboard focus, and while one of its popovers is open.
// The bar stays where it is while faded, so a tap on it still lands.

/** How long the bar stays after the last activity (the setting "guidance.barFade" is 2 seconds by default). */
export const FADE_AFTER_MS = 2000;

/** The class the stylesheet fades the bar with. */
export const FADED = "faded";

/** Whether the bar should stay: the pointer is over it, it has the focus, or the workspace has a popover open. */
export function holds(state: { hovered: boolean; focused: boolean; popoverOpen: boolean }): boolean {
    return state.hovered || state.focused || state.popoverOpen;
}

export class BarFade {
    private timer = 0;
    private hovered = false;

    constructor(
        private readonly workspace: HTMLElement,
        private readonly bar: HTMLElement,
        private delay = FADE_AFTER_MS,
    ) {
        for (const type of ["pointerdown", "pointermove", "keydown", "wheel", "touchstart"]) {
            document.addEventListener(type, this.wake, { passive: true });
        }
        bar.addEventListener("pointerenter", this.enter);
        bar.addEventListener("pointerleave", this.leave);
        this.wake();
    }

    /** The fade after another delay (the setting "guidance.barFade": 1 or 2 seconds), counted from now. */
    setDelay(milliseconds: number): void {
        if (Number.isFinite(milliseconds) && milliseconds > 0 && milliseconds !== this.delay) {
            this.delay = milliseconds;
            this.wake();
        }
    }

    dispose(): void {
        clearTimeout(this.timer);
        for (const type of ["pointerdown", "pointermove", "keydown", "wheel", "touchstart"]) {
            document.removeEventListener(type, this.wake);
        }
        this.bar.removeEventListener("pointerenter", this.enter);
        this.bar.removeEventListener("pointerleave", this.leave);
        this.bar.classList.remove(FADED);
    }

    private readonly wake = (): void => {
        this.bar.classList.remove(FADED);
        clearTimeout(this.timer);
        this.timer = window.setTimeout(this.fade, this.delay);
    };

    private readonly fade = (): void => {
        const state = {
            hovered: this.hovered,
            focused: keyboardFocusIn(this.bar),
            popoverOpen: this.workspace.classList.contains("popover-open"),
        };
        if (holds(state)) {
            this.timer = window.setTimeout(this.fade, this.delay);
            return;
        }
        this.bar.classList.add(FADED);
    };

    private readonly enter = (): void => {
        this.hovered = true;
        this.wake();
    };

    private readonly leave = (): void => {
        this.hovered = false;
    };
}

/** Whether the keyboard focus is in the element: a button a mouse clicked keeps the focus, but not a visible one. */
function keyboardFocusIn(element: HTMLElement): boolean {
    const active = document.activeElement;
    if (active === null || !element.contains(active)) {
        return false;
    }
    try {
        return active.matches(":focus-visible");
    } catch {
        return true;
    }
}

/** Entry point for Blazor. */
export function attach(workspace: HTMLElement, bar: HTMLElement, delay = FADE_AFTER_MS): BarFade {
    return new BarFade(workspace, bar, delay);
}
