import SwiftUI

/// Add piece (0018 section 9): opening a file runs on the device and is free. Scanning a page and hearing a recording come
/// to the iPad in a later step; until then they are on the web app.
struct AddPieceSheet: View {
    let text: UIText
    let picked: (Result<URL, Error>) -> Void
    /// A recording heard by Motiff Lab's scan server (Pro), to open as a new piece.
    let heard: (HeardRecording) -> Void
    let demo: () -> Void
    @EnvironmentObject private var account: Account
    @ObservedObject private var settings = DeviceSettings.shared
    @State private var choosing = false
    @State private var photographing = false
    @State private var progress: String?
    @State private var problem: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
                HStack {
                    Text(text["library.addAPiece"])
                        .dsType(.title)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                    Spacer()
                    SheetCloseButton(text: text)
                }
                Button {
                    choosing = true
                } label: {
                    HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                        Icon(name: "file", size: CGFloat.ds("icon-l"))
                            .foregroundStyle(Color.ds("accent"))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(text["library.openFile"])
                                .dsType(.bodyStrong)
                                .foregroundStyle(Color.ds("ink"))
                            Text(text["library.openFileHintHere"])
                                .dsType(.caption)
                                .foregroundStyle(Color.ds("ink-2"))
                        }
                        Spacer(minLength: 0)
                    }
                    .padding(CGFloat.ds("s-4"))
                    .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                }
                .buttonStyle(.plain)
                // On the button, not the sheet: the recording's chooser below has its own, and a second file importer on a
                // view that holds another is not shown.
                .fileImporter(isPresented: $choosing, allowedContentTypes: LibraryView.openable) { result in
                    picked(result)
                }
                if settings.isOn("experimental.photoPages") {
                    fromPhotos
                }
                if account.isAvailable {
                    fromRecording
                }
                Button(action: demo) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "play")
                        Text(text["library.demo"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            .padding(CGFloat.ds("s-6"))
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.medium, .large])
        .interactiveDismissDisabled(progress != nil)
        .fullScreenCover(isPresented: $photographing) {
            PhotoReviewScreen(text: text) { url in picked(.success(url)) }
        }
    }

    /// Photos of pages (v0.4, experiment «Фото страниц»): pages photographed in a row, or picked in Photos or Files, become
    /// one PDF piece on the iPad, free; reading its notes then goes page by page, as any scan.
    private var fromPhotos: some View {
        Button {
            photographing = true
        } label: {
            HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                Icon(name: "camera", size: CGFloat.ds("icon-l"))
                    .foregroundStyle(Color.ds("accent"))
                VStack(alignment: .leading, spacing: 2) {
                    Text(text["photos.addTitle"])
                        .dsType(.bodyStrong)
                        .foregroundStyle(Color.ds("ink"))
                    Text(text["photos.addHint"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
            }
            .padding(CGFloat.ds("s-4"))
            .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        }
        .buttonStyle(.plain)
    }

    /// From a recording (0014): the scan server hears the sound of an audio or video file, however long, and
    /// its notes become a new piece. Pro, as on the web; what it needs first is offered here.
    private var fromRecording: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                Icon(name: "mic", size: CGFloat.ds("icon-l"))
                    .foregroundStyle(Color.ds("accent"))
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(text["library.fromRecording"])
                            .dsType(.bodyStrong)
                            .foregroundStyle(Color.ds("ink"))
                        ProTag()
                    }
                    Text(text["library.fromRecordingHint"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Spacer(minLength: 0)
            }
            RecordingChooser(
                text: text,
                line: text["library.fromRecordingLineDevice"],
                button: text["library.chooseRecording"],
                hint: text["transcribe.choose"],
                progress: progress,
                problem: problem
            ) { url in
                Task { await hear(url) }
            }
        }
        .padding(CGFloat.ds("s-4"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    private func hear(_ url: URL) async {
        problem = nil
        defer { progress = nil }
        switch await RecordingHearing.hear(url, account: account, text: text, progress: { progress = $0 }) {
        case .success(let recording):
            heard(recording)
        case .failure(let failure):
            problem = failure.message
        }
    }
}

/// The close button at the top right of a sheet, which closes it.
struct SheetCloseButton: View {
    let text: UIText
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        Button {
            dismiss()
        } label: {
            Icon(name: "close")
                .foregroundStyle(Color.ds("ink-2"))
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
        }
        .accessibilityLabel(text["library.close"])
    }
}
