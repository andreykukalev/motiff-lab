import SwiftUI

/// A phrase's mark held on the score, whose menu is open: its phrase's number, and its number's bottom left on the page.
struct PhraseMarkPick: Equatable {
    var number: Int
    var at: CGPoint
}

/// The phrases' boundaries edited on the score (the phrases spec, decision 5, «Polish of option A» 2 and 3, «Player's
/// boundaries» 1, 2 and 4; v1.2 i-3a, i-3b): a mark dragged and let go on a note moves its boundary there (music-core snaps
/// it); held still, its menu opens under its number with what acts on that one boundary: «Почему здесь», «Выделить фразу»,
/// «Убрать границу», and on a found start the player moved «Вернуть найденную · 4.3». «Начать фразу здесь» (the selection
/// menu's More) starts one at the selection's first note, «Вернуть найденные границы» (the ⓘ list) puts back all the found
/// ones. Each edit is one step of the phrases' own Undo and Redo (SelectionModel's), offered by its toast, never ⌘Z's
/// (Andrei, 2026-11-20). Nothing here changes the selection by itself: when it was a phrase exactly and the edit changed
/// that phrase, the card «Выделить её по новым границам?» offers it, and only its «Выделить» selects, a step of the
/// selection's own history; «Вся фраза» takes a selection inside a phrase out to it.
extension ScoreController {
    /// The player's beats with their bar and beat, handed in with the beat lines once the sound has loaded: a dragged mark's
    /// caption writes them, so the marks are drawn again with them when they change.
    func setBarBeats(_ places: BarBeats) {
        guard places != barBeats else { return }
        barBeats = places
        Task { await drawPhraseMarks() }
    }

    /// A phrase's number held still and let go: its menu opens under it.
    func holdPhrase(_ number: Int, at point: CGPoint) {
        guard selectionModel.phrases?.unit(number) != nil else { return }
        selectionModel.closePhraseOffer()
        phraseMenu = PhraseMarkPick(number: number, at: point)
    }

    /// «Выделить фразу» in a mark's menu: the menu put away, its whole phrase selected as a tap on its number selects it.
    func selectPhrase(_ unit: PhraseUnit) {
        phraseMenu = nil
        pickPhrase(unit.number)
    }

    /// «Начать фразу здесь» (the selection menu's More): a phrase starts at the selection's first note.
    func startPhrase() {
        guard let selection = selectionModel.selection else { return }
        editPhrases("{\"start\": \(Int(selection.onset))}", done: text["phrases.started"])
    }

    /// «Вернуть найденные границы» (the ⓘ list, after its warning when the player has boundaries of their own): every
    /// boundary as the app found it, one step of the phrases' Undo.
    func resetPhrases() {
        editPhrases("{\"resetAll\": true}", done: text["phrases.resetDone"])
    }

    /// «Выделить» on the card after an edit: the phrase the selection was, selected by its new boundaries, one step of the
    /// selection's history.
    func takePhraseOffer() {
        guard let offer = selectionModel.phraseOffer else { return }
        selectionModel.closePhraseOffer()
        Task { await select(offer.onset, offer.end) }
    }

    /// «Вся фраза» in the selection menu: a selection inside a phrase taken out to the whole phrase, one step of its history.
    func selectWholePhrase() {
        guard let unit = selectionModel.phrases?.unit(enclosing: selectionModel.selection) else { return }
        Task { await select(unit.selectableOnset, unit.selectableEnd) }
    }

    /// The mark's menu put away.
    func closePhraseMenu() {
        phraseMenu = nil
    }

    /// A phrase's mark dragged from its start (`from`) and let go on the note at `to`: the boundary moves there.
    func movePhrase(from: Double, to: Double) {
        editPhrases("{\"move\": {\"from\": \(Int(from)), \"to\": \(Int(to))}}", done: text["phrases.moved"])
    }

    /// «Убрать границу»: the phrase joins the one before it.
    func removeBoundary(_ unit: PhraseUnit) {
        editPhrases("{\"remove\": \(Int(unit.onset))}", done: text["phrases.removed"])
    }

