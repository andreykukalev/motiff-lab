// The piano roll and keyboard views (S1-W2, S1-W3): notes on a bar and beat grid drawn on canvas,
// coloured and patterned by track or voice, selectable by pointer or keyboard, and a keyboard that
// lights the keys sounding at the playhead. Both redraw every animation frame from the player's
// currentTick (decision record 0007). Blazor imports this module through JS interop.

import {
    ENGLISH_ROLL_LABELS,
    RollModel,
    fill,
    keyIndexAt,
    laneKey,
    pitchClassName,
    pitchName,
    type ColourBy,
    type RollLabels,
    type RollNote,
    type RollPayload,
} from "./roll-model.js";
import { THEME_CHANGED } from "../layout/theme.js";
import { PRACTICE_EVENT, practiced, shownAt } from "../layout/practice-view.js";
import { measures, palette, prepare, readPalette } from "./roll-palette.js";
import { CHECK_FLAG, RollPainter, checkPlaces, drawPlayheadLine, type CheckFlag, type NotesFrame, type NotesScene, type VariationNote } from "./roll-painter.js";
import { drawKeyboard, keyAt, litKeys, type KeyLayout } from "./roll-keys-draw.js";
import { BAR_ROW, DEFAULT_ZOOM, GUTTER, RollViewport, type ViewportHost } from "./roll-viewport.js";
import { PlayheadControl, onPlayhead, type PlayheadContext, type Span } from "./roll-playhead.js";
import { type InteractionMode, modeOf } from "../score/score-modes.js";
import { drawBox, drawPlayheads, modeSelects, playheadKnobs, rollPlaces } from "./roll-modes.js";
import { LOOP_LANE, OVERVIEW_HEIGHT, drawLoopLane, drawOverview, drawPosition, laneShown, overviewGrab, overviewTick, viewGrabOffset, type StripInks } from "./roll-strips.js";
import { viewOf, type Handle, type ModelState } from "../playback/playback-model.js";
import { PlayheadDrag, type DragMessage } from "../playback/playhead-drag.js";
import { knobHeadAt, laneDragTick, laneHeadAt, lineTakesHead, reachedFrom } from "./roll-head-drag.js";
import { CLICK_SLOP } from "./roll-press.js";
import { RollPlaceGesture, type PlaceHost, type PlacePoint } from "./roll-place-gesture.js";
import { drawPickedNotes, drawPlaceEdges, drawPlacePreview } from "./roll-place-draw.js";
import { movedNote, type PickShape } from "./roll-place.js";
import { nextGestureId, type SelectionChange } from "../score/selection-gesture.js";
import { type GridSpan, gridLines } from "../score/selection-grid.js";
import { TICKS_PER_QUARTER } from "../score/score-timeline.js";
import { sameZoom, type AxisZoom } from "./roll-zoom.js";
import { frameOf, overviewSpan, revealSelection, selectionShift, viewAlong, viewOnLoad, zoomOutAfter, type ViewFrame, type ZoomOutEvent } from "./roll-view.js";
import { RollEditor, type EditTool, type EditorHost } from "./roll-editor.js";
import { RollLegend } from "./roll-legend.js";
import { RollInputRouter, type InputHost } from "./roll-input.js";
import type { PointerRules } from "./roll-pointer-rules.js";

interface DotNetObject {
    invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** The part of the playback player (Scripts/playback/playback.ts) the views read every frame. */
interface PlayerHandle {
    currentTick(): number;
    state(): string;
    /** Strikes and releases a key by hand, over playback or without it. */
    noteOn?(pitch: number, velocity?: number): void;
    noteOff?(pitch: number): void;
    /** The playback model while the player holds it (#495 UX-10), else null. */
    model?(): ModelState | null;
    /**
     * A playhead's handle taken, dragged and let go (#495 UX-08, T14–T16): the web's player hears it straight from the roll;
     * with the passage Expand is bound to, it is held inside it (#533 2).
     */
    drag?(message: DragMessage, passage?: Span | null): void;
}

/** Fields per note in setVariation's packed array: onset, end, pitch, the motif note's onset and pitch. */
const VARIATION_FIELDS = 5;

/** The zoom buttons' step, and the wheel's and the keys' (testing item 13). */
const ZOOM_STEP = 1.25;
/** The target round a Check flag on the bar numbers (roll-painter.ts CHECK_FLAG). */
const CHECK_TARGET = 22;
/** How near a mouse takes the playhead's handle under the bar numbers (Andrei, v0.2). */
const KNOB_MOUSE = 20;
/**
 * How long a span let go is shown as the selection while the host has not answered (v0.6.4): long enough for music-core's
 * answer behind a busy call, short enough that a span refused without an answer goes back by itself.
 */
const PLACED_MS = 2500;
const ROLL_HEIGHT = 360;
/** The notes area never gets shorter than this when the roll fills its panel (0012). */
const MIN_ROLL_HEIGHT = 160;

/**
 * A click outside the roll clears the selection unless it lands on a control or inside an element
 * marked data-keeps-selection, such as the panels that show or act on the selection. The score keeps it too: a tap on a note
 * there points at that note here, and the score clears its own selection (0018 section 3).
 */
const KEEPS_SELECTION = "button, input, select, textarea, label, a, summary, [role=button], [data-keeps-selection], .score";

/**
 * Space plays and pauses from anywhere on the page, buttons and sliders included (Enter still
 * presses a button), except where Space types or ticks: text fields, lists, checkboxes and radios.
 */
const SPACE_OWNERS = "input:not([type=range]), select, textarea, [contenteditable]";

/** How a phrase start was found, as setPhrases sends it: 1 inferred. */
const PHRASE_INFERRED_KIND = 1;

/** The names setNames takes, in its order. */
type RollNamesArgs = [notes: string[], keyTicks: number[], rows: string[][], short?: string[], shortRows?: string[][], octaves?: string[]];

/**
 * The last notes and names Blazor sent, by the keys it gave them (#419): the roll in the drawer and the one in focus that
 * Expand opens show the same piece, so the second takes the model and the names the first was sent instead of having
 * thousands of notes cross over from .NET again. The model never changes once made, so the rolls can share it.
 */
let keptModel: { key: string; model: RollModel } | null = null;
let keptNames: { key: string; names: RollNamesArgs } | null = null;
/**
 * The player's zoom and view as the last roll drew them (#511 section 5.2), by the key of its notes: the web's roll in focus
 * that Expand opens, and the drawer's opened again, start from the zoom the player left, and on the same notes from the
 * same place, so going into and out of Expand changes neither.
 */
let keptFrame: { key: string | null; frame: ViewFrame } | null = null;

/** The key of a frame brought back from the device (PDF | Notes, #511 section 10): the next roll takes its view on any notes. */
const RESTORED_FRAME = "restored";

/**
 * Brings back the zoom and the view the player left the roll at, kept on the device while the other view of the piece was
 * shown (PDF | Notes, #511 section 10): the next roll made, on the notes engraved again, starts from them. False for a frame
 * that is not one.
 */
export function restoreRollFrame(value: unknown): boolean {
    const frame = frameOf(value);
    if (frame !== null) {
        keptFrame = { key: RESTORED_FRAME, frame };
    }
    return frame !== null;
}

/** The zoom and the view the last roll drew, or the frame brought back and not yet drawn; null before any. */
export function keptRollFrame(): ViewFrame | null {
    return keptFrame === null ? null : { ...keptFrame.frame };
}

export class PianoRoll {
    private readonly host: HTMLElement;
    private readonly dotnet: DotNetObject;
    private readonly roll: HTMLCanvasElement;
    private readonly keys: HTMLCanvasElement;
    private readonly scroller: HTMLInputElement;
    private readonly legend = new RollLegend();
    /** The floating zoom (testing item 13): − the percentage + and Fit, over the corner of the notes, not a panel (0018). */
    private readonly zoomBar: HTMLElement;
    private readonly zoomOut: HTMLButtonElement;
    private readonly zoomIn: HTMLButtonElement;
    private readonly zoomLevel: HTMLElement;
    private readonly fitButton: HTMLButtonElement;
    /** Zoom Out (#521 2.2): back to the general overview, shown after Fit laid a selection across the roll. */
    private readonly overviewButton: HTMLButtonElement;
    /** Whether Zoom Out shows (roll-view.ts zoomOutAfter): from Fit of a selection until the overview or the selection goes. */
    private zoomOutShown = false;
    /** The playhead the player moves (roll-playhead.ts): its control for the keyboard and a screen reader, and its drag. */
    private readonly playhead: PlayheadControl;
    /** The whole piece in one line over the roll (v0.5.2, #479 item 25), shown with the lanes. */
    private readonly overview: HTMLCanvasElement;
    /**
     * The lanes of the iPad's roll (v0.5.2, #479 item 25, roll-strips.ts): the black and the blue playheads of the playback
     * model (#495 UX-10); the loop strip under the bar numbers and the whole piece over them. Off on the web.
     */
    private lanes = false;
    /** Whether the selection loops (Цикл on), and the position written as bar.beat for the whole-piece strip. */
    private looping = false;
    private placeLabel = "";
    /**
     * A press on the whole-piece strip (#521 1): on the black dot it holds the position (the lanes' black playhead, dragged
     * through laneDrag); anywhere else the frame round the bars on screen, `offset` ticks right of the view's left edge.
     */
    private overviewPress: { pointer: number; position: boolean; offset: number } | null = null;
    /**
     * The zoom, the view and the passage Expand binds the roll to (roll-viewport.ts). Bound to a passage (#521 19) the roll
     * shows only it, fitted across its width, playback is followed no further than its end, and no pointer changes the
     * selection; its playhead is taken and dragged inside it, as the model's blue one (#533 2), and no band shades it: the
     * passage is all there is to work on (#533 3).
     */
    private readonly view = new RollViewport(this.viewportHost());
    /** What draws the notes canvas (roll-painter.ts) and the keys (roll-keys-draw.ts). */
    private readonly painter = new RollPainter();
    /** Pointers down on the roll or its whole-piece strip (#521 16): while any is, playback does not move the view. */
    private readonly holding = new Set<number>();
    /**
     * The model's playheads dragged by their handles (#495 UX-08, roll-head-drag.ts): the handle taken, its places, its end.
     * The web's player hears them straight from the roll (slice W3); the iPad's page passes them on to Swift (OnRollDrag).
     */
    private readonly laneDrag = new PlayheadDrag((message) => {
        if (this.player?.drag !== undefined) {
            this.player.drag(message, this.view.passage);
            return;
        }
        void this.dotnet.invokeMethodAsync("OnRollDrag", message.phase, message.handle, "tick" in message ? message.tick : null);
    });
    /** Where the playheads were drawn across the roll lately while the music played, for a press to find them (#533 4). */
    private drawnHeads: { at: number; black: number; blue: number | null }[] = [];
    private readonly resizeObserver: ResizeObserver;
    private model: RollModel | null = null;
    private colourBy: ColourBy = "track";
    private noteNames = true;
    /** Each note's name spelled for its key by music-core (setNames), by note index; null until sent, when the names are unspelled. */
    private spelled: string[] | null = null;
    /** The same names without the octave, written inside the notes (testing item 13); null until sent. */
    private shortNames: string[] | null = null;
    /** Where each key signature starts, and the row names spelled for it without the octave, MIDI 0 to 127. */
    private keyTicks: number[] = [];
    private rowNames: string[][] = [];
    /** The row names with the octave, for the C named on the octave band where its words do not fit. */
    private fullRowNames: string[][] = [];
    /** Each octave's name in words, MIDI octave −1 first, for the band down the left; empty until sent. */
    private octaveNames: string[] = [];
    /** The bars Check flags, drawn on the bar numbers. */
    private checks: CheckFlag[] = [];
    /** The lowest and highest pitch of the selection's notes, brought into view with a new selection out of it; null for none. */
    private motifPitches: { lowest: number; highest: number } | null = null;
    private laneIndex = new Map<number, number>();
    /** Fit pressed before the roll had its width: it fits once it has. */
    private pendingFit = false;
    /** The key of the notes shown, under which the player's zoom and view are kept for the next roll (keptFrame). */
    private notesKey: string | null = null;
    /** When the last frame was drawn, for easing the view by the time that passed. */
    private lastFrame = 0;
    /**
     * The zoom on each axis (the iPad's roll, v0.5.2, #479 item 23, roll-zoom.ts): the host draws its own controls for the
     * bars and the octaves in view (OnRollZoom), so the floating zoom goes; a pinch zooms the axis it spreads along and two
     * fingers move the roll too. Off on the web.
     */
    private axisZoom = false;
    private zoomReported: AxisZoom | null = null;
    /**
     * Touch reading (the iPad's roll, #338): a finger on the notes moves through the music instead of selecting, two
     * fingers zoom the time only, and a tap never clears the selection or sounds a note. Off by default, as the web app has it.
     */
    private touchReading = false;
    private selected = new Set<number>();
    private focusIndex: number | undefined;
    /** The motif selection (S1-M1, S1-M2): its range and its notes, or null for none. */
    private motif: { start: number; end: number; notes: Set<number>; alone: boolean } | null = null;
    /** The selection's span shown before the notes came back (BUG-04), until the selection is sent again: not a new one. */
    private keptSpan: Span | null = null;
    /** Selection by where you touch (#601, roll-place-gesture.ts): the box or the lasso, and whether the selection was chosen by notes. */
    private pickShape: PickShape = "box";
    private picked = false;
    /**
     * A span let go and sent to the host, until the host's selection comes back (setMotif, clearMotif) or PLACED_MS pass
     * (v0.6.4): the band and the edges show it, and a press meanwhile reads it as the selection, so the edge never jumps
     * back on release and an edge taken again at once is taken where it now stands.
     */
    private placed: { start: number; end: number } | null = null;
    private placedTimer = 0;
    /** The tuplet groups as blocks (the score's, through the host), where the selection's grid has no line inside. */
    private tuplets: GridSpan[] = [];
    private gridMemo: { model: RollModel | null; lines: number[] } = { model: null, lines: [] };
    private readonly place = new RollPlaceGesture(this.placeHost());
    /** Indexes of the notes the Notes filter keeps, or null for all; the others are drawn faint and never light up. */
    private filtered: Set<number> | null = null;
    /** The range that plays for the selection [start, end), when it differs from what is selected; null for none. */
    private playRange: { start: number; end: number } | null = null;
    /** Another version's notes in the bar the player writes in a merge (testing item 34): dashed plum outlines to drag in. */
    private ghosts: { onset: number; end: number; pitch: number }[] = [];
    /** The variation of the motif, drawn as outlines over it, or null for none. */
    private variation: VariationNote[] | null = null;
    /** The places of the fact chosen on Understand (#25): their stretches of time and their notes, or null for none. */
    private highlight: { spans: { start: number; end: number }[]; notes: Set<number> } | null = null;
    /** Where phrases start and whether each start is inferred (#18). */
    private phrases: { tick: number; inferred: boolean }[] = [];
    /** Which sources are heard, so only notes being heard light up. */
    private originalAudible = true;
    private variationAudible = false;
    /** The key held on the keyboard by a pointer, so a drag across the keys plays each in turn. */
    private keysPress: { pointer: number; pitch: number | null } | null = null;

