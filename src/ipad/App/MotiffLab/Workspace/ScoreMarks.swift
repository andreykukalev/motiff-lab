import Foundation

/// ScoreController's part of the marks (MarksModel owns them, ADR 0031 section 3, S6): drawing the marks music-core
/// answered and changing them, the bookmarks drawn and gone to, a margin note opened, kept or removed, the layers and the
/// phrases' marks shown or hidden and what the page hides, the device's and the piece's own settings followed.
extension ScoreController {
    /// The ⓘ button: extra information on or off, kept with the piece.
    func toggleExtra() {
        showLayers(["extra"], marksModel.hiddenLayers.contains("extra"))
    }

    /// Draws the bookmarks as brackets in their categories' inks above the top staff (0018 section 5).
    func showBookmarks(_ shelf: BookmarkShelf?) {
        marksModel.keepBookmarks(shelf)
        guard shown else { return }
        Task { await drawBookmarks() }
    }

    func drawBookmarks() async {
        await scorePage.send(.bookmarks, ["items": marksModel.bookmarkItems])
    }

    /// A bookmark gone to from its bracket or its entry: the score selects it and brings it into view; gone to again with
    /// the selection as it left it, the passage lifts into focus. True when it lifted.
    @discardableResult
    func goToBookmark(_ id: String) -> Bool {
        guard let span = marksModel.span(ofBookmark: id) else { return false }
        if let shownBookmark = marksModel.shownBookmark, shownBookmark.id == id, shownBookmark.selection == selectionModel.selection, !penModel.penOn {
            enterFocus()
            return true
        }
        Task {
            await select(span.lowerBound, span.upperBound)
            marksModel.wentTo(bookmark: id)
            await scorePage.send(.reveal, ["start": span.lowerBound])
        }
        return false
    }

    /// Opens a note to read or change: its bars are selected and the card shows its words.
    func openNote(_ id: String) {
        guard let note = marksModel.marks.notes.first(where: { $0.id == id }) else { return }
        let draft = MarksModel.NoteDraft(id: id, onset: note.onset, end: note.end, firstBar: note.firstBar, lastBar: note.lastBar, text: note.text)
        Task {
            if selectionModel.selection?.onset != note.onset || selectionModel.selection?.end != note.end {
                await select(note.onset, note.end)
                await scorePage.send(.reveal, ["start": note.onset])
            }
            marksModel.noteDraft = draft
        }
    }

    /// Done on the note card: a new note is kept when it has words; a kept one changes, or goes when emptied.
    func saveNote() {
        guard let draft = marksModel.noteDraft, let piece else { return }
        marksModel.noteDraft = nil
        let words = draft.text.trimmingCharacters(in: .whitespacesAndNewlines)
        Task {
            if let id = draft.id {
                if words.isEmpty {
                    await changeMarks(piece.id, .noteRemove, ["id": id])
                } else {
                    await changeMarks(piece.id, .noteChange, ["id": id, "text": words])
                }
            } else if !words.isEmpty {
                let span = ["onset": Int(draft.onset), "end": Int(draft.end)]
                await changeMarks(piece.id, .noteAdd, ["span": span, "text": words, "now": ISO8601DateFormatter().string(from: Date())])
            }
        }
    }

    func deleteNote(_ id: String) {
        guard let piece else { return }
        marksModel.noteDraft = nil
        Task { await changeMarks(piece.id, .noteRemove, ["id": id]) }
    }

    /// Shows or hides layers of marks, kept with the piece.
    func showLayers(_ layers: [String], _ on: Bool) {
        guard let piece else { return }
        marksModel.showLayers(layers, on)
        let id = piece.id
        Task {
            await marksModel.keepLayers(layers, on, in: id)
            await applyLayers()
            await sourcesModel.drawVersions()
        }
    }

    /// The phrases' marks shown or hidden («Фразы» in the list of extra information), kept on this device; the selection
    /// stays as it is.
    func showPhraseMarks(_ on: Bool) {
        marksModel.showPhraseMarks(on)
        Task { await applyLayers() }
    }

    /// What the page hides: the layers hidden, Check while neither extra information nor the Check drawer shows it, and
    /// the phrases' marks while the player hid them.
    func applyLayers() async {
        guard shown else { return }
        var hidden = MarksModel.layers.filter(marksModel.hiddenLayers.contains)
        if !marksModel.extraShown("check") && !checkModel.drawerOpen { hidden.append("check") }
        if marksModel.phraseMarksHidden { hidden.append("phrases") }
        let slate = DeviceSettings.shared["appearance.theme"] == "slate"
        await scorePage.send(.layers, ["hidden": hidden, "slate": slate])
    }

    /// The device's settings changed in the sheet: the page follows at once.
    func settingsChanged() {
        marksModel.deviceSettingsChanged()
        Task {
            await applyLayers()
            await sourcesModel.drawVersions()
        }
    }

    /// "Use these settings for this piece": the piece's own choices go, and the device's hold for it too.
    func clearPieceOverrides() {
        guard let piece else { return }
        let ids = marksModel.clearOverrides()
        Task {
            await marksModel.forgetOverrides(ids, in: piece.id)
            settingsChanged()
        }
    }

    /// Draws the marks music-core answered (`marks`, `ink.add`, `ink.remove`).
    func draw(_ answer: String) async {
        guard let object = facadeObject(answer) else { return }
        if object["error"] != nil {
            showProblem(FacadeAnswer.message(answer))
            return
        }
        let ink = object["ink"] as? [Any] ?? []
        let shelf = MarksShelf(object: object)
        marksModel.show(shelf, inkCount: ink.count)
        await scorePage.send(.draw, ["marks": ink])
        let notes: [[String: Any]] = shelf.notes.map { note in
            let label = text.format("margin.markerLabel", String(shelf.number(of: note)), barsText(note.firstBar, note.lastBar, text), MarksShelf.firstWords(note.text))
            return ["id": note.id, "tick": note.onset, "number": shelf.number(of: note), "label": label]
        }
        let highlights: [[String: Any]] = shelf.highlights.map { ["id": $0.id, "start": $0.onset, "end": $0.end, "ink": $0.colour] }
        await scorePage.send(.notes, ["notes": notes, "highlights": highlights])
    }

    /// A change to the marks through music-core; the answer is drawn unless `draws` is false, for the first of two calls
    /// that make one change, so the page never shows it half done.
    func changeMarks(_ piece: Int32, _ call: PieceCore.Call, _ request: [String: Any], draws: Bool = true) async {
        let text = facadeRequest(request)
        let answer = await MusicCore.run { PieceCore(piece: piece).send(call, text) }
        if draws { await draw(answer) }
    }
}
