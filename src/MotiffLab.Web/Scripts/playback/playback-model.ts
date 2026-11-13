// The playback model of v0.5.2 (#495 UX-10, the architect's design architecture/playback-model-v0.5.2.md, the experiment
// «Два указателя и режимы»): one position in the engine, two playheads on screen, one Play button and the selection's Цикл.
// The engine's position P is the black playhead, the piece's place. The blue playhead stores nothing: it is P held to the
// selection S (equal to P inside it, waiting at its start before it, standing at its end after it). Цикл is the promise
// that while it is on, playback keeps the position inside the selection.
//
// This is the reference: pure functions over a state, written once. The iPad's Player runs a Swift port of it
// (ipad/App/MotiffLab/PlaybackPosition.swift, PlaybackModel), and both run the one case file
// ipad/App/MotiffLabTests/Golden/playback-model.json (tests/web/playback-model.test.mjs, PlaybackPositionTests.swift).
// The rules are the design's transitions T1–T16: a playhead taken (T14) pauses playback, dragged (T15) it moves the
// position, the blue one inside the selection, and the notes it passes sound (the engine's scrub, #495 UX-09), and let
// go (T16) the drag's notes fade and it plays on when it played.

/** A stretch of ticks [start, end), 480 to the quarter. */
export interface Span {
    start: number;
    end: number;
}

/** The engine's transport; counting in counts as playing. */
export type Transport = "stopped" | "countingIn" | "playing";

/** The model's state: the selection and Цикл are the player's own, the tick and the transport the engine's. */
export interface ModelState {
    selection: Span | null;
    /** Цикл: the selection loops while it is on; it means something only with a selection, and is kept without one. */
    loop: boolean;
    tick: number;
    transport: Transport;
}

/** What the engine is told, in order. */
export type Step =
    | { type: "pause" }
    | { type: "play" }
    | { type: "seek"; at: number }
    | { type: "loop"; start: number; end: number }
    | { type: "loopOff" }
    /** The position moved by a drag while stopped: the notes whose starts it passed sound (#495 UX-09). */
    | { type: "scrub"; from: number; to: number }
    /** The drag let go: the notes it sounded fade. */
    | { type: "scrubEnd" };

/** Which playhead a gesture held: the black one is the piece's, the blue one the selection's. */
export type Handle = "black" | "blue";

/** What happened. */
export type ModelEvent =
    /** T1–T3: the Play button, Space or a pedal. */
    | { type: "press" }
    /** T4–T6: the engine reported where it is and whether it plays (it loops and stops by itself). */
    | { type: "report"; tick: number; transport: Transport }
    /** T7, T8: the Цикл control. */
    | { type: "cycle" }
    /** T9–T11: the selection appeared, changed or went (null). */
    | { type: "select"; selection: Span | null }
    /** T12: a tap on a beat or a bar, or on the whole-piece strip, moves the black playhead. */
    | { type: "seek"; at: number }
    /** T13: a tap on the roll's loop strip moves both playheads inside the selection. */
    | { type: "seekInLoop"; at: number }
    /** T14: a playhead's handle taken by the pen or a finger: playback stands while it is held. */
    | { type: "grab" }
    /** T15: the held playhead dragged to a tick the view already put on the grid and in its bar (playhead-drag.ts). */
    | { type: "drag"; handle: Handle; at: number }
    /** T16: a playhead let go where the position now is, playing on when it played as the gesture began. */
    | { type: "release"; handle: Handle; resume: boolean };

/** The colour of the Play button and its words: the selection's (cobalt) or the piece's (ink). */
export type Context = "selection" | "piece";

/** What the views draw, derived from the state alone. */
export interface ModelView {
    /** Whether the position lies inside the selection, from its start up to, not at, its end. */
    inside: boolean;
    /** The black playhead: the position, always shown. */
    black: number;
    /** The blue playhead, with a selection: the position held to it; at its end it stands at the band's right edge. */
    blue: number | null;
    playing: boolean;
    /** Playing, where the sound is; stopped, what Play will play. */
    context: Context;
    /** The place the Play button names: the position, or stopped with Цикл on outside the selection, where Play starts. */
    at: number;
}

/** The model after an event, as the engine will report it once it has the steps, and the steps. */
export interface Decision {
    state: ModelState;
    steps: Step[];
}

export function insideOf(selection: Span | null, tick: number): boolean {
    return selection !== null && tick >= selection.start && tick < selection.end;
}

export function playingOf(transport: Transport): boolean {
    return transport !== "stopped";
}

/** The passage the engine loops for this model: the selection while Цикл is on, else none. Other owners of the engine's loop
 * (hearing in turn, a variation, the circle's scale, focus) put this one back when they end. */
export function engineLoop(state: ModelState): Span | null {
    return state.loop && state.selection !== null ? state.selection : null;
}

/** The two playheads, the Play button's colour and the place it names. */
export function viewOf(state: ModelState): ModelView {
    const { selection, loop, tick } = state;
    const inside = insideOf(selection, tick);
    const playing = playingOf(state.transport);
    let blue: number | null = null;
    if (selection !== null) {
        blue = inside ? tick : loop ? selection.start : Math.min(Math.max(tick, selection.start), selection.end);
    }
    const holds = playing ? inside : selection !== null && (loop || inside);
    const startsAtSelection = !playing && selection !== null && loop && !inside;
    return { inside, black: tick, blue, playing, context: holds ? "selection" : "piece", at: startsAtSelection ? selection.start : tick };
}

