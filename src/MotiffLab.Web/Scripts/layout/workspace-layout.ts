// The workspace's layout (0012, batch 2): on a wide screen, and in Focus mode at any width, the canvas
// fills the window between the header and the transport (data-fill, which the roll reads too), the
// divider between the score and the roll can be dragged or moved by keyboard, and Focus mode can ask
// the browser for the whole screen. Blazor owns the choices (view mode, Focus); this only measures,
// drags and tells Blazor where the divider settled.

import { clampSplit } from "../storage/preferences.js";

interface DotNetObject {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** From this width the inspector sits beside the canvas and the canvas fills the window (css: .app-body). */
export const WIDE = "(min-width: 64rem)";

/** How far the divider moves for one arrow key press, as a share of the canvas. */
export const KEY_STEP = 0.05;

/** The share the score takes when the pointer is at y within a canvas from top of height. */
export function splitFrom(y: number, top: number, height: number): number {
    return height <= 0 ? clampSplit(null) : clampSplit((y - top) / height);
}

/** The share after an arrow key: up or left gives the score less, down or right more; other keys leave it. */
export function splitAfterKey(key: string, share: number): number | null {
    switch (key) {
        case "ArrowUp":
        case "ArrowLeft":
            return clampSplit(share - KEY_STEP);
        case "ArrowDown":
        case "ArrowRight":
            return clampSplit(share + KEY_STEP);
        case "Home":
            return clampSplit(0);
        case "End":
            return clampSplit(1);
        default:
            return null;
    }
}

export class WorkspaceLayout {
    private readonly observer: ResizeObserver;
    private readonly wide: MediaQueryList;
    private share: number;
    private drag: number | null = null;

    constructor(
        private readonly main: HTMLElement,
        private readonly canvas: HTMLElement,
        private readonly handle: HTMLElement | null,
        private readonly dotnet: DotNetObject,
        share: number,
    ) {
        this.share = clampSplit(share);
        this.apply();
        // The header and the transport wrap on narrow screens, so their heights are measured, not assumed.
        this.observer = new ResizeObserver(() => this.measure());
        for (const part of main.querySelectorAll<HTMLElement>(".app-header, .focus-bar, .transport-bar")) {
            this.observer.observe(part);
        }
        this.wide = window.matchMedia(WIDE);
        this.wide.addEventListener("change", this.onWidthChange);
        this.measure();
        if (handle !== null) {
            handle.addEventListener("pointerdown", this.onPointerDown);
            handle.addEventListener("pointermove", this.onPointerMove);
            handle.addEventListener("pointerup", this.onPointerUp);
            handle.addEventListener("pointercancel", this.onPointerUp);
            handle.addEventListener("keydown", this.onKeyDown);
        }
    }

    /** Moves the divider to a share of the canvas, from a saved preference or a reset. */
    setSplit(share: number): void {
        this.share = clampSplit(share);
        this.apply();
    }

    dispose(): void {
        this.observer.disconnect();
        this.wide.removeEventListener("change", this.onWidthChange);
        if (this.handle !== null) {
            this.handle.removeEventListener("pointerdown", this.onPointerDown);
            this.handle.removeEventListener("pointermove", this.onPointerMove);
            this.handle.removeEventListener("pointerup", this.onPointerUp);
            this.handle.removeEventListener("pointercancel", this.onPointerUp);
            this.handle.removeEventListener("keydown", this.onKeyDown);
        }
        this.main.style.removeProperty("--ml-chrome");
        delete this.canvas.dataset.fill;
    }

    private measure(): void {
        this.canvas.dataset.fill = String(this.wide.matches || this.main.classList.contains("focus"));
        let chrome = 0;
        for (const part of this.main.querySelectorAll<HTMLElement>(".app-header, .focus-bar, .transport-bar")) {
            chrome += part.getBoundingClientRect().height;
        }
        this.main.style.setProperty("--ml-chrome", `${Math.round(chrome)}px`);
    }

    private apply(): void {
        this.main.style.setProperty("--ml-split", String(this.share));
        this.handle?.setAttribute("aria-valuenow", String(Math.round(this.share * 100)));
    }

    private readonly onWidthChange = (): void => this.measure();

    private readonly onPointerDown = (event: PointerEvent): void => {
        if (event.button !== 0) {
            return;
        }
        this.drag = event.pointerId;
        this.handle?.setPointerCapture(event.pointerId);
        this.handle?.classList.add("dragging");
        event.preventDefault();
    };

    private readonly onPointerMove = (event: PointerEvent): void => {
        if (this.drag !== event.pointerId) {
            return;
        }
        const box = this.canvas.getBoundingClientRect();
        this.share = splitFrom(event.clientY, box.top, box.height);
        this.apply();
    };

    private readonly onPointerUp = (event: PointerEvent): void => {
        if (this.drag !== event.pointerId) {
            return;
        }
        this.drag = null;
        this.handle?.classList.remove("dragging");
        void this.dotnet.invokeMethodAsync("OnSplitChanged", this.share);
    };

    private readonly onKeyDown = (event: KeyboardEvent): void => {
        const next = splitAfterKey(event.key, this.share);
        if (next === null) {
            return;
        }
        event.preventDefault();
        this.share = next;
        this.apply();
        void this.dotnet.invokeMethodAsync("OnSplitChanged", this.share);
    };
}

export function attach(main: HTMLElement, canvas: HTMLElement, handle: HTMLElement | null, dotnet: DotNetObject, share: number): WorkspaceLayout {
    return new WorkspaceLayout(main, canvas, handle, dotnet, share);
}

/** Asks the browser for the whole screen; false when it will not (an iPad in Safari, for one). */
export async function enterFullscreen(): Promise<boolean> {
    try {
        await document.documentElement.requestFullscreen?.();
        return document.fullscreenElement !== null;
    } catch {
        return false;
    }
}

export async function exitFullscreen(): Promise<void> {
    try {
        if (document.fullscreenElement !== null) {
            await document.exitFullscreen();
        }
    } catch {
        // Already left, or never there.
    }
}
