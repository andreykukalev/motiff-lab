// The layer that takes pencil marks on the score (decision record 0015): the Apple Pencil, or a
// pen, writes over the engraving at any time; with the Pencil tools on, a mouse writes too. A finger
// never writes, so the hand resting on the screen and a finger scrolling the score leave no marks. A
// tap with the pen picks a note or a bar, as a click does. Marks are kept per piece on the device
// (storage/pencil-store.ts) and drawn again in their bars after every layout (pencil-marks.ts).
//
// The iPad app (ios/, 0015) relays the Pencil's double tap and squeeze as a "motiff-pencil" event on
// the window: a double tap switches to the eraser and back, a squeeze shows or hides the tools.

import {
    HIGHLIGHTER_SIZE,
    MIN_POINT_DISTANCE,
    PEN_SIZE,
    TAP_DISTANCE,
    anchorMark,
    framesByTick,
    outlinePath,
    placeMark,
    readMarks,
    touches,
    writeMarks,
    type BarFrame,
    type InkPoint,
    type MarkColour,
    type PencilMark,
} from "./pencil-marks.js";
import { loadMarks, saveMarks } from "../storage/pencil-store.js";

export type PencilTool = "pen" | "highlighter" | "eraser";

/** What the Pencil tools show, sent to Blazor after every change. */
export interface PencilState {
    /** Whether the tools are on: then a mouse writes too. */
    on: boolean;
    tool: PencilTool;
    colour: MarkColour;
    /** Marks on this piece. */
    marks: number;
    /** Marks whose bar is not in the score shown (another engraving of the piece), kept but not drawn. */
    hidden: number;
    canUndo: boolean;
}

/** What the layer needs from the score view. */
export interface PencilHost {
    /** Where each engraved bar sits now, in the score's content pixels. */
    frames(): BarFrame[];
    /** A tap with the pen or, with the tools on, the mouse: picks what is under it, as a click does. */
    tap(target: Element, extend: boolean): void;
    changed(state: PencilState): void;
}

/** One step the player can take back: marks it added and marks it took away. */
interface Change {
    added: PencilMark[];
    removed: PencilMark[];
}

interface Stroke {
    pointerId: number;
    points: InkPoint[];
    erasing: boolean;
    moved: boolean;
    target: Element | null;
    extend: boolean;
    path: SVGPathElement | null;
    removed: PencilMark[];
}

/** The pen's eraser end, in PointerEvent.buttons. */
const ERASER_BUTTON = 32;

/** The eraser takes marks within this many pixels of its tip. */
const ERASER_REACH = 10;

/** After the layer has taken a press, the click the browser may still send for it is not a click on the score. */
const CLICK_AFTER_MS = 700;

const SAVE_DELAY_MS = 500;

const SVG = "http://www.w3.org/2000/svg";

/** The steps kept for Undo. */
const UNDO_DEPTH = 100;

let nextId = 0;

function newId(): string {
    nextId += 1;
    return `m${Date.now().toString(36)}${nextId.toString(36)}`;
}

export class PencilLayer {
    private readonly host: HTMLElement;
    private readonly score: PencilHost;
    private readonly svg: SVGSVGElement;
    private key: string | null = null;
    /** Counts keys set, so a load that finishes after the next key is set is dropped. */
    private generation = 0;
    private marks: PencilMark[] = [];
    private undo: Change[] = [];
    private on = false;
    private tool: PencilTool = "pen";
    private toolBeforeEraser: PencilTool = "pen";
    private colour: MarkColour = "ink";
    private frames: BarFrame[] = [];
    private byTick = new Map<number, BarFrame>();
    private placed = new Map<string, { points: InkPoint[]; radius: number; path: SVGPathElement }>();
    private hidden = 0;
    private stroke: Stroke | null = null;
    private clickAfter = 0;
    private saveTimer = 0;
    private dirty = false;

