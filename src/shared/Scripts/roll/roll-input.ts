// The roll's input router (architecture blueprint S2 and S8, the audit's W3.12): the notes canvas's pointer events. A
// press is read through the press table (roll-press.ts) into one gesture, begun here or in the gesture's own module (the
// place rule in roll-place-gesture.ts, the strips' in roll-strip-gesture.ts, the editor in roll-editor.ts, the fingers'
// pinch and pan in roll-fingers.ts, the playheads in roll-head-press.ts, a press under the modes in roll-rule-press.ts);
// every move and end goes to the gesture that has its pointer, and a cancel lets every one go. The router owns the presses and who may press what (the iPad's pointer rules, the place rule's switch); it draws
// nothing: PianoRoll reads `rangeDragged` and `boxDrawn` for the painter and the gestures' layers.

import type { RollModel } from "./roll-model.js";
import { measures } from "./roll-palette.js";
import { BAR_ROW, GUTTER, type RollViewport } from "./roll-viewport.js";
import { beatAt, type PlayheadControl, type Span } from "./roll-playhead.js";
import { actsOnRoll, holdKeepsSelection, modeSelects } from "./roll-modes.js";
import { RollStripGesture, keptDrag, type StripHost } from "./roll-strip-gesture.js";
import { CLICK_SLOP, rollPressFor } from "./roll-press.js";
import type { PlacePoint, RollPlaceGesture } from "./roll-place-gesture.js";
import { onPlaceHandle } from "./roll-place-draw.js";
import type { RollEditor } from "./roll-editor.js";
import { RollFingers, type Pan } from "./roll-fingers.js";
import { RollHeadPress } from "./roll-head-press.js";
import { RollRulePress } from "./roll-rule-press.js";
import { RollNotePress } from "./roll-note-press.js";
import { RollPointers, type PointerRules } from "./roll-pointer-rules.js";
import type { InteractionMode } from "../score/score-modes.js";
import { nextGestureId, type SelectionChange } from "../score/selection-gesture.js";
import type { Handle } from "../playback/playback-model.js";
import type { PlayheadDrag } from "../playback/playhead-drag.js";

/** How close, in pixels, a press must be to an edge of the selection to drag that edge. */
const EDGE_GRAB = 6;

/** A range dragged across the roll: where it started and where the pointer is (see RollInput.rangeDragged). */
interface RangeDrag {
    pointer: number;
    anchorX: number;
    anchor: number;
    current: number;
    edge: boolean;
    offset?: number;
    how?: SelectionChange;
    gesture?: string;
}

/** What the router reads of the roll and does through it. */
export interface InputHost {
    readonly canvas: HTMLCanvasElement;
    readonly view: RollViewport;
    readonly editor: RollEditor;
    readonly place: RollPlaceGesture;
    readonly playhead: PlayheadControl;
    readonly laneDrag: PlayheadDrag;
    /** The roll's width. */
    width(): number;
    /** Where the notes start: under the bar numbers, and under the loop strip with the lanes. */
    header(): number;
    model(): RollModel | null;
    /** The host's selection; null for none. */
    motif(): Span | null;
    /** The span let go and not answered yet, or the host's selection (PianoRoll.placeSpan). */
    placeSpan(): Span | null;
    /** The bar's tool (version 0.5); null before the modes. */
    mode(): InteractionMode | null;
    touchReading(): boolean;
    axisZoom(): boolean;
    lanes(): boolean;
    /** Whether the loop strip is shown and takes presses. */
    laneShown(): boolean;
    hasPlayer(): boolean;
    playing(): boolean;
    /** The playhead a press of this pointer at (x, y) takes; null for none. */
    playheadUnder(type: string, x: number, y: number): Handle | "line" | null;
    /** Where the playhead stays (the loop, else the selection); null for anywhere. */
    playheadSpan(): Span | null;
    /** The band a playhead taken by its handle is held in. */
    headBand(): Span | null;
    /** The bar of the Check flag on the bar numbers at x, if any. */
    checkAt(x: number): number | undefined;
    strike(pitch: number): number | null;
    release(pitch: number | null): void;
    post(method: string, ...args: unknown[]): void;
    /** Chooses a note, or with `add` adds it to the notes chosen or takes it out (the web's notes). */
    selectNote(index: number | undefined, add: boolean): void;
    /** Clears the notes chosen and asks the host to clear the selection. */
    clearAll(): void;
    /** A pointer went down on the roll, or the last one went up: following the playhead pauses and comes back. */
    holdBegan(pointer: number): void;
    holdEnded(pointer: number): void;
    changed(): void;
}

