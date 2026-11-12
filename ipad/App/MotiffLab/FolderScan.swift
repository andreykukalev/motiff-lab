import Foundation
import PDFKit

/// A score found in a folder the player chose (version 0.5, «Импорт папки»): its path inside the folder, "/" between the
/// folders, in Unicode's composed form as music-core keeps it, and its size and date as Files knows them without
/// downloading it.
struct FolderFile: Equatable, Sendable {
    let path: String
    var bytes: Int64
    let modified: Date?

    /// The file's own name, «Ballade No. 1.pdf».
    var name: String { path.split(separator: "/").last.map(String.init) ?? path }

    /// The folders it is in inside the chosen one, «Шопен/Баллады»; empty for a file right in it.
    var folder: String {
        guard let slash = path.lastIndex(of: "/") else { return "" }
        return String(path[..<slash])
    }

    var isPdf: Bool { path.lowercased().hasSuffix(".pdf") }
}

/// What the app read from a file's bytes: its fingerprint, a PDF's pages and its own Title and Author (only a guess), a
/// small MusicXML file itself for music-core to read its title and composer, and whether it could not be read at all.
struct FileFacts: Equatable, Sendable {
    var sha256: String?
    var pages: Int?
    var pdfTitle: String?
    var pdfAuthor: String?
    /// The MusicXML file, base64, when it is small enough to send to music-core for its header.
    var musicXml: String?
    var unreadable = false
}

/// Looking through a folder (version 0.5, «Импорт папки»): every file in it and in its folders at any depth whose kind
/// the app adds (PDF, MIDI, MusicXML), listed without reading them; then each read once, a coordinated read (iOS first
/// downloads one that is only in the cloud), for its facts. Only reads: nothing in the folder is changed.
enum FolderScan {
    /// The kinds of file the app adds, by extension.
    static let extensions: Set<String> = ["pdf", "mid", "midi", "musicxml", "mxl", "xml"]
    /// The largest MusicXML file sent whole to music-core for its title and composer.
    static let headerBytes = 1_000_000

    /// The scores in the folder, by path. A file iCloud Drive has not downloaded is listed under its own name (iOS shows
    /// it as a hidden «.name.icloud»), its size known once it is read.
    static func list(_ root: URL) async -> [FolderFile] {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                continuation.resume(returning: listNow(root))
            }
        }
    }

    private static func listNow(_ root: URL) -> [FolderFile] {
        var files: [FolderFile] = []
        var failure: NSError?
        NSFileCoordinator(filePresenter: nil).coordinate(readingItemAt: root, options: .immediatelyAvailableMetadataOnly, error: &failure) { folder in
            files = walk(folder)
        }
        if failure != nil && files.isEmpty { files = walk(root) }
        return files.sorted { $0.path < $1.path }
    }

    private static func walk(_ root: URL) -> [FolderFile] {
        let keys: [URLResourceKey] = [.isRegularFileKey, .fileSizeKey, .contentModificationDateKey]
        let base = root.standardizedFileURL.resolvingSymlinksInPath().pathComponents
        guard let walker = FileManager.default.enumerator(at: root, includingPropertiesForKeys: keys, options: [.skipsPackageDescendants]) else { return [] }
        var files: [FolderFile] = []
        for case let url as URL in walker {
            guard let found = file(url, base: base, keys: keys) else {
                if url.lastPathComponent.hasPrefix("."), !url.lastPathComponent.hasSuffix(".icloud") { walker.skipDescendants() }
                continue
            }
            files.append(found)
        }
        return files
    }

    /// The listed file at the url; nil for a folder, a hidden file or another kind of file.
    private static func file(_ url: URL, base: [String], keys: [URLResourceKey]) -> FolderFile? {
        var name = url.lastPathComponent
        let placeholder = name.hasPrefix(".") && name.hasSuffix(".icloud")
        if placeholder { name = String(name.dropFirst().dropLast(".icloud".count)) }
        guard !name.hasPrefix("."), extensions.contains((name as NSString).pathExtension.lowercased()),
              let values = try? url.resourceValues(forKeys: Set(keys)), values.isRegularFile == true else { return nil }
        let parts = url.deletingLastPathComponent().standardizedFileURL.resolvingSymlinksInPath().pathComponents + [name]
        guard parts.count > base.count, Array(parts.prefix(base.count)) == base else { return nil }
        let path = parts.dropFirst(base.count).joined(separator: "/").precomposedStringWithCanonicalMapping
        // A placeholder's own size is not the file's: 1 until it is read, never 0, which says «empty».
        let bytes = placeholder ? 1 : Int64(values.fileSize ?? 0)
        return FolderFile(path: path, bytes: bytes, modified: values.contentModificationDate)
    }

    /// What the bytes of a file named `name` say: its fingerprint always; a PDF's pages, Title and Author, or that it is
    /// locked with a password or broken; a small MusicXML file itself.
    static func facts(_ data: Data, name: String) -> FileFacts {
        var facts = FileFacts(sha256: CloudSync.hash(data))
        let kind = (name as NSString).pathExtension.lowercased()
        if kind == "pdf" {
            guard let document = PDFDocument(data: data), !document.isLocked else {
                facts.unreadable = true
                return facts
            }
            facts.pages = document.pageCount > 0 ? document.pageCount : nil
            let attributes = document.documentAttributes ?? [:]
            facts.pdfTitle = clean(attributes[AnyHashable(PDFDocumentAttribute.titleAttribute)])
            facts.pdfAuthor = clean(attributes[AnyHashable(PDFDocumentAttribute.authorAttribute)])
        } else if ["musicxml", "xml", "mxl"].contains(kind), data.count <= headerBytes {
            facts.musicXml = data.base64EncodedString()
        }
        return facts
    }

    /// A PDF's own text, trimmed; nil when empty.
    private static func clean(_ value: Any?) -> String? {
        guard let text = (value as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty else { return nil }
        return text
    }

    /// Reads a file in the folder for its facts, with its bytes for whoever adds it next.
    static func read(_ file: FolderFile, in folder: URL) async -> Result<(facts: FileFacts, data: Data), LinkProblem> {
        switch await LinkedFile.read(FolderAccess.file(file.path, in: folder)) {
        case .failure(let problem): .failure(problem)
        case .success(let data): .success((facts(data, name: file.name), data))
        }
    }
}
