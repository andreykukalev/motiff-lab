// The roll's note editor (testing item 15; the audit's W3.12, second part): on or off with its tool, the note picked, the
// notes a box took, a note dragged (moved by sixteenths and rows, or lengthened by its end), a finger's tap that waits to be
// lifted, the keys on the note picked. What it edits goes to the host as the note editor's calls (OnRollNote*); the notes
// come back from music-core. The input router (roll-input.ts) hands it the presses; the painter draws a note where
// `placed` puts it and rings what `picked` names.

import { notesInBox } from "./roll-modes.js";
import type { RollModel } from "./roll-model.js";
import { CLICK_SLOP } from "./roll-press.js";
import { GUTTER, type RollViewport } from "./roll-viewport.js";
import { TICKS_PER_QUARTER } from "../score/score-timeline.js";

/** What a press on the notes does while the notes are edited: move or lengthen a note, add one, or take one out. */
export type EditTool = "move" | "add" | "delete";

/** The note editor's grid (testing item 15): a sixteenth, the step the web app's note corrections move by. */
const EDIT_STEP = TICKS_PER_QUARTER / 4;
/** How near a note's end, in pixels, a press drags its length rather than moving it. */
const LENGTH_GRAB = 8;

/** What the editor reads of the roll and does through it. */
export interface EditorHost {
    readonly view: RollViewport;
    model(): RollModel | null;
    /** Strikes a key by hand and lights it; the pitch to release, or null. */
    strike(pitch: number): number | null;
    release(pitch: number | null): void;
    /** A call to the host page (Swift through roll.js, or Blazor). */
    post(method: string, ...args: unknown[]): void;
    /** Something the roll draws changed. */
    changed(): void;
}

/** A note dragged in the editor: by which pointer, moved or lengthened, from where, by how much, and the key sounding. */
interface EditDrag {
    pointer: number;
    index: number;
    mode: "move" | "length";
    x: number;
    y: number;
    ticks: number;
    pitches: number;
    pitch: number;
    sounding: number | null;
}

/** The note editor: its tool, the note picked by id, the length of the next note added, and a drag of a note. */
export class RollEditor {
    private on = false;
    private tool: EditTool = "move";
    private id: string | null = null;
    private length = TICKS_PER_QUARTER / 2;
    private drag: EditDrag | null = null;
    /** A finger's tap that adds or takes out a note when it is lifted, as a second finger may yet make it a scroll. */
    private tap: { pointer: number; x: number; y: number; tick: number; pitch: number } | null = null;
    /** The notes the last box took. */
    private boxPicked = new Set<number>();

    constructor(private readonly host: EditorHost) {}

    /** Whether the editor is on. */
    get editing(): boolean {
        return this.on;
    }

    /** Turns the editor on or off with its tool; off, nothing stays picked or dragged. */
    set(on: boolean, tool: EditTool): void {
        this.on = on;
        this.tool = tool;
        if (!on) {
            this.id = null;
            this.drag = null;
            this.boxPicked.clear();
        }
    }

    /** Picks a note by id, such as one just added, so the arrows and Delete act on it. */
    pick(id: string | null): void {
        this.id = id;
    }

    /** New notes shown: no drag or box goes on, and a note picked that is not among them is let go. */
    forget(model: RollModel): void {
        this.drag = null;
        this.boxPicked.clear();
        if (this.id !== null && !model.notes.some((note) => note.id === this.id)) {
            this.id = null;
        }
    }

    /**
     * A press on the notes: picks, starts a drag, adds or takes out a note; with `waits` (a finger while two fingers move the
     * roll) Add and Remove act when it is lifted. False for a press on empty space under Move, which begins a box there.
     */
    press(pointer: number, waits: boolean, model: RollModel, x: number, y: number, tick: number, pitch: number): boolean {
        const index = model.noteAt(tick, pitch);
        if (this.tool !== "move" && waits) {
            this.tap = { pointer, x, y, tick, pitch };
            return true;
        }
        if (this.tool === "delete" || (this.tool === "add" && index === undefined)) {
            this.tapEdit(model, tick, pitch);
            return true;
        }
        if (index === undefined) {
            this.id = null;
            this.boxPicked.clear();
            return false;
        }
        const note = model.notes[index];
        this.id = note.id;
        const nearEnd = Math.abs(x - this.host.view.xOf(note.end)) <= LENGTH_GRAB && note.end - note.onset > 0;
        this.drag = { pointer, index, mode: nearEnd ? "length" : "move", x, y, ticks: 0, pitches: 0, pitch: note.pitch, sounding: this.host.strike(note.pitch) };
        this.host.changed();
        return true;
    }

