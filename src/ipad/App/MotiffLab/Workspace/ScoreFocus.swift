import UIKit
import WebKit

/// ScoreController's part of Expand (#521 17, 19; FocusModel owns its state): the selection lifted into focus from the
/// notes or the roll and back, the previous or the next phrase, the card's bars engraved with the variation tried or the
/// version shown above them, the version for the phrase in focus, and the page held still under the veil.
extension ScoreController {
    /// Lifts the selection into focus (Expand, #521 19): the page dims, the card shows the selection's bars on their own, and
    /// the page neither scrolls nor takes the Pencil until the way back, which `entry` names (#521 17).
    func enterFocus(from entry: ExpandEntry = .notes) {
        guard shown, ExpandRules.opens(selected: selectionModel.selection != nil, penOn: penModel.penOn), !focusModel.focused else { return }
        focusModel.open(from: entry)
        setPageStill(true)
        let label = text.format("focus.cardLabel", focusModel.position(in: text))
        let request: [String: Any] = ["open": true, "label": label, "back": text[entry.backKey]]
        Task {
            let answer = try? await scorePage.call(.focus, request) as? [String: Any]
            if let options = answer?["options"] as? [String: Any] { focusModel.takeCardOptions(facadeRequest(options)) }
            focusModel.updatePlayRange()
            await engraveCard()
            await updateFocusVersion()
        }
    }

    /// Expand on the roll drawer: the selection in focus as a roll, its way back Collapse (#521 17). Only with a selection
    /// (#521 19.1): without one the button says so instead (showNotice).
    func expandRoll() {
        guard selectionModel.selection != nil else { return }
        focusModel.showAsRoll(true)
        enterFocus(from: .roll)
    }

    /// Back to the page, with the selection as it is: Back, a tap on the dimmed page, or Escape.
    func leaveFocus() {
        guard focusModel.focused else { return }
        focusModel.close()
        setPageStill(false)
        Task {
            await scorePage.send(.focus, ["open": false])
            focusModel.updatePlayRange()
        }
    }

    /// The previous or the next phrase: the card moves on to it. Outside focus (the pill's arrows, #328) the
    /// page brings it into view.
    func step(_ direction: Int) {
        guard let unit = focusModel.unit, let next = selectionModel.phrases?.unit(unit.number + direction.signum()) else { return }
        Task {
            await select(next.selectableOnset, next.selectableEnd)
            if !focusModel.focused { await reveal(next.selectableOnset) }
        }
    }

    /// Engraves the selection's bars at the card's width and shows them on the card.
    func engraveCard() async {
        guard focusModel.focused, let piece, let selection = selectionModel.selection, let options = focusModel.cardOptions else { return }
        let first = selection.firstBar
        let last = selection.lastBar
        let id = piece.id
        let name = ossia
        let withOssia = name != nil
        // Without a variation, another version of the piece stands above as the performer has it (0022, section 5).
        let version = withOssia ? nil : sourcesModel.versions.shown
        let engraved = await MusicCore.run {
            Engraver.engraveCard(piece: id, firstBar: first, lastBar: last, options: options, ossia: withOssia, version: version?.id)
        }
        // Stepped on, left, or the variation or the version changed while it engraved: the newer call shows its own.
        guard focusModel.focused, selectionModel.selection == selection, ossia == name, withOssia || sourcesModel.versions.shown == version else { return }
        switch engraved {
        case .success(let card):
            let timemap = (try? JSONSerialization.jsonObject(with: Data(card.timemap.utf8))) ?? []
            let label = name.map { text.format("focus.ossia", $0) } ?? version.map { text.format("focus.version", $0.name) } ?? ""
            let ossias = card.ossias.map { ["staves": $0.staves, "prefix": $0.prefix, "label": label, "added": $0.added] as [String: Any] }
            await scorePage.send(.card, ["pages": card.pages, "timemap": timemap, "start": selection.onset, "ossias": ossias])
        case .failure(let error):
            showProblem(error.message)
        }
    }

    /// In focus the page under the veil stays where it is: it does not scroll and the Pencil does not write on it.
    private func setPageStill(_ still: Bool) {
        webView.scrollView.isScrollEnabled = !still
        ink.still = still
    }

    /// The version for the phrase in focus: its notes to hear, and the bars of the phrase it has otherwise.
    func updateFocusVersion() async {
        guard focusModel.focused, let piece, let selection = selectionModel.selection, let shown = sourcesModel.versions.shown else {
            focusModel.showVersion(nil)
            return
        }
        guard let notes = await focusModel.versionNotes(of: shown.id, in: piece.id), focusModel.focused, selectionModel.selection == selection, sourcesModel.versions.shownId == shown.id else { return }
        let bars = selection.firstBar...selection.lastBar
        focusModel.showVersion(FocusModel.Version(
            id: shown.id, name: shown.name, notes: notes,
            sounds: notes.contains { $0.onset >= selection.onset && $0.onset < selection.end },
            differing: sourcesModel.versions.differing.map(\.bar).filter { bars.contains($0) }
        ))
    }
}
