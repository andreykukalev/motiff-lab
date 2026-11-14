// The web's owner of the playback model of v0.5.2 (#495 UX-10, the architect's design playback-model-v0.5.2, slices W1–W3;
// the experiment «Два указателя и режимы»): the TypeScript Player (playback.ts) holds the selection and Цикл here, takes the
// position and the transport from the engine, and runs every event through the shared rules (playback-model.ts, the ones
// the iPad's Player runs in Swift on the same case file). Blazor only sends the events and shows what comes back; a
// playhead dragged on the score or the roll comes here from the view straight (`gesture`, #495 UX-08).
//
// Other owners of the engine's loop (hearing in turn, the answer, the circle's scale, the DNA map) set their own loop: while
// one holds it (`take`), the model's loop steps change the model but not the engine, and `hold` gives the engine the
// model's loop again once they end. In Expand the model lets go, and the playhead on its roll is held over the passage
// (PassageGrip, #533 2).

import { engineLoop, next, playingOf, viewOf, type Context, type Handle, type ModelEvent, type ModelState, type Span, type Step, type Transport } from "./playback-model.js";
import type { DragMessage } from "./playhead-drag.js";

/** Where the engine is, from its last report. */
export interface EnginePlace {
    tick: number;
    transport: Transport;
}

/** What Blazor is told of the model: changes only when one of these does, not with every move of the position. The
 * playheads the views draw themselves from the player's model (score-view.ts, piano-roll.ts). */
export interface ModelReport {
    /** Цикл, as the pill shows it. */
    loop: boolean;
    context: Context;
    /** The place Play names when it is not the position (stopped with Цикл on outside the selection: its start), else null. */
    at: number | null;
}

/** The engine's loop after an answer: a span, none, or untouched (undefined). */
export type LoopAfter = Span | null | undefined;

/** What an event did: the engine's loop after it, and where the engine will be once it has the steps. */
export interface Applied {
    loop: LoopAfter;
    engine: EnginePlace;
}

/**
 * A playhead held by the mouse, the pen or a finger (T14–T16, as the iPad's PlayheadDrag.swift): which one, whether playback
 * played as it was taken, so it plays on when it is let go, and the place it was last dragged to, which the release rules
 * read before the engine has reported it.
 */
interface Held {
    handle: Handle;
    resume: boolean;
    last: number;
}

export class PlaybackOwner {
    /** Whether the model holds: the experiment is on and the page is not in focus. Off, the page drives the engine itself. */
    held = false;
    private selection: Span | null = null;
    /** Цикл: on by itself with a new selection (design question В2), kept while there is none. */
    private loop = true;
    /** Another owner holds the engine's loop until `hold`. */
    private taken = false;
    /** The playhead held now, or null. */
    private gripped: Held | null = null;

    constructor(private readonly perform: (step: Step) => void) {}

    /** The model's state with the engine where it is. */
    state(engine: EnginePlace): ModelState {
        return { selection: this.selection, loop: this.loop, tick: engine.tick, transport: engine.transport };
    }

    /** What Blazor is told, from the state with the engine where it is. */
    report(engine: EnginePlace): ModelReport {
        const view = viewOf(this.state(engine));
        return {
            loop: this.loop,
            context: view.context,
            at: view.at === view.black ? null : view.at,
        };
    }

    /**
     * One event of the model: the engine gets its steps (the loop's only while no other owner holds it), and the selection
     * and Цикл become what the model says. Returns the engine's loop after it, undefined when the steps leave it alone, and
     * where the engine will be once it has the steps.
     */
    apply(event: ModelEvent, engine: EnginePlace): Applied {
        const decided = next(this.state(engine), event);
        this.selection = decided.state.selection;
        this.loop = decided.state.loop;
        let loop: LoopAfter;
        for (const step of decided.steps) {
            if (step.type === "loop" || step.type === "loopOff") {
                if (this.taken) {
                    continue;
                }
                loop = step.type === "loop" ? { start: step.start, end: step.end } : null;
            }
            this.perform(step);
        }
        return { loop, engine: { tick: decided.state.tick, transport: decided.state.transport } };
    }

    /** Whether a playhead is held now: Blazor hears of the gesture at its start and its end only. */
    get dragging(): boolean {
        return this.gripped !== null;
    }