/** The input router of the roll's notes canvas. */
export class RollInput {
    /** Who may press what: the iPad's pointer rules and the place rule's switch (roll-pointer-rules.ts). */
    readonly pointers: RollPointers;
    /**
     * A drag across the roll selecting a range: where it started and where the pointer is; edge when it moves an edge of the
     * selection, `offset` ticks from the pointer; how it changes the selection and the gesture it belongs to (#511 11).
     */
    private drag: RangeDrag | null = null;
    private readonly strip: RollStripGesture;
    private readonly notes: RollNotePress;
    private readonly fingers: RollFingers;
    private readonly head: RollHeadPress;
    private readonly rules: RollRulePress;

    constructor(private readonly host: InputHost) {
        this.pointers = new RollPointers(host);
        this.strip = new RollStripGesture(this.stripHost());
        this.notes = new RollNotePress(host, (pointer, x, tick) => {
            this.drag = { pointer, anchorX: x, anchor: tick, current: tick, edge: false };
        });
        this.fingers = new RollFingers(host, { cancelPresses: () => this.cancelPresses(), letGo: () => this.letGoForPinch(), tapped: (pan) => this.tapped(pan) });
        this.head = new RollHeadPress(host, (pointer) => {
            this.fingers.dropPan(pointer);
            this.rules.drop(pointer);
        });
        this.rules = new RollRulePress(host, {
            navigates: (type) => this.pointers.navigates(type),
            waitForHold: (pointer, x, y) => this.head.waitForHold(pointer, x, y),
            beginRange: (pointer, x, tick) => {
                this.drag = { pointer, anchorX: x, anchor: tick, current: tick, edge: false };
            },
            tapStrip: (tick, y) => this.tapStrip(tick, y),
        });
        const canvas = host.canvas;
        canvas.addEventListener("pointerdown", this.onPointerDown);
        canvas.addEventListener("pointermove", this.onPointerMove);
        canvas.addEventListener("pointerup", this.onPointerUp);
        canvas.addEventListener("pointercancel", this.onPointerCancel);
        canvas.addEventListener("lostpointercapture", this.onLostCapture);
    }

    /** Takes the router's listeners off the canvas. */
    dispose(): void {
        const canvas = this.host.canvas;
        canvas.removeEventListener("pointerdown", this.onPointerDown);
        canvas.removeEventListener("pointermove", this.onPointerMove);
        canvas.removeEventListener("pointerup", this.onPointerUp);
        canvas.removeEventListener("pointercancel", this.onPointerCancel);
        canvas.removeEventListener("lostpointercapture", this.onLostCapture);
    }

    /** The iPad's pointer rules; null for every pointer, one finger. What the editor's pointer began goes, nothing sent. */
    setPointerRules(rules: PointerRules | null): void {
        this.pointers.setRules(rules);
        this.host.editor.cancel();
    }

    /** Selection by where you touch, on or off, with a stylus or not; a change lets every press go. */
    setPlaceSelection(on: boolean, stylus: boolean): void {
        if (on !== this.pointers.placeSelection) {
            this.cancelPresses();
        }
        this.pointers.setPlace(on, stylus);
    }

    /** The range being dragged across the roll, its edges in order; null for none. */
    rangeDragged(): Span | null {
        const drag = this.drag;
        return drag === null ? null : { start: Math.min(drag.anchor, drag.current), end: Math.max(drag.anchor, drag.current) };
    }

    /** The box being drawn in Edit notes, from where its press began to where the pointer is; null for none. */
    boxDrawn(): { from: { x: number; y: number }; to: { x: number; y: number } } | null {
        return this.rules.boxDrawn();
    }

    /** The host's selection came back: a new range the press drags on after a hold's clear is kept, any other let go (keptDrag). */
    keepDrag(): void {
        this.drag = keptDrag(this.drag);
    }

    /** New notes shown: no range, press, pan or press under the modes goes on. */
    dropPresses(): void {
        this.drag = null;
        this.notes.drop();
        this.fingers.dropPan();
        this.rules.drop();
    }