    constructor(host: HTMLElement, score: PencilHost) {
        this.host = host;
        this.score = score;
        this.svg = document.createElementNS(SVG, "svg");
        this.svg.classList.add("pencil-layer");
        this.svg.setAttribute("aria-hidden", "true");
        host.addEventListener("pointerdown", this.onPointerDown);
        host.addEventListener("pointermove", this.onPointerMove);
        host.addEventListener("pointerup", this.onPointerUp);
        host.addEventListener("pointercancel", this.onPointerCancel);
        // The Pencil would scroll the score as a finger does; stopping its touch at the start keeps it writing. Fingers still
        // scroll. Only touchstart listens, and not passively: a touchmove listener that is not passive makes Safari wait for
        // the page at every move of every finger, so scrolling the score would stutter whenever the page is busy.
        host.addEventListener("touchstart", this.onTouch, { passive: false });
        window.addEventListener("motiff-pencil", this.onNativePencil);
        window.addEventListener("pagehide", this.onPageHide);
    }

    /** Which piece's marks to show and keep; null turns the layer off, as for the score with a variation in it. */
    async setKey(key: string | null): Promise<void> {
        if (key === this.key) {
            return;
        }
        this.flush();
        this.key = key;
        this.generation += 1;
        const generation = this.generation;
        this.marks = [];
        this.undo = [];
        this.endStroke();
        this.svg.style.display = key === null ? "none" : "";
        this.draw();
        this.notify();
        if (key === null) {
            return;
        }

        let saved: PencilMark[] = [];
        try {
            saved = readMarks(await loadMarks(key));
        } catch {
            // Storage that is blocked keeps marks for this visit only.
        }
        if (generation === this.generation) {
            // Marks written while the saved ones loaded stay, on top of them.
            this.marks = [...saved, ...this.marks];
            this.draw();
            this.notify();
        }
    }

    /** Turns the tools on or off. Off, the pen still writes; the mouse picks notes and bars again. */
    setOn(on: boolean): void {
        this.on = on;
        if (!on && this.tool === "eraser") {
            this.tool = this.toolBeforeEraser;
        }
        this.host.classList.toggle("pencil-on", on);
        this.notify();
    }

    /** Chooses the pen (in a colour), the highlighter or the eraser, and turns the tools on. */
    setTool(tool: PencilTool, colour: MarkColour | null): void {
        if (tool === "eraser" && this.tool !== "eraser") {
            this.toolBeforeEraser = this.tool;
        }
        this.tool = tool;
        if (colour !== null) {
            this.colour = colour;
        } else if (tool === "highlighter") {
            this.colour = "yellow";
        } else if (tool === "pen" && this.colour === "yellow") {
            this.colour = "ink";
        }
        this.setOn(true);
    }

    /** Takes back the last mark written, erased or cleared. */
    undoLast(): void {
        const change = this.undo.pop();
        if (change === undefined) {
            return;
        }
        const added = new Set(change.added.map((mark) => mark.id));
        this.marks = [...this.marks.filter((mark) => !added.has(mark.id)), ...change.removed];
        this.changedMarks();
    }

    /** Takes away every mark on this piece; Undo brings them back. */
    clear(): void {
        if (this.marks.length === 0) {
            return;
        }
        this.remember({ added: [], removed: this.marks });
        this.marks = [];
        this.changedMarks();
    }

    /** True while a click the browser sends belongs to a press the layer took; the score view ignores it. */
    swallowsClick(): boolean {
        return this.stroke !== null || performance.now() < this.clickAfter;
    }

    /** Draws the marks in their bars again, after the score is engraved or laid out again. */
    relayout(): void {
        // Out of the score while it is measured, so the layer's own size does not keep the score tall.
        this.svg.remove();
        this.frames = this.score.frames();
        this.byTick = framesByTick(this.frames);
        this.svg.setAttribute("width", `${this.host.scrollWidth}`);
        this.svg.setAttribute("height", `${this.host.scrollHeight}`);
        this.host.append(this.svg);
        this.draw();
        this.notify();
    }

    dispose(): void {
        this.flush();
        this.endStroke();
        this.host.removeEventListener("pointerdown", this.onPointerDown);
        this.host.removeEventListener("pointermove", this.onPointerMove);
        this.host.removeEventListener("pointerup", this.onPointerUp);
        this.host.removeEventListener("pointercancel", this.onPointerCancel);
        this.host.removeEventListener("touchstart", this.onTouch);
        window.removeEventListener("motiff-pencil", this.onNativePencil);
        window.removeEventListener("pagehide", this.onPageHide);
        this.svg.remove();
    }