    /** Keys struck by hand on the roll or the keyboard, lit while held. */
    private readonly live = new Map<number, number>();
    /** Key x positions from the last keyboard draw, for finding the key under the pointer. */
    private keyLayout: KeyLayout | null = null;
    /** Where a press outside the roll started, so that a drag (such as selecting text) is not a click. */
    private outsidePress: { x: number; y: number; target: Element } | null = null;
    private player: PlayerHandle | null = null;
    private tickNow = 0;
    private dirty = true;
    private frame = 0;
    private disposed = false;
    private labels: RollLabels;
    /** The note editor (testing item 15, roll-editor.ts). */
    private readonly editor = new RollEditor(this.editorHost());
    /** The notes canvas's input router (roll-input.ts), made once the canvas and the playhead's control are. */
    private readonly input: RollInputRouter;
    /** The modes of version 0.5 (roll-modes.ts); null while the experiment is off. */
    private mode: InteractionMode | null = null;
    /** The selection, Цикл and transport of the player's model last drawn, so a change of them alone draws again. */
    private modelDrawn = "";

    constructor(host: HTMLElement, dotnet: DotNetObject, labels: RollLabels = ENGLISH_ROLL_LABELS) {
        this.host = host;
        this.dotnet = dotnet;

        this.roll = document.createElement("canvas");
        this.roll.className = "roll-notes";
        this.roll.tabIndex = 0;
        this.roll.setAttribute("role", "application");

        this.scroller = document.createElement("input");
        this.scroller.type = "range";
        this.scroller.className = "roll-scroll";
        this.scroller.min = "0";
        this.scroller.step = "1";

        this.keys = document.createElement("canvas");
        this.keys.className = "roll-keys";
        this.keys.setAttribute("role", "img");

        const zoom = floatingZoom({
            out: () => this.zoomBy(1 / ZOOM_STEP),
            in: () => this.zoomBy(ZOOM_STEP),
            fit: () => this.fit(),
            overview: () => this.zoomOutToOverview(),
        });
        ({ bar: this.zoomBar, out: this.zoomOut, level: this.zoomLevel, in: this.zoomIn, fit: this.fitButton, overview: this.overviewButton } = zoom);

        this.labels = { ...ENGLISH_ROLL_LABELS, ...labels };
        this.applyLabels();
        this.playhead = new PlayheadControl(
            () => this.playheadContext(),
            (tick, play) => void this.dotnet.invokeMethodAsync("OnRollSeek", tick, play),
            () => this.invalidate(),
        );

        this.overview = document.createElement("canvas");
        this.overview.className = "roll-overview";
        this.overview.style.display = "none";
        this.overview.setAttribute("aria-hidden", "true");
        this.overview.addEventListener("pointerdown", this.onOverviewDown);
        this.overview.addEventListener("pointermove", this.onOverviewMove);
        this.overview.addEventListener("pointerup", this.onOverviewUp);
        this.overview.addEventListener("pointercancel", this.onOverviewUp);
        this.overview.addEventListener("lostpointercapture", this.onOverviewUp);

        this.host.replaceChildren(this.overview, this.roll, this.scroller, this.keys, this.legend.element, this.zoomBar, this.playhead.element);

        this.input = new RollInputRouter(this.inputHost());
        this.roll.addEventListener("keydown", this.onKeyDown);
        this.roll.addEventListener("wheel", this.onWheel, { passive: false });
        this.keys.addEventListener("pointerdown", this.onKeysDown);
        this.keys.addEventListener("pointermove", this.onKeysMove);
        this.keys.addEventListener("pointerup", this.onKeysUp);
        this.keys.addEventListener("pointercancel", this.onKeysUp);
        this.scroller.addEventListener("input", this.onScroll);
        document.addEventListener("pointerdown", this.onDocumentPointerDown);
        document.addEventListener("pointerup", this.onDocumentPointerUp);
        document.addEventListener("keydown", this.onDocumentKeyDown);
        document.addEventListener(THEME_CHANGED, this.onThemeChanged);
        document.addEventListener(PRACTICE_EVENT, this.onPracticeChanged);
        this.resizeObserver = new ResizeObserver(() => this.onResize());
        this.resizeObserver.observe(host);
        this.frame = requestAnimationFrame(this.tick);
    }

    /**
     * Shows a piece, replacing what was shown, and keeps it under the key given for the next roll. The selection is cleared.
     * With `keepView` (the same piece's notes again, after an edit) the zoom and the stretch in view stay (BUG-04).
     */
    load(payload: RollPayload, key?: string, keepView = false): void {
        const model = RollModel.fromPayload(payload);
        if (key !== undefined) {
            keptModel = { key, model };
        }
        this.show(model, keepView, key ?? null);
    }

    /** Shows the piece kept under the key, as load does; false when another is kept, so Blazor sends this one. */
    loadKept(key: string, keepView = false): boolean {
        if (keptModel?.key !== key) {
            return false;
        }
        this.show(keptModel.model, keepView, key);
        return true;
    }

