import Foundation

/// One piece of a PDF or photo collection as the player marks it in «Пьесы в этом файле» (v0.4, experiment «Пьесы в
/// файле»; ideas bank «Несколько произведений внутри одного файла или записи»): its title and its pages, both included.
struct PartRow: Identifiable, Equatable, Sendable {
    var id = UUID()
    var title: String
    var first: Int
    var last: Int
}

/// Why the pieces cannot be made yet: none listed, a piece's pages outside the file, or a piece that starts before the one
/// above it ends. Pieces are numbered from 1, as the list shows them.
enum PartRowsProblem: Equatable, Sendable {
    case empty
    case outside(piece: Int)
    case overlap(piece: Int, previous: Int)

    func text(_ text: UIText, pages: Int) -> String {
        switch self {
        case .empty: text["parts.pdf.none"]
        case .outside(let piece): text.format("parts.pdf.outside", String(piece), String(pages))
        case .overlap(let piece, let previous): text.format("parts.pdf.overlap", String(piece), String(previous))
        }
    }
}

/// The pieces the player lists in a file of `pageCount` pages, in the list's order: each a row «название · с–по», first
/// filled from the page bookmarks. Pages are the file's own, from 1, as the bar counts them (not the printed numbers).
struct PartRows: Equatable, Sendable {
    var rows: [PartRow]
    let pageCount: Int

    init(rows: [PartRow] = [], pageCount: Int) {
        self.rows = rows
        self.pageCount = max(0, pageCount)
    }

    /// A row for each bookmarked page, in page order: from that page to the page before the next bookmark, the last to the
    /// end of the file; titled by the bookmark's name.
    static func fromBookmarks(_ bookmarks: [PageBookmarkEntry], pageCount: Int) -> PartRows {
        var firsts: [(page: Int, name: String)] = []
        // In page order, the first listed of two bookmarks on one page first.
        let ordered = bookmarks.enumerated().sorted { ($0.element.page, $0.offset) < ($1.element.page, $1.offset) }.map(\.element)
        for bookmark in ordered where (1...max(1, pageCount)).contains(bookmark.page) {
            guard firsts.last?.page != bookmark.page else { continue }
            firsts.append((bookmark.page, (bookmark.name ?? "").trimmingCharacters(in: .whitespacesAndNewlines)))
        }
        let rows = firsts.enumerated().map { index, start in
            PartRow(title: start.name, first: start.page, last: index + 1 < firsts.count ? firsts[index + 1].page - 1 : pageCount)
        }
        return PartRows(rows: rows, pageCount: pageCount)
    }

    /// The first problem, top to bottom, or nil when every piece can be made.
    var problem: PartRowsProblem? {
        guard !rows.isEmpty else { return .empty }
        for (index, row) in rows.enumerated() {
            if row.first < 1 || row.last > pageCount || row.last < row.first { return .outside(piece: index + 1) }
            if index > 0, row.first <= rows[index - 1].last { return .overlap(piece: index + 1, previous: index) }
        }
        return nil
    }

    /// Adds a piece starting at the page, in its place by page: the piece before it, if it ran past, now ends just before
    /// it, and the new one runs to where that one ended, or to the next piece or the end. False when a piece starts there.
    @discardableResult
    mutating func add(at page: Int, title: String = "") -> Bool {
        guard pageCount > 0 else { return false }
        let start = min(max(1, page), pageCount)
        guard !rows.contains(where: { $0.first == start }) else { return false }
        let index = rows.firstIndex { $0.first > start } ?? rows.count
        var last = index < rows.count ? rows[index].first - 1 : pageCount
        if index > 0, rows[index - 1].last >= start {
            last = min(last, rows[index - 1].last)
            rows[index - 1].last = start - 1
        }
        rows.insert(PartRow(title: title, first: start, last: max(start, last)), at: index)
        return true
    }

    /// Where «Добавить пьесу» starts a piece: the page under the bar, else the page after the last piece; nil when both
    /// already start one or the file has no page left.
    func suggestedStart(under page: Int) -> Int? {
        guard pageCount > 0 else { return nil }
        let here = min(max(1, page), pageCount)
        if !rows.contains(where: { $0.first == here }) { return here }
        let after = (rows.map(\.last).max() ?? 0) + 1
        return after <= pageCount && !rows.contains(where: { $0.first == after }) ? after : nil
    }

    mutating func remove(_ id: UUID) {
        rows.removeAll { $0.id == id }
    }

    /// Moves a piece's first or last page by `step`, kept within the file.
    mutating func step(_ id: UUID, first: Bool, by step: Int) {
        guard let index = rows.firstIndex(where: { $0.id == id }), pageCount > 0 else { return }
        if first {
            rows[index].first = min(max(1, rows[index].first + step), pageCount)
        } else {
            rows[index].last = min(max(1, rows[index].last + step), pageCount)
        }
    }
}

