import SwiftUI

/// «Мои правки: вкл / выкл» as music-core answers it (version 0.5, contract 12; on the iPad since v0.6.5): the open
/// answer's "edits" and every corrections answer carry "count", and once the piece is open with a layer, "layer" ("on" or
/// "off"), "moved" and "unfit". A piece opened strictly (a new import, a recording heard) answers no "layer": all its
/// corrections fit and are laid, as with the layer on. The player's corrections are always kept: off only shows the
/// notes as recognised, and a correction that no longer fits stays in the piece until the player takes it out.
struct EditsLayerState: Equatable, Sendable {
    /// A correction laid on a near note of the same pitch, not on the one it names.
    struct Moved: Equatable, Sendable {
        var noteId: String
        var to: String
        var bar: Int
    }

    /// A correction that no longer fits the notes, with music-core's reason ("missing", "changed", "idTaken") and words.
    struct Unfit: Equatable, Sendable {
        var noteId: String
        var bar: Int
        var reason: String
        var text: String
    }

    /// One row of the list: the corrections of one note that no longer fit, taken out together (`corrections.drop`).
    struct UnfitNote: Identifiable, Equatable, Sendable {
        var id: String
        var bar: Int
        /// Why, in music-core's words, each once.
        var reasons: [String]
    }

    /// False while the corrections are off: the notes are as recognised and no correction can be made.
    var on = true
    /// How many corrections the piece's edit log holds, laid or not.
    var count = 0
    var moved: [Moved] = []
    var unfit: [Unfit] = []

    init(count: Int) {
        self.count = count
    }

    /// The fields of a corrections answer, or of the open answer's "edits"; nil without "count".
    init?(fields: [String: Any]) {
        guard let count = (fields["count"] as? NSNumber)?.intValue else { return nil }
        self.count = count
        on = (fields["layer"] as? String) != "off"
        moved = (fields["moved"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let noteId = entry["noteId"] as? String, let to = entry["to"] as? String else { return nil }
            return Moved(noteId: noteId, to: to, bar: (entry["bar"] as? NSNumber)?.intValue ?? 0)
        }
        unfit = (fields["unfit"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let noteId = entry["noteId"] as? String else { return nil }
            return Unfit(noteId: noteId, bar: (entry["bar"] as? NSNumber)?.intValue ?? 0, reason: entry["reason"] as? String ?? "", text: entry["text"] as? String ?? "")
        }
    }

    /// A corrections answer (`corrections`, `.layer`, `.drop`, a correction, the note editor's calls); nil for a refusal.
    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        self.init(fields: object)
    }

    /// The state a piece opened with: the open answer's "edits", else (opened strictly) every correction laid, as many as
    /// its project says it has.
    static func opened(_ answer: [String: Any]) -> EditsLayerState {
        if let edits = answer["edits"] as? [String: Any], let state = EditsLayerState(fields: edits) { return state }
        return EditsLayerState(count: ((answer["project"] as? [String: Any])?["corrections"] as? NSNumber)?.intValue ?? 0)
    }

    /// `corrections.layer`: on lays the corrections one by one, off leaves them out; with the bar Check lists, if any.
    static func layerRequest(on: Bool, bar: Int?) -> [String: Any] {
        var fields: [String: Any] = ["on": on]
        if let bar { fields["bar"] = bar }
        return fields
    }

    /// `corrections.drop`: the notes whose corrections no longer fit, taken out of the edit log.
    static func dropRequest(_ noteIds: [String], bar: Int?) -> [String: Any] {
        var fields: [String: Any] = ["noteIds": noteIds]
        if let bar { fields["bar"] = bar }
        return fields
    }

    /// Shown in More once the piece has corrections, or while they are off, so they can be turned on again.
    var shows: Bool { count > 0 || !on }

    /// The corrections that no longer fit, one row per note, in music-core's order.
    var unfitNotes: [UnfitNote] {
        var rows: [UnfitNote] = []
        for entry in unfit {
            if let index = rows.firstIndex(where: { $0.id == entry.noteId }) {
                if !rows[index].reasons.contains(entry.text) { rows[index].reasons.append(entry.text) }
            } else {
                rows.append(UnfitNote(id: entry.noteId, bar: entry.bar, reasons: [entry.text]))
            }
        }
        return rows
    }

    /// The one line under the switch: off, the notes as recognised; on, how many corrections, how many no longer fit and
    /// how many were laid on a near note.
    func stateLine(_ text: UIText) -> String {
        guard on else { return text["edits.off"] }
        var parts = [text.count("edits.count", count)]
        if !unfit.isEmpty { parts.append(text.count("edits.unfit", unfit.count)) }
        if !moved.isEmpty { parts.append(text.format("edits.moved", String(moved.count))) }
        return parts.joined(separator: " · ")
    }
}

/// «Мои правки» in More: the switch, its one line, and while on, the notes whose corrections no longer fit, each with
/// «Убрать». The switch waits while the roll's note editor holds a draft (music-core refuses it then).
struct EditsLayerSection: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    /// The notes listed at most; the others come up as these are taken out.
    static let listed = 4

    var body: some View {
        if let edits = score.editsLayer, edits.shows {
            let editing = score.noteEditor != nil
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Toggle(text["edits.title"], isOn: Binding(get: { edits.on }, set: { score.switchEdits(on: $0) }))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink"))
                    .tint(Color.ds("accent"))
                    .disabled(editing)
                    .accessibilityHint(text["settings.editsLayerHint"])
                Text(editing ? text["padEdits.saveEditFirst"] : edits.stateLine(text))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
                let rows = edits.on ? edits.unfitNotes : []
                if !rows.isEmpty {
                    Text(text["edits.unfitHeading"]).overline().padding(.top, CGFloat.ds("s-1"))
                    ForEach(Array(rows.prefix(Self.listed))) { note in row(note, editing: editing) }
                    if rows.count > Self.listed {
                        Text(text.format("padEdits.more", String(rows.count - Self.listed)))
                            .dsType(.caption)
                            .foregroundStyle(Color.ds("ink-2"))
                    }
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["edits.title"])
        }
    }

    /// A note's corrections that no longer fit: amber, as the player's own marks, its bar and why, and «Убрать».
    private func row(_ note: EditsLayerState.UnfitNote, editing: Bool) -> some View {
        HStack(alignment: .center, spacing: CGFloat.ds("s-2")) {
            Circle()
                .fill(Color.ds("amber"))
                .frame(width: CGFloat.ds("s-2"), height: CGFloat.ds("s-2"))
                .accessibilityHidden(true)
            Text(text.format("padEdits.unfitRow", String(note.bar), note.reasons.joined(separator: ", ")))
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: CGFloat.ds("s-2"))
            Button(text["edits.drop"]) { score.dropUnfitEdits([note.id]) }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                .disabled(editing)
                .accessibilityHint(text["edits.dropHint"])
        }
        .accessibilityElement(children: .contain)
    }
}