    private show(model: RollModel, keepView: boolean, key: string | null): void {
        const motif = this.motif;
        const span = motif === null ? this.keptSpan : { start: motif.start, end: motif.end };
        let view = viewOnLoad({ scroll: this.view.scroll, pitchTop: this.view.pitchTop, span }, keepView, this.editor.editing);
        // A roll made anew (Expand on the web, the drawer opened again) takes the player's zoom, and on the same notes the view too.
        const kept = this.model === null ? keptFrame : null;
        if (kept !== null && (kept.key === RESTORED_FRAME || (key !== null && kept.key === key))) {
            view = { ...view, scroll: kept.frame.scroll, pitchTop: kept.frame.pitchTop };
        }
        this.notesKey = key;
        if (!keepView) {
            this.setZoomOut(zoomOutAfter("selectionCleared"));
        }
        this.model = model;
        this.selected = new Set();
        this.focusIndex = undefined;
        this.motif = null;
        this.motifPitches = null;
        this.playRange = null;
        this.filtered = null;
        this.variation = null;
        this.highlight = null;
        this.spelled = null;
        this.shortNames = null;
        this.input.dropPresses();
        this.editor.forget(model);
        this.keptSpan = view.span;
        this.view.show(view, kept === null ? null : kept.frame);
        if (kept !== null) {
            this.showZoom();
        }
        this.updateLanes();
    }

    /**
     * Turns the note editor on or off (testing item 15) with its tool. While it is on, a press on the notes edits them
     * instead of selecting: a note dragged moves (by sixteenths and rows), its end dragged changes its length, a press on an
     * empty place adds a note with Add, a note pressed with Delete is taken out; Delete or Backspace takes out the note
     * picked and the arrows move it. The roll keeps its scroll and zoom as the notes come back after each edit.
     */
    setEditing(on: boolean, tool: EditTool = "move"): void {
        this.editor.set(on, tool);
        this.roll.classList.toggle("editing", on);
        this.invalidate();
    }

    /** Picks a note in the editor, such as one just added, so the arrows and Delete act on it. */
    pickForEdit(id: string | null): void {
        this.editor.pick(id);
        this.invalidate();
    }

    /** The mode the bar chose (version 0.5, roll-modes.ts); a null mode keeps the roll as it was. */
    setRules(mode: string | null): void {
        const before = this.mode;
        this.mode = modeOf(mode);
        if (this.mode !== before) {
            // Another tool reads presses its own way: one begun under the old tool leaves nothing behind (#543 9).
            this.input.cancelPresses();
        }
        if (this.mode === null) {
            this.laneDrag.release();
            delete this.roll.dataset.mode;
        } else {
            this.roll.dataset.mode = this.mode;
        }
        this.invalidate();
    }

    /**
     * The names to write, spelled by music-core for the key as the score spells them (0018 section 4): one per note in load
     * order, and per key signature (from where it starts) one per row, MIDI 0 to 127, in the player's naming (C4 or до¹).
     */
    setNames(notes: string[], keyTicks: number[], rows: string[][], short?: string[], shortRows?: string[][], octaves?: string[], key?: string): void {
        if (key !== undefined) {
            keptNames = { key, names: [notes, keyTicks, rows, short, shortRows, octaves] };
        }
        const count = this.model?.notes.length ?? -1;
        this.spelled = notes.length === count ? notes : null;
        this.shortNames = short?.length === count ? short : null;
        this.keyTicks = keyTicks;
        this.rowNames = shortRows ?? rows;
        this.fullRowNames = rows;
        this.octaveNames = octaves ?? [];
        this.invalidate();
    }

    /** Writes the names kept under the key, as setNames does; false when others are kept, so Blazor sends these. */
    setNamesKept(key: string): boolean {
        if (keptNames?.key !== key) {
            return false;
        }
        this.setNames(...keptNames.names);
        return true;
    }

    /** The bars Check flags (0018 section 9): an amber "?" on each bar's number, which opens Check at that bar. */
    setChecks(checks: { tick: number; bar: number; label: string }[]): void {
        this.checks = checks;
        this.renderLegend();
        this.invalidate();
    }

    /**
     * Shows a note pointed at elsewhere, such as on the score: selects the note at that tick and key, brings it into view and
     * writes its name over it. A tick with no such note changes nothing.
     */
    pointAt(tick: number, pitch: number): void {
        const model = this.model;
        const index = model?.noteAt(tick, pitch);
        if (model === null || index === undefined) {
            return;
        }
        this.view.keepInView(model.notes[index].onset);
        // A note on a row out of view brings its row into the middle of the rows shown.
        const { low, high } = this.view.pitchRange();
        if (pitch < low || pitch > high) {
            this.view.setPitchTop(pitch + Math.floor((high - low) / 2));
        }
        this.selectIndex(index, false);
    }

    /** Writes each note's name inside it, where it fits (0018 section 4: on by default). */
    setNoteNames(on: boolean): void {
        this.noteNames = on;
        this.invalidate();
    }

    setColourBy(by: ColourBy): void {
        this.colourBy = by;
        this.updateLanes();
    }

    /**
     * Zooms in (factor above 1) or out, the time and the rows together (testing item 13), keeping the playhead, or else the
     * left edge, where it is; read by touch, the time only (roll-viewport.ts).
     */
    zoomBy(factor: number, anchorX?: number): void {
        this.view.zoomBy(factor, anchorX);
    }

    /**
     * Fit (testing item 13, #511 section 5.1, #521 2.1), only when the player asks, one action and never a toggle: lays the
     * selection across the roll's width as it is now with a margin either side, or without a selection the general overview
     * (overviewSpan: about sixteen bars round the playhead, the playhead in the middle), its rows in the roll's height as far
     * as the rows' limits allow, and glides there. What it made is then the player's zoom: nothing fits again by itself.
     * Before the roll has its width, it fits once it has.
     */
    fit(): void {
        const model = this.model;
        if (model === null || this.host.clientWidth === 0) {
            this.pendingFit = true;
            this.invalidate();
            return;
        }
        this.pendingFit = false;
        const motif = this.motif;
        const passage = this.view.passage;
        if (passage !== null) {
            this.view.fitPassage(model, passage);
        } else if (motif === null) {
            this.fitStretch(model, this.overviewStretch(model), "overview");
        } else {
            this.fitStretch(model, motif, "fitSelection");
        }
    }

    /**
     * Binds the roll to the passage Expand shows (#521 19), its [start, end) in ticks, or lets it go (null). Bound, the roll
     * lays the passage across its width at once, the zoom and the view before it kept; let go, they come back as they were
     * (#521 17), so the drawer's roll is where the player left it. The selection is the host's, as always.
     */
    setPassage(span: Span | null): void {
        if (!this.view.bind(span)) {
            return;
        }
        this.input.dropRange(span !== null);
        this.setZoomOut(this.zoomOutShown);
        if (span !== null) {
            this.fit();
        }
    }

    /**
     * Zoom Out (#521 2.2): the general overview round the playhead, as Fit shows it without a selection; the selection stays
     * as it is, whichever it is now, and Zoom Out is hidden until Fit lays a selection across the roll again.
     */
    zoomOutToOverview(): void {
        const model = this.model;
        // Bound to a passage there is nothing to see round it (#521 19.2): Zoom Out is not offered.
        if (model === null || this.host.clientWidth === 0 || this.view.passage !== null) {
            return;
        }
        this.fitStretch(model, this.overviewStretch(model), "overview");
    }

    /** The general overview's stretch round the playhead as it is drawn now (roll-view.ts overviewSpan). */
    private overviewStretch(model: RollModel): Span {
        return overviewSpan(model.bars.map((bar) => ({ start: bar.onset, end: bar.end })), this.tickNow, model.end);
    }

    /** Glides to a stretch laid across the roll with its notes' rows in its height; Zoom Out shows after a selection's. */
    private fitStretch(model: RollModel, span: Span, event: ZoomOutEvent): void {
        this.setZoomOut(zoomOutAfter(event));
        this.view.glideToStretch(model, span);
    }

    /** Shows or hides Zoom Out: the web's button in the floating zoom, and the iPad's in what the roll reports (OnRollZoom). */
    private setZoomOut(shown: boolean): void {
        this.zoomOutShown = shown;
        this.overviewButton.hidden = !shown || this.view.passage !== null;
        this.invalidate();
    }

    /**
     * Touch reading (#338), for the iPad's roll: a finger on the notes moves through the music sideways and through the rows
     * up and down, never selecting; two fingers zoom the time only; a tap on the notes neither selects, clears nor sounds.
     * The bar numbers still select bars.
     */
    setTouchReading(on: boolean): void {
        this.touchReading = on;
        this.input.dropPan();
        this.invalidate();
    }

    /**
     * The zoom on each axis (v0.5.2, #479 item 23), for the iPad's roll: the floating zoom goes, as the host draws its own
     * controls from what OnRollZoom tells it (the bars and the octaves in view, and which steps are left); a pinch zooms the
     * time across and the pitch up and down, and two fingers move the roll.
     */
    setAxisZoom(on: boolean): void {
        this.axisZoom = on;
        this.zoomBar.style.display = on ? "none" : "";
        this.zoomReported = null;
        this.invalidate();
    }

    /**
     * Which pointers edit with the editor on ("touch", "pen") and how many fingers move the roll; null for every pointer, one
     * finger. `penSelects` false: the Pen is on, so the stylus changes no selection here (#511 4.1).
     */
    setPointerRules(rules: PointerRules | null): void {
        this.input.setPointerRules(rules);
        this.invalidate();
    }

    /**
     * Selection by where you touch (#601, once the experiment experimental.placeSelection), as the iPad tells it: on or off, and whether a stylus
     * is used, so a finger moves the roll beside it; without one, one finger selects. Off, Select is as in version 0.6.1.
     */
    setPlaceSelection(on: boolean, stylus: boolean): void {
        this.input.setPlaceSelection(on, stylus);
        this.invalidate();
    }

    /** The tuplet groups as blocks, [start, end) in ticks, that the selection's grid never splits (v0.6.4, Andrei 19:54Z). */
    setTuplets(spans: readonly GridSpan[]): void {
        this.tuplets = spans.filter((span) => Number.isFinite(span.start) && Number.isFinite(span.end) && span.end > span.start).map((span) => ({ start: span.start, end: span.end }));
        this.gridMemo = { model: null, lines: [] };
    }

    /** The lines the selection's edges stop at (score/selection-grid.ts): sixteenths from each bar's start, a tuplet one block. */
    private selectionGrid(): number[] {
        const model = this.model;
        if (model === null) {
            return [];
        }
        if (this.gridMemo.model !== model) {
            this.gridMemo = { model, lines: gridLines(model.bars.map((bar) => ({ start: bar.onset, end: bar.end })), this.tuplets) };
        }
        return this.gridMemo.lines;
    }

    /** The span let go and not answered yet, or the selection the host gave; null for none. */
    private placeSpan(): { start: number; end: number } | null {
        return this.placed ?? this.motif;
    }