/** The model's answer to an event: what the engine is told and what the state becomes. */
export function next(state: ModelState, event: ModelEvent): Decision {
    switch (event.type) {
        case "press":
            return press(state);
        case "report":
            return { state: { ...state, tick: event.tick, transport: event.transport }, steps: [] };
        case "cycle":
            return cycle(state);
        case "select":
            return select(state, event.selection);
        case "seek":
            return seek(state, event.at);
        case "seekInLoop":
            return seekInLoop(state, event.at);
        case "grab":
            return grab(state);
        case "drag":
            return drag(state, event.handle, event.at);
        case "release":
            return release(state, event.handle, event.resume);
    }
}

/** T1 a pause while it plays; T2 stopped with Цикл on outside the selection, the selection from its start; T3 else from P. */
function press(state: ModelState): Decision {
    if (playingOf(state.transport)) {
        return { state: { ...state, transport: "stopped" }, steps: [{ type: "pause" }] };
    }
    const selection = state.selection;
    if (selection !== null && state.loop && !insideOf(selection, state.tick)) {
        return { state: { ...state, tick: selection.start, transport: "playing" }, steps: [{ type: "seek", at: selection.start }, { type: "play" }] };
    }
    return { state: { ...state, transport: "playing" }, steps: [{ type: "play" }] };
}

/** T7 on: the selection loops and the position goes to its start, playing on or waiting there as it was; T8 off: playback
 * goes on past the selection. Without a selection there is no Цикл to press. */
function cycle(state: ModelState): Decision {
    const selection = state.selection;
    if (selection === null) {
        return { state, steps: [] };
    }
    if (state.loop) {
        return { state: { ...state, loop: false }, steps: [{ type: "loopOff" }] };
    }
    return {
        state: { ...state, loop: true, tick: selection.start },
        steps: [{ type: "loop", start: selection.start, end: selection.end }, { type: "seek", at: selection.start }],
    };
}

function sameSpan(a: Span | null, b: Span | null): boolean {
    return a === b || (a !== null && b !== null && a.start === b.start && a.end === b.end);
}

/** T9 a selection appears: Цикл turns on (design question В2); T10 it changes: Цикл as it was; T11 it goes: nothing loops,
 * Цикл is kept. The engine loops the new selection while Цикл is on; the position never moves, playing or not. */
function select(state: ModelState, selection: Span | null): Decision {
    if (sameSpan(state.selection, selection)) {
        return { state, steps: [] };
    }
    const loop = state.selection === null && selection !== null ? true : state.loop;
    const after: ModelState = { ...state, selection: selection === null ? null : { ...selection }, loop };
    const was = engineLoop(state);
    const wanted = engineLoop(after);
    if (wanted !== null) {
        return { state: after, steps: [{ type: "loop", start: wanted.start, end: wanted.end }] };
    }
    return { state: after, steps: was !== null ? [{ type: "loopOff" }] : [] };
}

/** T12: the black playhead is the piece's, so taking it out of the selection leaves the loop: Цикл turns off first (В8). */
function seek(state: ModelState, at: number): Decision {
    const to = Math.max(0, at);
    if (engineLoop(state) !== null && !insideOf(state.selection, to)) {
        return { state: { ...state, loop: false, tick: to }, steps: [{ type: "loopOff" }, { type: "seek", at: to }] };
    }
    return { state: { ...state, tick: to }, steps: [{ type: "seek", at: to }] };
}

/** T13: the loop strip moves the position inside the selection only, so Цикл stays as it is. */
function seekInLoop(state: ModelState, at: number): Decision {
    const selection = state.selection;
    if (selection === null) {
        return { state, steps: [] };
    }
    const to = Math.min(Math.max(at, selection.start), Math.max(selection.start, selection.end - 1));
    return { state: { ...state, tick: to }, steps: [{ type: "seek", at: to }] };
}

/** T14: playback stands while a playhead is held, so the drag is heard alone; the player keeps whether it played (T16). */
function grab(state: ModelState): Decision {
    if (playingOf(state.transport)) {
        return { state: { ...state, transport: "stopped" }, steps: [{ type: "pause" }] };
    }
    return { state, steps: [] };
}

/**
 * T15: the position follows the held playhead. The black one goes anywhere in the piece (the view keeps it in its bar on
 * the score); the blue one stays inside the selection, so Цикл is never touched while it moves. Nothing is sent when the
 * place is where the position already is. The engine scrubs there: the notes whose starts it passes sound.
 */
function drag(state: ModelState, handle: Handle, at: number): Decision {
    const selection = state.selection;
    if (handle === "blue" && selection === null) {
        return { state, steps: [] };
    }
    const to = handle === "blue" && selection !== null ? Math.min(Math.max(at, selection.start), Math.max(selection.start, selection.end - 1)) : Math.max(0, at);
    if (to === state.tick) {
        return { state, steps: [] };
    }
    return { state: { ...state, tick: to }, steps: [{ type: "scrub", from: state.tick, to }] };
}

/** T16, the rules of letting go: the black playhead let go outside the selection with Цикл on turns Цикл off (as T12); the
 * blue one stays inside the selection and leaves Цикл alone. The drag's notes fade first; playing when the gesture began,
 * it plays on from there. */
function release(state: ModelState, handle: Handle, resume: boolean): Decision {
    const steps: Step[] = [{ type: "scrubEnd" }];
    let after = state;
    if (handle === "black" && engineLoop(state) !== null && !insideOf(state.selection, state.tick)) {
        after = { ...after, loop: false };
        steps.push({ type: "loopOff" });
    }
    if (resume && !playingOf(state.transport)) {
        after = { ...after, transport: "playing" };
        steps.push({ type: "play" });
    }
    return { state: after, steps };
}
