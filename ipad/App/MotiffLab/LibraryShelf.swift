import Foundation

/// How the library orders its pieces: the one opened last first (the default), by title, or by composer.
enum LibrarySort: String, CaseIterable, Sendable {
    case recent, title, composer
}

/// The shelf as cards, or as a compact list of one line a piece.
enum LibraryLayout: String, CaseIterable, Sendable {
    case cards, list
}

/// What the library is narrowed to: words to find in a title, composer or tag, and one composer and one tag.
struct LibraryQuery: Equatable, Sendable {
    var search = ""
    var sort = LibrarySort.recent
    var composer: String?
    var tag: String?

    var narrows: Bool {
        !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || composer != nil || tag != nil
    }

    /// Search and filters cleared; the order stays.
    var cleared: LibraryQuery {
        LibraryQuery(sort: sort)
    }
}

/// One piece on the shelf: its project file and the title, composer and tags its card shows.
struct LibraryEntry: Identifiable, Equatable, Sendable {
    let url: URL
    let title: String
    let composer: String?
    let tags: [String]
    /// When the project file was last saved: the order of "recently opened".
    let modified: Date
    /// The title, composer and tags in one folded text, for search.
    let folded: String

    var id: URL { url }

    init(url: URL, title: String, composer: String?, tags: [String], modified: Date) {
        self.url = url
        self.title = title
        self.composer = composer
        self.tags = tags
        self.modified = modified
        folded = LibraryShelf.fold(([title, composer ?? ""] + tags).joined(separator: "\n"))
    }
}

/// The library's search, filters and order (version 0.2), the web app's rules (src/MotiffLab.Web/Projects/LibraryShelf.cs):
/// pure functions over the cards, so the shelf narrows as the player types without reading a project file again. Search
/// ignores letter case and accents ("fur elise" finds "Für Elise", "ежи" finds "Ёжик", and "и" finds "й").
enum LibraryShelf {
    /// A shelf longer than this opens as a list until the player picks a view.
    static let listFrom = 12

    static func apply(_ entries: [LibraryEntry], _ query: LibraryQuery, locale: Locale) -> [LibraryEntry] {
        let words = fold(query.search).split(whereSeparator: \.isWhitespace).map(String.init)
        let found = entries.filter { entry in
            words.allSatisfy { entry.folded.contains($0) }
                && (query.composer.map { same(entry.composer, $0) } ?? true)
                && (query.tag.map { tag in entry.tags.contains { same($0, tag) } } ?? true)
        }
        switch query.sort {
        case .recent:
            return found
        case .title:
            return found.sorted { before($0, $1, locale: locale) }
        case .composer:
            return found.sorted { first, second in
                switch (first.composer, second.composer) {
                case let (one?, other?) where compare(one, other, locale: locale) != .orderedSame:
                    return compare(one, other, locale: locale) == .orderedAscending
                case (nil, _?):
                    return false
                case (_?, nil):
                    return true
                default:
                    return before(first, second, locale: locale)
                }
            }
        }
    }

    /// The composers on the shelf, each once whatever its letter case, in the language's order.
    static func composers(_ entries: [LibraryEntry], locale: Locale) -> [String] {
        distinct(entries.compactMap(\.composer), locale: locale)
    }

    /// The tags on the shelf, each once whatever its letter case, in the language's order.
    static func tags(_ entries: [LibraryEntry], locale: Locale) -> [String] {
        distinct(entries.flatMap(\.tags), locale: locale)
    }

    /// The query with a composer or tag that no piece has any more taken off, written as the shelf now writes it.
    static func kept(_ query: LibraryQuery, composers: [String], tags: [String]) -> LibraryQuery {
        var kept = query
        kept.composer = query.composer.flatMap { picked in composers.first { same($0, picked) } }
        kept.tag = query.tag.flatMap { picked in tags.first { same($0, picked) } }
        return kept
    }

    /// Cards or the list: the player's pick, else a list for a shelf longer than `listFrom`.
    static func layout(chosen: LibraryLayout?, count: Int) -> LibraryLayout {
        chosen ?? (count > listFrom ? .list : .cards)
    }

    /// Lower case without accents: "Für Élise" is "fur elise", "Ёлка" is "елка", "й" is "и".
    static func fold(_ text: String) -> String {
        var scalars = String.UnicodeScalarView()
        for scalar in text.decomposedStringWithCanonicalMapping.unicodeScalars
        where scalar.properties.generalCategory != .nonspacingMark {
            scalars.append(scalar)
        }
        return String(scalars).lowercased()
    }

    /// One line of text, as music-core keeps it (PieceMetadata.Clean): runs of white space as one space, trimmed; nil when
    /// nothing is left.
    static func clean(_ value: String?) -> String? {
        guard let value else { return nil }
        let cleaned = value.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        return cleaned.isEmpty ? nil : cleaned
    }

    /// The piece's tags with one more at the end, cleaned; a tag already there in any letter case is not added again, and
    /// one the library has in another letter case is taken as the library writes it.
    static func adding(_ typed: String, to tags: [String], library: [String] = []) -> [String] {
        guard let clean = clean(typed) else { return tags }
        let tag = library.first { same($0, clean) } ?? clean
        return tags.contains { same($0, tag) } ? tags : tags + [tag]
    }

    /// The library's tags not on the piece yet, narrowed to those holding what is typed.
    static func suggestions(_ library: [String], excluding tags: [String], typed: String) -> [String] {
        let wanted = clean(typed).map(fold)
        return library.filter { tag in
            !tags.contains { same($0, tag) } && (wanted.map { fold(tag).contains($0) } ?? true)
        }
    }

    private static func same(_ one: String?, _ other: String) -> Bool {
        one?.caseInsensitiveCompare(other) == .orderedSame
    }

    private static func compare(_ one: String, _ other: String, locale: Locale) -> ComparisonResult {
        one.compare(other, options: [.caseInsensitive, .diacriticInsensitive], range: nil, locale: locale)
    }

    private static func before(_ one: LibraryEntry, _ other: LibraryEntry, locale: Locale) -> Bool {
        let order = compare(one.title, other.title, locale: locale)
        return order == .orderedSame ? one.url.path < other.url.path : order == .orderedAscending
    }

    private static func distinct(_ texts: [String], locale: Locale) -> [String] {
        var seen = Set<String>()
        let unique = texts.filter { seen.insert($0.lowercased()).inserted }
        return unique.sorted { compare($0, $1, locale: locale) == .orderedAscending }
    }
}