    /** A span let go: shown and read as the selection until the host answers (setMotif, clearMotif) or PLACED_MS pass. */
    private keepPlaced(span: { start: number; end: number }): void {
        this.placed = { start: span.start, end: span.end };
        window.clearTimeout(this.placedTimer);
        this.placedTimer = window.setTimeout(() => {
            this.placed = null;
            this.invalidate();
        }, PLACED_MS);
    }

    private dropPlaced(): void {
        window.clearTimeout(this.placedTimer);
        this.placed = null;
    }

    /** How Select picks notes: a box (the default) or a lasso, the switch in the roll's head (report «Рамка или лассо»). */
    setPickShape(shape: string): void {
        this.pickShape = shape === "lasso" ? "lasso" : "box";
    }

    /** Whether the selection shown was chosen by notes: its notes are ringed and a drag of one moves them (#601). */
    setPicked(picked: boolean): void {
        this.picked = picked;
        this.invalidate();
    }

    /** What the viewport reads of the roll: its size, where the notes start, the notes, the reading, the playhead. */
    private viewportHost(): ViewportHost {
        return {
            width: () => this.host.clientWidth,
            height: () => this.rollHeight(),
            header: () => this.header,
            model: () => this.model,
            touchReading: () => this.touchReading,
            playhead: () => this.tickNow,
            zoomed: () => this.showZoom(),
            changed: () => this.invalidate(),
        };
    }

    /** What the note editor reads of the roll and does through it. */
    private editorHost(): EditorHost {
        return {
            view: this.view,
            model: () => this.model,
            strike: (pitch) => this.strike(pitch),
            release: (pitch) => this.release(pitch),
            post: (method, ...args) => void this.dotnet.invokeMethodAsync(method, ...args),
            changed: () => this.invalidate(),
        };
    }

    /** What the input router reads of the roll and does through it: the gestures' own objects, the state shown, the host's calls. */
    private inputHost(): InputHost {
        return {
            canvas: this.roll,
            view: this.view,
            editor: this.editor,
            place: this.place,
            playhead: this.playhead,
            laneDrag: this.laneDrag,
            width: () => this.host.clientWidth,
            header: () => this.header,
            model: () => this.model,
            motif: () => (this.motif === null ? null : { start: this.motif.start, end: this.motif.end }),
            placeSpan: () => this.placeSpan(),
            mode: () => this.mode,
            touchReading: () => this.touchReading,
            axisZoom: () => this.axisZoom,
            lanes: () => this.lanes,
            laneShown: () => this.laneShown(),
            hasPlayer: () => this.player !== null,
            playing: () => this.playing(),
            playheadUnder: (type, x, y) => this.playheadUnder(type, x, y),
            playheadSpan: () => this.playheadSpan(),
            headBand: () => this.view.passage ?? (this.lanes ? this.motif : (this.heldModel()?.selection ?? this.motif)),
            checkAt: (x) => this.checkAt(x),
            strike: (pitch) => this.strike(pitch),
            release: (pitch) => this.release(pitch),
            post: (method, ...args) => void this.dotnet.invokeMethodAsync(method, ...args),
            selectNote: (index, add) => this.selectIndex(index, add),
            clearAll: () => this.clearAll(),
            holdBegan: (pointer) => this.holdBegan(pointer),
            holdEnded: (pointer) => this.holdEnded(pointer),
            changed: () => this.invalidate(),
        };
    }

    /** The bar of the Check flag on the bar numbers at x (its target round the flag), if any. */
    private checkAt(x: number): number | undefined {
        const model = this.model;
        return model === null ? undefined : checkPlaces(this.checks, model, this.view.timeFrame()).find((place) => Math.abs(x - (place.x + CHECK_FLAG / 2)) <= CHECK_TARGET / 2)?.bar;
    }

    /** How the place gesture reaches the roll and the page: what it reads, and what it picks or moves as the host's calls. */
    private placeHost(): PlaceHost {
        const notes = (): RollModel["notes"] => this.model?.notes ?? [];
        const ids = (indices: number[]): string[] => indices.map((index) => notes()[index].id);
        return {
            selection: () => {
                const span = this.placeSpan();
                const motif = this.motif;
                if (span === null) {
                    return null;
                }
                const same = motif !== null && span.start === motif.start && span.end === motif.end;
                return { start: span.start, end: span.end, lit: same ? motif.notes : new Set<number>(), picked: same && this.picked };
            },
            notes,
            noteAt: (point) => this.model?.noteAt(point.tick, Math.round(point.pitch)),
            xOf: (tick) => this.view.xOf(tick),
            lines: () => this.selectionGrid(),
            shape: () => this.pickShape,
            now: () => performance.now(),
            pickNotes: (indices, change, gesture) => void this.dotnet.invokeMethodAsync("OnRollNotes", ids(indices), change, gesture),
            // The span exactly as drawn on the grid (v0.6.4): the host selects it as it is, as the score's place rule does.
            pickSpan: (span, how, gesture) => this.sendSpan(span, how, gesture),
            moveBegan: () => void this.dotnet.invokeMethodAsync("OnRollNotesMoveBegan"),
            moveNotes: (indices, steps, gesture) => void this.dotnet.invokeMethodAsync("OnRollNotesMoved", indices.map((index) => ({ id: notes()[index].id, ...movedNote(notes()[index], steps) })), gesture),
            tap: (point, strip, inside) => this.placeTap(point, strip, inside),
            fit: () => this.fit(),
        };
    }

    /**
     * A tap in Select that picks no note: a bar number outside the span selects that bar (report H 4); on the loop strip inside
     * it the position goes there, as before; on empty notes outside it, as before (a tap inside the loop plays from there).
     */
    private placeTap(point: PlacePoint, strip: boolean, inside: boolean): void {
        if (strip && inside) {
            this.input.pressLoopLane(point.y, point.tick);
            return;
        }
        // Outside the selection a tap selects its bar, on the bar numbers and on the notes alike, as a tap on the score's
        // music does (v0.6.4, one selection for both); «Вернуть» brings back the one it replaced.
        const bar = inside ? undefined : this.model?.barAt(point.tick);
        if (bar !== undefined) {
            this.sendSpan({ start: bar.onset, end: bar.end }, this.placeSpan() === null ? "create" : "replace", nextGestureId());
        }
    }

    /** A span of the selection to the host, shown at once and held until the host's selection comes back. */
    private sendSpan(span: { start: number; end: number }, how: SelectionChange, gesture: string): void {
        this.keepPlaced(span);
        this.invalidate();
        void this.dotnet.invokeMethodAsync("OnRollSpan", span.start, span.end, how, gesture);
    }

    /** What selection by where you touch draws in Select: the edges down the roll, the picked notes, a press going on. */
    private drawPlace(context: CanvasRenderingContext2D, width: number, height: number): void {
        const model = this.model;
        if (!this.input.pointers.placeSelection || this.mode !== "select" || model === null || this.view.passage !== null) {
            return;
        }
        const range = this.view.pitchRange();
        const frame = { xOf: (tick: number) => this.view.xOf(tick), yOf: (pitch: number) => this.view.yOf(pitch, range), row: range.row, header: this.header, left: GUTTER, right: width, height };
        const inks = { accent: palette.accent, tint: palette.selectionBand, surface: palette.charcoal, ink: palette.ink };
        const preview = this.place.preview();
        const span = this.placeSpan();
        if (preview !== null) {
            drawPlacePreview(context, frame, preview, model.notes, inks);
        } else if (span !== null) {
            drawPlaceEdges(context, frame, span, inks, null);
            if (this.picked && this.motif !== null && span === this.motif) {
                drawPickedNotes(context, frame, model.notes, this.motif.notes, inks);
            }
        }
    }

    /** A step of the time zoom: + (1) shows fewer bars, − (−1) more, round the middle of the view; the rows stay. */
    zoomTime(direction: 1 | -1): void {
        if (this.model !== null) {
            this.view.zoomTime(this.model, direction);
        }
    }

    /** A step of the pitch zoom: + (1) one octave fewer in view, − (−1) one more, round the middle row; the time stays. */
    zoomPitch(direction: 1 | -1): void {
        this.view.zoomPitch(direction);
    }

    /** Tells the host the bars and the octaves in view and which steps are left, when they change (the zoom on each axis). */
    private reportZoom(): void {
        const model = this.model;
        if (!this.axisZoom || model === null) {
            return;
        }
        const now: AxisZoom = this.view.axisZoom(model, this.zoomOutShown);
        if (!sameZoom(this.zoomReported, now)) {
            this.zoomReported = now;
            void this.dotnet.invokeMethodAsync("OnRollZoom", now);
        }
    }

    /**
     * The lanes (v0.5.2, #479 item 25): the two playheads of the playback model (#495 UX-10), the loop strip under the bar
     * numbers and the whole piece over the roll. A tap in either strip moves the position; on the whole piece
     * the roll goes there too. With the lanes a tap on the notes plays nothing.
     */
    setLanes(on: boolean): void {
        if (!on) {
            this.laneDrag.release();
        }
        this.lanes = on;
        this.overview.style.display = on ? "" : "none";
        this.invalidate();
    }

    /** The position written as bar.beat for the whole-piece strip, and whether the selection loops. */
    setPlace(label: string, looping: boolean): void {
        if (label !== this.placeLabel || looping !== this.looping) {
            this.placeLabel = label;
            this.looping = looping;
            this.invalidate();
        }
    }

    /** Selects the notes with these ids, for a selection made elsewhere. Unknown ids are ignored. */
    setSelection(ids: string[]): void {
        const model = this.model;
        if (model === null) {
            return;
        }
        this.selected = new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
        this.invalidate();
    }

    /**
     * Shows the motif selection: the range [start, end) shaded, its notes at full strength and
     * the other notes faint. When only the motif's notes play (alone), only they light up as
     * they sound. Unknown ids are ignored.
     */
    setMotif(start: number, end: number, ids: string[], alone: boolean): void {
        const model = this.model;
        if (model === null) {
            return;
        }
        const notes = new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
        const { moved } = selectionShift(this.placed ?? this.motif ?? this.keptSpan, start, end);
        // The span let go stays until its answer: the same selection sent again meanwhile (with a new loop) keeps it.
        const again = this.motif !== null && this.motif.start === start && this.motif.end === end;
        if (!again || (this.placed !== null && this.placed.start === start && this.placed.end === end)) {
            this.dropPlaced();
        }
        this.keptSpan = null;
        this.motif = { start, end, notes, alone };
        const pitches = [...notes].map((index) => model.notes[index].pitch);
        this.motifPitches = pitches.length === 0 ? null : { lowest: Math.min(...pitches), highest: Math.max(...pitches) };
        this.input.keepDrag();
        // The zoom is the player's (#511 section 5.1): a selection never fits. Only a new one wholly out of view (made on the
        // score far away, a phrase stepped to) is brought into it; one whose edge moved stays where it is.
        if (moved && this.host.clientWidth > 0) {
            const reveal = revealSelection({
                scroll: this.view.scroll,
                visible: this.view.visibleTicks(),
                rows: this.view.pitchRange(),
                span: { start, end },
                pitches: this.motifPitches,
                maxScroll: this.view.maxScroll(),
            });
            if (reveal !== null) {
                const from = this.view.currentFrame();
                this.view.glideTo(from, { ...from, scroll: reveal.scroll, pitchTop: reveal.pitchTop ?? from.pitchTop });
            }
        }
        this.invalidate();
    }