    state(): PencilState {
        return { on: this.on, tool: this.tool, colour: this.colour, marks: this.marks.length, hidden: this.hidden, canUndo: this.undo.length > 0 };
    }

    private draw(): void {
        this.svg.replaceChildren();
        this.placed = new Map();
        this.hidden = 0;
        if (this.key === null) {
            return;
        }
        for (const mark of this.marks) {
            const place = placeMark(mark, this.byTick);
            if (place === null) {
                this.hidden += 1;
                continue;
            }
            const path = this.pathFor(mark.tool, mark.colour);
            path.setAttribute("d", outlinePath(place.points, place.radius, mark.tool === "pen"));
            this.svg.append(path);
            this.placed.set(mark.id, { ...place, path });
        }
    }

    private pathFor(tool: "pen" | "highlighter", colour: MarkColour): SVGPathElement {
        const path = document.createElementNS(SVG, "path");
        path.setAttribute("class", `pencil-mark ${tool} ${colour}`);
        return path;
    }

    private changedMarks(): void {
        this.draw();
        this.dirty = true;
        clearTimeout(this.saveTimer);
        this.saveTimer = window.setTimeout(() => this.flush(), SAVE_DELAY_MS);
        this.notify();
    }

    private remember(change: Change): void {
        this.undo.push(change);
        if (this.undo.length > UNDO_DEPTH) {
            this.undo.shift();
        }
    }

    /** Saves now what is waiting to be saved. */
    private flush(): void {
        clearTimeout(this.saveTimer);
        if (!this.dirty || this.key === null) {
            return;
        }
        this.dirty = false;
        void saveMarks(this.key, this.marks.length === 0 ? null : writeMarks(this.marks)).catch(() => {
            // Storage that is blocked or full keeps the marks for this visit only.
        });
    }

    private notify(): void {
        this.score.changed(this.state());
    }

    private point(event: PointerEvent): InkPoint {
        const view = this.host.getBoundingClientRect();
        return {
            x: event.clientX - view.left + this.host.scrollLeft,
            y: event.clientY - view.top + this.host.scrollTop,
            pressure: event.pointerType === "pen" && event.pressure > 0 ? event.pressure : 0.5,
        };
    }

    private readonly onPointerDown = (event: PointerEvent): void => {
        if (this.key === null || this.stroke !== null) {
            return;
        }
        const pen = event.pointerType === "pen";
        const mouse = event.pointerType === "mouse" && event.button === 0;
        if (!pen && !(mouse && this.on)) {
            return;
        }
        event.preventDefault();
        this.host.setPointerCapture(event.pointerId);
        const erasing = this.tool === "eraser" || (event.buttons & ERASER_BUTTON) !== 0;
        const first = this.point(event);
        this.stroke = {
            pointerId: event.pointerId,
            points: [first],
            erasing,
            moved: false,
            target: event.target instanceof Element ? event.target : null,
            extend: event.shiftKey,
            path: null,
            removed: [],
        };
        if (erasing) {
            this.eraseAt(first);
        }
    };

    private readonly onPointerMove = (event: PointerEvent): void => {
        const stroke = this.stroke;
        if (stroke === null || event.pointerId !== stroke.pointerId) {
            return;
        }
        event.preventDefault();
        // The Pencil reports up to 240 points a second; the browser hands over the ones between frames here.
        const coalesced = typeof event.getCoalescedEvents === "function" ? event.getCoalescedEvents() : [];
        const first = stroke.points[0];
        for (const each of coalesced.length > 0 ? coalesced : [event]) {
            const point = this.point(each);
            if (Math.hypot(point.x - first.x, point.y - first.y) >= TAP_DISTANCE) {
                stroke.moved = true;
            }
            if (stroke.erasing) {
                this.eraseAt(point);
                continue;
            }
            const last = stroke.points[stroke.points.length - 1];
            if (Math.hypot(point.x - last.x, point.y - last.y) >= MIN_POINT_DISTANCE) {
                stroke.points.push(point);
            }
        }
        if (!stroke.erasing && stroke.moved) {
            stroke.path ??= this.svg.appendChild(this.pathFor(this.drawingTool(), this.colour));
            stroke.path.setAttribute("d", outlinePath(stroke.points, this.drawingRadius(), this.drawingTool() === "pen"));
        }
    };

