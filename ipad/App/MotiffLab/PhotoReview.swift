import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// Photos of pages (v0.4, experiment «Фото страниц»): the pages photographed in a row with the document camera, or picked in
/// Photos or Files, then put in order, turned, taken out or photographed again here, with a warning on a page too dark or too
/// soft to read well. «Сделать пьесу» puts them together as one PDF of images: an ordinary PDF piece, whose original is
/// described as photos of so many pages. Nothing leaves the iPad here.
struct PhotoReviewScreen: View {
    let text: UIText
    /// The new piece's project file, once it is made.
    let done: (URL) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var list = PhotoPageList()
    @State private var thumbnails: [UUID: UIImage] = [:]
    @State private var cameraOpen = false
    @State private var photosOpen = false
    @State private var filesOpen = false
    @State private var picked: [PhotosPickerItem] = []
    /// The page being photographed again; nil while adding pages.
    @State private var retaking: UUID?
    @State private var making = false
    /// The pages still being measured.
    @State private var measuring: Set<UUID> = []
    @State private var confirming = false
    @State private var problem: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
                    Text(text[list.pages.isEmpty ? "photos.empty" : "photos.reviewHint"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                        .fixedSize(horizontal: false, vertical: true)
                    if list.pages.isEmpty { addButtons }
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 200), spacing: CGFloat.ds("s-4"))], spacing: CGFloat.ds("s-4")) {
                        ForEach(list.pages) { page in cell(page) }
                    }
                    ForEach(list.warnings, id: \.page) { warning in
                        Text(warning.text(text)).dsType(.caption).foregroundStyle(Color.ds("warning"))
                    }
                    if let problem {
                        Text(problem).dsType(.caption).foregroundStyle(Color.ds("danger")).fixedSize(horizontal: false, vertical: true)
                    }
                }
                .padding(CGFloat.ds("s-6"))
            }
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .fullScreenCover(isPresented: $cameraOpen) {
            DocumentCamera { images in
                cameraOpen = false
                Task { await add(images.compactMap(PhotoImages.jpeg)) }
            }
            .ignoresSafeArea()
        }
        .photosPicker(isPresented: $photosOpen, selection: $picked, maxSelectionCount: retaking == nil ? nil : 1, matching: .images)
        .onChange(of: picked) { _, items in
            guard !items.isEmpty else { return }
            picked = []
            Task { await add(items) }
        }
        .fileImporter(isPresented: $filesOpen, allowedContentTypes: [.image], allowsMultipleSelection: true) { result in
            Task { await add(files: (try? result.get()) ?? []) }
        }
        .confirmationDialog(text["photos.warnTitle"], isPresented: $confirming, titleVisibility: .visible) {
            Button(text["photos.makeAnyway"]) { Task { await make() } }
            Button(text["photos.retakeFirst"], role: .cancel) {}
        } message: {
            Text(list.warnings.map { $0.text(text) }.joined(separator: "\n"))
        }
        .interactiveDismissDisabled(making)
    }

    private var header: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Text(text["photos.title"])
                .dsType(.title)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            Spacer()
            if !list.pages.isEmpty {
                addMenu
                Button {
                    if list.warnings.isEmpty { Task { await make() } } else { confirming = true }
                } label: {
                    Text(making ? text["photos.making"] : text.count("photos.make", list.pages.count))
                }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                .disabled(making || !measuring.isEmpty)
            }
            SheetCloseButton(text: text)
        }
        .padding(.horizontal, CGFloat.ds("s-6"))
        .padding(.top, CGFloat.ds("s-4"))
    }

    /// The three ways in, as buttons on the empty screen.
    private var addButtons: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            if DocumentCamera.isAvailable {
                Button { open(.camera, retake: nil) } label: { Label { Text(text["photos.camera"]) } icon: { Icon(name: "camera") } }
                    .buttonStyle(DSButtonStyle(kind: .primary))
            }
            Button { open(.photos, retake: nil) } label: { Label { Text(text["photos.fromPhotos"]) } icon: { Icon(name: "plus") } }
                .buttonStyle(DSButtonStyle(kind: .secondary))
            Button { open(.files, retake: nil) } label: { Label { Text(text["photos.fromFiles"]) } icon: { Icon(name: "file") } }
                .buttonStyle(DSButtonStyle(kind: .secondary))
        }
    }

    private var addMenu: some View {
        Menu {
            if DocumentCamera.isAvailable { Button(text["photos.camera"]) { open(.camera, retake: nil) } }
            Button(text["photos.fromPhotos"]) { open(.photos, retake: nil) }
            Button(text["photos.fromFiles"]) { open(.files, retake: nil) }
        } label: {
            Label { Text(text["photos.addMore"]) } icon: { Icon(name: "plus") }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
    }

    private func cell(_ page: PhotoPage) -> some View {
        let number = list.number(of: page.id) ?? 0
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            thumbnail(page)
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text.format("photos.page", String(number))).dsType(.label).foregroundStyle(Color.ds("ink"))
                if let warning = PageWarning(page: number, look: page.look) {
                    Tag(text: text[warning.dark ? "photos.tagDark" : "photos.tagBlurry"], kind: .warn)
                } else if measuring.contains(page.id) {
                    ProgressView().controlSize(.small)
                }
            }
            HStack(spacing: CGFloat.ds("s-1")) {
                Button(text["photos.turn"]) { list.turn(page.id) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Menu {
                    if DocumentCamera.isAvailable { Button(text["photos.camera"]) { open(.camera, retake: page.id) } }
                    Button(text["photos.fromPhotos"]) { open(.photos, retake: page.id) }
                } label: {
                    Text(text["photos.retake"])
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button { remove(page.id) } label: { Icon(name: "trash") }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    .accessibilityLabel(text.format("photos.remove", String(number)))
            }
        }
        .padding(CGFloat.ds("s-3"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .draggable(page.id.uuidString) { thumbnail(page).frame(width: 120, height: 120) }
        .dropDestination(for: String.self) { items, _ in
            guard let moved = items.first.flatMap(UUID.init(uuidString:)) else { return false }
            withAnimation(.easeOut(duration: 0.2)) { list.move(moved, to: page.id) }
            return true
        }
        .accessibilityElement(children: .contain)
        .accessibilityAction(named: text["photos.earlier"]) { list.step(page.id, by: -1) }
        .accessibilityAction(named: text["photos.later"]) { list.step(page.id, by: 1) }
    }

    private func thumbnail(_ page: PhotoPage) -> some View {
        Group {
            if let image = thumbnails[page.id] {
                Image(uiImage: image).resizable().scaledToFit().rotationEffect(.degrees(Double(page.turns) * 90))
            } else {
                Color.ds("paper")
            }
        }
        .frame(maxWidth: .infinity)
        .frame(height: 200)
        .clipped()
        .accessibilityHidden(true)
    }

    private enum Source { case camera, photos, files }

    private func open(_ source: Source, retake: UUID?) {
        retaking = retake
        problem = nil
        switch source {
        case .camera: cameraOpen = true
        case .photos: photosOpen = true
        case .files: filesOpen = true
        }
    }

    private func remove(_ id: UUID) {
        list.remove(id)
        thumbnails[id] = nil
    }

    private func add(_ items: [PhotosPickerItem]) async {
        var photos: [Data] = []
        for item in items {
            if let data = try? await item.loadTransferable(type: Data.self), let image = UIImage(data: data), let photo = PhotoImages.jpeg(image) {
                photos.append(photo)
            }
        }
        await add(photos)
    }

    private func add(files: [URL]) async {
        var photos: [Data] = []
        for url in files {
            let lent = url.startAccessingSecurityScopedResource()
            defer { if lent { url.stopAccessingSecurityScopedResource() } }
            if let data = try? Data(contentsOf: url), let image = UIImage(data: data), let photo = PhotoImages.jpeg(image) {
                photos.append(photo)
            }
        }
        await add(photos)
    }

    /// The photos in, after the page photographed again or at the end, each then measured.
    private func add(_ photos: [Data]) async {
        guard !photos.isEmpty else {
            retaking = nil
            return
        }
        var added: [UUID] = []
        if let retaking, let first = photos.first {
            list.replace(retaking, with: first)
            added = [retaking]
        } else {
            for photo in photos {
                let page = PhotoPage(photo: photo)
                list.pages.append(page)
                added.append(page.id)
            }
        }
        retaking = nil
        measuring.formUnion(added)
        for id in added {
            guard let photo = list.pages.first(where: { $0.id == id })?.photo else { continue }
            thumbnails[id] = UIImage(data: photo)?.preparingThumbnail(of: CGSize(width: 400, height: 400))
            if let look = await PhotoImages.look(photo) { list.measured(id, look) }
            measuring.remove(id)
        }
    }

    /// The pages as one PDF piece, saved as a project file of its own.
    private func make() async {
        making = true
        defer { making = false }
        let pages = list.pages
        let pdf = await Task.detached(priority: .userInitiated) { PhotoImages.pdf(pages) }.value
        let title = text.format("photos.pieceTitle", Date().formatted(Date.FormatStyle(date: .abbreviated, time: .omitted).locale(Locale(identifier: text.language))))
        switch await PhotoPiece.make(pdf: pdf, pages: pages.count, title: title, language: text.language) {
        case .success(let url):
            done(url)
            dismiss()
        case .failure(let failure):
            problem = failure.message
        }
    }
}

/// A PDF of photographed pages made a PDF piece: opened in music-core as photos of so many pages, saved to a new project
/// file in Documents, and closed again, for the library to open as any saved piece.
enum PhotoPiece {
    static func make(pdf: Data, pages: Int, title: String, language: String) async -> Result<URL, ProjectError> {
        let fields: [String: Any] = ["language": language, "photos": max(1, pages), "now": ISO8601DateFormatter().string(from: Date())]
        let request = (try? JSONSerialization.data(withJSONObject: fields)).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
        let fileName = title + ".pdf"
        let opened = await MusicCore.run { MusicCore.open(fileName: fileName, data: pdf, request: request) }
        guard let object = facadeObject(opened), let piece = (object["piece"] as? NSNumber)?.int32Value, piece > 0 else {
            return .failure(ProjectError(message: facadeObject(opened)?["message"] as? String ?? "music-core did not open the pages."))
        }
        let url = ProjectFiles.newURL(for: title)
        let saved: Result<URL, ProjectError>
        do {
            try await ProjectFiles.save(piece: piece, to: url)
            saved = .success(url)
        } catch {
            saved = .failure((error as? ProjectError) ?? ProjectError(message: error.localizedDescription))
        }
        _ = await MusicCore.run { MusicCore.close(piece: piece) }
        return saved
    }
}
