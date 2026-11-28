import SwiftUI

/// A PDF piece's screen (0023, the web app's `/pdf`): the score's paper with the printed pages on it, between the two strips
/// of «Пюпитр» as on the notes (spec score-reading §2, §3, v1.7 e-2): the top strip with the way back, the title, which
/// opens the bookmarked pages, the page in view, the PDF tag, the bookmark for the page, the metronome, the pen and the
/// overflow with Download the PDF; the bottom shelf with the metronome's pill while it is open. The pen's palette hangs
/// under the top strip as on the notes. No notes play here.
struct PdfScreen: View {
    let piece: OpenPiece
    /// Opens the piece's other view in its place, for PDF | Notes (0023); nil where the library has none.
    var switchView: ((Landing) async -> Void)? = nil
    /// Opens a project file just written, at a place, in this piece's place: the notes read from the PDF.
    var openRead: ((URL, Landing) async -> Void)? = nil
    /// The PDF linked where it lies in Files (version 0.3), shown as it is: marks and bookmarks on its pages are kept with
    /// a copy in the piece, so the pen and the bookmark are not offered here.
    var linked = false
    /// What the read-only tag says in place of «Файлы» (version 0.6: another original shown as a view of the switch).
    var readOnlyNote: String? = nil
    /// The piece's sources (version 0.6, 0030): the switch of all views and the Sources panel over the
    /// pages, which opens here and leaves the PDF as it is (#582 item 4: it went to the notes to open the sheet there).
    @StateObject var sources = SourcesController()
    @StateObject var originals = OriginalsController()
    /// Whether the switch of all views and Sources are on for this PDF: a PDF of the piece's own with notes, or with none
    /// yet (Sources alone, v1.1, finding 29).
    @State var sourcesOn = false
    /// The Sources panel over the lower half of the pages.
    @State var sourcesOpen = false
    @EnvironmentObject var account: Account
    @ObservedObject var scan = ScanServer.shared
    @ObservedObject var settings = DeviceSettings.shared
    /// The library's recognition, where «Распознать ноты» with four engines sends this PDF (Andrei, 2026-11-25).
    @Environment(\.backgroundReading) var backgroundReading
    @State var readingOpen = false
    @State var settingsOpen = false
    @State var settingsNext = false
    let text = UIText.current
    @Environment(\.dismiss) var dismiss
    @Environment(\.scenePhase) var scenePhase
    /// The piece under this screen when it is shown over it: its recording is heard under this bar.
    @Environment(\.heardOver) var pieceUnder
    @StateObject var pdf = PdfController()
    @StateObject var photos = PhotoReader()
    @ObservedObject var metronome = Metronome.shared
    @State var contentsOpen = false
    /// «Пьесы в этом файле» (v0.4, experiment «Пьесы в файле»), opened from the Contents.
    @State var partsOpen = false
    @State var more = false
    @State var metronomeOpen = false
    @State var referenceOpen = false
    /// The piece is being handed to its notes: a second press on the switch meanwhile does nothing (#582 item 2).
    @State var handingOver = false

    /// With the sources on, the switch of all views is drawn from the first frame as the notes' screen handed it over
    /// (SourcesHandover, v0.6.4 spec section 13): before, PDF | Notes showed until the sources were read.
    init(piece: OpenPiece, switchView: ((Landing) async -> Void)? = nil, openRead: ((URL, Landing) async -> Void)? = nil, linked: Bool = false, readOnlyNote: String? = nil) {
        self.piece = piece
        self.switchView = switchView
        self.openRead = openRead
        self.linked = linked
        self.readOnlyNote = readOnlyNote
        let handed = linked ? nil : SourcesHandover.layers(for: piece.file)
        _sources = StateObject(wrappedValue: SourcesController(layers: handed))
        _sourcesOn = State(initialValue: handed != nil && switchView != nil && piece.other != nil)
    }

