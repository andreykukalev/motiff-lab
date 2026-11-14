import Foundation

/// Undo and Redo of the selection (#511 section 11, Andrei's decision of 2026-11-14): one step is one finished gesture
/// that changed the selection: made, an edge moved (from inside or by a handle), taken away (✕ or a hold outside), or
/// replaced (a hold, the selection gone, a drag: one step). Every change a gesture makes carries its name, so they are
/// kept as one step. A touch that changes nothing, a gesture cancelled, scrolling, the zoom, playing and the loop are no
/// steps. A new step drops what Redo had. Up to 50 steps for the piece while it is open, apart from the ink's Undo. The
/// same rules as music-core's SelectionHistory, which the web app keeps (MotiffLab.MusicCore.Motifs).
struct SelectionHistory<Selection: Equatable> {
    /// One step: the selection before and after it, the gesture that made it, and whether the selection was gone on the way.
    struct Step: Equatable {
        var before: Selection?
        var after: Selection?
        var gesture: String?
        var cleared: Bool

        enum Kind: Equatable {
            case create, change, clear, replace
        }

        /// What the step did: the toast «Вернуть» comes after a clear and a replace.
        var kind: Kind {
            if before == nil { return .create }
            if after == nil { return .clear }
            return cleared ? .replace : .change
        }
    }

    static var depth: Int { 50 }

    private(set) var done: [Step] = []
    private(set) var undone: [Step] = []

    var canUndo: Bool { !done.isEmpty }
    var canRedo: Bool { !undone.isEmpty }

    /// The selection changed from `before` to `after`, by the gesture named (nil for a change of its own, ✕ or a menu): the
    /// step it makes, or the step of that gesture it joins; nil when the selection ends as it was.
    @discardableResult
    mutating func record(from before: Selection?, to after: Selection?, gesture: String? = nil) -> Step? {
        if let gesture, let last = done.last, last.gesture == gesture {
            done.removeLast()
            let joined = Step(before: last.before, after: after, gesture: gesture, cleared: last.cleared || after == nil)
            guard joined.before != joined.after else { return nil }
            done.append(joined)
            return joined
        }
        guard before != after else { return nil }
        let step = Step(before: before, after: after, gesture: gesture, cleared: after == nil)
        done.append(step)
        if done.count > Self.depth { done.removeFirst(done.count - Self.depth) }
        undone.removeAll()
        return step
    }

    /// Takes the last step back: the owner selects its `before` again.
    mutating func undo() -> Step? {
        guard let step = done.popLast() else { return nil }
        undone.append(step)
        return step
    }

    /// Takes the step undone last again: the owner selects its `after` again.
    mutating func redo() -> Step? {
        guard let step = undone.popLast() else { return nil }
        done.append(step)
        return step
    }

    /// The gesture named was cancelled: its step goes, as if it never was; the owner selects its `before` again.
    mutating func revert(_ gesture: String) -> Step? {
        guard let last = done.last, last.gesture == gesture else { return nil }
        return done.popLast()
    }
}

/// The selection's history of the piece open now, kept beside the screen, which PDF | Notes makes anew (#511 11): the
/// same piece (by its project file, as music-core opens it again under another number) finds its history again; another
/// piece starts its own, and the last one's goes.
@MainActor
enum SelectionHistories {
    private static var kept: (piece: String, history: SelectionHistory<SelectionSpan>)?

    /// The piece as the history knows it: its project file, else its number in music-core.
    static func key(_ piece: OpenPiece) -> String {
        piece.file?.standardizedFileURL.path ?? "open-\(piece.id)"
    }

    static func history(of piece: String) -> SelectionHistory<SelectionSpan> {
        if let kept, kept.piece == piece { return kept.history }
        return SelectionHistory()
    }

    static func keep(_ history: SelectionHistory<SelectionSpan>, of piece: String) {
        kept = (piece, history)
    }
}

/// A selection as the history keeps it: its span in ticks, which music-core selects again exactly (by beats).
struct SelectionSpan: Equatable, Sendable {
    var onset: Double
    var end: Double

    init(onset: Double, end: Double) {
        self.onset = onset
        self.end = end
    }

    init?(_ selection: ScoreSelection?) {
        guard let selection else { return nil }
        self.init(onset: selection.onset, end: selection.end)
    }
}
