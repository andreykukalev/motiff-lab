// The piano roll drawer's height (decision record 0018, section 4): its handle drags it taller, up to about
// 70% of the screen and never to full screen, or shorter; dragged nearly away it closes, and a tap on the
// handle puts it away. The arrow keys on the handle do the same in steps. The height is remembered on the
// device and given to the workspace as --drawer-h, which the drawer, the pill and the focus card follow.

/** The tallest and shortest the drawer goes, as shares of the screen's height. */
export const MAX_SHARE = 0.7;
export const MIN_SHARE = 0.2;

/** Dragged below this share, the drawer goes away. */
export const CLOSE_SHARE = 0.12;

/** A press that moves less than this is a tap. */
export const TAP_DISTANCE = 6;

/** What an arrow key moves the drawer by. */
export const KEY_STEP = 0.05;

const STORAGE_KEY = "ml-roll-height";

/** The drawer's share of the screen after a drag of dy pixels (down is positive); null when it should close. */
export function shareAfter(start: number, dy: number, viewport: number): number | null {
    const share = start - dy / Math.max(1, viewport);
    if (share < CLOSE_SHARE) {
        return null;
    }
    return Math.min(MAX_SHARE, Math.max(MIN_SHARE, share));
}

interface DrawerTarget {
    invokeMethodAsync(method: "OnDrawerClosed"): Promise<void>;
}

export class DrawerResize {
    private drag: { id: number; y: number; start: number; moved: boolean; grab: HTMLElement } | null = null;

    constructor(
        private readonly workspace: HTMLElement,
        private readonly target: DrawerTarget,
    ) {
        const saved = read();
        if (saved !== null) {
            this.apply(saved);
        }
        workspace.addEventListener("pointerdown", this.onDown);
        workspace.addEventListener("keydown", this.onKey);
    }

    dispose(): void {
        this.workspace.removeEventListener("pointerdown", this.onDown);
        this.workspace.removeEventListener("keydown", this.onKey);
        this.end();
    }

    private grabOf(target: EventTarget | null): HTMLElement | null {
        return target instanceof Element ? target.closest<HTMLElement>(".drawer > .grab") : null;
    }

    private current(grab: HTMLElement): number {
        const drawer = grab.parentElement;
        return drawer === null ? 0.4 : drawer.getBoundingClientRect().height / window.innerHeight;
    }

    private apply(share: number): void {
        this.workspace.style.setProperty("--drawer-h", `${Math.round(share * 1000) / 10}vh`);
    }

    private readonly onDown = (event: PointerEvent): void => {
        const grab = this.grabOf(event.target);
        if (grab === null || !event.isPrimary) {
            return;
        }
        this.drag = { id: event.pointerId, y: event.clientY, start: this.current(grab), moved: false, grab };
        grab.setPointerCapture(event.pointerId);
        grab.addEventListener("pointermove", this.onMove);
        grab.addEventListener("pointerup", this.onUp);
        grab.addEventListener("pointercancel", this.onCancel);
    };

    private readonly onMove = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.id) {
            return;
        }
        const dy = event.clientY - drag.y;
        drag.moved ||= Math.abs(dy) >= TAP_DISTANCE;
        if (drag.moved) {
            const share = shareAfter(drag.start, dy, window.innerHeight);
            this.apply(share ?? MIN_SHARE);
        }
    };

    private readonly onUp = (event: PointerEvent): void => {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.id) {
            return;
        }
        this.end();
        if (!drag.moved) {
            // A tap: the handle's own click puts the drawer away.
            return;
        }
        // A drag is not a tap: the click that follows it must not close the drawer.
        window.addEventListener("click", swallow, { capture: true, once: true });
        window.setTimeout(() => window.removeEventListener("click", swallow, { capture: true }), 0);
        const share = shareAfter(drag.start, event.clientY - drag.y, window.innerHeight);
        if (share === null) {
            this.apply(drag.start);
            void this.target.invokeMethodAsync("OnDrawerClosed");
            return;
        }
        this.apply(share);
        write(share);
    };

    private readonly onCancel = (): void => {
        const drag = this.drag;
        this.end();
        if (drag !== null) {
            this.apply(drag.start);
        }
    };

    private end(): void {
        const drag = this.drag;
        this.drag = null;
        if (drag !== null) {
            drag.grab.removeEventListener("pointermove", this.onMove);
            drag.grab.removeEventListener("pointerup", this.onUp);
            drag.grab.removeEventListener("pointercancel", this.onCancel);
        }
    }

    private readonly onKey = (event: KeyboardEvent): void => {
        const grab = this.grabOf(event.target);
        const step = event.key === "ArrowUp" ? KEY_STEP : event.key === "ArrowDown" ? -KEY_STEP : 0;
        if (grab === null || step === 0) {
            return;
        }
        event.preventDefault();
        const share = Math.min(MAX_SHARE, Math.max(MIN_SHARE, this.current(grab) + step));
        this.apply(share);
        write(share);
    };
}

function swallow(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
}

function read(): number | null {
    try {
        const value = Number(localStorage.getItem(STORAGE_KEY));
        return value >= MIN_SHARE && value <= MAX_SHARE ? value : null;
    } catch {
        return null;
    }
}

function write(share: number): void {
    try {
        localStorage.setItem(STORAGE_KEY, String(Math.round(share * 1000) / 1000));
    } catch {
        // Without storage the height lasts while the page is open.
    }
}

/** Entry point for Blazor. */
export function attach(workspace: HTMLElement, target: DrawerTarget): DrawerResize {
    return new DrawerResize(workspace, target);
}