    var body: some View {
        GeometryReader { screen in
            VStack(spacing: 0) {
                // The strips of «Пюпитр» as on the notes (`ScoreScreenContent.body`), over whatever the room holds.
                bar(width: screen.size.width - 2 * DockedStrip.margin).dockedAtTop().zIndex(1)
                // The pages between the strips (`room`), and what the player opens over them.
                GeometryReader { room in
                    desk(twoUp: screen.size.width > screen.size.height)
                        .overlay(alignment: .topTrailing) { underStrip }
                        .overlay {
                            if let call = pdf.pencilPalette {
                                // The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8).
                                PencilPalette(holder: pdf, call: call, text: text)
                            }
                        }
                        .animation(.easeOut(duration: 0.16), value: pdf.pencilPalette)
                        .overlay(alignment: .bottom) { sourcesPanel(height: room.size.height / 2) }
                        .animation(.easeOut(duration: 0.2), value: sourcesOpen)
                }
                shelf.zIndex(1)
            }
        }
        // The keyboard never changes the screen's size, as on the notes (#582 item 16): the pages stay where they are.
        .ignoresSafeArea(.keyboard)
        .toolbar(.hidden, for: .navigationBar)
        .sheet(isPresented: $readingOpen, onDismiss: {
            if settingsNext {
                settingsNext = false
                settingsOpen = true
            }
        }) {
            if pdf.isPhotos {
                PhotoReadingPanel(reader: photos, pdf: pdf, address: scanAddress, text: text) { read in
                    ScanServer.jobs ? readPanel(readNotes, pdf: pdf.original) : readPanel(read, heading: text["photos.readHeading"], keeps: text["photos.readKeeps"], line: "photos.readLine")
                } open: { musicXml, engine, pages, reports in
                    await fill(DocumentRead.request(musicXml: musicXml, engine: engine, pages: pages, reports: reports, language: text.language, now: Date()))
                }
                .presentationDetents([.large])
            } else {
                readPanel(readNotes, pdf: pdf.original).fittedSheet()
            }
        }
        .sheet(isPresented: $referenceOpen) {
            ReferenceSheet(text: text, settings: settings, piece: piece.id)
        }
        .sheet(isPresented: $settingsOpen) {
            SettingsSheet(settings: DeviceSettings.shared, account: account, text: text, section: .account)
        }
        .sheet(isPresented: $partsOpen) {
            PiecePartsSheet(piece: piece, pdf: pdf, text: text, reopen: reopenParts).presentationDetents([.large])
        }
        .task {
            pdf.inkable = !linked
            pdf.family = piece.family
            await pdf.load(piece: piece.id, text: text, page: piece.landing?.page)
            await readSourceLayers()
            metronome.load(piece: pdf.projectId)
            // Handed from the notes, the metronome may click already (#521 section 15): its pill shows with it.
            if metronome.running { metronomeOpen = true }
        }
        // A new interface language saves the piece and opens it again at this page (InterfaceLanguage).
        .handsOver(piece: piece.id) { await handOverForLanguage() }
        .onDisappear {
            // The Sources panel's way back held this screen, its PDF and controllers with it (v0.6.4 spec section 11).
            sources.reopen = nil
            // Handed to the notes, the piece was saved and closed already (#511 section 10); the metronome goes on there.
            guard !ClosedPieces.take(piece.id) else {
                sources.handedAway()
                return
            }
            // Not handed over: the recording heard goes with the screen (v0.10 s-2 review).
            sources.leave(piece.file)
            metronome.stop()
            pdf.keepView()
            save()
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .background {
                pdf.keepView()
                save()
            }
            // The iPad's setting «Only draw with Apple Pencil» may have changed meanwhile.
            if phase == .active { pdf.applyInput() }
        }
    }

    /// The pages on the desk between the strips; while they are read a turning wheel, and why not when they cannot be.
    private func desk(twoUp: Bool) -> some View {
        ZStack(alignment: .top) {
            Color.ds("desk")
            if let document = pdf.document {
                PdfPages(document: document, twoUp: twoUp, controller: pdf)
            } else if pdf.problem == nil {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            if let problem = pdf.problem {
                Text(problem)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("danger"))
                    .padding(CGFloat.ds("s-4"))
                    .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                    .padding(.top, CGFloat.ds("s-6"))
                    .accessibilityAddTraits(.isStaticText)
            }
        }
    }

    private func save() {
        guard let file = piece.file else { return }
        let id = piece.id
        BackgroundTime.run { try? await ProjectFiles.save(piece: id, to: file) }
    }

    /// Opens this piece's file again in the PDF view at the page under the bar, once «Подтвердить пьесы» has written the
    /// pieces layer into it (version 0.5); nil where the library cannot open it here.
    private var reopenParts: (() async -> Void)? {
        guard let switchView else { return nil }
        return { [pdf] in
            partsOpen = false
            pdf.keepView()
            await switchView(.page(max(1, pdf.page)))
        }
    }
}