    /**
     * A playhead's handle on the score or the roll (T14–T16, Player.handle(PlayheadGesture) on the iPad): taken, playback
     * stands; dragged, the position goes there and the notes it passes sound; let go, the release rules run on the place it
     * was let go at, and playback goes on when it played. A handle taken while another is still held lets the old one go
     * first. Null for a move or a release with nothing held.
     */
    gesture(message: DragMessage, engine: EnginePlace): Applied | null {
        const held = this.gripped;
        switch (message.phase) {
            case "grab":
                if (held !== null) {
                    this.gesture({ phase: "release", handle: held.handle }, engine);
                }
                this.gripped = { handle: message.handle, resume: playingOf(engine.transport), last: engine.tick };
                return this.apply({ type: "grab" }, engine);
            case "move": {
                if (held === null) {
                    return null;
                }
                const moved = this.apply({ type: "drag", handle: held.handle, at: message.tick }, standing(held));
                held.last = moved.engine.tick;
                return moved;
            }
            case "release":
                if (held === null) {
                    return null;
                }
                this.gripped = null;
                return this.apply({ type: "release", handle: held.handle, resume: held.resume }, standing(held));
        }
    }

    /** Another owner set the engine's loop: the model's waits until `hold`. */
    take(): void {
        this.taken = true;
    }

    /** The engine loops what the model says again (Player.holdLoop on the iPad): the selection while Цикл is on, or nothing. */
    hold(engine: EnginePlace): Span | null {
        this.taken = false;
        const loop = engineLoop(this.state(engine));
        this.perform(loop === null ? { type: "loopOff" } : { type: "loop", start: loop.start, end: loop.end });
        return loop;
    }

    /** A new piece: no selection and nothing held; Цикл and whether the model holds stay as they were. */
    reset(): void {
        this.selection = null;
        this.taken = false;
        this.gripped = null;
    }
}

/** The engine as a drag holds it: at the place last dragged to, and standing, as the grab made it, though it may not have
 * reported either yet. */
function standing(held: Held): EnginePlace {
    return { tick: held.last, transport: "stopped" };
}

/** The model as Expand holds a drag (#533 2): the passage as a selection that always loops, so the playhead is the blue one. */
export function passageState(passage: Span, engine: EnginePlace): ModelState {
    return { selection: { start: passage.start, end: passage.end }, loop: true, tick: engine.tick, transport: engine.transport };
}

/**
 * The playhead on Expand's roll held (#533 2, ExpandRules.gesture on the iPad): the model's T14–T16 over the passage, the
 * blue playhead of a selection that always loops. Taken, playback stands; dragged, the position goes there inside the
 * passage and the notes it passes sound; let go, they fade and it plays on when it played. The passage's loop is never
 * touched, and the model outside Expand is left as it is (T18).
 */
export class PassageGrip {
    private gripped: Held | null = null;

    constructor(private readonly perform: (step: Step) => void) {}

    /** Whether the playhead is held now. */
    get dragging(): boolean {
        return this.gripped !== null;
    }

    /** A message of the drag over the passage; where the engine will be once it has the steps, null with nothing held. */
    gesture(message: DragMessage, passage: Span, engine: EnginePlace): EnginePlace | null {
        const held = this.gripped;
        switch (message.phase) {
            case "grab": {
                const before = held === null ? engine : (this.gesture({ phase: "release", handle: "blue" }, passage, engine) ?? engine);
                this.gripped = { handle: "blue", resume: playingOf(before.transport), last: before.tick };
                return this.run(passageState(passage, before), { type: "grab" });
            }
            case "move": {
                if (held === null) {
                    return null;
                }
                const moved = this.run(passageState(passage, standing(held)), { type: "drag", handle: "blue", at: message.tick });
                held.last = moved.tick;
                return moved;
            }
            case "release":
                if (held === null) {
                    return null;
                }
                this.gripped = null;
                return this.run(passageState(passage, standing(held)), { type: "release", handle: "blue", resume: held.resume });
        }
    }

    private run(state: ModelState, event: ModelEvent): EnginePlace {
        const decided = next(state, event);
        for (const step of decided.steps) {
            this.perform(step);
        }
        return { tick: decided.state.tick, transport: decided.state.transport };
    }
}
