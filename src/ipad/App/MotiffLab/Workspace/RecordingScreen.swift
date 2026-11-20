import SwiftUI

/// A piece that is only its recording, while it has no notes (v1.1, finding 29, Andrei's answer 3 of 2026-11-19): the
/// recording's waveform across the whole screen with the play cursor, a tap putting the sound there, and under it the
/// same small player as over the notes and the PDF (`RecordingViewCapsule`). Nothing is trimmed or edited. The bar is the
/// score's (Back, the Contents, the title, Sources); in Sources «Распознать · Pro» on the recording's row hears its notes,
/// which become the piece's working notes under the recording, and the piece then opens again as notes, its switch with
/// «Ноты», as a PDF piece does. Nothing is docked: the bar and the player float over the waveform.
struct RecordingScreen: View {
    let piece: OpenPiece
    /// Opens the piece's project file again in this screen's place: on its notes once they are heard.
    let reopen: ((Landing) async -> Void)?
    @StateObject private var sources = SourcesController()
    @StateObject private var originals = OriginalsController()
    @State private var sourcesOpen = false
    @State private var contentsOpen = false
    @State private var wave: RecordingWave?
    /// The recording's sound could not be read here.
    @State private var unreadable = false
    /// The piece is being handed over: once is enough.
    @State private var handingOver = false
    private let text = UIText.current
    @Environment(\.dismiss) private var dismiss
    @Environment(\.scenePhase) private var scenePhase

    /// The recording's sound kept on this iPad, by the project's id and the recording's.
    private var sound: URL? {
        guard let project = piece.own?.id, let recording = piece.recording else { return nil }
        return OriginalSounds.file(project: project, original: recording)
    }

    var body: some View {
        GeometryReader { screen in
            ZStack(alignment: .top) {
                Color.ds("desk").ignoresSafeArea()
                waveArea
                    .padding(.horizontal, CGFloat.ds("s-6"))
                    .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-6"))
                    .padding(.bottom, CGFloat.ds("h-touch") + 2 * CGFloat.ds("s-6"))
                bar(width: screen.size.width - 2 * CGFloat.ds("s-4"))
                    .padding(.horizontal, CGFloat.ds("s-4"))
                    .padding(.top, CGFloat.ds("s-3"))
            }
            .overlay(alignment: .bottom) {
                VStack(spacing: CGFloat.ds("s-2")) {
                    if let toast = sources.toast {
                        SourcesToastView(sources: sources, toast: toast, text: text).transition(.opacity)
                    }
                    if let sound {
                        RecordingViewCapsule(url: sound, title: title, text: text)
                    }
                }
                .padding(.bottom, CGFloat.ds("s-6"))
            }
            .overlay(alignment: .bottom) {
                if sourcesOpen {
                    SourcesSheet(sources: sources, score: nil, originals: originals, text: text, current: piece.recording, close: { sourcesOpen = false })
                    .modifier(BottomPanel(height: screen.size.height / 2))
                }
            }
            .animation(.ds("t-base"), value: sourcesOpen)
        }
        .toolbar(.hidden, for: .navigationBar)
        .task {
            // Only while the screen is there: switched away meanwhile, the way back would hold it (#681).
            if !Task.isCancelled { sources.reopen = { await reopenHere() } }
            await sources.load(piece: piece.id, file: piece.file)
            await originals.load(piece)
            if let toast = SourcesHandover.toast(for: piece.file) { sources.toast = toast }
            if let sound { wave = await RecordingWave.read(sound) }
            unreadable = wave == nil
        }
        // A new interface language saves the piece and opens it again here (InterfaceLanguage).
        .handsOver(piece: piece.id) { await handOverForLanguage() }
        .onDisappear {
            sources.reopen = nil
            // Opened again in its place (its notes heard, a new language), the piece was saved and closed already.
            guard !ClosedPieces.take(piece.id) else {
                sources.handedAway()
                return
            }
            sources.leave(piece.file)
            save()
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .background { save() }
        }
    }

    /// The recording's name, as Sources shows it.
    private var title: String {
        piece.recording.flatMap { sources.layers.original($0)?.shown } ?? piece.title
    }

    /// The waveform, while it is read a turning wheel, and a line when the sound is not on this iPad.
    @ViewBuilder private var waveArea: some View {
        if let wave, let sound {
            RecordingWaveView(wave: wave, url: sound, text: text)
        } else if unreadable {
            Text(text["sources.sheet.soundNotHere"])
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    /// The score's bar: Back, the Contents and the title, then Sources, where the recording is recognised.
    private func bar(width: CGFloat) -> some View {
        TopBar(width: width, pages: nil) {
            BarLeading(title: piece.title, contentsOpen: $contentsOpen, text: text) { dismiss() }
                .popover(isPresented: $contentsOpen) {
                    Text(text["recording.noContents"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(CGFloat.ds("s-4"))
                        .frame(width: 320, alignment: .leading)
                        .background(Color.ds("surface"))
                }
            SourcesButton(open: sourcesOpen, text: text) { sourcesOpen.toggle() }
        } actions: {
            EmptyView()
        } compact: {
            EmptyView()
        }
    }

    private func save() {
        guard let file = piece.file else { return }
        let id = piece.id
        BackgroundTime.run { try? await ProjectFiles.save(piece: id, to: file) }
    }

    /// The piece opened again in this screen's place as its project file now is: on its notes once they are heard
    /// (`SourcesController.keepRecognised`), or as it was after the toast's Undo.
    private func reopenHere() async {
        guard let reopen, !handingOver else { return }
        handingOver = true
        defer { handingOver = false }
        await reopen(.tick(0))
    }

    /// A new interface language (InterfaceLanguage): the piece saved and closed, to open again here; nil without a project
    /// file or while handed over already.
    private func handOverForLanguage() async -> InterfaceLanguage.Reopen? {
        guard let file = piece.file, !handingOver else { return nil }
        handingOver = true
        try? await ProjectFiles.save(piece: piece.id, to: file)
        await ClosedPieces.close(piece.id)
        return InterfaceLanguage.Reopen(file: file, landing: .tick(0))
    }
}
