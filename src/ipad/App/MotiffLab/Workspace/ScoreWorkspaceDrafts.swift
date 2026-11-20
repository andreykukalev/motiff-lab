import Foundation

/// A fragment of the notes on the score into a draft (v1.1, ADR 0034 section 9 step 5; spec «Экран (iPad)»): whose drafts
/// it goes into, and the «В черновик» sheet asked for from the selection menu's «Черновик» or from Sources' «Новый
/// черновик». The screen presents the sheet (`ScoreScreenContent.draftSheet`).
extension ScoreWorkspace {
    /// The piece whose drafts a fragment selected here goes into: this piece, or `under`, the one a view shown over it was
    /// opened from; none on a draft's own view, as music-core takes fragments from notes, never from a draft. Nil: no
    /// «Черновик» in the selection menu.
    func draftOwner(under: SourcesController?) -> SourcesController? {
        piece.draft != nil ? nil : panels.sourcesOn ? sources : under
    }

    /// The sheet for the passage selected, `into` a new draft when asked from Sources; nil with nothing selected.
    func draftSheet(into: DraftChoice.Into? = nil) -> DraftSheetRequest? {
        guard let selection = score.selectionModel.selection else { return nil }
        return DraftSheetRequest(piece: piece.id, span: selection.onset..<selection.end, into: into)
    }

    /// «Новый черновик» in Sources (Andrei's open question; the lead's recommendation built, 2026-11-20): Sources closes
    /// for the notes; with a passage selected, the sheet for a new draft; with none, nil and the score's notice to select
    /// notes. No empty draft is ever made. This one place decides it.
    func newDraft() -> DraftSheetRequest? {
        change(.close(.versions))
        guard let request = draftSheet(into: .new) else {
            score.showNotice("drafts.selectNotes")
            return nil
        }
        return request
    }
}
