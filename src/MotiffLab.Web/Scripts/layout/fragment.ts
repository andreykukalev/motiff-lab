// A link to a place on another screen ("settings#account", "settings#scan"): Blazor's router changes
// the page but leaves the scroll at the top, so the layout asks for the named element once it has rendered.

/** Scrolls the element with this id to the top of the window; true when there was one. */
export function scrollToFragment(id: string): boolean {
    const element = document.getElementById(id);
    element?.scrollIntoView({ block: "start" });
    return element !== null;
}
