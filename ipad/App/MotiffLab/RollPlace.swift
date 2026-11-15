import SwiftUI

/// Selection by where you touch on the roll (#601, the experiment experimental.placeSelection, selection report E): in the
/// bar's Select a press over the whole roll is read by its place (Scripts/roll/roll-place-gesture.ts), a box or a lasso picks
/// notes as the one selection the score shows, and picked notes dragged are an edit on the note editor's draft. What the
/// roll page is told and what it says back, and «Рамка | Лассо» in the roll's head, live here.

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

    /// Whether «Рамка | Лассо» shows in the roll's head: only with the experiment on, in the bar's Select, while the roll is
    /// not editing notes (report E: «только пока включено „Выделение“»).
    static func shown(experiment: Bool, tool: InteractionMode?, rollMode: RollMode) -> Bool {
        experiment && tool == .select && rollMode == .navigate
    }
}

/// What the roll page is told of selection by where you touch, beside RollInput.page: whether the experiment is on, and
/// whether a stylus is used, so a finger beside it always moves the roll; without one, one finger selects (report C).
struct RollPlaceInput: Equatable, Sendable {
    /// The experiment's switch in Settings · Experimental (PlaceSelection.swift).
    static let setting = PlaceSelection.setting

    var on: Bool
    var stylus: Bool

    /// RollInput's page with `place` and `stylus` added (roll.js `input`); off, the page is told nothing new is on.
    func added(to page: [String: Any]) -> [String: Any] {
        var data = page
        data["place"] = on
        data["stylus"] = stylus
        return data
    }
}

/// Notes the roll picked in Select (`notes` message): their ids, how they change the selection, and the gesture. A box
/// from outside replaces the selection (the toast «Выделение заменено · Вернуть»), one from inside adds to it, an edge moved
/// narrows the picked notes (an edit, no toast).
struct RollNotesPick: Equatable, Sendable {
    enum Change: String, Sendable {
        case create, replace, add, edit
    }

    var ids: [String]
    var change: Change
    var gesture: String?

    init(ids: [String], change: Change, gesture: String? = nil) {
        self.ids = ids
        self.change = change
        self.gesture = gesture
    }

    /// From `{ ids, change, gesture }`; nil without notes. An unknown change is a new selection.
    init?(_ message: Any?) {
        guard let message = message as? [String: Any], let ids = message["ids"] as? [String], !ids.isEmpty else { return nil }
        self.init(ids: ids, change: (message["change"] as? String).flatMap(Change.init(rawValue:)) ?? .create, gesture: message["gesture"] as? String)
    }

    /// Whether the notes add to those picked (ScoreController.selectNotes `add`).
    var add: Bool { change == .add }

    /// Whether they replace a selection there was, so the old one is a step back with its toast (`replaces`).
    var replaces: Bool { change == .replace }
}

/// A span of the one selection the roll's place rule drew on the grid (v0.6.4, Web/roll.js `span`: a rectangle, an
/// extension, an edge moved, a bar tapped), selected exactly as it is, notes in it or none; `how` is "create", "replace" or
/// "edit".
struct RollSpan: Equatable, Sendable {
    var start: Double
    var end: Double
    var how: String
    var gesture: String?

    /// From `{ start, end, how, gesture }`; nil without a span of time.
    init?(_ message: Any?) {
        guard let message = message as? [String: Any], let start = (message["start"] as? NSNumber)?.doubleValue,
              let end = (message["end"] as? NSNumber)?.doubleValue, end > start else { return nil }
        self.start = start
        self.end = end
        how = message["how"] as? String ?? "create"
        gesture = message["gesture"] as? String
    }
}

extension RollNoteEdit {
    /// Picked notes let go moved (`moved.notes`: `[{ id, onset, pitch, length }]`), each a change on the draft, as a note
    /// moved in Edit notes is; a malformed entry is left out.
    static func moved(_ notes: Any?) -> [RollNoteEdit] {
        guard let notes = notes as? [[String: Any]] else { return [] }
        return notes.compactMap { note in
            var change = note
            change["kind"] = "change"
            return RollNoteEdit(change)
        }
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
