import SwiftUI

/// A phrase's mark held on the score, whose menu is open: its phrase's number, and its number's bottom left on the page.
struct PhraseMarkPick: Equatable {
    var number: Int
    var at: CGPoint
}

/// The phrases' boundaries edited on the score (the phrases spec, decision 5, «Polish of option A» 2 and 3, «Player's
/// boundaries» 1 and 4; v1.2 i-3a): a mark dragged and let go on a note moves its boundary there (music-core snaps it); held
/// still, its menu opens under its number with what acts on that one boundary: «Почему здесь», «Убрать границу», and on a
/// found start the player moved «Вернуть найденную · 4.3». Each edit is one step of the phrases' own Undo and Redo
/// (SelectionModel's), offered by its toast. Nothing here changes the selection: only the phrase it is named by follows.
/// Not here (i-3b): «Выделить фразу», «Начать фразу здесь», «Вся фраза», the reset and the card «Выделить её по новым
/// границам?».
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
        phraseMenu = PhraseMarkPick(number: number, at: point)
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
/// start or the player set it, «Почему здесь» for a found one, «Убрать границу» (not on the piece's first phrase, where the
/// piece starts), and «Вернуть найденную · 4.3» on a found start the player moved.
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
