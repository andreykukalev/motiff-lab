import SwiftUI

/// Selection by where you touch on the roll (#601, selection report E; Select's only way since v0.6.3): in the
/// bar's Select a press over the whole roll is read by its place (Scripts/roll/roll-place-gesture.ts), a box or a lasso picks
/// notes as the one selection the score shows, and picked notes dragged are an edit on the note editor's draft. What the
/// roll page is told and «Рамка | Лассо» in the roll's head live here; what it says back (RollNotesPick, RollSpan, the
/// notes moved) is the roll page's (Platform/RollPage.swift).

/// How Select picks notes on the roll: a box (the default) or a lasso, kept on the device as the player left it.
enum RollPickShape: String, CaseIterable, Sendable {
    case box, lasso

    static let key = "roll.pickShape"

    var label: String { "rollPick.\(rawValue)" }

    /// The shape kept on the device, or the box.
    static func saved(in defaults: UserDefaults = .standard) -> RollPickShape {
        defaults.string(forKey: key).flatMap(RollPickShape.init(rawValue:)) ?? .box
    }

    func keep(in defaults: UserDefaults = .standard) {
        defaults.set(rawValue, forKey: Self.key)
    }

    /// Whether «Рамка | Лассо» shows in the roll's head: only in the bar's Select, while the roll is not editing notes
    /// (report E: «только пока включено „Выделение“»).
    static func shown(tool: InteractionMode, rollMode: RollMode) -> Bool {
        tool == .select && rollMode == .navigate
    }
}

/// What the roll page is told of selection by where you touch, beside RollInput.page: that it is on (the page's `place`,
/// which the web app still turns on and off), and whether a stylus is used, so a finger beside it always moves the roll;
/// without one, one finger selects (report C).
struct RollPlaceInput: Equatable, Sendable {
    var stylus: Bool

    /// RollInput's page with `place` and `stylus` added (roll.js `input`).
    func added(to page: [String: Any]) -> [String: Any] {
        var data = page
        data["place"] = true
        data["stylus"] = stylus
        return data
    }
}

/// «Рамка | Лассо» in the roll's head, beside its mode switch, while Select picks notes there.
struct RollPickSwitch: View {
    @ObservedObject var roll: RollController
    let text: UIText

    var body: some View {
        SegmentedChoice(options: RollPickShape.allCases.map { ($0, text[$0.label]) }, chosen: roll.pickShape, label: text["rollPick.label"]) {
            roll.pickShape = $0
        }
        .fixedSize()
        .help(text["rollPick.hint"])
    }
}