    /** Where a note is drawn: where a drag would put it (its onset, pitch and length), else where it is. */
    placed(index: number): { onset: number; pitch: number; length: number } {
        const note = this.host.model()!.notes[index];
        const drag = this.drag;
        const length = note.end - note.onset;
        if (drag === null || drag.index !== index) {
            return { onset: note.onset, pitch: note.pitch, length };
        }
        return drag.mode === "length"
            ? { onset: note.onset, pitch: note.pitch, length: Math.max(EDIT_STEP, Math.round((length + drag.ticks) / EDIT_STEP) * EDIT_STEP) }
            : { onset: Math.max(0, note.onset + Math.round(drag.ticks / EDIT_STEP) * EDIT_STEP), pitch: Math.min(127, Math.max(0, note.pitch + drag.pitches)), length };
    }

    /** Whether a note is ringed as picked: the editor on, and the note picked or taken by the last box. */
    picked(model: RollModel, index: number): boolean {
        return this.on && (model.notes[index].id === this.id || this.boxPicked.has(index));
    }

    /** A move of the note dragged by this pointer; true when it was one. The note sounds again on each new row. */
    move(pointer: number, x: number, y: number): boolean {
        const drag = this.drag;
        if (drag === null || pointer !== drag.pointer) {
            return false;
        }
        const { row } = this.host.view.pitchRange();
        drag.ticks = this.host.view.ticksAcross(x - drag.x);
        drag.pitches = drag.mode === "move" ? Math.round((drag.y - y) / row) : 0;
        const pitch = this.placed(drag.index).pitch;
        if (pitch !== drag.pitch) {
            this.host.release(drag.sounding);
            drag.pitch = pitch;
            drag.sounding = this.host.strike(pitch);
        }
        this.host.changed();
        return true;
    }

    /** The pointer's cursor over the notes while the editor is on, by its tool and what lies under it; null while it is off. */
    cursor(model: RollModel, x: number, y: number): string | null {
        if (!this.on) {
            return null;
        }
        const view = this.host.view;
        const index = x >= GUTTER ? model.noteAt(view.tickAtX(x), view.pitchAt(y)) : undefined;
        if (index === undefined) {
            return this.tool === "add" ? "copy" : "";
        }
        if (this.tool === "delete") {
            return "not-allowed";
        }
        return Math.abs(x - view.xOf(model.notes[index].end)) <= LENGTH_GRAB ? "ew-resize" : "move";
    }

    /** The end of a press of this pointer at (x, y): a finger's tap adds or takes out a note, a drag sends where it went. */
    end(pointer: number, x: number, y: number): boolean {
        const tap = this.tap;
        if (tap !== null && pointer === tap.pointer) {
            this.tap = null;
            const model = this.host.model();
            if (model !== null && Math.hypot(x - tap.x, y - tap.y) < CLICK_SLOP) {
                this.tapEdit(model, tap.tick, tap.pitch);
            }
            return true;
        }
        const drag = this.drag;
        if (drag === null || pointer !== drag.pointer) {
            return false;
        }
        const { onset, pitch, length } = this.placed(drag.index);
        this.drag = null;
        this.host.release(drag.sounding);
        this.sendEdit(drag.index, onset, pitch, length);
        this.host.changed();
        return true;
    }

    /** Lets go of a note being dragged, sending nothing, and of a tap not yet lifted. */
    cancel(): void {
        if (this.drag !== null) {
            this.host.release(this.drag.sounding);
            this.drag = null;
        }
        this.tap = null;
    }

