import Foundation

/// The playback model of v0.5.2 (#495 UX-10, the architect's design playback-model-v0.5.2; Settings · Experimental, «Два
/// указателя и режимы»): one position in the engine, two playheads on screen, one Play button and the selection's Цикл. The
/// engine's position is the black playhead, the piece's place; the blue one stores nothing, it is the position held to the
/// selection; Цикл is the promise that while it is on, playback keeps the position inside the selection.
///
/// The Swift port of the reference, src/MotiffLab.Web/Scripts/playback/playback-model.ts: the same pure rules (the
/// design's T1–T16, the drag of a playhead included), run on the same case file, MotiffLabTests/Golden/playback-model.json
/// (PlaybackPositionTests). Player owns the selection and Цикл and sends the steps to the engine.
enum PlaybackModel {
    /// The selection and Цикл are the player's own, the tick and the transport the engine's.
    struct State: Equatable {
        /// The selection as the range it plays, [start, end).
        var selection: Range<Double>?
        /// Цикл: the selection loops while it is on; it means something only with a selection, and is kept without one.
        var loop: Bool
        var tick: Double
        var transport: TransportState
    }

    /// Which playhead a gesture held: the black one is the piece's, the blue one the selection's.
    enum Handle: String {
        case black, blue
    }

    enum Event: Equatable {
        /// T1–T3: the Play button, Space or a pedal.
        case press
        /// T4–T6: the engine reported where it is and whether it plays (it loops and stops by itself).
        case report(tick: Double, transport: TransportState)
        /// T7, T8: the Цикл control.
        case cycle
        /// T9–T11: the selection appeared, changed or went (nil).
        case select(Range<Double>?)
        /// T12: a tap on a beat or a bar, or on the whole-piece strip, moves the black playhead.
        case seek(Double)
        /// T13: a tap on the roll's loop strip moves both playheads inside the selection.
        case seekInLoop(Double)
        /// T14: a playhead's handle taken by the pen or a finger: playback stands while it is held.
        case grab
        /// T15: the held playhead dragged to a tick the view already put on the grid and in its bar (playhead-drag.ts).
        case drag(Handle, to: Double)
        /// T16: a playhead let go where the position now is, playing on when it played as the gesture began.
        case release(Handle, resume: Bool)
    }

    /// The colour of the Play button and its words: the selection's (cobalt) or the piece's (ink).
    enum Context: String {
        case selection, piece
    }

    /// What the views draw, derived from the state alone.
    struct View: Equatable {
        /// Whether the position lies inside the selection, from its start up to, not at, its end.
        var inside: Bool
        /// The black playhead: the position, always shown.
        var black: Double
        /// The blue playhead, with a selection: the position held to it; at its end it stands at the band's right edge.
        var blue: Double?
        var playing: Bool
        /// Playing, where the sound is; stopped, what Play will play.
        var context: Context
        /// The place the Play button names: the position, or stopped with Цикл on outside the selection, where Play starts.
        var at: Double
    }

    /// The model after an event, as the engine will report it once it has the steps, and the steps, in order.
    typealias Decision = (state: State, steps: [TransportStep])

    static func inside(_ tick: Double, _ selection: Range<Double>?) -> Bool {
        selection?.contains(tick) ?? false
    }

    static func playing(_ transport: TransportState) -> Bool {
        transport != .stopped
    }

    /// The passage the engine loops for this model: the selection while Цикл is on, else none. Other owners of the engine's
    /// loop (hearing in turn, a variation, the circle's scale, focus) put this one back when they end.
    static func engineLoop(_ state: State) -> Range<Double>? {
        state.loop ? state.selection : nil
    }

