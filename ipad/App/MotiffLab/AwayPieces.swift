import Foundation
import SwiftUI
import UniformTypeIdentifiers

/// Why the PDF of a piece that is only a PDF could not be read on this iPad (version 0.5).
enum AwayReason: Error, Equatable, Sendable {
    /// This iPad has no link to it: the PDF is on another device, or in a folder not linked here.
    case elsewhere
    /// The linked file is not where the link points: perhaps the folder was reorganised.
    case missing
    /// The provider cannot be reached and the file is not on the iPad.
    case offline
    /// iOS no longer lets the app into the folder the file is in: its bookmark did not open.
    case folderLost(String)
    /// The file there is another one now: its fingerprint is not the piece's PDF's.
    case changed
    case unreadable
}

extension AwayReason {
    /// One line on why the file could not be read.
    func text(_ text: UIText) -> String {
        switch self {
        case .missing: text["folderImport.missingTitle"] + ". " + text["folderImport.missingLine"]
        case .offline: text["folderImport.offlineTitle"]
        case .folderLost(let place): text.format("folderImport.folderLostLine", place)
        case .changed: text["originals.changed"]
        case .elsewhere: text["folderImport.awayTitle"]
        case .unreadable: text["originals.unreadable"]
        }
    }
}

/// The PDF of a piece read where it lies, with what the facade's `open` takes with it.
struct AwayFound: Sendable {
    let data: Data
    /// The piece's project file, as text.
    let project: String
    let fileName: String
}

/// A piece with no notes whose PDF is not in its project file (version 0.5): kept by link in a folder the player imported
/// (`kept: "link"`), or synced from another device without its PDF (PR #463). The library lists it; opening it reads the
/// PDF where this iPad links it — through the folder's bookmark and the path inside it, else the file's own bookmark —
/// checks its fingerprint and opens it with the facade's `open` and its `pdf`. Nothing is written where the file lies.
enum AwayPdf {
    /// Whether the summary is of such a piece: no notes, no PDF in the file, and a PDF described.
    static func isAway(_ summary: PieceSummary?) -> Bool {
        guard let summary, !summary.hasNotes, !summary.document else { return false }
        return summary.originals.contains(where: \.isDocument)
    }

    /// The piece's PDF read through this iPad's link to it, checked by fingerprint, with the project file to open it by.
    static func read(_ url: URL, _ summary: PieceSummary, links: URL = OriginalLinks.folder, folders: URL = ImportFolders.root) async -> Result<AwayFound, AwayReason> {
        guard let project = summary.id, let original = summary.originals.first(where: \.isDocument),
              let link = OriginalLinks.load(project: project, original: original.id, in: links) else { return .failure(.elsewhere) }
        guard let file = try? String(contentsOf: url, encoding: .utf8) else { return .failure(.unreadable) }
        let read = await bytes(link, folders: folders)
        switch read {
        case .failure(let reason):
            if reason == .offline, let kept = KeptCopies.data(project: project, sha256: link.sha256) {
                return .success(AwayFound(data: kept, project: file, fileName: original.fileName))
            }
            return .failure(reason)
        case .success(let data):
            guard CloudSync.hash(data) == (original.sha256 ?? link.sha256) else { return .failure(.changed) }
            return .success(AwayFound(data: data, project: file, fileName: original.fileName))
        }
    }

    /// The linked file's bytes: in its folder by its path there first, then by the file's own bookmark, which follows a
    /// file moved inside its provider.
    static func bytes(_ link: OriginalLink, folders: URL = ImportFolders.root) async -> Result<Data, AwayReason> {
        var reason = AwayReason.missing
        if let folder = ImportFolders.folder(link.folderId, in: folders), let path = link.path {
            let found = await FolderAccess.within(folder, root: folders) { root in
                await LinkedFile.read(FolderAccess.file(path, in: root))
            }
            switch found {
            case .success(.success(let data)): return .success(data)
            case .success(.failure(let problem)): reason = Self.reason(problem)
            case .failure(.offline): reason = .offline
            case .failure: reason = .folderLost(folder.place)
            }
            if reason == .offline { return .failure(reason) }
        }
        guard !link.bookmark.isEmpty, let target = LinkedFile.resolve(link.bookmark) else { return .failure(reason) }
        switch await LinkedFile.read(target.url) {
        case .success(let data): return .success(data)
        case .failure(let problem): return .failure(reason == .missing ? Self.reason(problem) : reason)
        }
    }

    static func reason(_ problem: LinkProblem) -> AwayReason {
        switch problem {
        case .missing: .missing
        case .offline: .offline
        case .unreadable: .unreadable
        }
    }

    /// «Привязать оригинал» for such a piece: a PDF the player picked in Files, read and checked against the piece's
    /// fingerprint. A PDF kept by link stays where it lies (this iPad keeps a bookmark of it; its piece is not changed);
    /// one kept in the file on another device opens and is kept in this iPad's file, a copy.
    static func relink(_ picked: URL, piece url: URL, _ summary: PieceSummary, links: URL = OriginalLinks.folder) async -> Result<AwayFound, AwayReason> {
        guard let project = summary.id, let original = summary.originals.first(where: \.isDocument),
              let file = try? String(contentsOf: url, encoding: .utf8) else { return .failure(.unreadable) }
        let lent = picked.startAccessingSecurityScopedResource()
        defer { if lent { picked.stopAccessingSecurityScopedResource() } }
        let bookmark = try? LinkedFile.bookmark(of: picked)
        let data: Data
        switch await LinkedFile.read(picked) {
        case .failure(let problem): return .failure(reason(problem))
        case .success(let read): data = read
        }
        let sha256 = CloudSync.hash(data)
        guard sha256 == original.sha256 else { return .failure(.changed) }
        if original.kept == "link", let bookmark {
            let attributes = LinkedFile.attributes(of: picked)
            let link = OriginalLink(bookmark: bookmark, fileName: picked.lastPathComponent, sha256: sha256, bytes: attributes.bytes, modified: attributes.modified, linkedAt: Date())
            OriginalLinks.save(link, project: project, original: original.id, in: links)
        }
        return .success(AwayFound(data: data, project: file, fileName: original.fileName))
    }
}