    /** Touch reading turned on or off: a finger moving the roll stops. */
    dropPan(): void {
        this.fingers.dropPan();
    }

    /** Bound to a passage or let go (Expand): no range goes on; bound, the strips' gesture is let go too. */
    dropRange(bound: boolean): void {
        this.drag = null;
        if (bound) {
            this.strip.cancel();
        }
    }

    /**
     * Every press the roll was reading is let go, sending nothing more (a cancel, a change of tool): no range, edge, pan,
     * hold or held playhead is left to take the next press (#543 9). A selection a hold took away comes back.
     */
    cancelPresses(): void {
        this.fingers.cancel();
        this.notes.cancel();
        this.host.editor.cancel();
        this.drag = null;
        this.rules.cancel();
        this.strip.cancel();
        this.host.place.cancel();
        this.head.cancel();
    }

    /** While a playhead is held at an edge of the roll the view goes along (roll-head-press.ts), once a frame. */
    stepEdgeScroll(elapsed: number): void {
        this.head.stepEdgeScroll(elapsed);
    }

    /** A second finger came: what the first began goes, nothing sent; the pinch begins (roll-fingers.ts). */
    private letGoForPinch(): void {
        this.notes.cancel();
        this.drag = null;
        this.host.place.cancel();
        if (this.host.touchReading()) {
            // Two fingers move the roll: what the first began in the editor goes, nothing sent (#479 item 23).
            this.host.editor.cancel();
        }
        this.head.cancel();
    }

    /** A finger's tap that moved the roll no further: on the strips it is a tap there; on the notes it plays from there (Andrei, v0.2). */
    private tapped(pan: Pan): void {
        if (pan.y < this.host.header()) {
            this.tapStrip(this.host.view.tickAtX(pan.x), pan.y);
        } else {
            this.head.playFrom(pan.x);
        }
    }

    /** A press on the loop strip inside the selection moves the position there (the lanes); elsewhere bars are selected as before. */
    pressLoopLane(y: number, tick: number): boolean {
        const motif = this.host.motif();
        if (!this.host.laneShown() || (this.host.editor.editing && !this.pointers.ruled) || motif === null || y < BAR_ROW || y >= this.host.header() || tick < motif.start || tick >= motif.end) {
            return false;
        }
        this.host.playhead.go(Math.round(tick), false);
        return true;
    }

    /** Where a pointer is on the notes canvas. */
    private at(event: PointerEvent): { x: number; y: number } {
        const box = this.host.canvas.getBoundingClientRect();
        return { x: event.clientX - box.left, y: event.clientY - box.top };
    }

    private readonly onPointerDown = (event: PointerEvent): void => {
        const model = this.host.model();
        if (model === null) {
            return;
        }
        this.host.holdBegan(event.pointerId);
        const { x, y } = this.at(event);
        if (this.fingers.down(event, x, y) || this.pensOwn(event)) {
            return;
        }
        const { view, canvas, editor } = this.host;
        const pitch = view.pitchAt(y);
        canvas.setPointerCapture(event.pointerId);
        // No text selection while dragging; focus by hand so the keys (Escape, arrows) reach the roll.
        event.preventDefault();
        canvas.focus({ preventScroll: true });
        const type = event.pointerType;
        const tick = view.tickAtX(x);
        const header = this.host.header();
        // Whether this press edits notes: with the editor on, by the pointer the iPad's rules give the editor (all on the web).
        const edits = editor.editing && this.pointers.editsWith(type);
        // What the press table found under the press, for the gesture it names (roll-press.ts).
        const under: { head: Handle | "line" | null; anchor: number | null } = { head: null, anchor: null };
        const press = rollPressFor({
            gutter: x < GUTTER,
            header: y < header,
            placeHandle: () => this.onSelectionHandle(type, x, y),
            playhead: () => (under.head = this.host.playheadUnder(type, x, y)) !== null,
            places: () => this.pointers.placesAt(type, y),
            // In touch reading a finger on the notes moves through the music, neither selecting nor sounding; so does a pointer
            // that does not edit while the editor is on (#479 item 23).
            pans: () => this.host.touchReading() && !edits && y >= header && type !== "mouse",
            edge: () => (under.anchor = this.edgeAnchorUnder(type, x, y)) !== null,
            editing: editor.editing,
            edits,
            rules: () => this.host.mode() !== null && !editor.editing,
        });
        switch (press) {
            case "none":
                return;
            case "keys":
                this.notes.keys(event.pointerId, x, y, pitch);
                return;
            case "place":
                this.host.place.begin(event.pointerId, this.placePoint(x, y, tick), y < header);
                return;
            case "playhead":
                // The table names a playhead only when playheadUnder found one.
                this.head.begin(under.head as Handle | "line", event.pointerId, x);
                return;
            case "pan":
                this.beginPan(event, x, y);
                return;
            case "edge":
                this.drag = { pointer: event.pointerId, anchorX: Number.NaN, anchor: under.anchor as number, current: tick, edge: true, how: "edit", gesture: nextGestureId() };
                this.host.changed();
                return;
            case "barNumbers":
                this.pressBarNumbers(event, x, y, tick, pitch);
                return;
            case "loopLane":
                this.pressLoopLane(y, tick);
                return;
            case "edit":
                if (!editor.press(event.pointerId, this.pointers.waitsForLift(event), model, x, y, tick, pitch)) {
                    this.rules.begin(event, x, y, tick, pitch, true);
                    this.host.changed();
                }
                return;
            case "rule":
                this.rules.begin(event, x, y, tick, pitch);
                return;
            case "note":
                this.notes.note(event, model, x, y, tick, pitch);
        }
    };

