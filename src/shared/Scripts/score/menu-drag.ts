// The selection's menu moved aside by the player (v0.6.1, #582 item 7): pressed and dragged, it goes where the pointer takes
// it, so the music under it can be read, and stays there while the selection lasts, whatever the score does under it. It
// keeps inside the window and clear of the drawers and sheets, as the menu placed over the band does (selection-menu.ts).
// Closed (the selection gone, the menu put away) it forgets the place: the next one opens over its band again. The place is
// never kept anywhere else. The rules are pure (keptInside, movedSpot, MenuPin); MenuDragger follows the web menu's pointer,
// and the iPad's Swift menu, which follows its own drag, hands the page where it was let go (SelectionLayer.moveMenuBy).

import { DRAG_DISTANCE } from "../selection/selection-gesture.js";
import { MENU_MARGIN, MENU_TOP, menuClear } from "./selection-menu.js";

/** Where the menu's top left corner is, in window pixels. */
export interface MenuSpot {
    left: number;
    top: number;
}

/** The window and the part of it the drawers and sheets leave the menu (menuClear). */
export interface MenuRoom {
    width: number;
    height: number;
    left?: number;
    right?: number;
    bottom?: number;
}

/**
 * The window as the menu has it: the drawers and sheets lie over the menu, so it keeps clear of them (data-menu-clear): to
 * the right of the Contents, to the left of a sheet on the right, above a drawer or sheet over the foot.
 */
export function menuRoom(): MenuRoom {
    const clear = menuClear([...document.querySelectorAll<HTMLElement>("[data-menu-clear]")], window.innerWidth, window.innerHeight);
    return { width: window.innerWidth, height: window.innerHeight, ...clear };
}

/** A spot for a menu of `size` kept inside the room: inside its sides, below the bar at the top and above its foot. */
export function keptInside(spot: MenuSpot, size: { width: number; height: number }, room: MenuRoom): MenuSpot {
    const start = (room.left ?? 0) + MENU_MARGIN;
    const end = (room.right ?? room.width) - MENU_MARGIN;
    const floor = (room.bottom ?? room.height) - MENU_MARGIN;
    return {
        left: Math.max(start, Math.min(end - size.width, spot.left)),
        top: Math.max(MENU_TOP, Math.min(floor - size.height, spot.top)),
    };
}

/** Where the menu taken at `from` goes once the pointer moved by (dx, dy): as far as the pointer, kept inside the room. */
export function movedSpot(from: MenuSpot, dx: number, dy: number, size: { width: number; height: number }, room: MenuRoom): MenuSpot {
    return keptInside({ left: from.left + dx, top: from.top + dy }, size, room);
}

/**
 * The menu's place set by the player, if any: taken by a press, moved once the pointer has gone DRAG_DISTANCE (a shorter
 * move is a tap on its button), and forgotten when the menu closes.
 */
export class MenuPin {
    /** Where the player put the menu; null while it stands over its band. */
    spot: MenuSpot | null = null;
    private press: { pointerId: number; x: number; y: number; from: MenuSpot; moving: boolean } | null = null;

    /** A press on the menu, standing at `from`, by the pointer at (x, y) in window pixels. */
    take(pointerId: number, x: number, y: number, from: MenuSpot): void {
        this.press = { pointerId, x, y, from, moving: false };
    }

    /** The pointer moved: the spot it takes the menu to once it is a drag; null while it is still a tap, or not this press. */
    move(pointerId: number, x: number, y: number, size: { width: number; height: number }, room: MenuRoom): MenuSpot | null {
        const press = this.press;
        if (press === null || press.pointerId !== pointerId) {
            return null;
        }
        if (!press.moving && Math.hypot(x - press.x, y - press.y) < DRAG_DISTANCE) {
            return null;
        }
        press.moving = true;
        this.spot = movedSpot(press.from, x - press.x, y - press.y, size, room);
        return this.spot;
    }

    /** Whether a press is moving the menu now. */
    moving(): boolean {
        return this.press?.moving === true;
    }

    /** The press let go or cancelled: true when it moved the menu, so its click is no tap on a button. */
    release(pointerId: number): boolean {
        const press = this.press;
        if (press === null || press.pointerId !== pointerId) {
            return false;
        }
        this.press = null;
        return press.moving;
    }