    /// The two playheads, the Play button's colour and the place it names.
    static func view(of state: State) -> View {
        let within = inside(state.tick, state.selection)
        let sounding = playing(state.transport)
        var blue: Double?
        var at = state.tick
        var holds = within
        if let selection = state.selection {
            if within {
                blue = state.tick
            } else {
                blue = state.loop ? selection.lowerBound : min(max(state.tick, selection.lowerBound), selection.upperBound)
            }
            if !sounding {
                holds = state.loop || within
                if state.loop && !within { at = selection.lowerBound }
            }
        }
        return View(inside: within, black: state.tick, blue: blue, playing: sounding, context: holds ? .selection : .piece, at: at)
    }

    /// The model's answer to an event: what the engine is told and what the state becomes.
    static func next(_ state: State, _ event: Event) -> Decision {
        switch event {
        case .press:
            return press(state)
        case .report(let tick, let transport):
            var after = state
            after.tick = tick
            after.transport = transport
            return (after, [])
        case .cycle:
            return cycle(state)
        case .select(let selection):
            return select(state, selection)
        case .seek(let at):
            return seek(state, to: at)
        case .seekInLoop(let at):
            return seekInLoop(state, to: at)
        case .grab:
            return grab(state)
        case .drag(let handle, let at):
            return drag(state, handle, to: at)
        case .release(let handle, let resume):
            return release(state, handle, resume: resume)
        }
    }

    /// T1 a pause while it plays; T2 stopped with Цикл on outside the selection, the selection from its start; T3 else from
    /// the position.
    private static func press(_ state: State) -> Decision {
        var after = state
        if playing(state.transport) {
            after.transport = .stopped
            return (after, [.pause])
        }
        after.transport = .playing
        if let selection = state.selection, state.loop, !selection.contains(state.tick) {
            after.tick = selection.lowerBound
            return (after, [.seek(selection.lowerBound), .play])
        }
        return (after, [.play])
    }

    /// T7 on: the selection loops and the position goes to its start, playing on or waiting there as it was; T8 off:
    /// playback goes on past the selection. Without a selection there is no Цикл to press.
    private static func cycle(_ state: State) -> Decision {
        guard let selection = state.selection else { return (state, []) }
        var after = state
        after.loop = !state.loop
        if state.loop {
            return (after, [.loopOff])
        }
        after.tick = selection.lowerBound
        return (after, [.loop(selection), .seek(selection.lowerBound)])
    }

    /// T9 a selection appears: Цикл turns on (design question В2); T10 it changes: Цикл as it was; T11 it goes: nothing
    /// loops, Цикл is kept. The engine loops the new selection while Цикл is on; the position never moves, playing or not.
    private static func select(_ state: State, _ selection: Range<Double>?) -> Decision {
        guard selection != state.selection else { return (state, []) }
        var after = state
        after.selection = selection
        if state.selection == nil && selection != nil {
            after.loop = true
        }
        if let wanted = engineLoop(after) {
            return (after, [.loop(wanted)])
        }
        let steps: [TransportStep] = engineLoop(state) != nil ? [.loopOff] : []
        return (after, steps)
    }

    /// T12: the black playhead is the piece's, so taking it out of the selection leaves the loop: Цикл turns off first (В8).
    private static func seek(_ state: State, to at: Double) -> Decision {
        let to = max(0, at)
        var after = state
        after.tick = to
        if engineLoop(state) != nil && !inside(to, state.selection) {
            after.loop = false
            return (after, [.loopOff, .seek(to)])
        }
        return (after, [.seek(to)])
    }

    /// T13: the loop strip moves the position inside the selection only, so Цикл stays as it is.
    private static func seekInLoop(_ state: State, to at: Double) -> Decision {
        guard let selection = state.selection else { return (state, []) }
        let to = min(max(at, selection.lowerBound), max(selection.lowerBound, selection.upperBound - 1))
        var after = state
        after.tick = to
        return (after, [.seek(to)])
    }

    /// T14: playback stands while a playhead is held, so the drag is heard alone; the player keeps whether it played (T16).
    private static func grab(_ state: State) -> Decision {
        guard playing(state.transport) else { return (state, []) }
        var after = state
        after.transport = .stopped
        return (after, [.pause])
    }

