import Foundation

/// A draft's view (v1.1, ADR 0034 section 9 steps 5 and 6; spec «Экран (iPad)»): one score, its places' results one after
/// another, as music-core opened it (`drafts.open`, `OpenPiece.draft`). Each place's label, music-core's words for where it
/// came from («① Ноты 9–16»), stands above the place's first bar, drawn with the version's labels (`performance`, with
/// `place`, in the ink); a tap on one selects the place, so it plays and loops as any passage. Above the bars of each приём
/// stands its plum number (`draftModel`); a tap on one selects that occurrence and opens its card there, and the pill's ‹ ›
/// steps from one to the next. Since v1.2 a bridge place's label opens its card too, and each anchor the player
/// confirmed stands as a small amber mark over its bar (`anchorsModel`), its menu «Открепить»; since v1.4 (F3) every
/// label opens its place's card (`DraftPlaceContent`, the ▾ menu the page draws after the label).
extension ScoreController {
    /// The labels of the draft's places for the page, in order, then the numbers of their приёмы, the diamonds of their
    /// seams and the marks of their anchors (with the numbers, hidden with them); none for any other piece.
    var placeMarkers: [[String: Any]] {
        (piece?.draft?.places ?? []).map { place -> [String: Any] in
            ["bar": place.firstBar, "tick": place.onset, "text": place.label, "label": place.label, "place": true]
        } + draftModel.markers(text) + seamsModel.markers(text) + DraftAnchorsModel.markers(piece?.draft, hidden: draftModel.numbersHidden, text)
    }

    /// A seam's diamond tapped (`key`, at its bottom left on the page, nil on the draft's map): its card opens there, and
    /// the приём card or an anchor's menu, if one is open, goes.
    func pickSeam(_ key: String, at point: CGPoint?) {
        guard seamsModel.pick(key, at: point) != nil else { return }
        draftModel.closeCard()
        anchorsModel.close()
    }

    /// An anchor's mark tapped (`key`, at its bottom left): its menu opens there, and any other card goes.
    func pickAnchor(_ key: String, at point: CGPoint) {
        guard anchorsModel.pick(key, in: piece?.draft, at: point) != nil else { return }
        draftModel.closeCard()
        seamsModel.close()
    }

    /// A variant's own music heard once over `span` (its own ticks): a bridge's candidate over the place's length, or one
    /// side of a приём's occurrence; nothing before a piece.
    func hearVariant(_ variant: String, of draft: String, over span: Range<Double>) {
        guard let piece else { return }
        Task {
            guard let notes = await DraftTechniquesModel.playback(of: variant, in: draft, on: piece.id).map(PlaybackScore.init)?.notes else { return }
            hearNotes?(notes, span)
        }
    }

    /// The bars of the occurrence's variant in its own ticks (number and onset), for «Сопоставить…»; empty when music-core
    /// does not open it.
    func variantBars(_ spot: DraftTechniquesModel.Spot) async -> [PlaybackAnswer.Bar] {
        guard let piece else { return [] }
        return await DraftTechniquesModel.playback(of: spot.technique.variant, in: spot.draft, on: piece.id)?.bars ?? []
    }

    /// «▶ Послушать стык»: the bar before the seam and the bar after it, once, as the draft view plays them (music-core's
    /// `listen`), each place in its own key and tempo.
    func hearSeam(_ seam: DraftSeamsAnswer.Seam) {
        hearDraft(over: Double(seam.listen.onset)..<Double(seam.listen.end))
    }

    /// The draft view's own notes heard once over `span` (its ticks): a seam, or a place as the draft has it now («▶ До»
    /// on the assistant's proposal, v1.2).
    func hearDraft(over span: Range<Double>) {
        Task {
            guard let notes = await draftNotes() else { return }
            hearNotes?(notes, span)
        }
    }