    clearMotif(): void {
        this.setZoomOut(zoomOutAfter("selectionCleared"));
        this.dropPlaced();
        this.motif = null;
        this.keptSpan = null;
        this.motifPitches = null;
        // The page's answer to a hold's clear (#543 9): a new range the press drags on after it is kept (keptDrag).
        this.input.keepDrag();
        this.invalidate();
    }

    /**
     * Shows the range that plays for the selection: a line over the bar numbers, a band where it runs
     * past the selection and a dashed line where it ends, when that differs from the selection's end.
     */
    setPlayRange(start: number, end: number): void {
        this.playRange = { start, end };
        this.invalidate();
    }

    clearPlayRange(): void {
        this.playRange = null;
        this.invalidate();
    }

    /** Marks where phrases start: packed as pairs of tick and kind (0 from the file, 1 inferred, 2 the player's). */
    setPhrases(packed: number[]): void {
        const phrases: { tick: number; inferred: boolean }[] = [];
        for (let at = 0; at + 2 <= packed.length; at += 2) {
            phrases.push({ tick: packed[at], inferred: packed[at + 1] === PHRASE_INFERRED_KIND });
        }
        this.phrases = phrases;
        this.invalidate();
    }

    /**
     * Highlights the places a chosen fact names: spans packed as pairs of start and end, and the ids
     * of their notes. The first place is scrolled into view. Empty clears the highlight; unknown ids are ignored.
     */
    setHighlight(packed: number[], ids: string[]): void {
        const model = this.model;
        const spans: { start: number; end: number }[] = [];
        for (let at = 0; at + 2 <= packed.length; at += 2) {
            spans.push({ start: packed[at], end: packed[at + 1] });
        }
        if (model === null || spans.length === 0) {
            this.highlight = null;
        } else {
            const notes = new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
            this.highlight = { spans, notes };
            if (this.player?.state() !== "playing") {
                this.view.keepInView(Math.min(...spans.map((span) => span.start)));
            }
        }
        this.renderLegend();
        this.invalidate();
    }

    /** Keeps only the notes with these ids at full strength, for the Notes filter; null keeps all. */
    setFilter(ids: string[] | null): void {
        const model = this.model;
        if (model === null) {
            return;
        }
        this.filtered = ids === null ? null : new Set(ids.map((id) => model.indexOf(id)).filter((index): index is number => index !== undefined));
        this.invalidate();
    }

    /**
     * Draws a variation over the motif (V1 and later): each note as an outline where it lands, with
     * a line from the motif note it came from when it moved. Packed as VARIATION_FIELDS per note.
     */
    setVariation(packed: number[]): void {
        const notes: VariationNote[] = [];
        for (let at = 0; at + VARIATION_FIELDS <= packed.length; at += VARIATION_FIELDS) {
            notes.push({ onset: packed[at], end: packed[at + 1], pitch: packed[at + 2], fromOnset: packed[at + 3], fromPitch: packed[at + 4] });
        }
        this.variation = notes;
        this.renderLegend();
        this.invalidate();
    }

    /** The version's notes as dashed plum outlines, packed as onset, end and pitch per note; empty for none. */
    setGhosts(packed: number[]): void {
        const ghosts: { onset: number; end: number; pitch: number }[] = [];
        for (let at = 0; at + 3 <= packed.length; at += 3) {
            ghosts.push({ onset: packed[at], end: packed[at + 1], pitch: packed[at + 2] });
        }
        this.ghosts = ghosts;
        this.invalidate();
    }

    clearVariation(): void {
        this.variation = null;
        this.renderLegend();
        this.invalidate();
    }

    /** Which sources are being heard, so only their notes light up as they sound. */
    setAudible(original: boolean, variation: boolean): void {
        this.originalAudible = original;
        this.variationAudible = variation;
        this.invalidate();
    }

    /** Follows the player every animation frame, until called with null. */
    follow(player: PlayerHandle | null): void {
        if (player !== this.player) {
            // A playhead held as the roll follows another player, or none, is let go first (#495 UX-08).
            this.laneDrag.release();
        }
        this.player = player;
        this.invalidate();
    }

    dispose(): void {
        this.disposed = true;
        cancelAnimationFrame(this.frame);
        this.resizeObserver.disconnect();
        this.input.dispose();
        this.overview.removeEventListener("pointerdown", this.onOverviewDown);
        this.overview.removeEventListener("pointermove", this.onOverviewMove);
        this.overview.removeEventListener("pointerup", this.onOverviewUp);
        this.overview.removeEventListener("pointercancel", this.onOverviewUp);
        this.overview.removeEventListener("lostpointercapture", this.onOverviewUp);
        this.roll.removeEventListener("keydown", this.onKeyDown);
        this.roll.removeEventListener("wheel", this.onWheel);
        this.keys.removeEventListener("pointerdown", this.onKeysDown);
        this.keys.removeEventListener("pointermove", this.onKeysMove);
        this.keys.removeEventListener("pointerup", this.onKeysUp);
        this.keys.removeEventListener("pointercancel", this.onKeysUp);
        this.releaseAllLive();
        this.laneDrag.release();
        this.scroller.removeEventListener("input", this.onScroll);
        document.removeEventListener("pointerdown", this.onDocumentPointerDown);
        document.removeEventListener("pointerup", this.onDocumentPointerUp);
        document.removeEventListener("keydown", this.onDocumentKeyDown);
        document.removeEventListener(THEME_CHANGED, this.onThemeChanged);
        document.removeEventListener(PRACTICE_EVENT, this.onPracticeChanged);
        this.host.replaceChildren();
    }

    private readonly tick = (): void => {
        if (this.disposed) {
            return;
        }
        const time = performance.now();
        const elapsed = this.lastFrame === 0 ? 0 : time - this.lastFrame;
        this.lastFrame = time;
        const player = this.player;
        if (player !== null) {
            const now = this.laneDrag.heard(this.playhead.heard(player.currentTick()));
            if (now !== this.tickNow) {
                const jumped = Math.abs(now - this.tickNow) > TICKS_PER_QUARTER;
                this.tickNow = now;
                this.dirty = true;
                this.followPlayer(now, jumped, player.state() === "playing");
            }
            this.dirty ||= this.modelChanged(player);
        }
        if (!this.view.stepGlide(time)) {
            this.view.stepFollow(elapsed);
        }
        this.input.stepEdgeScroll(elapsed);
        if (this.dirty) {
            this.dirty = false;
            this.draw();
        }
        this.frame = requestAnimationFrame(this.tick);
    };

    private invalidate(): void {
        this.dirty = true;
    }

    /** Where the notes start: under the bar numbers, and under the loop strip with the lanes. */
    private get header(): number {
        return this.lanes ? BAR_ROW + LOOP_LANE : BAR_ROW;
    }

    /**
     * Where the playhead takes the view (#511 section 6.1, roll-view.ts followPlayhead): along with it past FOLLOW_AT of the
     * width while playing, no further than the end of what plays, and back to it once after a jump out of view; not while a
     * playhead is held (its drag and scrub stay as they are, #495), while a finger or the stylus is down on the roll (#521
     * 16), during a glide, or for a while after the player moved the view by hand. A reveal under way holds until another jump.
     */
    private followPlayer(tick: number, jumped: boolean, playing: boolean): void {
        if (this.holding.size > 0 || this.playhead.dragging || this.laneDrag.handle !== null) {
            return;
        }
        const play = this.playRange;
        const passage = this.view.passage;
        const end = passage !== null ? passage.end : play !== null && tick >= play.start && tick < play.end ? play.end : (this.model?.end ?? 0);
        this.view.follow(tick, jumped, playing, end);
    }

    /** The roll changed size (a turn of the iPad, the drawer, Expand): the zoom and the view stay; more or less of the music shows. */
    private onResize(): void {
        // Bound to a passage, it stays laid across the roll as long as the player has not zoomed it, and within it anyway.
        if (this.view.passage !== null && this.model !== null) {
            if (this.view.fitted) {
                this.fit();
            } else {
                this.view.setFrame(this.view.currentFrame());
            }
        }
        this.invalidate();
    }

    /** Whether the music plays now. */
    private playing(): boolean {
        return this.player?.state() === "playing";
    }

    /**
     * A finger, the stylus or the mouse went down on the roll or its whole-piece strip (#521 16.2), in any mode: while the
     * music plays, the view stops following the playhead at once and stays while it is down, the music and the playhead going
     * on. Nothing else changes: what the press does is its own.
     */
    private holdBegan(pointer: number): void {
        this.holding.add(pointer);
        if (this.playing()) {
            this.view.stopFollowing();
        }
    }

    /**
     * The last pointer down on the roll was let go (#521 16.3): while the music plays, the view catches up with the playhead
     * at once (eased, never a page jump) and follows it again, whatever the press did to the view meanwhile.
     */
    private holdEnded(pointer: number): void {
        if (!this.holding.delete(pointer) || this.holding.size > 0 || !this.playing()) {
            return;
        }
        this.view.endHandPause();
        this.view.stopFollowing();
        this.followPlayer(this.tickNow, true, true);
    }

    /** Shows the roll's words in another language (#134): the lanes, the legend, the hand letters and what a screen reader hears. */
    setLabels(labels: RollLabels): void {
        this.labels = { ...ENGLISH_ROLL_LABELS, ...labels };
        this.applyLabels();
        this.renderLegend();
        this.invalidate();
    }

