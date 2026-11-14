// Where the selection's contextual menu goes (decision record 0018, section 3; design/kit/ds.css .menu): above the band's
// bracket number, or under the band when there is no room, kept clear of the drawers and sheets over the score.

import { BRACKET_LIFT } from "./score-geometry.js";

/** The menu sits this far above the band's bracket number (the kit: 14 px above the selection, clear of bracket numbers). */
const MENU_GAP = 14;

/** The bracket's number stands this tall above the bracket line. */
export const NUMBER_HEIGHT = 16;

/** The menu stays this far inside the window's sides, and below the bar at the top. */
const MENU_MARGIN = 16;
const MENU_TOP = 72;

/**
 * The part of the window the drawers and sheets leave the menu: data-menu-clear="left" keeps it right of the element; any
 * other value ("auto") keeps it left of an element on the right half of the window or above one over its foot.
 */
export function menuClear(elements: readonly HTMLElement[], width: number, height: number): { left: number; right: number; bottom: number } {
    let left = 0;
    let right = width;
    let bottom = height;
    for (const element of elements) {
        const box = element.getBoundingClientRect();
        if (box.width === 0 || box.height === 0) {
            continue;
        }
        if (element.dataset.menuClear === "left") {
            left = Math.max(left, box.right);
        } else if (box.left >= width / 2) {
            right = Math.min(right, box.left);
        } else if (box.top >= height / 3) {
            bottom = Math.min(bottom, box.top);
        }
    }
    return { left, right, bottom };
}

/**
 * Where the menu goes for a band (all in window pixels): centred over the band's first run, above its
 * bracket number, or under the band's last run when there is no room above; kept inside the window, right of
 * `window.left`, left of `window.right` and above `window.bottom` where drawers and sheets cover the window.
 * `pointer` is where its little arrow sits across the menu.
 */
export function menuPlace(
    first: { left: number; right: number; top: number },
    last: { bottom: number },
    menu: { width: number; height: number },
    window: { width: number; height: number; left?: number; right?: number; bottom?: number },
): { left: number; top: number; below: boolean; pointer: number } {
    const centre = (first.left + first.right) / 2;
    const start = (window.left ?? 0) + MENU_MARGIN;
    const end = (window.right ?? window.width) - MENU_MARGIN;
    const left = Math.max(start, Math.min(end - menu.width, centre - menu.width / 2));
    const above = first.top - BRACKET_LIFT - NUMBER_HEIGHT - MENU_GAP - menu.height;
    const below = above < MENU_TOP;
    // Under the band, the menu (or the card it holds) stays above a drawer over the foot of the window.
    const floor = (window.bottom ?? window.height) - MENU_MARGIN;
    const top = below ? Math.max(MENU_TOP, Math.min(last.bottom + MENU_GAP, floor - menu.height)) : above;
    const pointer = Math.max(20, Math.min(menu.width - 20, centre - left));
    return { left, top, below, pointer };
}