    /// «Вернуть найденную»: a found start the player moved goes back where it was found.
    func putBackFound(_ unit: PhraseUnit) {
        editPhrases("{\"putBack\": \(Int(unit.onset))}", done: text["phrases.putBackDone"])
    }

    /// The toast's Undo (`redo` false) or Redo of the last edit of the phrases; the marks drawn again as they are then.
    func stepPhraseEdit(redo: Bool) {
        guard let piece else { return }
        Task {
            _ = await selectionModel.stepPhrases(in: piece.id, redo: redo, done: text[redo ? "phrases.redone" : "phrases.undone"])
            await drawPhraseMarks()
        }
    }

    /// The marks drawn as the phrases are now, and the selection's band with the phrase it is (whose number is cobalt); the
    /// selection itself stays as it is.
    func drawPhraseMarks() async {
        await scorePage.send(.phraseMarks, phraseMarks)
        await showSelection()
    }

    /// An edit of the boundaries through music-core, the menu put away first and the marks drawn again after it, also
    /// when it was refused (the mark the page lifted stands where it is).
    private func editPhrases(_ request: String, done words: String) {
        phraseMenu = nil
        guard let piece else { return }
        Task {
            _ = await selectionModel.editPhrases(in: piece.id, request, done: words)
            await drawPhraseMarks()
        }
    }
}

/// The menu of a phrase's mark held (the phrases spec, «Polish of option A» 3), under its number: whether the app found the
/// start or the player set it, «Почему здесь» for a found one, «Выделить фразу», «Убрать границу» (not on the piece's first
/// phrase, where the piece starts), and «Вернуть найденную · 4.3» on a found start the player moved.
struct PhraseMarkMenu: View {
    let score: ScoreController
    let unit: PhraseUnit
    let first: Bool
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text[unit.own ? "phrases.menuOwn" : "phrases.menuFound"]).overline()
            if !unit.own, !unit.why.isEmpty {
                Text(text.format("phrases.whyHere", unit.why)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            Button(text["phrases.select"]) { score.selectPhrase(unit) }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            if let found = unit.found {
                Button(text.format("phrases.putBack", BarBeat(bar: found.bar, beat: found.beat).text)) { score.putBackFound(unit) }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
            if !first {
                Button(text["phrases.remove"]) { score.removeBoundary(unit) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("phrases.menu", String(unit.number)))
    }
}

/// The toast after an edit of the phrases (SelectionModel.phraseToast), the score's UndoToast: what was done with
/// «Отменить», after an Undo with «Повторить», or why it could not be; ✕ puts it away.
struct PhraseEditToast: View {
    @ObservedObject var selectionModel: SelectionModel
    let score: ScoreController
    let text: UIText

    var body: some View {
        if let toast = selectionModel.phraseToast {
            let redo = toast.offer == .redo
            UndoToast(words: toast.words, text: text, undo: toast.offer.map { _ in { score.stepPhraseEdit(redo: redo) } }, close: { selectionModel.closePhraseToast() }, actionKey: redo ? "phrases.redo" : UndoToast.undoKey)
                .transition(.opacity)
        }
    }
}

/// The card at the selection menu after an edit changed the phrase the selection was exactly (the phrases spec, «Player's
/// boundaries» 4): «Выделение было фразой 2. Выделить её по новым границам?» with «Выделить» (a step of the selection's
/// history) and «Оставить»; unanswered, it goes after a while (SelectionModel.phraseOffer), the selection as it is.
struct PhraseOfferCard: View {
    let score: ScoreController
    let offer: SelectionModel.PhraseOffer
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("phrases.offer", String(offer.number))).dsType(.caption).foregroundStyle(Color.ds("ink"))
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["phrases.offerSelect"]) { score.takePhraseOffer() }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                Button(text["phrases.offerKeep"]) { score.selectionModel.closePhraseOffer() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        .padding(.vertical, CGFloat.ds("s-1"))
        .accessibilityElement(children: .contain)
    }
}