    private applyLabels(): void {
        this.roll.setAttribute("aria-label", this.labels.rollAria);
        this.scroller.setAttribute("aria-label", this.labels.scrollAria);
        this.keys.setAttribute("aria-label", this.labels.keysAria);
        this.zoomBar.setAttribute("aria-label", this.labels.zoomGroup);
        this.zoomOut.setAttribute("aria-label", this.labels.zoomOut);
        this.zoomOut.title = this.labels.zoomOut;
        this.zoomIn.setAttribute("aria-label", this.labels.zoomIn);
        this.zoomIn.title = this.labels.zoomIn;
        this.fitButton.textContent = this.labels.fit;
        this.fitButton.title = this.labels.fitHint;
        this.overviewButton.textContent = this.labels.overview;
        this.overviewButton.title = this.labels.overviewHint;
        this.showZoom();
    }

    /** The zoom as a percentage of 100 % (DEFAULT_ZOOM), with − and + off at the limits. */
    private showZoom(): void {
        this.zoomLevel.textContent = fill(this.labels.zoomLevel, Math.round((this.view.zoom / DEFAULT_ZOOM) * 100));
        this.zoomOut.disabled = this.view.atLeastZoom();
        this.zoomIn.disabled = this.view.atGreatestZoom();
    }

    private updateLanes(): void {
        const lanes = this.model?.lanes(this.colourBy) ?? [];
        this.laneIndex = new Map(lanes.map((lane, index) => [lane.key, index]));
        this.renderLegend();
        this.invalidate();
    }

    /**
     * Selects the bar after or before the selection (the first bar when there is none), or with
     * extend adds it to the selection: a click or Shift-click on its number, by keyboard (WCAG 2.1.1).
     */
    private stepBar(model: RollModel, step: 1 | -1, extend: boolean): void {
        if (this.view.passage !== null) {
            return;
        }
        const motif = this.motif;
        let index = 0;
        if (motif !== null) {
            const edge = step > 0 ? motif.end - 1 : motif.start;
            index = model.bars.findIndex((bar) => bar.onset <= edge && edge < bar.end) + step;
        }
        const bar = model.bars[index];
        if (bar === undefined) {
            return;
        }
        this.view.keepInView(bar.onset);
        void this.dotnet.invokeMethodAsync("OnRollRange", bar.onset, bar.onset, true, extend && motif !== null, extend && motif !== null ? "edit" : motif === null ? "create" : "replace", nextGestureId());
    }

    /**
     * The height of the notes area: fixed, or when the canvas fills the window (data-fill on the host or
     * a panel around it, 0012), what the host leaves after the scroller, the keyboard and the legend.
     */
    private rollHeight(): number {
        if (this.host.closest<HTMLElement>("[data-fill]")?.dataset.fill !== "true") {
            return ROLL_HEIGHT;
        }
        const rest = [this.scroller, this.keys, this.legend.element].reduce((sum, part) => sum + part.getBoundingClientRect().height, 0);
        // The parts' margins (css: .roll-scroll, .roll-keys, .roll-legend) sit between them, and the bar numbers sit above the
        // notes; a page that hides the parts (the iPad's, in touch reading) has no margins to leave.
        const margins = rest > 0 ? 20 : 0;
        const overview = this.lanes ? OVERVIEW_HEIGHT : 0;
        return Math.max(MIN_ROLL_HEIGHT, Math.floor(this.host.clientHeight - rest - margins - this.header - overview));
    }

    private draw(): void {
        const width = this.host.clientWidth;
        if (width === 0) {
            return;
        }
        if (readPalette(this.host)) {
            this.painter.forgetPatterns();
        }
        if (this.pendingFit) {
            this.fit();
        }
        const range = this.view.pitchRange();
        // The frame of Expand is not the player's (#521 17): the drawer's roll opened again starts from the one before it.
        if (this.model !== null && this.view.passage === null) {
            keptFrame = { key: this.notesKey, frame: this.view.currentFrame() };
        }
        const height = this.header + (range.high - range.low + 1) * range.row;
        const model = this.model;
        const sounding = model === null ? [] : this.soundingNow(model);
        const frame: NotesFrame = { ...this.view.timeFrame(), height, header: this.header, ...range, zoom: this.view.zoom };
        const context = this.painter.paint(this.roll, frame, model === null ? null : this.scene(model, sounding));
        if (model !== null) {
            this.drawPlayhead(context, width, height);
        }
        this.drawKeys(width, model, sounding);
        if (this.lanes) {
            this.drawOverview(width);
        }
        // The zoom floats over the bottom right corner of the notes.
        this.zoomBar.style.top = `${this.roll.offsetTop + height - this.zoomBar.offsetHeight - 8}px`;
        this.reportZoom();

        this.scroller.max = String(this.view.maxScroll());
        this.scroller.value = String(this.view.scroll);
    }

    /** What the painter draws: the piece and every layer over it, as the roll holds them now. */
    private scene(model: RollModel, sounding: readonly number[]): NotesScene {
        const drag = this.input.rangeDragged();
        const unbound = this.view.passage === null;
        // The selected range, or the range being dragged; not in Expand (#533 3): the passage on screen is the selection already.
        const band = drag ?? (this.place.drawing ? null : this.placeSpan());
        const rowNames = this.fullRowNames[keyIndexAt(this.keyTicks, this.view.scroll)];
        return {
            model,
            lane: (note) => this.laneOf(note),
            sounding: new Set(sounding),
            chosen: this.selected,
            motif: this.motif,
            filtered: this.filtered,
            highlight: this.highlight,
            variation: this.variation,
            variationAudible: this.variationAudible,
            ghosts: this.ghosts,
            phrases: this.phrases,
            checks: this.checks,
            band: unbound ? band : null,
            // Grips on the bar numbers, never under the place rule, whose handles sit at the edges' middles (v0.6.4).
            grips: drag === null && !this.input.pointers.placeSelection,
            // The range that plays, which a variation can make longer or shorter than the selection.
            play: drag === null && !this.lanes && unbound ? this.playRange : null,
            tick: this.tickNow,
            noteNames: this.noteNames,
            name: (index) => this.nameOf(index),
            shortName: (index) => this.shortNameOf(index),
            rowName: (pitch) => this.rowName(pitch),
            octaveName: (octave) => ({ words: this.octaveNames[octave] ?? "", c: rowNames?.[octave * 12] ?? pitchName(octave * 12) }),
            placed: (index) => this.editor.placed(index),
            picked: (index) => this.editor.picked(model, index),
        };
    }

    /** A note's lane, by its index among the piece's lanes as the colouring chosen orders them. */
    private laneOf(note: RollNote): number {
        return this.laneIndex.get(laneKey(note, this.colourBy)) ?? 0;
    }

    /**
     * The keyboard under the roll (roll-keys-draw.ts): the keys sounding lit in their lanes' colours, and those struck by hand
     * or sounding in the variation in the ink colour, so they read apart from the piece's lanes.
     */
    private drawKeys(width: number, model: RollModel | null, sounding: readonly number[]): void {
        const lit = model === null ? new Map<number, number>() : litKeys(model, sounding, (note) => this.laneOf(note));
        const struck = new Set<number>(this.live.keys());
        if (this.variation !== null && this.variationAudible) {
            for (const note of this.variation) {
                if (note.onset <= this.tickNow && note.end > this.tickNow) {
                    struck.add(note.pitch);
                }
            }
        }
        this.keyLayout = drawKeyboard(this.keys, width, lit, struck, this.rowName(60));
    }

    /**
     * The playhead and what the gestures draw over the notes: the place rule's edges, the lanes, the box and the model's
     * playheads, else the playhead's line (roll-painter.ts). Its control sits over the handle, kept in view when the line is not.
     */
    private drawPlayhead(context: CanvasRenderingContext2D, width: number, height: number): void {
        const at = this.view.xOf(this.tickNow);
        this.keepDrawnHeads();
        const playheadContext = this.playheadContext();
        if (playheadContext !== null) {
            const left = this.roll.offsetLeft + Math.min(Math.max(at, GUTTER), width);
            this.playhead.place(left, this.roll.offsetTop + BAR_ROW, measures.touch, playheadContext, this.labels);
        }
        this.drawPlace(context, width, height);
        if (this.lanes) {
            this.drawLanes(context, width, height);
            return;
        }
        if (this.drawRules(context, width, height)) {
            return;
        }
        drawPlayheadLine(context, { width, height, header: this.header }, at, this.playhead.focused, measures.knobRadius);
    }

    /** Where the playhead may go and where it is, for its control; null before a piece is shown. */
    private playheadContext(): PlayheadContext | null {
        const model = this.model;
        if (model === null) {
            return null;
        }
        return { bars: model.bars, span: this.playheadSpan(), end: model.end, tick: this.player?.currentTick() ?? this.tickNow };
    }

    /** The loop strip and the two playheads of the playback model, with the lanes (roll-strips.ts, playback-model.ts). */
    private drawLanes(context: CanvasRenderingContext2D, width: number, height: number): void {
        const frame = { top: BAR_ROW, left: GUTTER, right: width, height, xOf: (tick: number) => this.view.xOf(tick) };
        const playing = this.player !== null && this.player.state() !== "stopped";
        // Practice view (#533 11): the black playhead alone, without the loop strip and the blue one.
        const view = rollPlaces(practiced(viewOf({ selection: this.motif, loop: this.looping, tick: this.tickNow, transport: playing ? "playing" : "stopped" }), shownAt(this.host)));
        if (this.laneShown()) {
            drawLoopLane(context, frame, this.motif, this.looping, this.stripInks(), modeSelects(this.mode));
        }
        drawPosition(context, frame, view, this.stripInks());
    }

    /** The whole piece in one line over the roll: the position, the bars on screen, the selection. */
    private drawOverview(width: number): void {
        const model = this.model;
        if (model === null) {
            return;
        }
        const context = prepare(this.overview, width, OVERVIEW_HEIGHT);
        const view = { start: this.view.scroll, end: this.view.scroll + this.view.visibleTicks() };
        const lastBar = String(model.bars.at(-1)?.number ?? "");
        const playing = this.player !== null && this.player.state() !== "stopped";
        drawOverview(context, width, { tick: this.tickNow, label: this.placeLabel, end: model.end, lastBar, view, selection: this.motif, playing }, this.stripInks());
    }

    private stripInks(): StripInks {
        return { ink: palette.ink, accent: palette.accent, tint: palette.selectionBand, surface: palette.surface, line: palette.stripLine, muted: palette.stripMuted };
    }

