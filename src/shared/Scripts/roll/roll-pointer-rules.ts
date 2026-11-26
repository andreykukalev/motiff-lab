// Who may press what on the roll (the input router's, roll-input.ts; the audit's W3.12): the iPad's pointer rules (which
// pointers edit, how many fingers move the roll, whether the stylus selects), the place rule's switch, and what they and
// the bar's tool let a pointer do (roll-modes.ts selectsOnRoll, navigatesOnRoll). Written by PianoRoll's setters alone.

import { BAR_ROW } from "./roll-viewport.js";
import { navigatesOnRoll, selectsOnRoll } from "./roll-modes.js";
import type { InputHost } from "./roll-input.js";

/** Who edits and who moves the iPad's roll (InputRules.swift RollInput), and whether the stylus selects (#511 4.1). */
export interface PointerRules {
    touch: boolean;
    pen: boolean;
    scrollFingers: number;
    penSelects?: boolean;
}

/** What the rules read of the roll. */
export type PointersHost = Pick<InputHost, "view" | "editor" | "mode" | "lanes" | "laneShown" | "header">;

/** The pointers' rules on the roll. */
export class RollPointers {
    /**
     * Which pointers edit with the editor on and how many fingers move the roll (the iPad's InputRules, #479 item 23): with a
     * stylus the stylus edits and a finger moves the roll; without one a finger edits and two move it. Null: every pointer
     * edits, as on the web.
     */
    private rules: PointerRules | null = null;
    /** Selection by where you touch (#601, roll-place-gesture.ts): on, and whether a stylus is used (a finger then moves the roll). */
    private placeOn = false;
    private placeStylus = true;

    constructor(private readonly host: PointersHost) {}

    /** Whether Select picks by place (the host's switch; off on the web, which passes nothing). */
    get placeSelection(): boolean {
        return this.placeOn;
    }

    /** Whether a stylus is used beside the fingers. */
    get stylus(): boolean {
        return this.placeStylus;
    }

    /** Whether the iPad's rules are given; without them every pointer edits, as on the web. */
    get ruled(): boolean {
        return this.rules !== null;
    }

    setRules(rules: PointerRules | null): void {
        this.rules = rules;
    }

    /** The place rule on or off, with a stylus or not. */
    setPlace(on: boolean, stylus: boolean): void {
        this.placeOn = on;
        this.placeStylus = stylus;
    }

    /** Whether a pointer of this type edits with the editor on, under the iPad's rules; every pointer without them. */
    editsWith(type: string): boolean {
        const rules = this.rules;
        // Expand inspects a passage; it edits no notes (#521 19.3).
        return this.host.view.passage === null && (rules === null || (type === "touch" ? rules.touch : rules.pen));
    }

    /**
     * Whether a press of this pointer type may change the selection: only under Select with the modes (#543 2), not the
     * stylus while the Pen is on (#511 4.1), none in Expand (#521 19.3).
     */
    selectsWith(type: string): boolean {
        return selectsOnRoll(type, this.penHolds(), this.host.view.passage !== null, this.host.mode());
    }

    /** Whether a press of this pointer type navigates under the bar's tool (#543 2, roll-modes.ts navigatesOnRoll). */
    navigates(type: string): boolean {
        return navigatesOnRoll(this.host.mode(), type);
    }

    /** Whether the bar's Pen holds the stylus, which then does nothing on the roll (#511 4.1, #521 10). */
    penHolds(): boolean {
        return this.host.mode() === "pen" || this.rules?.penSelects === false;
    }

    /** Whether a finger's tap in the editor waits to be lifted: two fingers move the roll, so a second may still come. */
    waitsForLift(event: PointerEvent): boolean {
        return event.pointerType === "touch" && (this.rules?.scrollFingers ?? 1) > 1;
    }

    /** Whether a press of this pointer selects by its place: Select, the switch on, not the editor, nor a finger beside a stylus. */
    placeSelects(type: string): boolean {
        return this.placeOn && this.host.mode() === "select" && !this.host.editor.editing && this.selectsWith(type) && (type !== "touch" || !this.placeStylus);
    }

    /** Whether Select reads a press of this pointer at y by its place (roll-place-gesture.ts), over the notes and the bar numbers alike. */
    placesAt(type: string, y: number): boolean {
        return this.placeSelects(type) && !this.inHiddenLane(y);
    }

    /** Whether a point lies in the loop strip's lane while the strip is hidden (laneShown): there no press reaches the strip. */
    inHiddenLane(y: number): boolean {
        return this.host.lanes() && !this.host.laneShown() && y >= BAR_ROW && y < this.host.header();
    }
}