    /// T15: the position follows the held playhead. The black one goes anywhere in the piece (the view keeps it in its bar on
    /// the score); the blue one stays inside the selection, so Цикл is never touched while it moves. Nothing is sent when the
    /// place is where the position already is. The engine scrubs there: the notes whose starts it passes sound (#495 UX-09).
    private static func drag(_ state: State, _ handle: Handle, to at: Double) -> Decision {
        var to = max(0, at)
        if handle == .blue {
            guard let selection = state.selection else { return (state, []) }
            to = min(max(at, selection.lowerBound), max(selection.lowerBound, selection.upperBound - 1))
        }
        guard to != state.tick else { return (state, []) }
        var after = state
        after.tick = to
        return (after, [.scrub(from: state.tick, to: to)])
    }

    /// T16, the rules of letting go: the black playhead let go outside the selection with Цикл on turns Цикл off (as T12);
    /// the blue one stays inside the selection and leaves Цикл alone. The drag's notes fade first; playing when the gesture
    /// began, it plays on from there.
    private static func release(_ state: State, _ handle: Handle, resume: Bool) -> Decision {
        var after = state
        var steps: [TransportStep] = [.scrubEnd]
        if handle == .black && engineLoop(state) != nil && !inside(state.tick, state.selection) {
            after.loop = false
            steps.append(.loopOff)
        }
        if resume && !playing(state.transport) {
            after.transport = .playing
            steps.append(.play)
        }
        return (after, steps)
    }
}

/// What the engine is told, in order (PlaybackModel's steps, Player.perform).
enum TransportStep: Equatable {
    case pause
    case play
    case seek(Double)
    case loop(Range<Double>)
    case loopOff
    /// The position moved by a drag while stopped: the notes whose starts it passed sound (#495 UX-09).
    case scrub(from: Double, to: Double)
    /// The drag let go: the notes it sounded fade.
    case scrubEnd
}

/// A place written as bar.beat: "16.2" is bar 16, beat 2, the bar's printed number and the beat as a selection counts it.
struct BarBeat: Equatable, Sendable {
    var bar: Int
    var beat: Int

    var text: String { "\(bar).\(beat)" }
}

/// The piece's beats with their bar and beat, from music-core's playback score (its beats' "bar" and "beat", v0.5.2): what
/// writes the position and the selection's span as bar.beat. Empty before the score is read, and for an older answer.
struct BarBeats: Equatable, Sendable {
    struct Mark: Equatable, Sendable {
        var at: Double
        var place: BarBeat
    }

    /// In time order.
    var marks: [Mark] = []

    /// The beat a tick lies in: the last beat at or before it, or the first beat before any.
    func at(_ tick: Double) -> BarBeat? {
        var low = 0
        var high = marks.count
        while low < high {
            let middle = (low + high) / 2
            if marks[middle].at <= tick {
                low = middle + 1
            } else {
                high = middle
            }
        }
        return low > 0 ? marks[low - 1].place : marks.first?.place
    }

    /// The selection's span: the beat it starts in and the last beat that starts inside it, as "9.1" and "12.3".
    func span(_ selection: Range<Double>) -> (first: String, last: String)? {
        guard let first = at(selection.lowerBound), let last = at(max(selection.lowerBound, selection.upperBound - 1)) else { return nil }
        return (first.text, last.text)
    }

    /// The beats of music-core's playback answer, each with its "at", "bar" and "beat".
    static func read(_ beats: [[String: Any]]) -> BarBeats {
        let marks = beats.compactMap { beat -> Mark? in
            guard let at = (beat["at"] as? NSNumber)?.doubleValue, let bar = (beat["bar"] as? NSNumber)?.intValue,
                  let number = (beat["beat"] as? NSNumber)?.intValue else { return nil }
            return Mark(at: at, place: BarBeat(bar: bar, beat: number))
        }
        return BarBeats(marks: marks)
    }
}
