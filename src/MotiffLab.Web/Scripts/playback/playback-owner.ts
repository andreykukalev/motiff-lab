// The web's owner of the playback model of v0.5.2 (#495 UX-10, the architect's design playback-model-v0.5.2, slice W1; the
// experiment «Два указателя и режимы»): the TypeScript Player (playback.ts) holds the selection and Цикл here, takes the
// position and the transport from the engine, and runs every event through the shared rules (playback-model.ts, the ones
// the iPad's Player runs in Swift on the same case file). Blazor only sends the events and shows what comes back.
//
// Other owners of the engine's loop (hearing in turn, the answer, the circle's scale, the DNA map) set their own loop: while
// one holds it (`take`), the model's loop steps change the model but not the engine, and `hold` gives the engine the
// model's loop again once they end.

import { engineLoop, next, viewOf, type Context, type ModelEvent, type ModelState, type Span, type Step, type Transport } from "./playback-model.js";

/** Where the engine is, from its last report. */
export interface EnginePlace {
    tick: number;
    transport: Transport;
}

/** What Blazor is told of the model: changes only when one of these does, not with every move of the position. */
export interface ModelReport {
    /** Цикл, as the pill shows it. */
    loop: boolean;
    context: Context;
    /** Whether the position lies inside the selection. */
    inside: boolean;
    /** The blue playhead while it does not move with the black one (outside the selection), else null. */
    waits: number | null;
    /** The place Play names when it is not the position (stopped with Цикл on outside the selection: its start), else null. */
    at: number | null;
}

/** The engine's loop after an answer: a span, none, or untouched (undefined). */
export type LoopAfter = Span | null | undefined;

export class PlaybackOwner {
    /** Whether the model holds: the experiment is on and the page is not in focus. Off, the page drives the engine itself. */
    held = false;
    private selection: Span | null = null;
    /** Цикл: on by itself with a new selection (design question В2), kept while there is none. */
    private loop = true;
    /** Another owner holds the engine's loop until `hold`. */
    private taken = false;

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
            inside: view.inside,
            waits: view.blue === null || view.inside ? null : view.blue,
            at: view.at === view.black ? null : view.at,
        };
    }

    /**
     * One event of the model: the engine gets its steps (the loop's only while no other owner holds it), and the selection
     * and Цикл become what the model says. Returns the engine's loop after it, undefined when the steps leave it alone, and
     * where the engine will be once it has the steps.
     */
    apply(event: ModelEvent, engine: EnginePlace): { loop: LoopAfter; engine: EnginePlace } {
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

    /** A new piece: no selection; Цикл and whether the model holds stay as they were. */
    reset(): void {
        this.selection = null;
        this.taken = false;
    }
}
