import UIKit
import WebKit

/// ScoreController's side of Expand (`FocusScore`, v1.6 i-10b; FocusModel lifts the selection into focus, steps, engraves
/// the card and keeps the version in focus): the version shown on the score, read from SourcesModel, which Playback may
/// not name; the page held still under the veil; and Expand opened where the root connects it, from a bookmark gone to
/// again (`MarksScore`) or the place the player stopped at.
extension ScoreController {
    var versionShown: VersionEntry? { sourcesModel.versions.shown }

    var versionShownId: String? { sourcesModel.versions.shownId }

    var versionDiffering: [Int] { sourcesModel.versions.differing.map(\.bar) }

    /// In focus the page under the veil stays where it is: it does not scroll and the Pencil does not write on it.
    func holdPage(still: Bool) {
        webView.scrollView.isScrollEnabled = !still
        ink.still = still
    }

    /// Lifts the selection into focus from the notes (`FocusModel.enter`).
    func enterFocus() {
        focusModel.enter()
    }
}
