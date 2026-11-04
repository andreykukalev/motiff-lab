import SwiftUI
import UniformTypeIdentifiers

/// The first screen until the redesign's library replaces it (0018 section 9): open a MIDI or MusicXML file from
/// Files, or the example, and see its score. Opening a file runs on the device and is free.
struct LibraryView: View {
    private let text = UIText.current
    @State private var opening = false
    @State private var choosing = false
    @State private var piece: OpenPiece?
    @State private var problem: String?

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
            }
            .disabled(opening)
            .overlay { if opening { ProgressView() } }
            .navigationTitle("Motiff Lab")
            .navigationDestination(item: $piece) { piece in
                ScoreScreen(piece: piece)
            }
            .fileImporter(isPresented: $choosing, allowedContentTypes: Self.openable) { result in
                Task { await open(result) }
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
        await show { await OpenPiece.open(fileName: example.fileName, data: example.data, language: language, title: example.title) }
    }

    private func open(_ result: Result<URL, Error>) async {
        switch result {
        case .failure(let error):
            problem = error.localizedDescription
        case .success(let url):
            // A file from Files is lent to the app only while it reads it.
            let lent = url.startAccessingSecurityScopedResource()
            defer { if lent { url.stopAccessingSecurityScopedResource() } }
            do {
                let data = try Data(contentsOf: url)
                let language = text.language
                await show { await OpenPiece.open(fileName: url.lastPathComponent, data: data, language: language) }
            } catch {
                problem = error.localizedDescription
            }
        }
    }

    private func show(_ opener: () async -> Result<OpenPiece, EngraveError>) async {
        opening = true
        defer { opening = false }
        problem = nil
        switch await opener() {
        case .success(let opened):
            if let earlier = piece {
                _ = await MusicCore.run { MusicCore.close(piece: earlier.id) }
            }
            piece = opened
        case .failure(let error):
            problem = error.message
        }
    }
}

extension OpenPiece: Hashable {
    static func == (lhs: OpenPiece, rhs: OpenPiece) -> Bool { lhs.id == rhs.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}
