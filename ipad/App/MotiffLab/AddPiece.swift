import SwiftUI
import UniformTypeIdentifiers

/// Add piece (0018 section 9): opening a file runs on the device and is free. Scanning a page and hearing a recording come
/// to the iPad in a later step; until then they are on the web app.
struct AddPieceSheet: View {
    let text: UIText
    let picked: (Result<URL, Error>) -> Void
    /// A recording heard by Motiff Lab's scan server (Pro), to open as a new piece.
    let heard: (HeardRecording) -> Void
    let demo: () -> Void
    /// «Добавить папку» (version 0.5, experiment «Импорт папки»): a folder picked in Files, to look through.
    var folder: ((Result<URL, Error>) -> Void)?
    @EnvironmentObject private var account: Account
    @ObservedObject private var settings = DeviceSettings.shared
    @State private var choosing = false
    @State private var photographing = false
    @State private var choosingFolder = false
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
                    AddRoute(icon: "file", title: text["library.openFile"], hint: text["library.openFileHintHere"])
                }
                .buttonStyle(.plain)
                // On the button, not the sheet: the recording's chooser below has its own, and a second file importer on a
                // view that holds another is not shown.
                .fileImporter(isPresented: $choosing, allowedContentTypes: LibraryView.openable) { result in
                    picked(result)
                }
                if let folder, settings.isOn("experimental.libraryImport") {
                    fromFolder(folder)
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

    /// «Добавить папку» (import-ipad-1-source): a folder in Files, the iPad's or a provider's (Google Drive, OneDrive,
    /// Dropbox), whose scores are listed on «Что нашлось» before anything is added. The folder is only read.
    private func fromFolder(_ picked: @escaping (Result<URL, Error>) -> Void) -> some View {
        Button {
            choosingFolder = true
        } label: {
            AddRoute(icon: "folder", title: text["folderImport.addFolder"], hint: text["folderImport.addFolderHint"])
        }
        .buttonStyle(.plain)
        .fileImporter(isPresented: $choosingFolder, allowedContentTypes: [.folder]) { result in
            picked(result)
        }
    }

    /// Photos of pages (v0.4, experiment «Фото страниц»): pages photographed in a row, or picked in Photos or Files, become
    /// one PDF piece on the iPad, free; reading its notes then goes page by page, as any scan.
    private var fromPhotos: some View {
        Button {
            photographing = true
        } label: {
            AddRoute(icon: "camera", title: text["photos.addTitle"], hint: text["photos.addHint"])
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

/// One way in on Add piece: its icon, what it does and a line on it, on a card.
struct AddRoute: View {
    let icon: String
    let title: String
    let hint: String

    var body: some View {
        HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
            Icon(name: icon, size: CGFloat.ds("icon-l"))
                .foregroundStyle(Color.ds("accent"))
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .dsType(.bodyStrong)
                    .foregroundStyle(Color.ds("ink"))
                Text(hint)
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
