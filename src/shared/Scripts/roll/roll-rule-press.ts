// A press under the modes of version 0.5 on the roll (the input router's, roll-input.ts; the audit's W3.12): what it begins
// by the bar's tool (roll-modes.ts gestureFor): the hand in Navigate, a range from outside the selection in Select, a box in
// Edit notes; else only a click, which places a playhead on the beat or opens the editor on a note.

import { gestureFor, type RollGesture } from "./roll-modes.js";
import { CLICK_SLOP } from "./roll-press.js";
import { movePan, type Pan } from "./roll-fingers.js";
import type { InputHost } from "./roll-input.js";

/** A press under the modes: what it began and where; a pan keeps the scroll and rows it started from. */
interface RulePress extends Pan {
    tick: number;
    pitch: number;
    gesture: RollGesture;
    shift: boolean;
}

/** A point on the notes canvas. */
interface Point {
    x: number;
    y: number;
}

/** What a press under the modes reads of the roll. */
export type RulePressHost = Pick<InputHost, "view" | "editor" | "model" | "motif" | "mode" | "lanes" | "header" | "post" | "changed">;

/** What it asks of the router: whether a pointer navigates, a hold to wait for, a range drag to take on, a tap on the strips. */
export interface RulePressRouter {
    navigates(type: string): boolean;
    waitForHold(pointer: number, x: number, y: number): void;
    beginRange(pointer: number, x: number, tick: number): void;
    tapStrip(tick: number, y: number): void;
}

/** The press under the modes, and the box Edit notes draws. */
export class RollRulePress {
    private press: RulePress | null = null;
    /** The box being drawn in Edit notes, to where the pointer is. */
    private box: Point | null = null;

    constructor(
        private readonly host: RulePressHost,
        private readonly router: RulePressRouter,
    ) {}

    /** The box being drawn, from where its press began to where the pointer is; null for none. */
    boxDrawn(): { from: Point; to: Point } | null {
        return this.press === null || this.box === null ? null : { from: this.press, to: this.box };
    }

    /** The press goes, or only this pointer's, and the box it drew, so a later press cannot pick that box. */
    drop(pointer?: number): void {
        if (pointer === undefined || this.press?.pointer === pointer) {
            this.press = null;
            this.box = null;
        }
    }

    /** No press or box goes on. */
    cancel(): void {
        this.drop();
    }

    /**
     * A press under the modes of version 0.5 (roll-modes.ts): the hand in Navigate, a range from outside the selection in
     * Select, a box in Edit notes (`box`, the editor's press on empty space); else only a click. False while the modes are
     * off, or the editor has the press.
     */
    begin(event: PointerEvent, x: number, y: number, tick: number, pitch: number, box = false): boolean {
        const mode = this.host.mode();
        if (mode === null || (this.host.editor.editing && !box)) {
            return false;
        }
        const motif = this.host.motif();
        const navigates = !box && this.router.navigates(event.pointerType);
        const gesture = box ? "box" : gestureFor(navigates ? "navigate" : mode, motif !== null && tick >= motif.start && tick < motif.end);
        const view = this.host.view;
        this.press = { pointer: event.pointerId, x, y, tick, pitch, gesture, shift: event.shiftKey, scroll: view.scroll, top: view.pitchRange().high, moving: false };
        if (navigates) {
            this.router.waitForHold(event.pointerId, x, y);
        }
        return true;
    }

    /** A move of the press; false when a range drag takes it on from here (the router's), as before the modes. */
    move(pointer: number, x: number, y: number): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        if (press.gesture === "pan") {
            if (movePan(this.host.view, press, x, y)) {
                this.host.changed();
            }
            return true;
        }
        if (!press.moving && Math.hypot(x - press.x, y - press.y) < CLICK_SLOP) {
            return true;
        }
        press.moving = true;
        if (press.gesture === "range") {
            this.press = null;
            this.router.beginRange(press.pointer, press.x, press.tick);
            return false;
        }
        if (press.gesture === "box") {
            this.box = { x, y };
            this.host.changed();
        }
        return true;
    }

    /** The end of the press: a box picks its notes for the editor; a click places a playhead, or picks a note in Edit notes. */
    end(pointer: number): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        this.press = null;
        const box = this.box;
        const model = this.host.model();
        this.box = null;
        if (box !== null) {
            if (model !== null) {
                this.host.editor.pickBox(model, press, box);
            }
        } else if (!press.moving) {
            this.click(press);
        }
        this.host.changed();
        return true;
    }

    /**
     * A click under the modes: in Edit notes on a note, the editor opens on it; anywhere else a playhead goes to the beat. On
     * the iPad's strips (the lanes) a click is a tap there (the router's tapStrip).
     */
    click(press: { tick: number; pitch: number; shift: boolean; y?: number }): void {
        const model = this.host.model();
        const index = this.host.mode() === "edit" ? model?.noteAt(press.tick, press.pitch) : undefined;
        if (index !== undefined && model !== null) {
            this.host.editor.pickNote(model, index);
            return;
        }
        if (this.host.lanes() && press.y !== undefined && press.y < this.host.header()) {
            this.router.tapStrip(press.tick, press.y);
            return;
        }
        this.host.post("OnRollPlace", Math.round(press.tick), press.shift);
    }
}