    /**
     * A press on the whole piece (#521 1): the black dot and the frame round the bars on screen are two targets. On the dot it
     * takes the position, the black playhead, as its line down the roll does (laneDrag); anywhere else the frame, which a
     * drag moves through the piece, the position staying where it is. A press outside the frame brings the frame there.
     */
    private readonly onOverviewDown = (event: PointerEvent): void => {
        const model = this.model;
        if (model === null || this.overviewPress !== null) {
            return;
        }
        this.overview.setPointerCapture(event.pointerId);
        event.preventDefault();
        this.holdBegan(event.pointerId);
        const x = this.overviewX(event);
        const width = this.host.clientWidth;
        if (this.player !== null && overviewGrab(x, width, this.tickNow, model.end, measures.touch / 2) === "position" && this.laneDrag.grab("black", event.pointerId, this.playing())) {
            this.overviewPress = { pointer: event.pointerId, position: true, offset: 0 };
            return;
        }
        const tick = overviewTick(x, width, model.end);
        const offset = viewGrabOffset(tick, { start: this.view.scroll, end: this.view.scroll + this.view.visibleTicks() });
        this.overviewPress = { pointer: event.pointerId, position: false, offset };
        this.view.byHand();
        this.view.scrollTo(tick - offset);
    };

    /**
     * A drag on the whole piece: the position goes to the place under the pointer, the view staying while it is inside and
     * going along past its edge (viewAlong, #521 1.2); or the frame goes with the pointer, the position staying.
     */
    private readonly onOverviewMove = (event: PointerEvent): void => {
        const press = this.overviewPress;
        const model = this.model;
        if (press === null || model === null || press.pointer !== event.pointerId) {
            return;
        }
        const tick = overviewTick(this.overviewX(event), this.host.clientWidth, model.end);
        if (!press.position) {
            this.view.byHand();
            this.view.scrollTo(tick - press.offset);
            return;
        }
        const bars = model.bars.map((bar) => ({ start: bar.onset, end: bar.end }));
        const place = laneDragTick("black", tick, bars, this.motif, model.end, model.onsets);
        if (place !== undefined) {
            this.laneDrag.move(event.pointerId, place);
            this.view.scrollTo(viewAlong(this.view.scroll, this.view.visibleTicks(), place, this.view.maxScroll()));
        }
    };

    private readonly onOverviewUp = (event: PointerEvent): void => {
        const press = this.overviewPress;
        if (press === null || press.pointer !== event.pointerId) {
            return;
        }
        this.overviewPress = null;
        if (press.position) {
            this.laneDrag.release(event.pointerId);
        }
        this.holdEnded(event.pointerId);
        this.invalidate();
    };

    /** Where a pointer is across the whole-piece strip. */
    private overviewX(event: PointerEvent): number {
        return event.clientX - this.overview.getBoundingClientRect().left;
    }

    /** Where the playhead stays: the loop, else the selection; null for neither, and with the lanes, where it goes anywhere. */
    private playheadSpan(): Span | null {
        if (this.lanes) {
            return null;
        }
        return this.playRange ?? (this.motif === null ? null : { start: this.motif.start, end: this.motif.end });
    }

    /**
     * The playhead a press takes rather than what lies under it: on its handle, or with a finger in touch reading anywhere
     * down its line, a touch target wide; "line" for the black one before the modes, taken by RollPlayhead. Not while the
     * notes are being edited, nor while another pointer holds one (PlayheadDrag.grab). Nothing begins here. In Expand
     * (#533 2) it is the model's blue one over the passage, dragged inside the passage on the sixteenth grid, heard.
     */
    private playheadUnder(type: string, x: number, y: number): Handle | "line" | null {
        if (this.editor.editing || this.player === null) {
            return null;
        }
        const finger = type !== "mouse";
        const onLine = (): boolean => onPlayhead(x, y, this.headsNear(x).black, BAR_ROW, finger ? measures.touch : KNOB_MOUSE, this.touchReading && finger);
        const passage = this.view.passage;
        const state = passage === null && !this.lanes ? this.heldModel() : null;
        let handle: Handle | null;
        if (passage !== null) {
            handle = onLine() ? "blue" : null;
        } else if (this.lanes) {
            handle = this.laneHeadUnder(type, x, y);
        } else if (state !== null) {
            handle = this.modelHeadUnder(type, state, x, y);
        } else {
            return onLine() ? "line" : null;
        }
        return this.laneDrag.handle === null ? handle : null;
    }

    /** Where the two playheads are drawn now, across the roll: the black one always, the blue one with the model's selection. */
    private headsNow(): { black: number; blue: number | null } {
        const playing = this.playing();
        const state = this.lanes ? { selection: this.motif, loop: this.looping } : this.heldModel();
        if (state === null) {
            return { black: this.view.xOf(this.tickNow), blue: null };
        }
        // In practice view (#533 11) the blue playhead is neither drawn nor taken; outside the selection it is not either (#543 6).
        const view = rollPlaces(practiced(viewOf({ selection: state.selection, loop: state.loop, tick: this.tickNow, transport: playing ? "playing" : "stopped" }), shownAt(this.host)));
        return { black: this.view.xOf(view.black), blue: view.blue === null ? null : this.view.xOf(view.blue) };
    }

    /** Keeps where the playheads are drawn while the music plays, for as long as a press may still find them there (#533 4). */
    private keepDrawnHeads(): void {
        if (!this.playing()) {
            this.drawnHeads = [];
            return;
        }
        const at = performance.now();
        this.drawnHeads = this.drawnHeads.filter((drawn) => drawn.at >= at - measures.reachBackMs);
        this.drawnHeads.push({ at, ...this.headsNow() });
    }

    /**
     * Where a press at `x` measures each playhead from (#533 4, roll-head-drag.ts reachedFrom): playing, the nearest place it
     * was drawn at in the kit's slow time, at most a touch target behind or ahead of where it is now, as the finger lands
     * where it saw the line; stopped, where it is.
     */
    private headsNear(x: number): { black: number; blue: number | null } {
        const now = this.headsNow();
        const since = performance.now() - measures.reachBackMs;
        const drawn = this.playing() ? this.drawnHeads : [];
        const black = reachedFrom(x, now.black, drawn.map((head) => ({ at: head.at, x: head.black })), since, measures.touch);
        const blue = now.blue === null ? null
            : reachedFrom(x, now.blue, drawn.flatMap((head) => (head.blue === null ? [] : [{ at: head.at, x: head.blue }])), since, measures.touch);
        return { black, blue };
    }

    /**
     * With the lanes (#495 UX-08 and UX-10) a press takes the blue playhead by its handle in the loop strip, or the black one
     * down its line a touch target wide (lineTakesHead: not the stylus mode's stylus in Select): the drag moves it on the grid.
     */
    private laneHeadUnder(type: string, x: number, y: number): Handle | null {
        const { black, blue } = this.headsNear(x);
        const heads = { black, blue, stripTop: BAR_ROW, stripBottom: this.header, reach: measures.touch / 2 };
        return laneHeadAt(x, y, heads, lineTakesHead(type, this.input.pointers.stylus, this.input.pointers.placeSelects("pen")));
    }

    /**
     * On the web, the model's two playheads (#495 UX-08 and UX-10, slice W3): a press takes the blue one by its knob or the
     * black one by its dot, on the bar numbers' edge, a touch target wide for a finger. The drag moves the one taken, not
     * the selection, and never selects a bar; only a press on neither goes on to what lies under it.
     */
    private modelHeadUnder(type: string, state: ModelState, x: number, y: number): Handle | null {
        const knobs = playheadKnobs({ xOf: (tick) => this.view.xOf(tick), header: this.header }, rollPlaces(practiced(viewOf({ ...state, tick: this.tickNow }), shownAt(this.host))));
        // Playing, the knob is found where it was drawn a moment ago too (headsNear); its height stays.
        const near = this.headsNear(x);
        const found = {
            black: knobs.black === null ? null : { ...knobs.black, x: near.black },
            blue: knobs.blue === null || near.blue === null ? null : { ...knobs.blue, x: near.blue },
        };
        const reach = (type === "mouse" ? KNOB_MOUSE : measures.touch) / 2;
        return knobHeadAt(x, y, found, reach);
    }

    /** The web's playback model while the player holds it under the modes, which the roll draws and drags; else null. */
    private heldModel(): ModelState | null {
        return this.mode === null || this.model === null ? null : (this.player?.model?.() ?? null);
    }

    /**
     * The box being drawn and the two playheads of the playback model (#495 UX-10), from the player's model with the position
     * of this frame, under the modes of version 0.5; false while they are off or the player does not hold the model.
     */
    private drawRules(context: CanvasRenderingContext2D, width: number, height: number): boolean {
        const box = this.input.boxDrawn();
        if (box !== null) {
            drawBox(context, box.from, box.to, this.ruleInks());
        }
        const state = this.heldModel();
        if (state === null) {
            return false;
        }
        const frame = { xOf: (tick: number) => this.view.xOf(tick), header: this.header, left: GUTTER, right: width, height };
        drawPlayheads(context, frame, rollPlaces(practiced(viewOf({ ...state, tick: this.tickNow }), shownAt(this.host))), this.ruleInks());
        return true;
    }

    private ruleInks(): { ink: string; accent: string; surface: string; paper: string } {
        return { ink: palette.ink, accent: palette.accent, surface: palette.charcoal, paper: palette.paper };
    }

    /** Whether the player's model changed but for its position since it was drawn: Цикл or a pause moves or fills a playhead. */
    private modelChanged(player: PlayerHandle): boolean {
        const state = this.mode === null ? null : (player.model?.() ?? null);
        const key = state === null ? "" : `${state.selection?.start ?? ""}:${state.selection?.end ?? ""}:${state.loop}:${state.transport}`;
        if (key === this.modelDrawn) {
            return false;
        }
        this.modelDrawn = key;
        return true;
    }

    /** A note's name spelled for its key, or its unspelled name before music-core sent the names. */
    private nameOf(index: number): string {
        return this.spelled?.[index] ?? pitchName(this.model?.notes[index].pitch ?? 60);
    }

    /** A note's name without its octave, as it is written inside the note. */
    private shortNameOf(index: number): string {
        return this.shortNames?.[index] ?? pitchClassName(this.model?.notes[index].pitch ?? 60);
    }

    /** A row's name without its octave, spelled for the key in force at the left of the view (0018 section 4). */
    private rowName(pitch: number): string {
        return this.rowNames[keyIndexAt(this.keyTicks, this.view.scroll)]?.[pitch] ?? pitchClassName(pitch);
    }

