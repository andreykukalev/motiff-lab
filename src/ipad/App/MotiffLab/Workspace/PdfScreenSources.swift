import SwiftUI

/// The PDF screen's other views and hand-overs: PDF | Notes and the switch of all views, the Sources panel, the piece
/// opened again in this place, and a new interface language.
extension PdfScreen {
    /// A new interface language (InterfaceLanguage): the PDF's place kept, the piece saved and closed, to open again at the
    /// page under the bar; nil without a project file or while handed over already.
    func handOverForLanguage() async -> InterfaceLanguage.Reopen? {
        guard let file = piece.file, !handingOver else { return nil }
        handingOver = true
        metronome.stop()
        pdf.keepView()
        try? await ProjectFiles.save(piece: piece.id, to: file)
        await ClosedPieces.close(piece.id)
        return InterfaceLanguage.Reopen(file: file, landing: .page(max(1, pdf.page)))
    }

    /// The notes read from the PDF as the player left them (#511 section 10), or the first time where the page under the bar
    /// starts in them, once the piece is saved and the PDF's place kept (the web app's `ShowNotesAsync`).
    func showNotes() async {
        guard let other = piece.other, let switchView, !handingOver else { return }
        handingOver = true
        defer { handingOver = false }
        ProbeCounts.shared.begin()
        pdf.keepView()
        if sourceLayers != nil { sources.handOver(to: other.file) }
        if let file = piece.file { try? await ProjectFiles.save(piece: piece.id, to: file) }
        ProbeCounts.shared.mark("saved")
        await switchView(.tick(other.pages.start(of: max(1, pdf.page))))
        // A switch that never ran takes the recording's hand-over back.
        sources.switchReturned()
    }

    /// The first PDF's view of the switch: the one this screen shows.
    static let documentView = "src_pdf"

    /// The piece's views for the switch of all views, as music-core lists them for this iPad; nil while the piece has no
    /// notes (PDF | Notes stays), except a PDF of its own with none yet (`withoutNotes`, v1.1): it and its recordings.
    var sourceLayers: SourceLayers? {
        guard sourcesOn else { return nil }
        if sources.layers.working != nil { return sources.layers }
        return withoutNotes ? sources.layers.views(showing: PdfScreen.documentView) : nil
    }

    /// A PDF of the piece's own with no notes yet (v1.1, finding 29): Sources, where a recording added is recognised.
    var withoutNotes: Bool { !linked && piece.other == nil && piece.file != nil && openRead != nil }

    /// The piece's sources (version 0.6, 0030), for a PDF whose notes the library holds or one with none yet; then what the
    /// screen before handed over: the toast of what was just done in the panel.
    func readSourceLayers() async {
        guard !linked, toNotes != nil || withoutNotes else {
            sourcesOn = false
            sourcesOpen = false
            // No switch here, so no recording heard over the pages (v0.10, finding 24).
            sources.stopHandedRecording(for: piece.file)
            return
        }
        // Only while the screen is there: switched away while its pages were read, the way back would hold it (#681).
        if !Task.isCancelled { sources.reopen = { await reopenHere() } }
        await sources.load(piece: piece.id, file: piece.file)
        SourcesHandover.drop(piece.file)
        await originals.load(piece)
        sourcesOn = true
        if let toast = SourcesHandover.toast(for: piece.file) { sources.toast = toast }
    }

    /// The Sources panel over the lower half of the pages (#582 item 4), as on the score, and the toast of what it did.
    @ViewBuilder func sourcesPanel(height: CGFloat) -> some View {
        VStack(spacing: CGFloat.ds("s-2")) {
            if let toast = sources.toast, sourcesOn {
                SourcesToastView(sources: sources, toast: toast, text: text).transition(.opacity)
            }
            if sourcesOpen, sourceLayers != nil {
                SourcesSheet(
                    sources: sources, versions: nil, originals: originals, text: text, current: PdfScreen.documentView,
                    close: { sourcesOpen = false }
                )
                .modifier(BottomPanel(height: height))
            }
        }
        .padding(.bottom, sourcesOpen ? 0 : CGFloat.ds("s-6"))
    }

    /// The piece opened again in this screen's place as «Сделать рабочими», Undo or the first notes heard left its project
    /// file (`reopening`): the PDF view is kept for it and nothing more is written to the file.
    private func reopenHere() async {
        guard let openRead, let file = piece.file else { return }
        pdf.keepView()
        await openRead(file, PdfScreen.reopening(hadNotes: piece.other != nil, hasNotes: sources.layers.working != nil, page: pdf.page))
    }

    /// Where the piece opens again in the PDF's place: on its notes when it has its first, else at the page under the bar.
    static func reopening(hadNotes: Bool, hasNotes: Bool, page: Int) -> Landing {
        !hadNotes && hasNotes ? .tick(0) : .page(max(1, page))
    }

    /// A view picked in the switch: a recording is heard over the pages (v0.10, finding 24); other views, none with no notes
    /// yet (v1.1: nothing left pending), the notes' screen shows once the piece opens there, as only it holds the notes.
    func pickView(_ view: SourceLayers.ViewEntry) {
        guard view.id != PdfScreen.documentView, !withoutNotes || view.kind == "performance" else { return }
        if view.kind == "performance" {
            sources.performance = view.id
            return
        }
        if view.id != sourceLayers?.working, let file = piece.other?.file ?? piece.file {
            SourcesHandover.show(view.id, on: file)
        }
        Task { await showNotes() }
    }

    /// Sources from the PDF: its panel opens over the pages, and the PDF stays as it is (#582 item 4).
    func openSources() {
        sourcesOpen.toggle()
    }
}