    private readonly onPointerMove = (event: PointerEvent): void => {
        const { x, y } = this.at(event);
        const pointer = event.pointerId;
        this.head.heardMove(pointer, x, y);
        if (this.fingers.move(event, x, y) || this.head.moveHandle(pointer, x) || this.movePlace(event, x, y) || this.head.moveLine(pointer, x) || this.rules.move(pointer, x, y) || this.moveEdit(event, x, y) || this.notes.move(pointer, x, y) || this.strip.move(pointer, x, y)) {
            return;
        }
        this.moveRange(event, x);
    };

    /**
     * Ends a press. A drag sends its range; music-core snaps it to beats and replies with setMotif.
     * A click on a note selects it; a click on an empty place clears the selection.
     */
    private readonly onPointerUp = (event: PointerEvent): void => {
        // A hold that fired made the press the playhead's: releasePlayhead lets it go; one still waiting is a tap.
        this.head.heardUp(event.pointerId);
        this.pointerUp(event);
        this.head.letGo();
        this.host.holdEnded(event.pointerId);
    };

    private readonly onPointerCancel = (event: PointerEvent): void => {
        this.fingers.forget(event.pointerId);
        this.cancelPresses();
        this.host.holdEnded(event.pointerId);
        this.host.changed();
    };

    /** A playhead whose pointer the roll lost is let go, so nothing is left held (#495 UX-08), nor the view (#521 16). */
    private readonly onLostCapture = (event: PointerEvent): void => {
        this.head.lost(event.pointerId);
        this.host.holdEnded(event.pointerId);
    };