    private readonly onPointerUp = (event: PointerEvent): void => {
        const stroke = this.stroke;
        if (stroke === null || event.pointerId !== stroke.pointerId) {
            return;
        }
        this.endStroke();
        if (stroke.erasing) {
            if (stroke.removed.length > 0) {
                this.remember({ added: [], removed: stroke.removed });
                this.changedMarks();
            }
            return;
        }
        if (!stroke.moved) {
            if (stroke.target !== null) {
                this.score.tap(stroke.target, stroke.extend);
            }
            return;
        }
        const tool = this.drawingTool();
        const mark = anchorMark(this.frames, stroke.points, {
            id: newId(),
            tool,
            colour: this.colour,
            size: tool === "highlighter" ? HIGHLIGHTER_SIZE : PEN_SIZE,
        });
        if (mark !== null) {
            this.marks = [...this.marks, mark];
            this.remember({ added: [mark], removed: [] });
            // Writing with the Pencil shows the tools, so the eraser and Undo are at hand.
            this.on = true;
            this.host.classList.add("pencil-on");
            this.changedMarks();
        } else {
            this.draw();
        }
    };

    private readonly onPointerCancel = (event: PointerEvent): void => {
        const stroke = this.stroke;
        if (stroke === null || event.pointerId !== stroke.pointerId) {
            return;
        }
        this.endStroke();
        if (stroke.removed.length > 0) {
            this.remember({ added: [], removed: stroke.removed });
            this.changedMarks();
        }
    };

    private endStroke(): void {
        const stroke = this.stroke;
        if (stroke === null) {
            return;
        }
        this.stroke = null;
        this.clickAfter = performance.now() + CLICK_AFTER_MS;
        stroke.path?.remove();
        if (this.host.hasPointerCapture(stroke.pointerId)) {
            this.host.releasePointerCapture(stroke.pointerId);
        }
    }

    private drawingTool(): "pen" | "highlighter" {
        return this.tool === "highlighter" ? "highlighter" : "pen";
    }

    /** Half the thickness of the line being written, in pixels, at the staff size of the bar nearest its start. */
    private drawingRadius(): number {
        const first = this.stroke?.points[0];
        const space = first === undefined ? 0 : this.nearestSpace(first);
        return ((this.drawingTool() === "highlighter" ? HIGHLIGHTER_SIZE : PEN_SIZE) * space) / 2;
    }

    private nearestSpace(point: InkPoint): number {
        let best = this.frames[0]?.space ?? 9;
        let distance = Infinity;
        for (const frame of this.frames) {
            const d = Math.abs((frame.top + frame.bottom) / 2 - point.y) + Math.abs((frame.left + frame.right) / 2 - point.x);
            if (d < distance) {
                distance = d;
                best = frame.space;
            }
        }
        return best;
    }

    private eraseAt(point: InkPoint): void {
        const stroke = this.stroke;
        let taken = false;
        for (const [id, place] of this.placed) {
            if (touches(place.points, place.radius, point.x, point.y, ERASER_REACH)) {
                const mark = this.marks.find((candidate) => candidate.id === id);
                if (mark !== undefined) {
                    stroke?.removed.push(mark);
                }
                place.path.remove();
                this.placed.delete(id);
                taken = true;
            }
        }
        if (taken) {
            const left = new Set(this.placed.keys());
            this.marks = this.marks.filter((mark) => left.has(mark.id) || !this.byTick.has(mark.bar));
        }
    }

    private readonly onTouch = (event: TouchEvent): void => {
        if (this.key === null) {
            return;
        }
        for (const touch of Array.from(event.changedTouches)) {
            if ((touch as Touch & { touchType?: string }).touchType === "stylus") {
                event.preventDefault();
                return;
            }
        }
    };

    private readonly onNativePencil = (event: Event): void => {
        if (this.key === null || !this.host.isConnected) {
            return;
        }
        const action = (event as CustomEvent<{ action?: string }>).detail?.action;
        if (action === "switch-eraser") {
            this.setTool(this.tool === "eraser" ? this.toolBeforeEraser : "eraser", null);
        } else if (action === "toggle-tools") {
            this.setOn(!this.on);
        }
    };

    private readonly onPageHide = (): void => {
        this.flush();
    };
}
