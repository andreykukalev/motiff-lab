import SwiftUI

/// The piece's originals, its sources and the switch of all its views on the score's screen (ScoreWorkspace; version
/// 0.6, decision record 0030): read as the screen opens, and what picking a view does. What only the screen has comes
/// in as a parameter, read by the view at the call: its ways to the piece's other view (`switchView`) and to open the
/// piece again in its place (`reopen`), its texts, and the cover it shows a view on (`show`).
extension ScoreWorkspace {
    /// The piece's originals on this iPad are read, and the score's sources follow each change of them. The screen's
    /// `.task(id: piece.id)`.
    func loadOriginals() async {
        originals.changed = { [weak self] in self?.originalsChanged() }
        await originals.load(piece)
    }

    /// The piece changed in its originals: the score reads its sources again.
    func originalsChanged() {
        score.refreshSources()
    }

    /// The sources as the screen opens then what the screen before handed over: the toast
    /// of what was done, or a view picked on the PDF's switch. The screen's `.task(id: sourcesOn)`; `pick` is the
    /// screen's `pickView`.
    func loadSources(reopen: ((Landing) async -> Void)?, pick: (SourceLayers.ViewEntry) -> Void) async {
        guard panels.sourcesOn else { return }
        sources.reopen = { [weak self] in await self?.reopenHere(reopen) }
        await sources.load(piece: piece.id, file: piece.file)
        SourcesHandover.drop(piece.file)
        if let toast = SourcesHandover.toast(for: piece.file) { sources.toast = toast }
        if let id = SourcesHandover.view(for: piece.file), let view = sources.layers.views.first(where: { $0.id == id }) {
            pick(view)
        }
    }

    /// A view picked in the switch or its menu. The recording heard over the views stays as it is (v0.10, finding 24). A
    /// version opens Sources and is the one compared on the score (its page, the bar-by-bar merge, left in v1.1). A draft
    /// opens its own view over the score (`drafts.open`, v1.1), never as Notes.
    func pickView(_ view: SourceLayers.ViewEntry, switchView: ((Landing) async -> Void)?, text: UIText, show: @escaping (ScoreScreenContent.ShownView) -> Void) {
        switch view.kind {
        case "performance":
            if player.state == .playing { player.toggle() }
            sources.performance = view.id
        case "draft":
            Task { await showDraft(view.id, show: show) }
        case "original":
            if view.id == PdfScreen.documentView, let toPdf = self.toPdf(switchView) {
                toPdf()
            } else {
                Task { await showOriginal(view.id, show: show) }
            }
        default:
            guard view.id != sources.layers.working else { return }
            if sources.layers.notes(view.id)?.role == "version", score.sourcesModel.versions.entries.contains(where: { $0.id == view.id }) {
                change(.open(.versions))
                score.compareVersion(view.id)
            } else {
                Task { await showNotes(view.id, text: text, show: show) }
            }
        }
    }

    /// PDF in the bar's PDF | Notes: for a piece with a PDF kept inside it, or notes read from a PDF piece the library holds;
    /// else the PDF linked where it lies in Files, over the score (version 0.3).
    func toPdf(_ switchView: ((Landing) async -> Void)?) -> (() -> Void)? {
        if switchView != nil, piece.other != nil || score.sourcesModel.info.document != nil {
            // The switch of all views goes with the piece to its PDF (SourcesHandover).
            return { [self] in ProbeCounts.shared.begin(); if panels.sourcesOn { sources.handOver(to: piece.other?.file ?? piece.file) }; Task { await showPdf(switchView); sources.switchReturned() } }
        }
        guard originals.canOpenLinkedPdf else { return nil }
        return { [self] in
            if player.state == .playing { player.toggle() }
            Task {
                await originals.openLinkedPdf(title: piece.title)
                // Changed, gone or not downloaded: Sources says why, under the PDF.
                if originals.linkedPdf == nil { change(.open(.versions)) }
            }
        }
    }

    /// Another PDF or photos kept in the piece, shown as they are; why not, in the sheet.
    private func showOriginal(_ id: String, show: (ScoreScreenContent.ShownView) -> Void) async {
        guard let original = sources.layers.original(id) else { return }
        if player.state == .playing { player.toggle() }
        guard let opened = await sources.openOriginal(original, title: original.shown) else {
            change(.open(.versions))
            return
        }
        show(ScoreScreenContent.ShownView(piece: opened, pdf: true))
    }

    /// Other Notes shown on their own, at the place in the music in view here; why not, in the sheet.
    private func showNotes(_ id: String, text: UIText, show: (ScoreScreenContent.ShownView) -> Void) async {
        guard let notes = sources.layers.notes(id) else { return }
        if player.state == .playing { player.toggle() }
        let at = await score.tickInView()
        guard let opened = await sources.openNotes(notes, title: sources.layers.title(of: notes, text), at: at) else {
            change(.open(.versions))
            return
        }
        show(ScoreScreenContent.ShownView(piece: opened, pdf: false))
    }

    /// A draft's view: its places as one score, its labels above them, on a screen of its own over this one, with its map
    /// and the assistant on its first place when `assisting` (v1.5 d-11, «С ассистентом · Pro» on the card «Новый
    /// черновик»); why not, in the sheet.
    func showDraft(_ id: String, assisting: Bool = false, show: (ScoreScreenContent.ShownView) -> Void) async {
        guard let draft = sources.layers.draft(id) else { return }
        if player.state == .playing { player.toggle() }
        guard let opened = await sources.openDraft(draft) else {
            change(.open(.versions))
            return
        }
        show(ScoreScreenContent.ShownView(piece: opened, pdf: false, assisting: assisting))
    }

    /// The piece opened again in this screen's place from its project file, as «Сделать рабочими» or the toast's Undo has
    /// just left it: the "Notes" view is kept for it and nothing more is written to the file.
    private func reopenHere(_ reopen: ((Landing) async -> Void)?) async {
        guard let reopen else { return }
        let at = await score.tickInView() ?? 0
        let view = notesViewNow()
        player.shutdown()
        await keepNotesView(view)
        await reopen(.tick(at))
    }
}
