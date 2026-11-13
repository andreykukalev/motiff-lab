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
