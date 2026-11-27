import SwiftUI

/// The switch of all the piece's views and the Sources sheet (version 0.6; decision record
/// 0030). The working notes are this screen; the first PDF is the PDF view of PDF | Notes; another PDF or photos, and other
/// Notes, open on a screen of their own over this one, read-only, from a copy never saved; a version opens its page in
/// the sheet, as Other versions did; a recording's sound is heard from a capsule under the bar.
extension ScoreScreenContent {
    /// A view of the switch on a screen of its own over the score, its piece closed in music-core as it goes.
    struct ShownView: Identifiable {
        let piece: OpenPiece
        /// Shown as a PDF (another original) rather than as notes.
        let pdf: Bool
        /// A draft's view opened with its map and the assistant on its first place (v1.5 d-11).
        var assisting = false
        var id: Int32 { piece.id }

        /// The number a view was shown under, closed in music-core once its screen has gone.
        static func close(_ id: Int32) {
            Task {
                // After the screen's own leaving, which still reads the piece.
                try? await Task.sleep(for: .seconds(2))
                _ = await MusicCore.run { MusicCore.close(piece: id) }
            }
        }
    }

    /// Whether the switch of all views and Sources are there: never on a view shown over the piece (0030).
    var sourcesOn: Bool { panels.sourcesOn }

    /// The Sources sheet in Other versions' place.
    var sourcesSheet: some View {
        SourcesSheet(
            sources: sources, versions: sourcesModel, originals: originals, text: text, current: currentView,
            close: { change(.close(.versions)) },
            back: panels.fromMore.contains(.versions) ? (label: text["bar.more"], action: { backToMore(.versions) }) : nil,
            versionStatus: sourcesModel.status, newDraft: { draftSheet = workspace.newDraft() }
        )
    }

    /// The «В черновик» sheet (ScoreWorkspaceDrafts.swift), over the screen; once a fragment is added the screen says so.
    @ViewBuilder func draftSheetScreen(_ request: DraftSheetRequest) -> some View {
        if let owner = workspace.draftOwner(under: pieceUnder) {
            DraftSheet(
                request: request, sources: owner, text: text,
                added: { choice in
                    draftSheet = nil
                    // Into another piece's draft, its own toast says so, with «Открыть» (`toastsOverSelection`).
                    if choice.into.elsewhere == nil { score.showNotice("drafts.added") }
                },
                // Only where the switch is, which shows a draft's view (E-drafts-2).
                assist: panels.sourcesOn ? { (draft: String) in
                    assisted = draft
                    draftSheet = nil
                } : nil,
                assistLabel: DraftAssistantLabel(text: text),
                cancel: { draftSheet = nil }
            )
        }
    }

    /// Over the selection's toast: that of a fragment just added to another piece's draft, with «Открыть» (v1.2, collage),
    /// and that of an edit of the phrases, with its Undo or Redo (ScorePhraseEdits.swift). Only on the screen in sight: not
    /// behind a view shown over the piece, whose owner is the same and which shows it itself.
    @ViewBuilder var toastsOverSelection: some View {
        if shownView == nil, let owner = workspace.draftOwner(under: pieceUnder) {
            DraftElsewhereToast(owner: owner, text: text, open: openElsewhere)
        }
        PhraseEditToast(selectionModel: selectionModel, score: score, text: text)
    }

    /// «Открыть» on that toast: the other piece opens in this one's place, its screen showing that draft's view, handed over
    /// as a view picked on the PDF's switch is (`SourcesHandover`); this piece is kept first (`ScoreWorkspace.openElsewhere`).
    /// On a view shown over the piece, `openFile` is the piece's under it, which does that and closes the view. Nil where
    /// there is no way.
    private var openElsewhere: ((DraftChoice.Elsewhere) async -> Void)? {
        guard let openFile else { return nil }
        let shown = viewing != nil
        return { [workspace] other in
            SourcesHandover.show(other.draft, on: other.file)
            if shown { await openFile(other.file, .tick(0)) } else { await workspace.openElsewhere(other.file, at: .tick(0), openFile) }
        }
    }

    /// The views shown over the score, the sources read as the screen opens, and a variation only tried left before the
    /// Sources sheet writes the project file.
    func sourcing(_ content: some View) -> some View {
        content
            .fullScreenCover(item: $shownView) { view in shownViewScreen(view) }
            .sheet(item: $draftSheet, onDismiss: openAssisted) { request in draftSheetScreen(request) }
            .task(id: sourcesOn) { await workspace.loadSources(reopen: reopen, pick: pickView) }
            .onChange(of: panels[.versions]) { _, open in workspace.sourcesOpened(open) }
    }

    /// A draft just made with «С ассистентом · Pro» (v1.5 d-11), once the sheet has gone (a cover is not presented while a
    /// sheet goes): its view over the score, its map open and the assistant on ① (`ScoreWorkspace.showDraft`).
    private func openAssisted() {
        guard let draft = assisted else { return }
        assisted = nil
        Task { await workspace.showDraft(draft, assisting: true) { shownView = $0 } }
    }

    /// The view shown now: the working notes (the recording heard is over the view, not one: v0.10, finding 24; a draft's
    /// view is shown over it).
    var currentView: String? { sources.layers.working }

    /// The views for the bar's switch, and what picking one does; nil while the switch is off.
    var switchLayers: SourceLayers? { sourcesOn ? sources.layers : nil }
    var switchPick: ((SourceLayers.ViewEntry) -> Void)? {
        guard sourcesOn else { return nil }
        return { view in pickView(view) }
    }

    /// A view picked in the switch or its menu (ScoreWorkspace.pickView), with what only the screen has: its way to the
    /// piece's other view, its texts and the cover a view is shown on.
    func pickView(_ view: SourceLayers.ViewEntry) {
        workspace.pickView(view, switchView: switchView, text: text) { shownView = $0 }
    }

    /// The screen of a view shown over the piece, with the recording heard under its bar (v0.10, finding 24: `heardOver`,
    /// as over the PDF linked in Files); its piece is closed in music-core once the screen has gone.
    func shownViewScreen(_ view: ShownView) -> some View {
        Group {
            if view.pdf {
                PdfScreen(piece: view.piece, linked: true, readOnlyNote: text["sources.sheet.viewOnly"])
            } else if view.piece.draft != nil {
                // Opened again in its place after each step (v1.1), closing each number it was open under itself.
                ShownDraftScreen(piece: view.piece, owner: sources, viewing: text["sources.sheet.viewOnly"], assisting: view.assisting)
            } else {
                ScoreScreen(piece: view.piece, viewing: text["sources.sheet.viewOnly"], openFile: openFile.map { open in { (file: URL, landing: Landing) async in
                    // The piece under the view, kept, opens it, the view closing first.
                    shownView = nil
                    await workspace.openElsewhere(file, at: landing, open)
                } })
            }
        }
        .heardOver(sources)
        .environmentObject(account)
        .environmentObject(settings)
        .onDisappear {
            if view.piece.draft == nil { ShownView.close(view.piece.id) }
        }
    }
}
