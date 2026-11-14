// Who selects on the score and on the roll, and how (#511 sections 2, 4.1 and 12.3): how a press of each pointer starts a
// selection, and whether it takes a selection's handle. The Pen owns the stylus: while it is on, the stylus (and on the web
// the mouse) writes everywhere, over the handles too, and a finger keeps its own moves. Pure; nothing here touches the DOM.

/** How a press may start a selection: "now" drags at once, "longPress" after a long press, "none" never. */
export type DragStart = "now" | "longPress" | "none";

/**
 * How a press starts a selection: the host's rule for its pointer where it gives one, else the rules before it: the pen
 * writes and the mouse and a finger write while the pen is on; a finger waits for the long press unless it drags at once.
 */
export function dragStartOf(pointerType: string, rule: DragStart | undefined, penOn: boolean, fingerDrags: boolean): DragStart {
    if (rule !== undefined) {
        return rule;
    }
    if (pointerType === "pen" || penOn) {
        return "none";
    }
    return pointerType === "touch" && !fingerDrags ? "longPress" : "now";
}

/**
 * Whether a press on a selection handle takes it (#511 4.1): the host's rule where it gives one, else a finger always and
 * the pen and the mouse unless the Pen is on, where they write over the handles as everywhere.
 */
export function handleTakes(pointerType: string, rule: boolean | undefined, penOn: boolean): boolean {
    return rule ?? (pointerType === "touch" || !penOn);
}
