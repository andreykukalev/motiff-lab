import SwiftUI
import UniformTypeIdentifiers

/// Add piece (0018 section 9): a file, photos of pages, a picture of a page or a recording, each added on the device and
/// free (v1.1, finding 29: a recording too, recognised later in Sources); a folder is another way in. Every way in is a card
/// that acts on a tap, and the sheet is as tall as its cards, so all of them are in view the moment it opens (v0.5.6, #533
/// section 8).
struct AddPieceSheet: View {
    let text: UIText
    /// A file to open, or the project file of a piece made here (photos, a picture, a recording).
    let picked: (Result<URL, Error>) -> Void
    let demo: () -> Void
    /// «Добавить папку» (version 0.5, experiment «Импорт папки»): a folder picked in Files, to look through.
    var folder: ((Result<URL, Error>) -> Void)?
    @State private var choosing = false
    @State private var photographing = false
    @State private var choosingFolder = false
    @State private var choosingRecording = false
    /// A recording picked is being made a piece.
    @State private var adding = false
    /// Why the recording picked made no piece.
    @State private var problem: String?
    /// Why the picture picked with «Открыть файл» made no piece.
    @State private var unopened: String?

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
                // A JPEG or PNG becomes a piece of one page here, as a photo of a page does, and opens as its project file.
                .fileImporter(isPresented: $choosing, allowedContentTypes: LibraryView.openable + PhotoPiece.images) { result in
                    guard case .success(let url) = result, PhotoPiece.isImage(url) else { return picked(result) }
                    Task { await openImage(url) }
                }
                if let unopened {
                    problemLine(unopened)
                }
                if let folder {
                    fromFolder(folder)
                }
                fromPhotos
                fromRecording
                Button(action: demo) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "play")
                        Text(text["library.demo"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            .padding(CGFloat.ds("s-6"))
            .fittedSheet()
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .interactiveDismissDisabled(adding)
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

    /// From a recording (v1.1, finding 29; ADR 0030): an audio or video file added as it is, free and on this iPad, as the
    /// only original of a new piece (`RecordingPiece`), which opens in its own view with the recording's waveform. Nothing
    /// is heard here: its notes are recognised later with «Распознать · Pro» on its row in Sources, which says where the
    /// sound goes. The whole card picks the recording, as Open file picks a file.
    private var fromRecording: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Button {
                choosingRecording = true
            } label: {
                AddRoute(icon: "mic", title: text["library.fromRecording"], hint: text["library.fromRecordingHint"])
            }
            .buttonStyle(.plain)
            .disabled(adding)
            .fileImporter(isPresented: $choosingRecording, allowedContentTypes: RecordingHearing.recordings) { result in
                guard case .success(let url) = result else { return }
                Task { await addRecording(url) }
            }
            if let problem {
                problemLine(problem)
            }
        }
    }

    /// Why a way in made no piece, under its card.
    private func problemLine(_ words: String) -> some View {
        Text(words)
            .dsType(.caption)
            .foregroundStyle(Color.ds("danger"))
            .fixedSize(horizontal: false, vertical: true)
    }

    /// A JPEG or PNG made a piece of one page (`PhotoPiece.make(image:)`), opened as the project file it was saved to.
    private func openImage(_ url: URL) async {
        unopened = nil
        switch await PhotoPiece.make(image: url, text: text) {
        case .success(let project): picked(.success(project))
        case .failure(let failure): unopened = failure.message
        }
    }

    /// The recording picked made a new piece (`RecordingPiece.make`), opened as the project file it was saved to.
    private func addRecording(_ url: URL) async {
        problem = nil
        adding = true
        defer { adding = false }
        switch await RecordingPiece.make(url, text: text) {
        case .success(let project): picked(.success(project))
        case .failure(let failure): problem = failure.message
        }
    }
}

/// One way in on Add piece: its icon, what it does and a line on it, on a card. Every way in runs on the device and is
/// free (v1.1, finding 29: a recording's card lost its Pro tag and the line on where its sound goes, said now when it is
/// recognised in Sources).
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
        .contentShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
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

/// A sheet as tall as what it holds (v0.5.6, #533 sections 1 and 8): all of it in view the moment it opens, with no empty
/// space under it and nothing below the edge to drag up for; the full height stays one drag away for larger text. Put it
/// on the sheet's own content, inside any scroll view.
struct FittedSheet: ViewModifier {
    @State private var height: CGFloat = 0

    func body(content: Content) -> some View {
        content
            .onGeometryChange(for: CGFloat.self) { $0.size.height } action: { height = $0 }
            .presentationDetents(height > 0 ? [.height(height), .large] : [.medium, .large])
    }
}

extension View {
    /// The sheet as tall as this content (`FittedSheet`).
    func fittedSheet() -> some View { modifier(FittedSheet()) }
}
