import Foundation

/// A draft's view (v1.1, ADR 0034 section 9 steps 5 and 6; spec «Экран (iPad)»): one score, its places' results one after
/// another, as music-core opened it (`drafts.open`, `OpenPiece.draft`). Each place's label, music-core's words for where it
/// came from («① Ноты 9–16»), stands above the place's first bar, drawn with the version's labels (`performance`, with
/// `place`, in the ink); a tap on one selects the place, so it plays and loops as any passage. Above the bars of each приём
/// stands its plum number (`draftModel`); a tap on one selects that occurrence and opens its card there, and the pill's ‹ ›
/// steps from one to the next.
extension ScoreController {
    /// The labels of the draft's places for the page, in order, then the numbers of their приёмы; none for any other piece.
    var placeMarkers: [[String: Any]] {
        (piece?.draft?.places ?? []).map { place -> [String: Any] in
            ["bar": place.firstBar, "tick": place.onset, "text": place.label, "label": place.label, "place": true]
        } + draftModel.markers(text)
    }

    /// A label tapped at `bar` (`left`, `bottom`: its bottom left on the page): in a draft's view the place that starts
    /// there, else a version's marker, whose card opens there and whose bar is selected.
    func pickLabel(_ bar: Int, left: Double, bottom: Double) {
        guard !pickPlace(startingAt: bar) else { return }
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
        showOccurrence(spot)
    }

    /// The pill's ‹ (-1) or › (1): the occurrence before or after is the current one, selected and brought into view.
    func stepTechnique(_ direction: Int) {
        guard let spot = draftModel.step(direction) else { return }
        showOccurrence(spot)
    }

    /// «Скрыть номера» or «Показать номера»: the numbers leave the page or come back.
    func hideTechniqueNumbers(_ hidden: Bool) {
        draftModel.hideNumbers(hidden)
        Task { await drawVersions() }
    }

    /// «↻ Место»: the current place (`DraftTechniquesModel.place`) selected and played round and round by the playback
    /// model (Цикл on), from its start.
    func loopPlace(on player: Player) async {
        guard let place = DraftTechniquesModel.place(among: piece?.draft?.places ?? [], picked: draftModel.picked?.spot, at: player.tick) else { return }
        await select(Double(place.onset), Double(place.end))
        player.hold(selection: focusModel.playRange?.range)
        if !player.cycle { player.apply(.cycle) }
        if player.state == .stopped { player.apply(.press) }
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

    /// What can be heard of the occurrence: as in the notes, the draft's own notes there while the приём is not taken (they
    /// are the base's then), never over bars of a recording not yet put in; as in the variant, the stretch of the recording
    /// it was heard from when that sound is on this iPad, else the variant's own notes over its ticks.
    func canHear(_ spot: DraftTechniquesModel.Spot, variant: Bool) -> Bool {
        guard variant else { return !spot.span.isEmpty && !spot.isTaken }
        return recordingHeard(spot) != nil || !spot.variantSpan.isEmpty
    }

    /// The recording the occurrence's variant was heard from, when its sound is kept on this iPad (c-4b).
    func recordingHeard(_ spot: DraftTechniquesModel.Spot) -> URL? {
        spot.occurrence.recording.flatMap { OriginalSounds.file(project: $0.project, original: $0.original) }
    }

    /// «▶ Как в нотах», «▶ Как на записи» or «▶ Как в варианте» on the card or the pill (`canHear`): the base as the draft's
    /// notes have it there, the recording's stretch (`RecordingListening`, the score's `player` paused first: one thing
    /// sounds at a time), or the variant's own notes, heard once as a version's bar is heard from its marker.
    func hearTechnique(_ spot: DraftTechniquesModel.Spot, variant: Bool, pausing player: Player) {
        guard let piece, canHear(spot, variant: variant) else { return }
        if variant, let url = recordingHeard(spot), let stretch = spot.occurrence.recording {
            if PlaybackModel.playing(player.state) { player.apply(.press) }
            RecordingListening.shared.playStretch(url, from: stretch.from, to: stretch.to)
            return
        }
        Task {
            let answer: PlaybackAnswer?
            if variant {
                answer = await DraftTechniquesModel.variantPlayback(spot, on: piece.id)
            } else {
                answer = await PieceCore(piece: piece.id).playback().value
            }
            guard let notes = answer.map(PlaybackScore.init)?.notes else { return }
            hearNotes?(notes, variant ? spot.variantSpan : spot.span)
        }
    }
}