    private pointerUp(event: PointerEvent): void {
        const { x, y } = this.at(event);
        const pointer = event.pointerId;
        if (this.fingers.end(event) || this.head.release(pointer, x, this.host.touchReading()) || this.endPlace(event) || this.rules.end(pointer) || this.host.editor.end(pointer, x, y) || this.strip.end(pointer)) {
            return;
        }
        if (this.notes.end(pointer)) {
            return;
        }
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointer) {
            return;
        }
        const click = !drag.edge && Math.abs(x - drag.anchorX) < CLICK_SLOP;
        // An end edge let go on a beat line ends there, not a beat later: music-core rounds an end up to the next beat.
        const current = drag.edge && drag.current > drag.anchor ? Math.max(drag.anchor, drag.current - 1) : drag.current;
        this.host.post("OnRollRange", Math.round(drag.anchor), Math.round(current), click, false, drag.how ?? "create", drag.gesture ?? nextGestureId());
        this.drag = null;
        this.host.changed();
    }

    /**
     * Whether a press of this pointer at (x, y) lands on a handle of the selection's edges by the place rule: only there the
     * edge wins over a playhead, so the blue playhead standing at the selection's start is taken first, as on the score's
     * page (0.8 finding 1, Andrei's answer 3; since v0.6.4 the whole edge won; roll-press.ts).
     */
    private onSelectionHandle(type: string, x: number, y: number): boolean {
        const span = this.host.placeSpan();
        const frame = { xOf: (tick: number) => this.host.view.xOf(tick), header: this.host.header(), height: this.host.canvas.clientHeight };
        return this.pointers.placesAt(type, y) && span !== null && onPlaceHandle(frame, span, x, y);
    }

    /** A point on the roll for the place gesture: its pitch a real number, so a row's centre is its pitch. */
    private placePoint(x: number, y: number, tick: number): PlacePoint {
        return { x, y, tick, pitch: this.host.view.exactPitchAt(y) };
    }

    private movePlace(event: PointerEvent, x: number, y: number): boolean {
        const view = this.host.view;
        const tick = view.tickAtX(Math.max(GUTTER, x));
        if (!this.host.place.move(event.pointerId, this.placePoint(x, y, tick))) {
            return false;
        }
        // Drawing past either side scrolls the roll along, as a range dragged does.
        if (x > this.host.width() - 8 || x < GUTTER + 8) {
            view.keepInView(tick);
        }
        this.host.changed();
        return true;
    }

    private endPlace(event: PointerEvent): boolean {
        const ended = this.host.place.end(event.pointerId);
        if (ended) {
            this.host.changed();
        }
        return ended;
    }

    /** While the Pen is on, the stylus is the Pen's: a gesture belongs to one tool, so on the roll it does nothing (#521 10). */
    private pensOwn(event: PointerEvent): boolean {
        if (actsOnRoll(event.pointerType, this.pointers.penHolds())) {
            return false;
        }
        event.preventDefault();
        return true;
    }

    /**
     * The edge of the selection a press drags, as the tick the other edge keeps; under the iPad's rules the handles over the
     * notes take it with the editor on too, as the selection's handles do in every mode. Null for none.
     */
    private edgeAnchorUnder(type: string, x: number, y: number): number | null {
        const edge = !this.pointers.selectsWith(type) || (this.host.editor.editing && (!this.pointers.ruled || y >= this.host.header())) ? null : this.edgeAt(x);
        const motif = this.host.motif();
        if (edge === null || motif === null) {
            return null;
        }
        return edge === "start" ? motif.end - 1 : motif.start;
    }

    /** Which edge of the selection is under x, if any. */
    private edgeAt(x: number): "start" | "end" | null {
        const motif = this.host.motif();
        const lanes = this.host.lanes();
        // The lanes' handles are the loop strip's: hidden with it in practice view, they take nothing (#543, 0.5.6 tail).
        // Only Select changes the selection, so outside it the handles take nothing (#543 2, 4.2).
        if (motif === null || !modeSelects(this.host.mode()) || (lanes && !this.host.laneShown())) {
            return null;
        }
        // The lanes' handles take a finger (WCAG 2.2, 2.5.8).
        const grab = lanes ? measures.touch / 2 : EDGE_GRAB;
        const view = this.host.view;
        if (Math.abs(x - view.xOf(motif.end)) <= grab) {
            return "end";
        }
        return Math.abs(x - view.xOf(motif.start)) <= grab ? "start" : null;
    }

    /** A press on the bar numbers: a Check flag opens Check at its bar; else it begins selecting bars, or Shift extends. */
    private pressBarNumbers(event: PointerEvent, x: number, y: number, tick: number, pitch: number): void {
        const flag = this.host.checkAt(x);
        if (flag !== undefined) {
            this.host.post("OnRollCheckPicked", flag);
            return;
        }
        // In Navigate the strips select nothing (#543 2): a drag moves the roll, a tap moves the playhead, a hold puts it there.
        if (this.pointers.navigates(event.pointerType)) {
            this.beginNavigatePress(event, x, y, tick, pitch);
            return;
        }
        if (this.pointers.inHiddenLane(y)) {
            return;
        }
        // Select follows the one selection gesture on the strips (#511 2.1, 6.3); Edit notes and the Pen keep their rules.
        const mode = this.host.mode();
        const ruled = mode !== null && mode !== "select";
        if ((ruled && this.rules.begin(event, x, y, tick, pitch)) || !this.pointers.selectsWith(event.pointerType)) {
            return;
        }
        // Shift-click on a bar number extends the selection to that bar.
        if (event.shiftKey && this.host.motif() !== null) {
            this.host.post("OnRollRange", Math.round(tick), Math.round(tick), true, true, "edit", nextGestureId());
            return;
        }
        // Inside the selection a move edits it, outside a hold clears it; without one a press selects at once (#511 2.1).
        this.strip.begin(event, x, y, tick);
        this.host.changed();
    }

    /**
     * A navigating press on the strips (#543 2): it moves the roll as a press on the notes does (a finger or the stylus on
     * the iPad, the mouse on the web), a tap moves the playhead, and held still it puts the black playhead there.
     */
    private beginNavigatePress(event: PointerEvent, x: number, y: number, tick: number, pitch: number): void {
        if (this.host.touchReading() && event.pointerType !== "mouse") {
            this.beginPan(event, x, y);
            return;
        }
        this.rules.begin(event, x, y, tick, pitch);
    }

    /** A press that moves the roll (touch reading); Navigate's hold begins with it. */
    private beginPan(event: PointerEvent, x: number, y: number): void {
        this.fingers.beginPan(event.pointerId, x, y);
        if (this.pointers.navigates(event.pointerType)) {
            this.head.waitForHold(event.pointerId, x, y);
        }
    }

    /**
     * A navigating tap on the iPad's strips (#543 2): on the loop strip inside the selection the position goes there (T13);
     * anywhere else on the bar numbers or the strip the black playhead goes to the beat under it (T12), as a click on the
     * web's bar numbers does. It never selects.
     */
    private tapStrip(tick: number, y: number): void {
        const model = this.host.model();
        if (model === null || this.pressLoopLane(y, tick)) {
            return;
        }
        this.host.playhead.go(beatAt(model.bars, tick), false);
    }

    /** A move of the note dragged in the editor, or the editor's cursor over the notes while no range is dragged. */
    private moveEdit(event: PointerEvent, x: number, y: number): boolean {
        const editor = this.host.editor;
        if (editor.move(event.pointerId, x, y)) {
            return true;
        }
        const model = this.host.model();
        const cursor = this.drag === null && model !== null ? editor.cursor(model, x, y) : null;
        if (cursor === null) {
            return false;
        }
        this.host.canvas.style.cursor = cursor;
        return true;
    }

    /** A move of the range dragged, which scrolls the roll along past either edge; without one, the cursor over an edge. */
    private moveRange(event: PointerEvent, x: number): void {
        const drag = this.drag;
        if (drag === null || event.pointerId !== drag.pointer) {
            this.host.canvas.style.cursor = x >= GUTTER && this.edgeAt(x) !== null ? "ew-resize" : "";
            return;
        }
        const view = this.host.view;
        drag.current = Math.max(0, view.tickAtX(Math.max(GUTTER, x)) + (drag.offset ?? 0));
        if (x > this.host.width() - 8 || x < GUTTER + 8) {
            view.keepInView(drag.current);
        }
        this.host.changed();
    }

    /** How the strips' selection gesture reaches the roll and the page. */
    private stripHost(): StripHost {
        return {
            selection: () => this.host.motif(),
            beginRange: (pointer, x, tick, how, gesture) => {
                this.drag = { pointer, anchorX: x, anchor: tick, current: tick, edge: false, how, gesture };
                this.host.changed();
            },
            beginEdge: (pointer, anchor, offset, gesture) => {
                this.drag = { pointer, anchorX: Number.NaN, anchor, current: anchor, edge: true, offset, how: "edit", gesture };
            },
            beginPan: (pointer, x, y) => this.fingers.beginPan(pointer, x, y, true),
            clear: (gesture) => this.host.post("OnRollClear", gesture),
            cancelled: (gesture) => this.host.post("OnRollGestureCancelled", gesture),
            hint: () => this.host.post("OnRollHoldHint"),
            keepsSelection: () => holdKeepsSelection(this.host.mode(), this.host.playing()),
            tap: (tick, y, inside) => {
                // A tap inside the selection on the loop strip moves the position there; a tap elsewhere leaves the selection,
                // and under the modes places the playhead on the beat, as a click there always has.
                if (inside && this.pressLoopLane(y, tick)) {
                    return;
                }
                if (this.host.mode() !== null) {
                    this.rules.click({ tick, pitch: -1, shift: false, y });
                }
            },
        };
    }
}
