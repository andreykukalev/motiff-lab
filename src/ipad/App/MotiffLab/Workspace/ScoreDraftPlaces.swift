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
        (piece?.draft?.places ?? []).map { place in
            ["bar": place.firstBar, "tick": place.onset, "text": place.label, "label": place.label, "place": true]
        } + draftModel.markers(UIText.current)
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

    /// The occurrence selected and brought into view; bars of a recording not yet put in only come into view, as they
    /// hold no notes of the draft yet.
    private func showOccurrence(_ spot: DraftTechniquesModel.Spot) {
        guard !spot.span.isEmpty else {
            Task { await scorePage.send(.reveal, ["start": spot.span.lowerBound]) }
            return
        }
        showSpan(spot.span)
    }

    /// What can be heard of the occurrence here: the draft's own notes there, which are the base's while the приём is not
    /// taken and the variant's once it is (`variant`); not the other, which music-core does not answer on its own yet,
    /// nor bars of a recording not yet put in.
    func canHear(_ spot: DraftTechniquesModel.Spot, variant: Bool) -> Bool {
        !spot.span.isEmpty && spot.isTaken == variant
    }

    /// «▶ Как в нотах» or «▶ Как на записи» on the card or the pill: the occurrence heard once, as the draft's notes have
    /// it now (`canHear`), as a version's bar is heard from its marker.
    func hearTechnique(_ spot: DraftTechniquesModel.Spot, variant: Bool) {
        guard let piece, canHear(spot, variant: variant) else { return }
        Task {
            guard let notes = await PieceCore(piece: piece.id).playback().value.map(PlaybackScore.init)?.notes else { return }
            hearNotes?(notes, spot.span)
        }
    }
}