    /** The menu closed: the next one opens over its band. */
    reset(): void {
        this.spot = null;
        this.press = null;
    }
}

/**
 * A press and a drag on the menu element over the page (the web app's, which Blazor draws): the pointer is followed over
 * the whole window while the press lasts, as the drag soon leaves the menu, and not captured, which would take the click
 * from the menu's button a tap was on; the click after a drag is no tap. `moved` places the menu again.
 */
export class MenuDragger {
    readonly pin = new MenuPin();
    private menu: HTMLElement | null = null;
    private swallowClick = false;

    constructor(private readonly moved: () => void) {}

    /**
     * Places the menu where the player put it, if they did (#582 item 7): there whatever the score does under it, kept inside
     * the window, with no arrow to the band. False when it stands over its band. A menu with nothing in it (the web's, once
     * Blazor put it away) has closed: the place is forgotten.
     */
    placePinned(): boolean {
        const menu = this.menu;
        const spot = this.pin.spot;
        if (menu === null || spot === null) {
            delete menu?.dataset.pinned;
            return false;
        }
        menu.hidden = false;
        const size = menu.getBoundingClientRect();
        if (size.width === 0 && size.height === 0) {
            this.pin.reset();
            delete menu.dataset.pinned;
            return false;
        }
        const kept = keptInside(spot, size, menuRoom());
        menu.style.left = `${kept.left}px`;
        menu.style.top = `${kept.top}px`;
        menu.dataset.pinned = "true";
        return true;
    }

    /** The menu moved by (dx, dy) from where it stands now, kept inside the window; nothing while it is hidden. */
    moveBy(dx: number, dy: number): void {
        const menu = this.menu;
        if (menu === null || menu.hidden) {
            return;
        }
        const box = menu.getBoundingClientRect();
        this.pin.spot = movedSpot({ left: box.left, top: box.top }, dx, dy, box, menuRoom());
        this.moved();
    }

    /** The menu element to drag, or none; a new one opens over its band. */
    attach(menu: HTMLElement | null): void {
        this.menu?.removeEventListener("pointerdown", this.onDown);
        this.menu?.removeEventListener("click", this.onClick, true);
        this.follow(false);
        this.pin.reset();
        this.menu = menu;
        menu?.addEventListener("pointerdown", this.onDown);
        menu?.addEventListener("click", this.onClick, true);
    }

    private follow(on: boolean): void {
        if (on) {
            window.addEventListener("pointermove", this.onMove);
            window.addEventListener("pointerup", this.onUp);
            window.addEventListener("pointercancel", this.onUp);
        } else {
            window.removeEventListener("pointermove", this.onMove);
            window.removeEventListener("pointerup", this.onUp);
            window.removeEventListener("pointercancel", this.onUp);
        }
    }

    private readonly onDown = (event: PointerEvent): void => {
        const menu = this.menu;
        // Text is written and chosen in a field of a card the menu holds (a bookmark, a note): there a drag is the field's.
        const field = event.target instanceof Element && event.target.closest("input, textarea, select, [contenteditable]") !== null;
        if (menu === null || field || (event.pointerType === "mouse" && event.button !== 0)) {
            return;
        }
        const box = menu.getBoundingClientRect();
        this.pin.take(event.pointerId, event.clientX, event.clientY, { left: box.left, top: box.top });
        this.follow(true);
    };

    private readonly onMove = (event: PointerEvent): void => {
        const menu = this.menu;
        if (menu === null || this.pin.move(event.pointerId, event.clientX, event.clientY, menu.getBoundingClientRect(), menuRoom()) === null) {
            return;
        }
        event.preventDefault();
        this.moved();
    };

    private readonly onUp = (event: PointerEvent): void => {
        this.follow(false);
        if (this.pin.release(event.pointerId)) {
            this.swallowClick = true;
            window.setTimeout(() => (this.swallowClick = false), 0);
        }
    };

    private readonly onClick = (event: MouseEvent): void => {
        if (this.swallowClick) {
            this.swallowClick = false;
            event.preventDefault();
            event.stopPropagation();
        }
    };
}
