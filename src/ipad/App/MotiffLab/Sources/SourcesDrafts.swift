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
    func openDraft(_ draft: SourceLayers.Draft) async -> OpenPiece? {
        await openShown("drafts.open", ["draft": draft.id], title: draft.name, at: nil, draft: true)
    }
}
