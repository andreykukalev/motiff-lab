import Foundation

/// The piece's drafts in its Sources (v1.1, ADR 0034 section 9 step 5; spec «Экран (iPad)»): a fragment selected in any
/// view of notes added to a draft from the «В черновик» sheet, a draft renamed or deleted from the group «Черновики», and
/// a draft opened to be shown, its tab in the switch. Each change is music-core's (`drafts.*`), answered as the sources
/// and saved to the piece's project file at once; the notes it came from never change.
extension SourcesController {
    /// «Добавить» on the sheet: the fragment `span` of the notes shown on `piece` (this piece, or notes opened only to be
    /// shown over it, whose drafts are this piece's) goes where `choice` says; into another piece's draft, by
    /// `addElsewhere`. False, with why in `status`, when music-core refused it.
    func addToDraft(_ choice: DraftChoice, span: Range<Double>, from piece: Int32) async -> Bool {
        if let other = choice.into.elsewhere { return await addElsewhere(choice, into: other, span: span, from: piece) }
        return await change(.draftsAdd, choice.fields(span: span, now: Self.now()), on: piece) != nil
    }

    /// The fragment into the draft of another piece (v1.2, collage, the plan's answer 5): music-core takes it into that
    /// piece's project file, sent as "file", and answers the file changed, which is written in place of the old one exactly
    /// as it is, atomically, so a failed write leaves the old one as it was. Nothing changes here: not this piece, its file,
    /// its sources nor its undo. Done, `addedElsewhere` tells it; else false, with why in `status`.
    private func addElsewhere(_ choice: DraftChoice, into other: DraftChoice.Elsewhere, span: Range<Double>, from piece: Int32) async -> Bool {
        guard let data = try? Data(contentsOf: other.file), let file = String(data: data, encoding: .utf8) else {
            status = text.format("drafts.elsewhereUnread", other.piece)
            return false
        }
        var fields = choice.fields(span: span, now: Self.now())
        fields["file"] = file
        let request = facadeRequest(fields)
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.draftsAdd, request) }
        guard let added = FacadeAnswer.decode(DraftAddedElsewhereAnswer.self, answer, of: "drafts.add") else {
            status = FacadeAnswer.message(answer) ?? text["sources.sheet.failed"]
            return false
        }
        do {
            try Data(added.file.utf8).write(to: other.file, options: .atomic)
        } catch {
            status = text.format("drafts.elsewhereUnwritten", other.name, other.piece)
            return false
        }
        status = nil
        addedElsewhere = DraftAddedElsewhere(draft: other)
        return true
    }

    /// «Переименовать…» on a draft's row.
    func renameDraft(_ draft: SourceLayers.Draft, to name: String) async {
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name != draft.name else { return }
        await change(.draftsRename, ["draft": draft.id, "name": name])
    }

    /// «Удалить…» on a draft's row: only the draft goes (0034, section 1); the toast undoes it by writing the file back as
    /// it was, as for the other things the sheet deletes.
    func removeDraft(_ draft: SourceLayers.Draft) async {
        guard let before = await snapshot() else { return }
        guard await change(.draftsRemove, ["draft": draft.id]) != nil else { return }
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
        return await draftStep(.draftsTake, fields, of: spot.draft, toast: text.format("drafts.toastTaken", String(spot.technique.number)))
    }

    /// «Оставить»: the приём kept as in the base, everywhere in its place.
    func leave(_ spot: DraftTechniquesModel.Spot) async -> Bool {
        let fields: [String: Any] = ["place": spot.place, "technique": spot.technique.number]
        return await draftStep(.draftsLeave, fields, of: spot.draft, toast: text.format("drafts.toastLeft", String(spot.technique.number)))
    }

    /// «Правая рука из варианта…» or «Левая рука…» (`part` "right" or "left"): that hand of the occurrence's variant taken
    /// over the occurrence's bars.
    func takeHand(_ part: String, at spot: DraftTechniquesModel.Spot) async -> Bool {
        let span = ["onset": spot.occurrence.onset, "end": spot.occurrence.end]
        let fields: [String: Any] = ["place": spot.place, "variant": spot.technique.variant, "part": part, "span": span]
        return await draftStep(.draftsTakeFrom, fields, of: spot.draft, toast: text[part == "left" ? "drafts.toastLeftHand" : "drafts.toastRightHand"])
    }

    // MARK: - Seams and the draft's map (v1.1, ADR 0034 sections 2 and 5, step 7)

    /// «② в соль мажор» or «Темп как в ①» on a seam's card: the step music-core offers for the place after the seam.
    func take(_ offer: DraftSeamsAnswer.Offer, of draft: String) async -> Bool {
        if offer.kind == "transpose", let semitones = offer.semitones {
            return await draftStep(.draftsTranspose, ["place": offer.place, "semitones": semitones], of: draft, toast: offer.label)
        }
        guard offer.kind == "tempo", let tempo = offer.quarterNotesPerMinute else { return false }
        return await draftStep(.draftsTempo, ["place": offer.place, "quarterNotesPerMinute": tempo], of: draft, toast: offer.label)
    }

    /// «Оставить» on a seam's card: the seam kept as it is, remembered.
    func leave(_ seam: DraftSeamsAnswer.Seam, of draft: String) async -> Bool {
        await draftStep(.draftsLeaveSeam, ["place": seam.after], of: draft, toast: text["drafts.toastSeamLeft"])
    }

    /// «Связка · 3 кандидата» on a seam's card (v1.2, the plan's answer 2): music-core's candidates put as the variants of a
    /// new place just before the place after the seam, the first its base; listened to, chosen (`makeBase`) and removed
    /// («Оставить», `leaveBridge`) on the bridge's card.
    func putBridge(_ bridge: DraftSeamsAnswer.Bridge, of draft: String) async -> Bool {
        await draftStep(.draftsBridge, ["place": bridge.place], of: draft, toast: text["drafts.toastBridge"])
    }

    /// «Оставить» on a bridge's card: the bridge place goes with its candidates, the seam as it was.
    func leaveBridge(_ place: DraftViewAnswer.Place, of draft: String) async -> Bool {
        await draftStep(.draftsLeaveBridge, ["place": place.id], of: draft, toast: text["drafts.toastBridgeLeft"])
    }

    /// «Не тот такт? Сопоставить…» on a приём's card (v1.2, the plan's answer 4): the occurrence's start in the place's base
    /// sounds with the variant's tick `variantTick` (the start of the variant's bar the player tapped); music-core aligns
    /// the variant again around it, and the приёмы follow.
    func anchor(_ spot: DraftTechniquesModel.Spot, variantTick: Int) async -> Bool {
        let anchor = ["base": spot.occurrence.inBase.onset, "variant": variantTick]
        let fields: [String: Any] = ["place": spot.place, "variant": spot.technique.variant, "anchor": anchor]
        return await draftStep(.draftsAnchor, fields, of: spot.draft, toast: text.format("drafts.toastAnchored", String(spot.occurrence.firstBar)))
    }

    /// «Открепить» in an anchor's menu: the anchor goes, and music-core aligns the variant without it.
    func unanchor(_ mark: DraftAnchorsModel.Mark, of draft: String) async -> Bool {
        let fields: [String: Any] = ["place": mark.place, "variant": mark.variant.id, "base": mark.anchor.base]
        return await draftStep(.draftsUnanchor, fields, of: draft, toast: text.format("drafts.toastUnanchored", String(mark.anchor.bar)))
    }

    /// «Взять всё уверенное» on a place's card of the map: every sure open приём of the place taken, as one group.
    func takeSure(_ place: DraftViewAnswer.Place, of draft: String) async -> Bool {
        await draftStep(.draftsTakeSure, ["place": place.id], of: draft, toast: text["drafts.toastTakenSure"])
    }

    /// «Сделать основой» on the map: `variant` the base of `place`; the place's steps go (the map warns first).
    func makeBase(_ variant: DraftViewAnswer.Variant, of place: DraftViewAnswer.Place, in draft: String) async -> Bool {
        await draftStep(.draftsBase, ["place": place.id, "variant": variant.id], of: draft, toast: text.format("drafts.toastBase", variant.label))
    }

    /// «Поставить…» on the shelf, or a variant moved on the map: `variant` as a variant of `place`, or as a new place at the
    /// end with `place` nil, or onto the shelf with `shelf`; out of a place it takes the steps that name it (the map warns).
    func put(_ variant: String, label: String, into place: String?, shelf: Bool = false, of draft: String) async -> Bool {
        var fields: [String: Any] = ["variant": variant, "drop": shelf ? "shelf" : place == nil ? "place" : "variant"]
        if let place, !shelf { fields["place"] = place }
        return await draftStep(.draftsPlace, fields, of: draft, toast: text.format(shelf ? "drafts.toastShelved" : "drafts.toastPut", label))
    }

    /// «Принять» on the assistant's proposal in a draft (v1.2, ADR 0034 section 6, the plan's answer 7): music-core makes its
    /// call (`fields`: the place, the tool, its parameters and the role) again on the draft as it is now and keeps what it
    /// proposes, its steps marked as the assistant's (`drafts.accept`), saved at once and undone from the toast as any step
    /// (`words` say what was taken). Kept; else why not, in music-core's words (the draft changed under it), or in none when
    /// `still`, asked once no other step is on its way, says it is no longer what its card says (v1.4); held, not sent,
    /// while another step is on its way (`stepping`).
    func acceptProposal(_ fields: [String: Any], of draft: String, words: String, still: () async -> Bool = { true }) async -> DraftAcceptance {
        guard !stepping else { return .held }
        // Still what its card says (v1.4), asked once no other step is on its way, and none starts while it is asked; else
        // refused in no words of music-core's.
        stepping = true
        let fits = await still()
        stepping = false
        guard fits else { return .refused("") }
        return await draftStep(.draftsAccept, fields, of: draft, toast: text.format("drafts.toastProposal", words)) ? .kept : .refused(status ?? "")
    }

    // MARK: - The draft's structure (v1.4: F1, F2, a place's length, a приём carried)

    /// «Раньше» or «Позже» on a place's card of the map (F1): `place` moved to `index` among the draft's places. A bridge
    /// place is not moved, music-core's words in `status`; a bridge whose neighbours change goes, and so does each seam kept
    /// as it was («Оставить») whose place before it is another now: the toast says what went (`telling`).
    func movePlace(_ place: DraftViewAnswer.Place, to index: Int, of draft: String) async -> Bool {
        await draftStep(.draftsMovePlace, ["place": place.id, "index": index], of: draft, toast: text["drafts.toastMoved"], telling: true)
    }

    /// «Найти на записи» in a place's card (F2): the stretch music-core finds most put in as a variant of `place`, `found`
    /// (`findOnRecording`'s) naming it in the toast. No "notes" is sent: music-core takes the stretch it finds most, never
    /// another recording's in its stead, and refuses one the place has already, in its words.
    func addFromRecording(_ found: DraftRecordingAnswer.Found, to place: String, of draft: String) async -> Bool {
        await draftStep(.draftsAddFromRecording, ["place": place], of: draft, toast: text.format("drafts.toastPut", found.label))
    }

    /// A place's length in its card (v1.4): its base cut again from its Notes over `bars` of its source. The steps outside
    /// them go, a seam kept as it was whose edge moved and a bridge at an edge that moved too: the toast says what went
    /// (`telling`). Why it cannot be done is music-core's, in `status`.
    func resize(_ place: String, to bars: ClosedRange<Int>, of draft: String) async -> Bool {
        let fields: [String: Any] = ["place": place, "bars": ["first": bars.lowerBound, "last": bars.upperBound]]
        let words = text.format("drafts.toastResized", String(bars.lowerBound), String(bars.upperBound))
        return await draftStep(.draftsResize, fields, of: draft, toast: words, telling: true)
    }

    /// «Перенести приём» on a приём's card (v1.4): music-core builds a variant of the place `target` with the приём of
    /// `spot` (read at its bar) done where its shape fits, and puts it in; one carried there already is refused, in its words.
    func carry(_ spot: DraftTechniquesModel.Spot, to target: DraftCarryPlacesAnswer.Place) async -> Bool {
        let fields: [String: Any] = ["place": spot.place, "technique": spot.technique.number, "bar": spot.occurrence.bar, "to": target.place]
        return await draftStep(.draftsCarry, fields, of: spot.draft, toast: text.format("drafts.toastCarried", String(spot.technique.number), target.label))
    }

    /// Whether the piece has a recording heard as notes, the only notes «Найти на записи» searches (F2).
    var hasHeardNotes: Bool {
        layers.notes.contains(where: \.isHeard)
    }

    /// «Найти на записи» tapped (F2; spec «Ноты + запись»: the engine finds the stretch): what music-core finds for `place`
    /// of `draft` (`drafts.findOnRecording`, read only), searched only then; nothing on a piece with no recording heard.
    func findOnRecording(_ place: String, of draft: String) async -> DraftRecordingOffer {
        guard hasHeardNotes else { return .noRecording }
        let found = await PieceCore(piece: piece).draftRecording(draft: draft, place: place)
        return DraftRecordingOffer(found.value, refused: found.message)
    }

    /// How long `place` of `draft` is in the bars of its source and whether that can change (`drafts.length`, read only).
    func length(of place: String, in draft: String) async -> DraftLengthAnswer? {
        await PieceCore(piece: piece).draftLength(draft: draft, place: place).value
    }

    /// The places the приём of `spot` can be carried to (`drafts.carryPlaces`, read only), or why not in music-core's
    /// words (bars put in are not carried).
    func carryPlaces(_ spot: DraftTechniquesModel.Spot) async -> PieceCore.Answer<DraftCarryPlacesAnswer> {
        await PieceCore(piece: piece).draftCarryPlaces(draft: spot.draft, place: spot.place, technique: spot.technique.number, bar: spot.occurrence.bar)
    }

    /// Whether a step that answered `taken` was refused by music-core: not when it was taken, nor when it was not sent as
    /// another was on its way (`stepping`), which says nothing.
    func refused(_ taken: Bool) -> Bool {
        !taken && !stepping
    }

    /// What a card says of a step that answered `taken`: nothing when it was taken or not sent (`refused`); else, when
    /// `told`, music-core's own words where it gave them to the player ("unavailable", `refusedInWords`: the steps of the
    /// draft's structure, v1.4: a bridge moved, a place whose length cannot change, a приём carried there already), and
    /// the card's own `words` otherwise, never a bad request's developer text.
    func refusal(_ taken: Bool, told: Bool, otherwise words: String) -> String? {
        guard refused(taken) else { return nil }
        return told && refusedInWords ? status ?? words : words
    }

    /// The toast's words of a step of the draft's structure (`words`) with what went with it: `steps` of the journal
    /// (music-core's "dropped") and `bridges` (the draft has that many places fewer: only a bridge goes with a move or a
    /// resize).
    nonisolated static func told(_ words: String, steps: Int, bridges: Int, _ text: UIText) -> String {
        var parts = [words]
        if bridges > 0 { parts.append(text.count("drafts.goneBridges", bridges)) }
        if steps > 0 { parts.append(text.count("drafts.goneSteps", steps)) }
        return parts.joined(separator: " · ")
    }

    /// The draft's view opening again after a step taken (`work`, a card's `reopen`), every other step held meanwhile
    /// (`stepping`, as while a step is on its way): the cards still show the draft as it was, so a step sent from them
    /// would act on the draft as it no longer is.
    func reopening(_ work: @MainActor () async -> Void) async {
        stepping = true
        defer { stepping = false }
        await work()
    }

    /// A step of the draft `draft` (`name`, one of the calls above), saved to the piece's project file at once; its toast
    /// (`toast`, with what went with it when `telling`: `told`) undoes it with `drafts.undo` (a change of the draft's
    /// structure too, while the piece is open). While one is on its way (`stepping`) another is not sent (a second tap):
    /// false, `status` untouched (`refused`).
    private func draftStep(_ name: PieceCore.Call, _ fields: [String: Any], of draft: String, toast words: String, telling: Bool = false) async -> Bool {
        guard !stepping else { return false }
        stepping = true
        defer { stepping = false }
        var fields = fields
        fields["draft"] = draft
        fields["now"] = Self.now()
        let places = layers.draft(draft)?.places ?? 0
        guard let answer = await change(name, fields) else { return false }
        let dropped = (answer["dropped"] as? [Any])?.count ?? 0
        let bridges = max(0, places - (layers.draft(draft)?.places ?? places))
        toast = SourcesToast(text: telling ? Self.told(words, steps: dropped, bridges: bridges, text) : words, undo: .draftStep(draft))
        return true
    }
}
