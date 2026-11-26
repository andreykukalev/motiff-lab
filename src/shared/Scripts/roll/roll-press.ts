// The roll's input router (architecture blueprint S2, the audit's W3.12 first part): what a press on the roll begins, as one
// press table. The router (roll-input.ts) reads the facts under the press (the tool, the pointer, what is under it) and
// starts the gesture this names; the order of the table is the order in which an approved gesture outranks the next (R7: a
// playhead taken while playing; R8: a new selection over one that stands). Before, the order lived in a hand-ordered chain
// of calls in PianoRoll.onPointerDown. Nothing here touches the DOM.

/** A press that moves less than this many pixels is a click, not a drag. */
export const CLICK_SLOP = 4;

/** The gesture a press on the roll begins. */
export type RollPress =
    /** Nothing: the corner left of the bar numbers. */
    | "none"
    /** The pitch names left of the notes: the row's key sounds. */
    | "keys"
    /** Select by the place of the touch (roll-place-gesture.ts): an edge, inside, outside, a tap. */
    | "place"
    /** A playhead taken by its handle, or down its line. */
    | "playhead"
    /** A finger (or a pointer that does not edit) moving the roll in touch reading. */
    | "pan"
    /** An edge of the selection dragged, by the rules before the place rule. */
    | "edge"
    /** The bar numbers' strips: Check flags, the strips' own selection, Navigate's hold. */
    | "barNumbers"
    /** The loop strip while the editor is on. */
    | "loopLane"
    /** The note editor's press. */
    | "edit"
    /** A press under the modes of version 0.5 (roll-modes.ts gestureFor). */
    | "rule"
    /** A press on a note or the grid before the modes: it sounds the note. */
    | "note";

/**
 * What the router reads under a press. The facts that take a hit test are asked (called) only when the table reaches them,
 * so a press asks no more than it needs, in the order the table reads them.
 */
export interface RollPressFacts {
    /** Left of the notes, over the pitch names. */
    readonly gutter: boolean;
    /** Over the bar numbers' strips, above the notes. */
    readonly header: boolean;
    /**
     * On a handle of the selection's edges by the place rule, while Select reads this pointer by its place: the edge wins
     * over a playhead there; elsewhere on the edge the playhead is taken first (0.8 finding 1, Andrei's answer 3).
     */
    readonly placeHandle: () => boolean;
    /** On a playhead this pointer takes (its handle, or its line), with none held already. */
    readonly playhead: () => boolean;
    /** Select reads this pointer by its place here (Select, the host's place selection on, not the editor, not a hidden lane). */
    readonly places: () => boolean;
    /** In touch reading, on the notes, a pointer that neither edits nor is a mouse: it moves the roll. */
    readonly pans: () => boolean;
    /** On an edge of the selection this pointer may drag by the earlier rules. */
    readonly edge: () => boolean;
    /** The note editor is on. */
    readonly editing: boolean;
    /** The editor is on and takes this pointer. */
    readonly edits: boolean;
    /** The modes of version 0.5 are on and the editor does not have the press. */
    readonly rules: () => boolean;
}

/** The press table: the first row whose fact holds names the gesture. */
export function rollPressFor(facts: RollPressFacts): RollPress {
    if (facts.gutter) {
        return facts.header ? "none" : "keys";
    }
    if (facts.placeHandle()) {
        return "place";
    }
    if (facts.playhead()) {
        return "playhead";
    }
    if (facts.places()) {
        return "place";
    }
    if (facts.pans()) {
        return "pan";
    }
    if (facts.edge()) {
        return "edge";
    }
    if (facts.header) {
        return facts.editing ? "loopLane" : "barNumbers";
    }
    if (facts.edits) {
        return "edit";
    }
    return facts.rules() ? "rule" : "note";
}