/// The facade's calls for a PDF kept by link in a folder, on piece 0: `library.linked` makes a new piece of it, and
/// `library.link` moves a piece's original to where it was found again (or takes a changed file the player said is it).
enum LibraryLink {
    /// The project file of a new piece whose PDF stays in the folder, held by this iPad; nil when music-core refused.
    static func create(fileName: String, folder: String, sha256: String, bytes: Int64, pages: Int?) async -> String? {
        var request: [String: Any] = [
            "fileName": fileName, "folder": folder, "sha256": sha256, "bytes": bytes,
            "device": "ipad", "now": ISO8601DateFormatter().string(from: Date()),
        ]
        if let pages, pages > 0 { request["pages"] = pages }
        return await call("library.linked", request)
    }

    /// The project file with its PDF at `folder`/`fileName`; with `changed`, the fingerprint, size and pages of a changed
    /// file the player confirmed. Nothing else of the piece changes.
    static func move(_ file: String, folder: String, fileName: String, changed: (sha256: String, bytes: Int64, pages: Int?)? = nil) async -> String? {
        var request: [String: Any] = ["file": file, "original": OriginalEntry.documentId, "folder": folder, "fileName": fileName]
        if let changed {
            request["sha256"] = changed.sha256
            request["bytes"] = changed.bytes
            if let pages = changed.pages, pages > 0 { request["pages"] = pages }
        }
        return await call("library.link", request)
    }

    private static func call(_ name: String, _ request: [String: Any]) async -> String? {
        guard let body = try? JSONSerialization.data(withJSONObject: request) else { return nil }
        let json = String(decoding: body, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, name, json) }
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        return object["file"] as? String
    }
}

/// What the library asks when a piece's PDF could not be read (the approved screen import-web-7-missing.png, on the iPad):
/// «Файл не найден. Возможно, папку переорганизовали.» with «Проверить папку» when the piece came from a folder, «Нужна
/// сеть, чтобы открыть» without a connection, a folder iOS no longer lets the app into, a file that changed, or a PDF on
/// another device; and always «Привязать оригинал», a file picked in Files with the same fingerprint.
struct AwayPrompt: Identifiable {
    let url: URL
    let summary: PieceSummary
    let title: String
    let reason: AwayReason
    var id: URL { url }
}

struct AwayPieceSheet: View {
    let prompt: AwayPrompt
    let text: UIText
    /// «Проверить папку»; nil when no folder is known here or the experiment is off.
    let recheck: (() -> Void)?
    /// Opens the piece with its PDF found.
    let open: (AwayFound) async -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var choosing = false
    @State private var problem: String?
    @State private var busy = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(heading)
                .dsType(.title)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            Text(line)
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
            Text(place)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            if let problem {
                Text(problem).dsType(.caption).foregroundStyle(Color.ds("danger")).fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Spacer(minLength: 0)
                Button(text["folderImport.notNow"]) { dismiss() }
                    .buttonStyle(DSButtonStyle(kind: .quiet))
                Button(text["originals.relink"]) { choosing = true }
                    .buttonStyle(DSButtonStyle(kind: recheck == nil ? .primary : .secondary))
                if let recheck {
                    Button(text["folderImport.recheck"]) {
                        dismiss()
                        recheck()
                    }
                    .buttonStyle(DSButtonStyle(kind: .primary))
                }
            }
            .disabled(busy)
        }
        .padding(CGFloat.ds("s-6"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.medium])
        .fileImporter(isPresented: $choosing, allowedContentTypes: [.pdf]) { result in
            guard case .success(let picked) = result else { return }
            Task { await relink(picked) }
        }
    }

    private var heading: String {
        switch prompt.reason {
        case .missing: text["folderImport.missingTitle"]
        case .offline: text["folderImport.offlineTitle"]
        case .folderLost: text["folderImport.folderLostTitle"]
        case .changed: text["originals.changed"]
        case .elsewhere, .unreadable: text["folderImport.awayTitle"]
        }
    }

    private var line: String {
        switch prompt.reason {
        case .missing: text["folderImport.missingLine"]
        case .offline: text["folderImport.offlineLine"]
        case .folderLost(let place): text.format("folderImport.folderLostLine", place)
        case .changed: text["folderImport.changedLine"]
        case .elsewhere: OriginalAway.of(prompt.summary).map { $0.text(text) } ?? text["originals.nowhere"]
        case .unreadable: text["originals.unreadable"]
        }
    }

    /// The piece and where its PDF was: «Ballade No. 1 · Шопен › Баллады».
    private var place: String {
        let original = prompt.summary.originals.first(where: \.isDocument)
        let folder = original?.folder.flatMap { $0.isEmpty ? nil : $0.replacingOccurrences(of: "/", with: " › ") }
        return [prompt.title, folder ?? original?.fileName].compactMap { $0 }.joined(separator: " · ")
    }

    private func relink(_ picked: URL) async {
        busy = true
        defer { busy = false }
        switch await AwayPdf.relink(picked, piece: prompt.url, prompt.summary) {
        case .success(let found):
            dismiss()
            await open(found)
        case .failure(.changed):
            problem = text["originals.otherFile"]
        case .failure(let reason):
            problem = reason == .offline ? text["originals.offline"] : text["originals.unreadable"]
        }
    }
}
