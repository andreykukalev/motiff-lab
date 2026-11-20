import Foundation

/// A draft's view (v1.1, ADR 0034 section 9 step 5; spec «Экран (iPad)»): one score, its places' results one after
/// another, as music-core opened it (`drafts.open`, `OpenPiece.draft`). Each place's label, music-core's words for where it
/// came from («① Ноты 9–16»), stands above the place's first bar, drawn with the version's labels (`performance`, with
/// `place`, in the ink); a tap on one selects the place, so it plays and loops as any passage.
extension ScoreController {
    /// The labels of the draft's places for the page, in order; none for any other piece.
    var placeMarkers: [[String: Any]] {
        (piece?.draft?.places ?? []).map { place in
            ["bar": place.firstBar, "tick": place.onset, "text": place.label, "label": place.label, "place": true]
        }
    }

    /// A label tapped at `bar`: the place that starts there is selected and comes into view. False when no place of a
    /// draft starts there, as on any piece that is not a draft.
    func pickPlace(startingAt bar: Int) -> Bool {
        guard let place = piece?.draft?.places.first(where: { $0.firstBar == bar }) else { return false }
        showSpan(Double(place.onset)..<Double(place.end))
        return true
    }
}
