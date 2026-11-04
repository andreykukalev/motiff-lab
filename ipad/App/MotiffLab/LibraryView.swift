import SwiftUI
import UniformTypeIdentifiers

/// The first screen until the redesign's library replaces it (0018 section 9): the player's pieces, which are project
/// files in the app's Documents (0019 section 2.3), and opening a MIDI or MusicXML file from Files, or the example.
/// A piece opened from a file becomes a project file here at once. Everything here runs on the device and is free.
struct LibraryView: View {
    private let text = UIText.current
    @EnvironmentObject private var account: Account
    @State private var settings = false
    @State private var opening = false
    @State private var choosing = false
    @State private var piece: OpenPiece?
    @State private var problem: String?
    @State private var projects: [ProjectFiles.Entry] = []

    /// MIDI, MusicXML (plain and compressed) and Motiff Lab project files.
    static let openable: [UTType] = [
        .midi,
        UTType(filenameExtension: "musicxml") ?? .xml,
        UTType(filenameExtension: "mxl") ?? .data,
        .xml,
        .json,
    ]

    var body: some View {
        NavigationStack {
            List {
                Section {
                    Button {
                        choosing = true
                    } label: {
                        Label { row(text["home.fromFile"], text["home.fromFileHint"]) } icon: { Image(systemName: "doc") }
                    }
                    Button {
                        Task { await openExample() }
                    } label: {
                        Label { row(text["home.tryFurElise"], text["home.furEliseHint"]) } icon: { Image(systemName: "music.note") }
                    }
                } header: {
                    Text(text["home.startHeading"])
                } footer: {
                    if let problem {
                        Text(problem).foregroundStyle(.red)
                    }
                }

                if !projects.isEmpty {
                    Section(text["home.projects"]) {
                        ForEach(projects) { project in
                            Button {
                                Task { await openProject(project.url) }
                            } label: {
                                row(project.title, text.format("home.lastOpened", project.modified.formatted(date: .abbreviated, time: .shortened)))
                            }
                        }
                        .onDelete(perform: delete)
                    }
                }
            }
            .disabled(opening)
            .overlay { if opening { ProgressView() } }
            .navigationTitle("Motiff Lab")
            .toolbar {
                if account.isAvailable {
                    ToolbarItem(placement: .topBarTrailing) {
                        Button {
                            settings = true
                        } label: {
                            Image(systemName: account.player == nil ? "person.crop.circle" : "person.crop.circle.fill")
                        }
                        .accessibilityLabel(text["account.nav"])
                    }
                }
            }
            .sheet(isPresented: $settings) { SettingsView(account: account, text: text) }
            .navigationDestination(item: $piece) { piece in
                ScoreScreen(piece: piece)
            }
            .fileImporter(isPresented: $choosing, allowedContentTypes: Self.openable) { result in
                Task { await open(result) }
            }
            .onAppear { projects = ProjectFiles.list() }
            .onChange(of: piece == nil) { _, closed in
                // Back from a score: its project was saved as the screen closed.
                if closed { projects = ProjectFiles.list() }
            }
        }
    }

    private func row(_ title: String, _ hint: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
            Text(hint).font(.footnote).foregroundStyle(.secondary)
        }
    }

    private func openExample() async {
        guard let example = OpenPiece.example else { return }
        let language = text.language
        await show(saveAs: example.title) {
            await OpenPiece.open(fileName: example.fileName, data: example.data, language: language, title: example.title)
        }
    }

    private func openProject(_ url: URL) async {
        do {
            let data = try Data(contentsOf: url)
            let language = text.language
            await show(file: url) { await OpenPiece.open(fileName: url.lastPathComponent, data: data, language: language) }
        } catch {
            problem = error.localizedDescription
        }
    }

    private func open(_ result: Result<URL, Error>) async {
        switch result {
        case .failure(let error):
            problem = error.localizedDescription
        case .success(let url):
            if ProjectFiles.isInFolder(url) {
                await openProject(url)
                return
            }
            // A file from Files is lent to the app only while it reads it; the piece becomes a project file here.
            let lent = url.startAccessingSecurityScopedResource()
            defer { if lent { url.stopAccessingSecurityScopedResource() } }
            do {
                let data = try Data(contentsOf: url)
                let language = text.language
                let name = url.lastPathComponent
                await show(saveAs: ProjectFiles.title(of: name)) { await OpenPiece.open(fileName: name, data: data, language: language) }
            } catch {
                problem = error.localizedDescription
            }
        }
    }

    /// Opens a piece and shows its score: saved to its own project file, or as a new one in Documents.
    private func show(file: URL? = nil, saveAs title: String? = nil, _ opener: () async -> Result<OpenPiece, EngraveError>) async {
        opening = true
        defer { opening = false }
        problem = nil
        switch await opener() {
        case .success(var opened):
            if let earlier = piece {
                _ = await MusicCore.run { MusicCore.close(piece: earlier.id) }
            }
            if let file {
                opened.file = file
            } else if let title {
                let url = ProjectFiles.newURL(for: title)
                do {
                    try await ProjectFiles.save(piece: opened.id, to: url)
                    opened.file = url
                } catch {
                    problem = (error as? ProjectError)?.message ?? error.localizedDescription
                }
            }
            projects = ProjectFiles.list()
            piece = opened
        case .failure(let error):
            problem = error.message
        }
    }

    private func delete(_ offsets: IndexSet) {
        for index in offsets {
            try? FileManager.default.removeItem(at: projects[index].url)
        }
        projects = ProjectFiles.list()
    }
}

extension OpenPiece: Hashable {
    static func == (lhs: OpenPiece, rhs: OpenPiece) -> Bool { lhs.id == rhs.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}