    /**
     * The editor's keys: Delete or Backspace takes out every note a box picked, or the note picked; on that note the arrows
     * move it, Shift with ← → changes its length, Escape lets it go. True when the key was the editor's.
     */
    key(event: KeyboardEvent, model: RollModel): boolean {
        if (this.removeBoxPicked(event, model)) {
            return true;
        }
        const index = this.on ? this.index(model) : undefined;
        return index !== undefined && this.editKey(event, model, index);
    }

    /** The notes a box drawn in Edit notes takes, picked for the editor (not a selection); the editor opens on them. */
    pickBox(model: RollModel, from: { x: number; y: number }, to: { x: number; y: number }): void {
        const view = this.host.view;
        const picked = notesInBox(model.notes, { fromTick: view.tickAtX(from.x), toTick: view.tickAtX(to.x), fromPitch: view.pitchAt(from.y), toPitch: view.pitchAt(to.y) });
        this.boxPicked = new Set(picked);
        this.id = picked.length > 0 ? model.notes[picked[0]].id : null;
        if (picked.length > 0) {
            this.host.post("OnRollNotesPicked", picked.map((index) => model.notes[index].id));
        }
    }

    /** A click on a note in Edit notes: the editor opens on it alone. */
    pickNote(model: RollModel, index: number): void {
        this.boxPicked.clear();
        this.id = model.notes[index].id;
        this.host.post("OnRollNotesPicked", [this.id]);
    }

    private index(model: RollModel): number | undefined {
        if (this.id === null) {
            return undefined;
        }
        const index = model.notes.findIndex((note) => note.id === this.id);
        return index < 0 ? undefined : index;
    }

    /** A tap with Add or Remove: a note added where there is none, taken out, or picked. */
    private tapEdit(model: RollModel, tick: number, pitch: number): void {
        const index = model.noteAt(tick, pitch);
        if (this.tool === "delete") {
            if (index !== undefined) {
                this.host.post("OnRollNoteRemove", model.notes[index].id);
            }
            return;
        }
        if (index !== undefined) {
            this.id = model.notes[index].id;
            this.host.changed();
            return;
        }
        const onset = Math.max(0, Math.floor(tick / EDIT_STEP) * EDIT_STEP);
        this.host.release(this.host.strike(pitch));
        this.host.post("OnRollNoteAdd", onset, pitch, this.length);
    }

    /** Sends a note's new onset, pitch and length to the host, when they changed. */
    private sendEdit(index: number, onset: number, pitch: number, length: number): void {
        const note = this.host.model()!.notes[index];
        if (onset === note.onset && pitch === note.pitch && length === note.end - note.onset) {
            return;
        }
        this.length = length;
        this.host.post("OnRollNoteChange", note.id, onset, pitch, length);
    }

    /** Delete or Backspace takes out every note a box picked. True when it did. */
    private removeBoxPicked(event: KeyboardEvent, model: RollModel): boolean {
        if (!this.on || this.boxPicked.size < 2 || (event.key !== "Delete" && event.key !== "Backspace")) {
            return false;
        }
        for (const index of this.boxPicked) {
            this.host.post("OnRollNoteRemove", model.notes[index].id);
        }
        this.boxPicked.clear();
        this.id = null;
        return true;
    }

    /** The keys on the note picked. */
    private editKey(event: KeyboardEvent, model: RollModel, index: number): boolean {
        const note = model.notes[index];
        const length = note.end - note.onset;
        switch (event.key) {
            case "Delete":
            case "Backspace":
                this.host.post("OnRollNoteRemove", note.id);
                return true;
            case "ArrowUp":
            case "ArrowDown":
                this.sendEdit(index, note.onset, Math.min(127, Math.max(0, note.pitch + (event.key === "ArrowUp" ? 1 : -1))), length);
                return true;
            case "ArrowLeft":
            case "ArrowRight": {
                const step = event.key === "ArrowRight" ? EDIT_STEP : -EDIT_STEP;
                if (event.shiftKey) {
                    this.sendEdit(index, note.onset, note.pitch, Math.max(EDIT_STEP, length + step));
                } else {
                    this.sendEdit(index, Math.max(0, note.onset + step), note.pitch, length);
                }
                return true;
            }
            case "Escape":
                this.id = null;
                this.host.changed();
                return true;
            default:
                return false;
        }
    }
}
