import Foundation

/// The modes of version 0.5 (the experiment Two playheads and modes, Settings · Experimental; product spec
/// interaction-modes, «Правила выделения по режимам»): one group in the bar for the score and the roll, as on the web.
/// On the iPad one finger does the mode's action and two fingers scroll, except in Navigate, where one finger moves the
/// page. The Pen is the bar's Pen being on (`ScoreController.penOn`).
enum InteractionMode: String, CaseIterable, Sendable {
    case navigate, select, edit, pen

    /// The kit's icon (design/kit/icons.js): move, select, edit notes and the pen.
    var icon: String {
        switch self {
        case .navigate: "move"
        case .select: "select"
        case .edit: "editnote"
        case .pen: "pen"
        }
    }

    /// The key that chooses it on a keyboard, as on the web: H, S, E and P.
    var key: Character {
        switch self {
        case .navigate: "h"
        case .select: "s"
        case .edit: "e"
        case .pen: "p"
        }
    }

    /// Its name and its hint in the web app's words.
    var label: String { "mode.\(rawValue)" }
    var hint: String { "mode.\(rawValue)Hint" }

    /// How many fingers scroll the score: two while one finger does the mode's action or writes, one in Navigate and
    /// before the modes.
    static func scrollFingers(mode: InteractionMode?, penOn: Bool) -> Int {
        if penOn {
            return 2
        }
        guard let mode else { return 1 }
        return mode == .navigate ? 1 : 2
    }
}

/// The two playheads of version 0.5 (product spec two-playheads): one player, two places. The global playhead plays the
/// piece from the bar's Play and never stops at the selection; the selection's plays only inside it, from Play first in
/// its menu, round on Loop or once without it. Whichever has the player is the player's place; the other waits here.
struct TwoPlayheads: Equatable, Sendable {
    /// Where the global playhead waits while the selection has the player.
    var globalAt = 0.0
    /// Where the selection playhead waits while the piece has the player; nil for the selection's start.
    var selectionAt: Double?
    /// Loop in the selection's menu: the selection plays round, or once and back at its start.
    var loopChosen = true

    /// Where a tap places a playhead: the selection's inside it, else the global one.
    enum Placed: Equatable {
        case selection(Double)
        case global(Double)
    }

    static func placed(_ tick: Double, selection: Range<Double>?) -> Placed {
        if let selection, selection.contains(tick) {
            return .selection(tick)
        }
        return .global(tick)
    }

    /// The selection playhead's place while the piece has the player: where it was left inside the selection, else its start.
    func selectionWaits(in selection: Range<Double>) -> Double {
        if let selectionAt, selection.contains(selectionAt) {
            return selectionAt
        }
        return selection.lowerBound
    }

    /// What the page draws (the web app's Scripts/score/playheads.ts): whether the selection has the player, where the
    /// waiting playhead stands, and whether the other bars fade while the piece plays.
    func page(selection: Range<Double>?, holds: Bool, fade: Bool) -> [String: Any] {
        var data: [String: Any] = ["selectionHolds": holds, "global": globalAt, "fade": fade]
        if let selection {
            data["selection"] = holds ? selection.lowerBound : selectionWaits(in: selection)
        } else {
            data["selection"] = NSNull()
        }
        return data
    }
}
