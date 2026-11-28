import SwiftUI

/// The PDF screen's strips (v1.7 e-2, as the notes' `ScoreScreenBars`): the bar, the top strip's row, with the way back,
/// the Contents, the switch of views or the PDF tag, Recognise notes, the bookmark, the modes, the Reference and More; what
/// hangs under the top strip; the bottom shelf with the metronome.
extension PdfScreen {
    func bar(width: CGFloat) -> some View {
        TopBar(width: width, pages: pageText) {
            barLeading
            if let sourceLayers, toNotes != nil || withoutNotes {
                // The switch of all the piece's views and Sources after it (version 0.6), as on the score; a PDF alone has none.
                if sourceLayers.shownViews.count < 2 { Tag(text: text["pdf.title"], kind: .assumed) }
                SourcesViewSwitch(layers: sourceLayers, current: PdfScreen.documentView, text: text, pick: pickView, openSources: openSources)
                SourcesButton(open: sourcesOpen, text: text, action: openSources)
            } else if let toNotes {
                PdfNotesSwitch(onPdf: true, text: text, other: toNotes)
            } else {
                Tag(text: text["pdf.title"], kind: .assumed)
            }
            // Why this PDF takes no pen or bookmark: it is read where it lies, after the places it shares with the notes.
            if linked { Tag(text: readOnlyNote ?? text["originals.linkFiles"], kind: .assumed) }
        } actions: {
            readNotesButton(compact: false)
            barActions
        } compact: {
            readNotesButton(compact: true)
            barActions
        }
    }

    /// Under the top strip, over the pages, what hung under the floating bar, as on the notes (E-score-3): the library's
    /// recognition of this PDF, the Pen's palette and the recording heard over the pages (v0.10, finding 24).
    var underStrip: some View {
        VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
            if let file = background.file, let line = background.line {
                line(handingOver) { Task { await openBackgroundNotes(file) } }
            }
            if pdf.penOn {
                PdfPenPalette(pdf: pdf, text: text)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }
            if let heard = PerformanceCapsule.sources(over: pieceUnder, own: sources, switchOn: sourceLayers != nil) {
                PerformanceCapsule(sources: heard, text: text)
            }
        }
        .hungUnderTopStrip()
        .animation(.easeOut(duration: 0.16), value: pdf.penOn)
    }

    /// The bottom shelf, as the notes' (`ScoreScreenContent.bottomShelf`), always there so the pages never move (E-pdf-3):
    /// in its middle, where the notes' shelf has it, the metronome, the notes' same item (`Pill`). It starts the metronome
    /// at once with its pill for the beats and the tapped tempo, which stands in its place until it is put away; held, a
    /// slide sets its tempo (#521 section 15).
    var shelf: some View {
        ZStack {
            if metronomeOpen {
                MetronomePill(metronome: metronome, text: text) {
                    metronome.stop()
                    metronomeOpen = false
                }
                .transition(.opacity)
            } else {
                iconItem("metronome", text["transport.metronome"], on: metronome.running, action: toggleMetronome)
                    .modifier(MetronomeHold(metronome: metronome, text: text, toggle: toggleMetronome))
                    .transition(.opacity)
            }
        }
        .animation(.easeOut(duration: 0.2), value: metronomeOpen)
        .modifier(DockedStrip(edge: .bottom))
        .keyboardLift(barOnly: true)
    }

    /// Notes in PDF | Notes: the notes read from this PDF, in this piece or the one they were read into; for the PDF linked
    /// in Files, which is only ever shown over its notes, those notes again (#495 BUG-02: it showed a tag in the switch's
    /// place, so «Ноты» was missing though the notes were right under it). Nil for a PDF with no notes yet.
    var toNotes: (() -> Void)? {
        if linked { return { dismiss() } }
        guard switchView != nil, piece.other != nil else { return nil }
        return { Task { await showNotes() } }
    }

    /// Recognise notes, in the bar while the PDF has pages and no notes yet (Andrei, 2026-11-11: in More it was not found):
    /// with its word where the bar has room, else its icon alone; not while the piece is recognised in the background,
    /// whose line under the bar stands in its place.
    @ViewBuilder private func readNotesButton(compact: Bool) -> some View {
        if let readNotesAction, pdf.pageCount > 0, !background.reading {
            Button(action: readNotesAction) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "roll", size: CGFloat.ds("icon-l"))
                    if !compact {
                        Text(text["pdf.readNotes"]).dsType(.label).fontWeight(.semibold).lineLimit(1)
                    }
                    if hosted { Tag(text: "Pro", kind: .saved) }
                }
                .padding(.horizontal, CGFloat.ds("s-2"))
                .frame(height: CGFloat.ds("h-control"))
            }
            .buttonStyle(BarActionStyle(on: readingOpen))
            .disabled(pdf.original == nil)
            .accessibilityLabel(text["pdf.readNotes"])
            .accessibilityValue(hosted ? "Pro" : "")
        }
    }

    /// Back, the Contents and the title, which opens them too, as on the score.
    private var barLeading: some View {
        BarLeading(title: piece.title, contentsOpen: $contentsOpen, text: text) { dismiss() }
            .popover(isPresented: $contentsOpen) {
                PdfContents(pdf: pdf, text: text, parts: openParts) { page in
                    contentsOpen = false
                    pdf.go(to: page)
                }
            }
    }

    private func toggleMetronome() {
        metronome.toggle()
        metronomeOpen = metronome.running
    }

    /// Bookmark the page, then how the pages are used (the modes, or the Pen before them), then the Circle and More: the
    /// modes, the Circle and More where the score's bar has them (the approved mockup v0.5.2, #479 item 21). The metronome
    /// is the shelf's since v1.7 e-2.
    @ViewBuilder private var barActions: some View {
        if !linked {
            Button {
                Task { await pdf.toggleBookmark() }
            } label: {
                Icon(name: "bookmark", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: pdf.currentBookmark != nil))
            .disabled(pdf.page == 0)
            .accessibilityLabel(text["pdf.bookmarkPage"])
            .accessibilityAddTraits(pdf.currentBookmark != nil ? .isSelected : [])
        }
        if !linked {
            PaletteDivider()
            // The score's modes: nothing on a PDF's pages is selected or edited, so only Navigate and Pen choose.
            ModeCapsule(chosen: pdf.penOn ? .pen : .navigate, text: text, unavailable: [.select]) { mode in
                if mode == .pen && !pdf.penOn { pdf.pen.writeOnChoosing() }
                pdf.penOn = mode == .pen
            }
        }
        PaletteDivider()
        Button {
            referenceOpen = true
        } label: {
            Icon(name: "keysig", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: referenceOpen))
        .accessibilityLabel(text["reference.open"])
        .accessibilityHint(text["reference.openHint"])
        Button {
            more.toggle()
        } label: {
            Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: more))
        .accessibilityLabel(text["bar.more"])
        .popover(isPresented: $more) {
            PdfMore(pdf: pdf, text: text)
        }
    }

    /// "p. 2 of 5", or "pp. 2–3 of 5" with two pages to a row.
    private var pageText: String? {
        guard pdf.page > 0 else { return nil }
        return pdf.page == pdf.lastPage
            ? text.format("bar.page", String(pdf.page), String(pdf.pageCount))
            : text.format("bar.pages", String(pdf.page), String(pdf.lastPage), String(pdf.pageCount))
    }

    /// «Пьесы в этом файле» from the Contents, for a PDF kept in the piece.
    private var openParts: (() -> Void)? {
        guard !linked else { return nil }
        return {
            contentsOpen = false
            partsOpen = true
        }
    }
}
