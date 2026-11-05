// Contextual settings (decision record 0018, section 10): a long press on an element of the score page (the transport
// pill, an action of the bar, the piano roll's handle) opens a small popover with that element's two or three
// switches. Elements say which popover they open with data-long-press="name". A press held still for HOLD_MS opens it,
// and the click that follows the release does nothing else; a right click or the system's long-press menu
// (contextmenu) opens it at once.

/** How long a press is held before it counts as a long press. */
export const HOLD_MS = 550;

/** A press that moves further than this is a drag or a scroll, not a long press. */
export const SLOP = 8;

/** Whether a press that moved by dx, dy is still held in place. */
export function heldStill(dx: number, dy: number): boolean {
    return Math.hypot(dx, dy) <= SLOP;
}

interface LongPressTarget {
    invokeMethodAsync(method: "OnLongPress", name: string): Promise<void>;
}

export class LongPress {
    private press: { id: number; x: number; y: number; timer: number } | null = null;

    /** When a long press last fired, so a contextmenu for the same press is ignored. */
    private firedAt = 0;

    /** Whether a held press fired, so the click its release makes is not also a tap. A right click makes no such click. */
    private swallowClick = false;

    constructor(
        private readonly root: HTMLElement,
        private readonly target: LongPressTarget,
    ) {
        root.addEventListener("pointerdown", this.onDown);
        root.addEventListener("pointermove", this.onMove);
        root.addEventListener("pointerup", this.cancel);
        root.addEventListener("pointercancel", this.cancel);
        root.addEventListener("contextmenu", this.onContextMenu);
        root.addEventListener("click", this.onClick, true);
    }

    dispose(): void {
        this.cancel();
        this.root.removeEventListener("pointerdown", this.onDown);
        this.root.removeEventListener("pointermove", this.onMove);
        this.root.removeEventListener("pointerup", this.cancel);
        this.root.removeEventListener("pointercancel", this.cancel);
        this.root.removeEventListener("contextmenu", this.onContextMenu);
        this.root.removeEventListener("click", this.onClick, true);
    }

    private nameOf(target: EventTarget | null): string | null {
        const element = target instanceof Element ? target.closest<HTMLElement>("[data-long-press]") : null;
        return element && this.root.contains(element) ? element.dataset.longPress ?? null : null;
    }

    private readonly onDown = (event: PointerEvent): void => {
        const name = this.nameOf(event.target);
        if (name === null || !event.isPrimary || event.button !== 0) {
            return;
        }
        this.cancel();
        const timer = window.setTimeout(() => {
            this.press = null;
            this.swallowClick = true;
            this.fire(name);
        }, HOLD_MS);
        this.press = { id: event.pointerId, x: event.clientX, y: event.clientY, timer };
    };

    private readonly onMove = (event: PointerEvent): void => {
        if (this.press && event.pointerId === this.press.id && !heldStill(event.clientX - this.press.x, event.clientY - this.press.y)) {
            this.cancel();
        }
    };

    private readonly cancel = (): void => {
        if (this.press) {
            window.clearTimeout(this.press.timer);
            this.press = null;
        }
    };

    private readonly onContextMenu = (event: MouseEvent): void => {
        const name = this.nameOf(event.target);
        if (name === null) {
            return;
        }
        event.preventDefault();
        this.cancel();
        if (!this.recentlyFired()) {
            this.fire(name);
        }
    };

    /** The release after a long press is not also a tap on the element. */
    private readonly onClick = (event: MouseEvent): void => {
        if (!this.swallowClick) {
            return;
        }
        this.swallowClick = false;
        if (this.recentlyFired() && this.nameOf(event.target) !== null) {
            event.preventDefault();
            event.stopPropagation();
        }
    };

    private recentlyFired(): boolean {
        return performance.now() - this.firedAt < 1000;
    }

    private fire(name: string): void {
        this.firedAt = performance.now();
        void this.target.invokeMethodAsync("OnLongPress", name);
    }
}

/** Entry point for Blazor. */
export function attach(root: HTMLElement, target: LongPressTarget): LongPress {
    return new LongPress(root, target);
}
