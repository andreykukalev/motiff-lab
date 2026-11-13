import Foundation

/// One playback position (v0.5.2, #479 items 19, 22 and 25; product spec two-playheads, as approved on 2026-11-13): the
/// score, the roll and the pill show the same place. Inside the selection it is the selection's, in cobalt; outside it is
/// the piece's, in ink. With Цикл on the selection loops, also when playing began before it was made; with Цикл off playback
/// goes on past it. Pure decisions, so they are tested without the audio.
enum PlaybackPosition {
    /// Whether a tick lies inside the selection, from its start up to, not at, its end.
    static func inside(_ tick: Double, _ selection: Range<Double>?) -> Bool {
        selection?.contains(tick) ?? false
    }

    /// The passage the engine loops: the selection while Цикл is on, else none.
    static func loop(selection: Range<Double>?, cycle: Bool) -> Range<Double>? {
        cycle ? selection : nil
    }

    /// Цикл after the selection changed: on by itself when a selection appears where there was none; else as it was.
    static func cycle(after old: Range<Double>?, _ new: Range<Double>?, was cycle: Bool) -> Bool {
        old == nil && new != nil ? true : cycle
    }

    /// The cobalt Play (or Space with a selection): a pause while the position plays inside the selection; else the selection
    /// from where the position is inside it, or from its start, playing on past it when Цикл is off.
    static func playSelection(_ selection: Range<Double>, at tick: Double, playing: Bool) -> [TransportStep] {
        let within = inside(tick, selection)
        if playing && within {
            return [.pause]
        }
        var steps: [TransportStep] = within ? [] : [.seek(selection.lowerBound)]
        if !playing {
            steps.append(.play)
        }
        return steps
    }

    /// The ink Play (or Space with nothing selected): the piece from the position, or a pause while it plays outside the
    /// selection. Inside the selection it plays on past it, so Цикл goes off (`cycle` is what Цикл becomes). From outside
    /// with Цикл on, the selection still loops when playback reaches it: the engine plays first, then gets the loop, since
    /// its Play would jump into a loop it already had.
    static func playPiece(selection: Range<Double>?, at tick: Double, playing: Bool, cycle: Bool) -> (steps: [TransportStep], cycle: Bool) {
        guard let selection else {
            return ([playing ? .pause : .play], cycle)
        }
        if inside(tick, selection) {
            var steps: [TransportStep] = cycle ? [.loopOff] : []
            if !playing {
                steps.append(.play)
            }
            return (steps, false)
        }
        if playing {
            return ([.pause], cycle)
        }
        return (cycle ? [.loopOff, .play, .loop(selection)] : [.play], cycle)
    }
}

/// What the player is told to do, in order (Player.perform).
enum TransportStep: Equatable {
    case pause
    case play
    case seek(Double)
    case loop(Range<Double>)
    case loopOff
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

/// The pill's state (v0.5.2, #479 item 22, approved mockup): with a selection a cobalt Play with its span, Цикл, and an ink
/// Play from the position; without one only the ink Play. The Play that has the position plays in its colour.
struct TransportPill: Equatable {
    /// What the cobalt button says: the span "9.1 – 12.3", or while it plays inside, the position and the span "10.3 · 9.1–12.3".
    enum SelectionLabel: Equatable {
        case span(first: String, last: String)
        case inside(position: String, first: String, last: String)
    }

    /// What the ink button says: "С 16.2" (from the position), or "Пауза · 16.2" while the piece plays.
    enum PieceLabel: Equatable {
        case from(String)
        case pauseAt(String)
    }

    /// The ink button's sign: a pause while the piece plays; a filled Play alone; an outlined Play beside the cobalt one.
    enum PieceIcon: Equatable {
        case pause
        case play
        case playOutline
    }

    struct SelectionButton: Equatable {
        var playing: Bool
        var label: SelectionLabel
    }

    struct PieceButton: Equatable {
        var icon: PieceIcon
        /// A filled ink disc (it has the position or stands alone), else an ink ring.
        var filled: Bool
        var label: PieceLabel
    }

    /// Nil without a selection.
    var selection: SelectionButton?
    var cycle: Bool
    var piece: PieceButton

    /// The pill for the position (`position`, as bar.beat), the selection's span when there is one, whether the position is
    /// inside it, and whether the player plays.
    static func of(position: String, span: (first: String, last: String)?, inside: Bool, playing: Bool, cycle: Bool) -> TransportPill {
        let within = span != nil && inside
        let piecePlaying = playing && !within
        let selection = span.map { span in
            SelectionButton(
                playing: playing && within,
                label: playing && within ? .inside(position: position, first: span.first, last: span.last) : .span(first: span.first, last: span.last)
            )
        }
        let icon: PieceIcon = piecePlaying ? .pause : span == nil ? .play : .playOutline
        let piece = PieceButton(icon: icon, filled: piecePlaying || span == nil, label: piecePlaying ? .pauseAt(position) : .from(position))
        return TransportPill(selection: selection, cycle: cycle, piece: piece)
    }
}