    /** Practice view began, ended or showed its controls (#533 11): the playheads and the strips are drawn again. */
    private readonly onPracticeChanged = (): void => this.invalidate();

    /** A new theme: the roll and its legend are drawn again in its colours. */
    private readonly onThemeChanged = (): void => {
        this.renderLegend();
        this.invalidate();
    };

    /** The legend again (roll-legend.ts): the lanes as the colouring chosen orders them, and the marks on the roll now. */
    private renderLegend(): void {
        if (readPalette(this.host)) {
            this.painter.forgetPatterns();
        }
        const model = this.model;
        this.legend.show({
            lanes: (model?.lanes(this.colourBy, this.labels) ?? []).map((lane) => ({ index: this.laneIndex.get(lane.key) ?? 0, label: lane.label })),
            variation: this.variation !== null,
            highlight: this.highlight !== null,
            checks: this.checks.length > 0,
            inferred: model?.notes.some((note) => note.noteInferred || note.handInferred) === true,
        }, this.labels);
    }

    private selectIndex(index: number | undefined, add: boolean): void {
        if (index === undefined) {
            if (!add) {
                this.selected = new Set();
            }
        } else if (add) {
            if (!this.selected.delete(index)) {
                this.selected.add(index);
            }
        } else {
            this.selected = new Set([index]);
        }
        this.focusIndex = index ?? this.focusIndex;
        this.invalidate();
        const model = this.model;
        if (model !== null) {
            const ids = [...this.selected].sort((a, b) => a - b).map((i) => model.notes[i].id);
            void this.dotnet.invokeMethodAsync("OnRollSelection", ids);
        }
    }

    /** Whether the loop strip is shown and takes presses (roll-strips.ts laneShown): with the lanes, not in practice view. */
    private laneShown(): boolean {
        return laneShown(this.lanes, shownAt(this.host));
    }

    /** Strikes a key by hand and lights it; returns the pitch, to release later. */
    private strike(pitch: number): number | null {
        if (pitch < 0 || pitch > 127 || this.player?.noteOn === undefined) {
            return null;
        }
        this.player.noteOn(pitch);
        this.live.set(pitch, (this.live.get(pitch) ?? 0) + 1);
        this.invalidate();
        return pitch;
    }

    private release(pitch: number | null): void {
        if (pitch === null) {
            return;
        }
        const held = (this.live.get(pitch) ?? 1) - 1;
        if (held > 0) {
            this.live.set(pitch, held);
        } else {
            this.live.delete(pitch);
            this.player?.noteOff?.(pitch);
        }
        this.invalidate();
    }

    private releaseAllLive(): void {
        for (const pitch of this.live.keys()) {
            this.player?.noteOff?.(pitch);
        }
        this.live.clear();
    }

    private readonly onKeysDown = (event: PointerEvent): void => {
        const box = this.keys.getBoundingClientRect();
        this.keys.setPointerCapture(event.pointerId);
        event.preventDefault();
        this.keysPress = { pointer: event.pointerId, pitch: this.strike(keyAt(this.keyLayout, event.clientX - box.left, event.clientY - box.top) ?? -1) };
    };

    private readonly onKeysMove = (event: PointerEvent): void => {
        const press = this.keysPress;
        if (press === null || event.pointerId !== press.pointer) {
            return;
        }
        const box = this.keys.getBoundingClientRect();
        const pitch = keyAt(this.keyLayout, event.clientX - box.left, event.clientY - box.top);
        if (pitch !== press.pitch) {
            this.release(press.pitch);
            press.pitch = this.strike(pitch ?? -1);
        }
    };

    private readonly onKeysUp = (event: PointerEvent): void => {
        const press = this.keysPress;
        if (press === null || event.pointerId !== press.pointer) {
            return;
        }
        this.release(press.pitch);
        this.keysPress = null;
    };

    /** Clears the selected notes and asks the page to clear the motif selection, when there is any. */
    private clearAll(): void {
        // In Expand the selection is the passage shown: nothing here takes it away (#521 19.3).
        if (this.view.passage !== null) {
            return;
        }
        if (this.selected.size > 0) {
            this.selectIndex(undefined, false);
        }
        if (this.motif !== null) {
            void this.dotnet.invokeMethodAsync("OnRollClear", nextGestureId());
        }
    }

    private readonly onDocumentPointerDown = (event: PointerEvent): void => {
        const target = event.target;
        this.outsidePress =
            target instanceof Element && !this.host.contains(target) && target.closest(KEEPS_SELECTION) === null
                ? { x: event.clientX, y: event.clientY, target }
                : null;
    };

    /** A click on the page outside the roll, away from any control, clears the selection. */
    /** Space asks the page to play or pause: the selection on a loop when there is one, else the piece. */
    private readonly onDocumentKeyDown = (event: KeyboardEvent): void => {
        if (event.key !== " " || event.repeat || event.ctrlKey || event.altKey || event.metaKey || this.model === null) {
            return;
        }
        const target = event.target;
        if (target instanceof Element && target.closest(SPACE_OWNERS) !== null) {
            return;
        }
        event.preventDefault();
        void this.dotnet.invokeMethodAsync("OnRollSpace");
    };

    private readonly onDocumentPointerUp = (event: PointerEvent): void => {
        const press = this.outsidePress;
        this.outsidePress = null;
        if (press === null || Math.hypot(event.clientX - press.x, event.clientY - press.y) >= CLICK_SLOP) {
            return;
        }
        // Under the modes of version 0.5 only ✕ or Escape removes the selection.
        if (this.host.isConnected && this.host.offsetParent !== null && !this.touchReading && this.mode === null) {
            this.clearAll();
        }
    };

    /** The piece's notes being heard: none while only the variation plays, the motif's when it plays alone, and only those the Notes filter keeps. */
    private soundingNow(model: RollModel): number[] {
        if (!this.originalAudible) {
            return [];
        }
        const motif = this.motif;
        const filtered = this.filtered;
        return model
            .soundingAt(this.tickNow)
            .filter((index) => (motif === null || !motif.alone || motif.notes.has(index)) && (filtered === null || filtered.has(index)));
    }

    private readonly onKeyDown = (event: KeyboardEvent): void => {
        const model = this.model;
        if (model === null) {
            return;
        }
        // By key position, so Shift (which turns them into braces) and other layouts keep working.
        if (event.code === "BracketRight" || event.code === "BracketLeft") {
            this.stepBar(model, event.code === "BracketRight" ? 1 : -1, event.shiftKey);
            event.preventDefault();
            return;
        }
        if (this.editor.key(event, model)) {
            event.preventDefault();
            return;
        }
        if (this.touchReading && this.nudgeKey(event)) {
            return;
        }
        switch (event.key) {
            case "ArrowRight":
            case "ArrowLeft": {
                const next = model.step(this.focusIndex, event.key === "ArrowRight" ? 1 : -1);
                this.selectIndex(next, event.shiftKey);
                if (next !== undefined) {
                    this.view.keepInView(model.notes[next].onset);
                }
                break;
            }
            case "Escape":
                this.clearAll();
                break;
            case "+":
            case "=":
                this.zoomBy(ZOOM_STEP);
                break;
            case "-":
                this.zoomBy(1 / ZOOM_STEP);
                break;
            default:
                return;
        }
        event.preventDefault();
    };

    /** Read by touch, the roll selects no notes: ← and → move the playhead by a beat. True when the key was one of them. */
    private nudgeKey(event: KeyboardEvent): boolean {
        if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") {
            return false;
        }
        event.preventDefault();
        this.playhead.nudge(event.key === "ArrowRight" ? 1 : -1);
        return true;
    }

    private readonly onWheel = (event: WheelEvent): void => {
        // Ctrl (or a pinch) zooms around the pointer; a sideways or Shift scroll moves through time.
        // A plain vertical scroll is left to the page.
        if (event.ctrlKey) {
            const box = this.roll.getBoundingClientRect();
            this.zoomBy(event.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP, Math.max(0, event.clientX - box.left - GUTTER));
        } else if (!event.shiftKey && Math.abs(event.deltaY) > Math.abs(event.deltaX)) {
            // A vertical scroll moves through the rows when they do not all fit; else it is left to the page.
            const { above, below, row } = this.view.pitchRange();
            if ((event.deltaY < 0 && !above) || (event.deltaY > 0 && !below)) {
                return;
            }
            this.view.byHand();
            this.view.scrollPitch(-Math.sign(event.deltaY) * Math.max(1, Math.round(Math.abs(event.deltaY) / row)));
        } else {
            const delta = event.shiftKey ? event.deltaY : event.deltaX;
            if (delta === 0) {
                return;
            }
            this.view.byHand();
            this.view.scrollTo(this.view.scroll + this.view.ticksAcross(delta));
        }
        event.preventDefault();
    };

    private readonly onScroll = (): void => {
        this.view.byHand();
        this.view.scrollTo(Number(this.scroller.value));
    };
}

/** A button of the floating zoom. */
/**
 * The floating zoom (testing item 13): − the percentage + and Fit, and Zoom Out after Fit of a selection (#521 2.2), hidden
 * until then; each button does what it is given.
 */
function floatingZoom(actions: { out: () => void; in: () => void; fit: () => void; overview: () => void }): {
    bar: HTMLElement;
    out: HTMLButtonElement;
    level: HTMLElement;
    in: HTMLButtonElement;
    fit: HTMLButtonElement;
    overview: HTMLButtonElement;
} {
    const bar = document.createElement("div");
    bar.className = "roll-zoom";
    bar.setAttribute("role", "group");
    const level = document.createElement("span");
    level.className = "roll-zoom-level";
    const buttons = { out: zoomButton("−"), in: zoomButton("+"), fit: zoomButton(""), overview: zoomButton("") };
    buttons.fit.classList.add("roll-fit");
    buttons.overview.classList.add("roll-fit");
    buttons.overview.hidden = true;
    for (const key of ["out", "in", "fit", "overview"] as const) {
        buttons[key].addEventListener("click", actions[key]);
    }
    bar.replaceChildren(buttons.out, level, buttons.in, buttons.fit, buttons.overview);
    return { bar, level, ...buttons };
}

function zoomButton(text: string): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "roll-zoom-button";
    button.textContent = text;
    return button;
}

/** Entry point for Blazor. */
export function createPianoRoll(host: HTMLElement, dotnet: DotNetObject, labels?: RollLabels): PianoRoll {
    return new PianoRoll(host, dotnet, labels);
}