    /// «▶ После» on the assistant's proposal in a place (v1.2 step 8): the draft as the proposal would leave it
    /// (`drafts.proposal`, opened only to be shown, its playback read, closed again), heard once over what it changes
    /// there (`proposedSpan`); nothing is kept.
    func hearProposal(_ proposal: AssistantPlaceProposal) {
        guard let piece else { return }
        Task {
            guard let view = await PieceCore(piece: piece.id).openProposal(proposal.callFields).value else { return }
            let notes = await PieceCore(piece: view.piece).playback().value.map(PlaybackScore.init)?.notes
            _ = await MusicCore.run { MusicCore.close(piece: view.piece) }
            guard let notes, let span = Self.proposedSpan(of: proposal.place, in: view.draft, before: piece.draft) else { return }
            hearNotes?(notes, span)
        }
    }

    /// The ticks of `place` in the draft a proposal would leave (`after`), from the start of a place it puts just before it
    /// (a bridge, which `before`, the draft now, does not have); nil when the place is not there.
    static func proposedSpan(of place: String, in after: DraftViewAnswer.Draft, before: DraftViewAnswer.Draft?) -> Range<Double>? {
        guard let index = after.places.firstIndex(where: { $0.id == place }) else { return nil }
        let known = Set(before?.places.map(\.id) ?? [])
        let put = after.places[..<index].last.flatMap { known.contains($0.id) ? nil : $0 }
        return Double((put ?? after.places[index]).onset)..<Double(after.places[index].end)
    }

    /// The draft view's own notes as it plays them, in its ticks (the seam heard, the map's thumbnails); nil before a piece.
    func draftNotes() async -> [PlaybackNote]? {
        guard let piece else { return nil }
        return await PieceCore(piece: piece.id).playback().value.map(PlaybackScore.init)?.notes
    }

    /// A marker tapped over the bars (at its bottom left on the page): a label, a приём's number or a seam's diamond.
    func pickMarker(_ event: PageEvent) {
        switch event {
        case let .performanceBar(bar, left, bottom):
            pickLabel(bar, left: left, bottom: bottom)
        case let .techniqueNumber(key, left, bottom):
            pickTechnique(key, at: CGPoint(x: left, y: bottom))
        case let .seamDiamond(key, left, bottom):
            pickSeam(key, at: CGPoint(x: left, y: bottom))
        case let .anchorMark(key, left, bottom):
            pickAnchor(key, at: CGPoint(x: left, y: bottom))
        default:
            break
        }
    }

    /// The mark whose card shows moved on the page (`point`, its bottom left now: a scroll, or the page laid out again on a
    /// rotation or in Split View): the card goes with it, a version's marker's, a phrase's number's or a draft's (r-1). The
    /// page follows the mark tapped last, and one card at a mark shows at a time. False when no card shows at a mark.
    func moveCardAtMark(to point: CGPoint) -> Bool {
        var moved = false
        if let pick = sourcesModel.markerPick {
            sourcesModel.pickMarker(SourcesModel.MarkerPick(bar: pick.bar, left: point.x, bottom: point.y))
            moved = true
        }
        if phraseMenu != nil {
            phraseMenu?.at = point
            moved = true
        }
        let draftCards = [draftModel.moveCard(to: point), seamsModel.moveCard(to: point), anchorsModel.moveCard(to: point)]
        return moved || draftCards.contains(true)
    }

    /// A label tapped at `bar` (`left`, `bottom`: its bottom left on the page): in a draft's view the place that starts
    /// there, selected, with its card at its label (a bridge's candidates, v1.2; any other place's ▾ menu of its variants,
    /// v1.4, none while «Скрыть номера» leaves the draft to practise on: DRAFT-05), else a version's marker, whose card
    /// opens there and whose bar is selected.
    func pickLabel(_ bar: Int, left: Double, bottom: Double) {
        guard !pickPlace(startingAt: bar) else {
            if let place = piece?.draft?.places.first(where: { $0.firstBar == bar }), place.bridge || !draftModel.numbersHidden {
                seamsModel.pickLabel(place.id, at: CGPoint(x: left, y: bottom))
                draftModel.closeCard()
                anchorsModel.close()
            }
            return
        }
        sourcesModel.pickMarker(SourcesModel.MarkerPick(bar: bar, left: left, bottom: bottom))
        showVersionBar(bar)
    }

