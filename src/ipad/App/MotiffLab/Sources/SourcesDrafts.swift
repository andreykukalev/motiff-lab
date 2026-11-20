import Foundation

/// The piece's drafts in its Sources (v1.1, ADR 0034 section 9 step 5; spec «Экран (iPad)»): a fragment selected in any
/// view of notes added to a draft from the «В черновик» sheet, a draft renamed or deleted from the group «Черновики», and
/// a draft opened to be shown, its tab in the switch. Each change is music-core's (`drafts.*`), answered as the sources
/// and saved to the piece's project file at once; the notes it came from never change.
extension SourcesController {
    /// «Добавить» on the sheet: the fragment `span` of the notes shown on `piece` (this piece, or notes opened only to be
    /// shown over it, whose drafts are this piece's) goes where `choice` says. False, with why in `status`, when music-core
    /// refused it.
    func addToDraft(_ choice: DraftChoice, span: Range<Double>, from piece: Int32) async -> Bool {
        await change("drafts.add", choice.fields(span: span, now: Self.now()), on: piece) != nil
    }

    /// «Переименовать…» on a draft's row.
    func renameDraft(_ draft: SourceLayers.Draft, to name: String) async {
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name != draft.name else { return }
        await change("drafts.rename", ["draft": draft.id, "name": name])
    }

    /// «Удалить…» on a draft's row: only the draft goes (0034, section 1); the toast undoes it by writing the file back as
    /// it was, as for the other things the sheet deletes.
    func removeDraft(_ draft: SourceLayers.Draft) async {
        guard let before = await snapshot() else { return }
        guard await change("drafts.remove", ["draft": draft.id]) != nil else { return }
        toast = SourcesToast(text: text.format("drafts.toastRemoved", draft.name), undo: .restore(before))
    }

    /// A draft's tab picked in the switch: the draft opened by music-core under a number of its own only to be shown
    /// (`drafts.open`, never `sources.notes.open`), its places with it; nil, with why in `status`, for one that cannot be
    /// shown (a draft with no places yet is unavailable).
    func openDraft(_ draft: SourceLayers.Draft, at tick: Double? = nil) async -> OpenPiece? {
        let answer = await PieceCore(piece: piece).openDraft(draft.id)
        return await engraveShown(answer.text, title: draft.name, at: tick, draft: answer.value?.draft)
    }

    // MARK: - A place's приёмы (v1.1, ADR 0034 section 4, step 6)

    /// «Взять везде · N», or with `here` «Только здесь» (the occurrence's bar): the приём taken into its place. False, with
    /// why in `status`, when music-core refused it; else the toast undoes it.
    func take(_ spot: DraftTechniquesModel.Spot, here: Bool) async -> Bool {
        var fields: [String: Any] = ["place": spot.place, "technique": spot.technique.number]
        if here { fields["bar"] = spot.occurrence.bar }
        return await draftStep("drafts.take", fields, of: spot.draft, toast: text.format("drafts.toastTaken", String(spot.technique.number)))
    }

    /// «Оставить»: the приём kept as in the base, everywhere in its place.
    func leave(_ spot: DraftTechniquesModel.Spot) async -> Bool {
        let fields: [String: Any] = ["place": spot.place, "technique": spot.technique.number]
        return await draftStep("drafts.leave", fields, of: spot.draft, toast: text.format("drafts.toastLeft", String(spot.technique.number)))
    }

    /// «Правая рука из варианта…» or «Левая рука…» (`part` "right" or "left"): that hand of the occurrence's variant taken
    /// over the occurrence's bars.
    func takeHand(_ part: String, at spot: DraftTechniquesModel.Spot) async -> Bool {
        let span = ["onset": spot.occurrence.onset, "end": spot.occurrence.end]
        let fields: [String: Any] = ["place": spot.place, "variant": spot.technique.variant, "part": part, "span": span]
        return await draftStep("drafts.takeFrom", fields, of: spot.draft, toast: text[part == "left" ? "drafts.toastLeftHand" : "drafts.toastRightHand"])
    }

    /// A step of the draft `draft` (`name`, one of the calls above), saved to the piece's project file at once; its toast
    /// (`toast`) undoes it with `drafts.undo`.
    private func draftStep(_ name: String, _ fields: [String: Any], of draft: String, toast words: String) async -> Bool {
        var fields = fields
        fields["draft"] = draft
        fields["now"] = Self.now()
        guard await change(name, fields) != nil else { return false }
        toast = SourcesToast(text: words, undo: .draftStep(draft))
        return true
    }
}
