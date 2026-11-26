// A press on a note or on the pitch names (the input router's, roll-input.ts; the audit's W3.12): before the modes (the
// web's), a note pressed sounds, a click on it chooses it and a click on an empty place clears, and a press that moves
// becomes a range drag; on the pitch names (any mode) the row's key sounds, and a drag down them scrolls the rows.

import { CLICK_SLOP } from "./roll-press.js";
import type { RollModel } from "./roll-model.js";
import type { InputHost } from "./roll-input.js";

/** A press on the notes area that becomes a click on a note or the grid, or a range drag once it moves; tick −1 on the pitch names. */
interface NotePress {
    pointer: number;
    x: number;
    y: number;
    tick: number;
    pitch: number;
    add: boolean;
    sounding: number | null;
    top: number | null;
}

/** What the press reads of the roll and does through it. */
export type NotePressHost = Pick<InputHost, "view" | "model" | "strike" | "release" | "touchReading" | "selectNote" | "clearAll">;

/** The press on a note or the pitch names. */
export class RollNotePress {
    private press: NotePress | null = null;

    /** `beginRange` takes a press that moved on as a range drag from where it went down. */
    constructor(
        private readonly host: NotePressHost,
        private readonly beginRange: (pointer: number, x: number, tick: number) => void,
    ) {}

    /** The pitch names play their row's key, as the keyboard does. */
    keys(pointer: number, x: number, y: number, pitch: number): void {
        this.press = { pointer, x, y, tick: -1, pitch, add: false, sounding: this.host.strike(pitch), top: null };
    }

    /** A note sounds as soon as it is pressed, over playback too; Shift, Ctrl or ⌘ adds it to the notes chosen. */
    note(event: PointerEvent, model: RollModel, x: number, y: number, tick: number, pitch: number): void {
        const index = model.noteAt(tick, pitch);
        const sounding = index !== undefined ? this.host.strike(model.notes[index].pitch) : null;
        this.press = { pointer: event.pointerId, x, y, tick, pitch, add: event.shiftKey || event.ctrlKey || event.metaKey, sounding, top: null };
    }

    /** The press goes, its key let go. */
    cancel(): void {
        this.host.release(this.press?.sounding ?? null);
        this.press = null;
    }

    /** The press goes (new notes shown). */
    drop(): void {
        this.press = null;
    }

    /**
     * A move of the press: down the pitch names it scrolls the rows when they do not all fit (any iPad, no wheel), else plays
     * each key in turn, true; a press on the notes that moves becomes a range drag, as on the bar numbers, not in Expand
     * (#521 19.3), false.
     */
    move(pointer: number, x: number, y: number): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        if (press.tick < 0) {
            this.moveKeys(press, y);
            return true;
        }
        if (Math.abs(x - press.x) >= CLICK_SLOP && this.host.view.passage === null) {
            this.host.release(press.sounding);
            this.beginRange(press.pointer, press.x, press.tick);
            this.press = null;
        }
        return false;
    }

    /** The end of the press: a click on a note chooses it, on an empty place clears (not in touch reading). */
    end(pointer: number): boolean {
        const press = this.press;
        if (press === null || pointer !== press.pointer) {
            return false;
        }
        this.press = null;
        this.host.release(press.sounding);
        if (press.tick < 0) {
            return true;
        }
        const index = this.host.model()?.noteAt(press.tick, press.pitch);
        if (index !== undefined || press.add) {
            this.host.selectNote(index, press.add);
        } else if (!this.host.touchReading()) {
            // In touch reading no click clears the selection, a mouse's or a trackpad's included.
            this.host.clearAll();
        }
        return true;
    }

    private moveKeys(press: NotePress, y: number): void {
        const view = this.host.view;
        const { high, row, above, below } = view.pitchRange();
        if (press.top !== null || ((above || below) && Math.abs(y - press.y) >= CLICK_SLOP)) {
            this.host.release(press.sounding);
            press.sounding = null;
            press.top ??= high;
            view.scrollPitch(Math.round((y - press.y) / row), press.top);
            return;
        }
        const pitch = view.pitchAt(y);
        if (pitch !== press.pitch) {
            this.host.release(press.sounding);
            press.pitch = pitch;
            press.sounding = this.host.strike(pitch);
        }
    }
}
