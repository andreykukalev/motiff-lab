import Foundation

/// ScoreController's part of the selection (#338, #349, #511 section 11; SelectionModel owns it): selecting through
/// music-core by a span, by beats or by bars, clearing, the band drawn over it with the phrase it is, the beat lines it
/// snaps to sent to the page, a passage selected and brought into view, the selection's words for the assistant and a new
/// bookmark, and its Undo and Redo, what follows them (the play range, Expand's card) included.
extension ScoreController {
    /// Selects a passage through music-core, which keeps it within what a selection may hold; with `gesture`, as part of
    /// that gesture's step of the history; `replaces` for a new selection in place of the one there was (a phrase's
    /// number or a section's letter tapped, ScorePhraseMarks.swift).
    func select(_ start: Double, _ end: Double, gesture: String? = nil, step: Bool = true, replaces: Bool = false) async {
        await select(request: "{\"span\": {\"onset\": \(Int(start)), \"end\": \(Int(end))}}", gesture: gesture, step: step, replaces: replaces)
    }

    /// Selects a passage by beats (#349): music-core takes its edges out to the beat lines. A drag on the score or on the
    /// roll's bar numbers and handles (#479 item 23), where the score selects by beats.
    func selectBeats(_ start: Double, _ end: Double, gesture: String? = nil) {
        Task { await select(request: Self.beatsRequest(start, end), gesture: gesture) }
    }

    /// A passage by beats, as music-core's `select` takes it: its edges out to the beat lines.
    static func beatsRequest(_ start: Double, _ end: Double) -> String {
        "{\"span\": {\"onset\": \(Int(start)), \"end\": \(Int(end))}, \"snap\": \"beat\"}"
    }

    /// Selects whole bars, as the piano roll picks them; a tap that extends takes the selection to the bar tapped.
    func selectBars(_ first: Int, _ last: Int, extend: Bool, gesture: String? = nil) {
        var from = first
        var to = last
        if extend, let selection = selectionModel.selection {
            if first < selection.firstBar { to = selection.lastBar } else { from = selection.firstBar }
        }
        Task { await select(request: Self.barsRequest(from, to), gesture: gesture) }
    }

    /// Whole bars, as music-core's `select` takes them.
    static func barsRequest(_ first: Int, _ last: Int) -> String {
        "{\"bars\": {\"first\": \(first), \"last\": \(last)}}"
    }

    /// Selects through music-core; `step` false for a selection the player did not make now (the resume point, Undo);
    /// `picked` for notes picked one by one (v0.6.2, PlaceSelection.swift), which the selection then names; `replaces` for
    /// a new selection in place of the one there was (a replace with «Вернуть»).
    func select(request: String, gesture: String? = nil, step: Bool = true, picked: Bool = false, replaces: Bool = false) async {
        guard let piece else { return }
        let refused = await selectionModel.select(in: piece.id, request: request, gesture: gesture, step: step, picked: picked, replaces: replaces)
        if let message = refused {
            if !softlyRefused(message) { showProblem(message) }
            return
        }
        await showSelection()
        focusModel.updatePlayRange()
        if focusModel.focused {
            await engraveCard()
            await updateFocusVersion()
        }
    }

    /// Takes the selection away: ✕, Escape, or a hold outside it (`gesture`, #511 2.1); `step` false for Undo.
    func clearSelection(gesture: String? = nil, step: Bool = true) {
        guard selectionModel.selection != nil else { return }
        if step { selectionModel.record(from: selectionModel.selection, to: nil, gesture: gesture) }
        // Nothing to lift: focus goes with the selection.
        leaveFocus()
        selectionModel.answered(nil)
        Task {
            await showSelection()
            focusModel.updatePlayRange()
        }
    }

    /// The assistant's scope chip for the selection (testing item 23): "This phrase · bars 9–16" or "This selection · bar 3".
    var askLabel: String? {
        guard let selection = selectionModel.selection else { return nil }
        let phrase = selectionModel.phrases?.unit(selected: selection) != nil
        return text.format(phrase ? "ask.thisPhrase" : "ask.thisSelection", barsText(selection.firstBar, selection.lastBar, text))
    }

    /// The title a new bookmark starts with: "Phrase 3, bars 9–12" for a phrase selected exactly, else "Bars 9–12".
    var bookmarkTitle: String {
        guard let selection = selectionModel.selection else { return "" }
        if let unit = selectionModel.phrases?.unit(selected: selection) {
            return label(of: unit)
        }
        let bars = barsText(selection.firstBar, selection.lastBar, text)
        return bars.prefix(1).uppercased() + bars.dropFirst()
    }

    /// A passage selected, and brought into view.
    func showSpan(_ span: Range<Double>) {
        Task {
            await select(span.lowerBound, span.upperBound)
            await scorePage.send(.reveal, ["start": span.lowerBound])
        }
    }

    func applyBeats() async {
        guard shown else { return }
        await scorePage.send(.beats, ["lines": beatLines])
    }

    /// Draws the band over the selection, and says which phrase it is exactly, whose number the page turns cobalt.
    func showSelection() async {
        guard shown else { return }
        var band: Any = NSNull()
        if let selection = selectionModel.selection {
            var drawn: [String: Any] = ["start": selection.onset, "end": selection.end, "startLabel": text["selection.handleStart"], "endLabel": text["selection.handleEnd"]]
            if let hand = selection.hand { drawn["hand"] = hand }
            if let picked = selection.picked { drawn["picked"] = picked }
            if let phrase = selectionModel.phrases?.unit(selected: selection) { drawn["phrase"] = phrase.number }
            band = drawn
        }
        await scorePage.send(.select, band)
    }

    /// What a unit is called, as a screen reader and the assistant say it: "Phrase 3, bars 9–12".
    private func label(of unit: PhraseUnit) -> String {
        let name = unit.name.prefix(1).uppercased() + unit.name.dropFirst()
        let bars = unit.firstBar == unit.lastBar
            ? text.format("bars.one", String(unit.firstBar))
            : text.format("bars.range", String(unit.firstBar), String(unit.lastBar))
        return text.format("phrases.bracketLabel", name, String(unit.number), bars)
    }

    // MARK: - Undo and Redo (#511 section 11): the steps and the selecting are SelectionModel's, what follows the root's

    /// Undo of the selection (the Selection row, the Pencil's ring, ⌘Z, «Вернуть»): the selection before the last step,
    /// selected again exactly; the score neither scrolls nor zooms.
    func undoSelection() {
        guard let step = selectionModel.undo() else { return }
        Task { await restore(step.before) }
    }

    /// Redo of the selection (the Selection row, ⇧⌘Z).
    func redoSelection() {
        guard let step = selectionModel.redo() else { return }
        Task { await restore(step.after) }
    }

    /// A gesture cancelled after its hold took the selection away: the selection comes back and no step stays.
    func revertGesture(_ gesture: String) {
        guard let step = selectionModel.revert(gesture) else { return }
        Task { await restore(step.before) }
    }

    private func restore(_ span: SelectionSpan?) async {
        guard let span else {
            clearSelection(step: false)
            return
        }
        await select(request: span.request, step: false, picked: span.picked != nil)
    }
}