/// `parts.split` for a PDF or photo collection (facade 0020, within contract 12): one piece per call, each with its own
/// pages copied as `pdf`, so a songbook of photos never travels through the core whole more than once at a time.
enum PartsSplit {
    /// The piece's title: as typed, else "<collection> · Piece 3", as music-core would name it.
    static func title(of row: PartRow, number: Int, collection: String, text: UIText) -> String {
        let typed = row.title.trimmingCharacters(in: .whitespacesAndNewlines)
        return typed.isEmpty ? "\(collection) · \(text.format("parts.piece", String(number)))" : String(typed.prefix(200))
    }

    /// The playlist the pieces join: as typed, else the collection's title.
    static func playlist(_ typed: String, collection: String) -> String {
        let trimmed = typed.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? collection : String(trimmed.prefix(80))
    }

    /// The request for one piece: the collection's project file, the piece's title and pages with its own PDF, the playlist,
    /// this iPad as the device that holds the collection's original, the file's pages as PDFKit counts them (music-core
    /// counts a PDF with compressed objects as one page), the time and the language.
    static func request(file: String, row: PartRow, number: Int, pdf: Data, pages: Int, playlist: String, collection: String, now: Date, text: UIText) -> [String: Any] {
        [
            "file": file,
            "parts": [[
                "title": title(of: row, number: number, collection: collection, text: text),
                "pages": ["first": row.first, "last": row.last],
                "pdf": pdf.base64EncodedString(),
            ] as [String: Any]],
            "playlist": playlist,
            "device": "ipad",
            "pages": pages,
            "now": ISO8601DateFormatter().string(from: now),
            "language": text.language,
        ]
    }

    /// One piece made: its project's id, title and file to save, and the pages of the collection it took.
    struct Made: Equatable, Sendable {
        var id: String
        var title: String
        var file: String
        var first: Int
        var last: Int
    }

    /// The piece music-core made, or its words on why not.
    static func made(_ answer: String) -> Result<Made, PartsSplitFailure> {
        guard let object = facadeObject(answer) else { return .failure(PartsSplitFailure(message: nil)) }
        if object["error"] != nil { return .failure(PartsSplitFailure(message: object["message"] as? String)) }
        guard let part = (object["parts"] as? [[String: Any]])?.first,
              let file = part["file"] as? String,
              let id = (part["project"] as? [String: Any])?["id"] as? String else { return .failure(PartsSplitFailure(message: nil)) }
        let pages = part["pages"] as? [String: Any]
        return .success(Made(
            id: id,
            title: part["title"] as? String ?? "",
            file: file,
            first: (pages?["first"] as? NSNumber)?.intValue ?? 0,
            last: (pages?["last"] as? NSNumber)?.intValue ?? 0
        ))
    }

    /// «Разделено на 19 пьес · плейлист «Islands»».
    static func summary(count: Int, playlist: String, text: UIText) -> String {
        text.format("parts.done", text.count("parts.pieces", count), playlist)
    }

    /// «с. 6–11», or «с. 6» for one page.
    static func pages(first: Int, last: Int, text: UIText) -> String {
        first == last ? text.format("contents.page", String(first)) : text.format("parts.pdf.pages", String(first), String(last))
    }
}

struct PartsSplitFailure: Error, Equatable {
    let message: String?
}

/// One piece made from a collection, as this iPad keeps it: its project, its title and the collection's pages it took.
struct PartLink: Codable, Equatable, Sendable {
    var id: String
    var title: String
    var first: Int
    var last: Int
}

/// A collection's split: its title, the playlist the pieces joined and the pieces, in order.
struct SplitLink: Codable, Equatable, Sendable {
    var title: String
    var playlist: String
    var parts: [PartLink]
}

/// Where a piece came from: the collection it was split from, by project id, and its place there.
struct PartOrigin: Equatable, Sendable {
    var collection: String
    var title: String
    var playlist: String
    var part: PartLink
}

/// The pieces this iPad split from its collections, by the collection's project id, so the collection says «Разделено на
/// N пьес» and a piece where it came from (the web keeps the same in local storage, `piece-parts.ts`). The project file
/// says nothing of it until the format step adds `partOf`; until then it is kept in Application Support/piece-parts.json,
/// shared by the environments (project ids are unique). Nothing here leaves the iPad.
enum PieceSplits {
    static var url: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("piece-parts.json")
    }

    static func load(from url: URL = url) -> [String: SplitLink] {
        guard let data = try? Data(contentsOf: url) else { return [:] }
        return (try? JSONDecoder().decode([String: SplitLink].self, from: data)) ?? [:]
    }

    /// Keeps the collection's split in place of any kept before; a split with no pieces forgets it.
    static func keep(_ link: SplitLink, collection: String, in url: URL = url) {
        var splits = load(from: url)
        splits[collection] = link.parts.isEmpty ? nil : link
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(splits) { try? data.write(to: url, options: .atomic) }
    }

    static func origin(of part: String, in splits: [String: SplitLink]) -> PartOrigin? {
        for (collection, link) in splits.sorted(by: { $0.key < $1.key }) {
            if let found = link.parts.first(where: { $0.id == part }) {
                return PartOrigin(collection: collection, title: link.title, playlist: link.playlist, part: found)
            }
        }
        return nil
    }
}