    /// A label tapped at `bar`: the place that starts there is selected and comes into view. False when no place of a
    /// draft starts there, as on any piece that is not a draft.
    func pickPlace(startingAt bar: Int) -> Bool {
        guard let place = piece?.draft?.places.first(where: { $0.firstBar == bar }) else { return false }
        showSpan(Double(place.onset)..<Double(place.end))
        return true
    }

    /// A приём's number tapped (`key`, at the number's bottom left): its card opens there and the occurrence is selected.
    func pickTechnique(_ key: String, at point: CGPoint) {
        guard let spot = draftModel.pick(key, at: point) else { return }
        seamsModel.close()
        anchorsModel.close()
        showOccurrence(spot)
    }

    /// The pill's ‹ (-1) or › (1): the occurrence before or after is the current one, selected and brought into view.
    func stepTechnique(_ direction: Int) {
        guard let spot = draftModel.step(direction) else { return }
        showOccurrence(spot)
    }

    /// «Скрыть номера» or «Показать номера»: the numbers and the anchors' marks leave the page or come back; hidden, an
    /// anchor's menu and a place's ▾ menu go (DRAFT-05), a bridge's card stays, as its label still opens it.
    func hideTechniqueNumbers(_ hidden: Bool) {
        draftModel.hideNumbers(hidden)
        if hidden {
            anchorsModel.close()
            if let open = seamsModel.label, piece?.draft?.places.first(where: { $0.id == open.place })?.bridge != true { seamsModel.close() }
        }
        Task { await drawVersions() }
    }

    /// «↻ Место»: the current place (`DraftTechniquesModel.place`) selected and played round and round by the playback
    /// model (Цикл on), from its start.
    func loopPlace(on player: Player) async {
        guard let place = DraftTechniquesModel.place(among: piece?.draft?.places ?? [], picked: draftModel.picked?.spot, at: player.tick) else { return }
        await select(Double(place.onset), Double(place.end))
        playSelection(looped: true, on: player)
    }

    /// «▶ Всё»: the whole draft from its start, Цикл off so playback goes on past the selection; the selection stays.
    func playDraft(on player: Player) {
        if player.cycle, player.selection != nil { player.apply(.cycle) }
        player.go(to: 0, play: true)
    }

    /// The occurrence selected and brought into view; bars of a recording not yet put in only come into view, as they
    /// hold no notes of the draft yet.
    private func showOccurrence(_ spot: DraftTechniquesModel.Spot) {
        guard !spot.span.isEmpty else {
            Task { await scorePage.send(.reveal, ["start": spot.span.lowerBound]) }
            return
        }
        showSpan(spot.span)
    }

    /// What can be heard of the occurrence: as in the notes, the base variant's own notes there, taken or not, never where
    /// the base has no bars (an inserted bar); as in the variant, the stretch of the recording it was heard from when that
    /// sound is on this iPad, else the variant's own notes.
    func canHear(_ spot: DraftTechniquesModel.Spot, variant: Bool) -> Bool {
        (variant && recordingHeard(spot) != nil) || !spot.heard(variant: variant).span.isEmpty
    }

    /// The recording the occurrence's variant was heard from, when its sound is kept on this iPad (c-4b).
    func recordingHeard(_ spot: DraftTechniquesModel.Spot) -> URL? {
        spot.occurrence.recording.flatMap { OriginalSounds.file(project: $0.project, original: $0.original) }
    }

    /// «▶ Как в нотах», «▶ Как на записи» or «▶ Как в варианте» on the card or the pill (`canHear`): the recording's stretch
    /// (`RecordingListening`, the score's `player` paused first: one thing sounds at a time), else the base's or the
    /// variant's own notes, heard once as a version's bar is heard from its marker.
    func hearTechnique(_ spot: DraftTechniquesModel.Spot, variant: Bool, pausing player: Player) {
        guard canHear(spot, variant: variant) else { return }
        if variant, let url = recordingHeard(spot), let stretch = spot.occurrence.recording {
            if PlaybackModel.playing(player.state) { player.apply(.press) }
            RecordingListening.shared.playStretch(url, from: stretch.from, to: stretch.to)
            return
        }
        let side = spot.heard(variant: variant)
        hearVariant(side.variant, of: spot.draft, over: side.span)
    }
}
