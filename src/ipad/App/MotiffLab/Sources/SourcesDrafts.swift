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

    // MARK: - Seams and the draft's map (v1.1, ADR 0034 sections 2 and 5, step 7)

    /// «② в соль мажор» or «Темп как в ①» on a seam's card: the step music-core offers for the place after the seam.
    func take(_ offer: DraftSeamsAnswer.Offer, of draft: String) async -> Bool {
        if offer.kind == "transpose", let semitones = offer.semitones {
            return await draftStep("drafts.transpose", ["place": offer.place, "semitones": semitones], of: draft, toast: offer.label)
        }
        guard offer.kind == "tempo", let tempo = offer.quarterNotesPerMinute else { return false }
        return await draftStep("drafts.tempo", ["place": offer.place, "quarterNotesPerMinute": tempo], of: draft, toast: offer.label)
    }

    /// «Оставить» on a seam's card: the seam kept as it is, remembered.
    func leave(_ seam: DraftSeamsAnswer.Seam, of draft: String) async -> Bool {
        await draftStep("drafts.leaveSeam", ["place": seam.after], of: draft, toast: text["drafts.toastSeamLeft"])
    }

    /// «Связка · 3 кандидата» on a seam's card (v1.2, the plan's answer 2): music-core's candidates put as the variants of a
    /// new place just before the place after the seam, the first its base; listened to, chosen (`makeBase`) and removed
    /// («Оставить», `leaveBridge`) on the bridge's card.
    func putBridge(_ bridge: DraftSeamsAnswer.Bridge, of draft: String) async -> Bool {
        await draftStep("drafts.bridge", ["place": bridge.place], of: draft, toast: text["drafts.toastBridge"])
    }

    /// «Оставить» on a bridge's card: the bridge place goes with its candidates, the seam as it was.
    func leaveBridge(_ place: DraftViewAnswer.Place, of draft: String) async -> Bool {
        await draftStep("drafts.leaveBridge", ["place": place.id], of: draft, toast: text["drafts.toastBridgeLeft"])
    }

    /// «Не тот такт? Сопоставить…» on a приём's card (v1.2, the plan's answer 4): the occurrence's start in the place's base
    /// sounds with the variant's tick `variantTick` (the start of the variant's bar the player tapped); music-core aligns
    /// the variant again around it, and the приёмы follow.
    func anchor(_ spot: DraftTechniquesModel.Spot, variantTick: Int) async -> Bool {
        let anchor = ["base": spot.occurrence.inBase.onset, "variant": variantTick]
        let fields: [String: Any] = ["place": spot.place, "variant": spot.technique.variant, "anchor": anchor]
        return await draftStep("drafts.anchor", fields, of: spot.draft, toast: text.format("drafts.toastAnchored", String(spot.occurrence.firstBar)))
    }

    /// «Открепить» in an anchor's menu: the anchor goes, and music-core aligns the variant without it.
    func unanchor(_ mark: DraftAnchorsModel.Mark, of draft: String) async -> Bool {
        let fields: [String: Any] = ["place": mark.place, "variant": mark.variant.id, "base": mark.anchor.base]
        return await draftStep("drafts.unanchor", fields, of: draft, toast: text.format("drafts.toastUnanchored", String(mark.anchor.bar)))
    }

    /// «Взять всё уверенное» on a place's card of the map: every sure open приём of the place taken, as one group.
    func takeSure(_ place: DraftViewAnswer.Place, of draft: String) async -> Bool {
        await draftStep("drafts.takeSure", ["place": place.id], of: draft, toast: text["drafts.toastTakenSure"])
    }

    /// «Сделать основой» on the map: `variant` the base of `place`; the place's steps go (the map warns first).
    func makeBase(_ variant: DraftViewAnswer.Variant, of place: DraftViewAnswer.Place, in draft: String) async -> Bool {
        await draftStep("drafts.base", ["place": place.id, "variant": variant.id], of: draft, toast: text.format("drafts.toastBase", variant.label))
    }

    /// «Поставить…» on the shelf, or a variant moved on the map: `variant` as a variant of `place`, or as a new place at the
    /// end with `place` nil, or onto the shelf with `shelf`; out of a place it takes the steps that name it (the map warns).
    func put(_ variant: String, label: String, into place: String?, shelf: Bool = false, of draft: String) async -> Bool {
        var fields: [String: Any] = ["variant": variant, "drop": shelf ? "shelf" : place == nil ? "place" : "variant"]
        if let place, !shelf { fields["place"] = place }
        return await draftStep("drafts.place", fields, of: draft, toast: text.format(shelf ? "drafts.toastShelved" : "drafts.toastPut", label))
    }

    /// A step of the draft `draft` (`name`, one of the calls above), saved to the piece's project file at once; its toast
    /// (`toast`) undoes it with `drafts.undo` (a change of the draft's structure too, while the piece is open).
    private func draftStep(_ name: String, _ fields: [String: Any], of draft: String, toast words: String) async -> Bool {
        var fields = fields
        fields["draft"] = draft
        fields["now"] = Self.now()
        guard await change(name, fields) != nil else { return false }
        toast = SourcesToast(text: words, undo: .draftStep(draft))
        return true
    }
}
