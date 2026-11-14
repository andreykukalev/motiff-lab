import Foundation

/// How the list of pieces is ordered: by title (the default), by composer, or the one opened last first.
enum LibrarySort: String, CaseIterable, Sendable {
    case recent, title, composer
}

/// The library's tabs (design: ux-prototype/v2/screens/lib-1-list.png, lib-3-sessions.png), in Andrei's order: every
/// piece as a list, the composers with their pieces, the ones opened last, the playlists with their pieces, the
/// sessions of current work as cards, and the pieces saved online that are not on this iPad (version 0.5, #479).
enum LibraryTab: String, CaseIterable, Sendable {
    case all, composers, recent, playlists, sessions, online

    /// The tabs shown: «Нет на этом iPad» only while a piece saved online is not here.
    static func tabs(online: Bool) -> [LibraryTab] {
        allCases.filter { $0 != .online || online }
    }

    /// The tab to show: this one, or All pieces once nothing saved online is missing here.
    func shown(online: Bool) -> LibraryTab {
        self == .online && !online ? .all : self
    }
}

/// A line of the Composers or Playlists tab: a composer or a playlist and how many pieces it holds; no name for the pieces
/// without a composer.
struct LibraryGroup: Identifiable, Equatable, Sendable {
    let name: String?
    let count: Int

    var id: String { name ?? "" }

    /// The first letter of the name, folded and in capitals, for the index at the list's edge.
    var initial: String {
        name.flatMap { LibraryShelf.fold($0).first }.map { String($0).uppercased() } ?? ""
    }
}

/// What the library is narrowed to: words to find in a title, composer, tag or playlist, and one composer, one tag and one
/// playlist; and the order of the list, by the column chosen, either way.
struct LibraryQuery: Equatable, Sendable {
    var search = ""
    var sort = LibrarySort.title
    var ascending = true
    var composer: String?
    var tag: String?
    var playlist: String?
    /// «Без нот» (version 0.5): only the pieces that are only a PDF.
    var withoutNotes = false

    var narrows: Bool {
        !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || composer != nil || tag != nil || playlist != nil || withoutNotes
    }

    /// Search and filters cleared; the order stays.
    var cleared: LibraryQuery {
        LibraryQuery(sort: sort, ascending: ascending)
    }

    /// Ordered by this column: the same column again turns the order round, another one starts its natural way (titles
    /// and composers from A, the last opened first).
    func sorted(by column: LibrarySort) -> LibraryQuery {
        var next = self
        next.ascending = column == sort ? !ascending : true
        next.sort = column
        return next
    }
}

/// One piece on the shelf: its project file and the title, composer, tags and playlists its line shows.
struct LibraryEntry: Identifiable, Equatable, Sendable {
    let url: URL
    let title: String
    let composer: String?
    let tags: [String]
    /// The playlists it is on, by name, in the player's order.
    let playlists: [String]
    /// When the project file was last saved: the order of "recently opened".
    let modified: Date
    /// Whether there is work on it to come back to: a session (a resume point), or a PDF read as it is.
    let inSession: Bool
    /// The title, composer, tags and playlists in one folded text, for search.
    let folded: String
    /// Its first PDF, photos or recording that is not on this iPad, said on its line (version 0.3); nil when all are here.
    var originalAway: OriginalAway?
    /// A piece that is only a PDF, whose notes can be recognised (version 0.5, «Распознать ноты» for several), and its pages.
    var withoutNotes = false
    var pages = 0
    /// The sources it really has, for the small icons on its line (v0.5.6, #533 section 10).
    var sources: [LibrarySource] = []

    var id: URL { url }

    init(url: URL, title: String, composer: String?, tags: [String], playlists: [String] = [], modified: Date, inSession: Bool = false) {
        self.url = url
        self.title = title
        self.composer = composer
        self.tags = tags
        self.playlists = playlists
        self.modified = modified
        self.inSession = inSession
        folded = LibraryShelf.fold(([title, composer ?? ""] + tags + playlists).joined(separator: "\n"))
    }

    /// The first letter of the title, folded and in capitals, for the index at the list's edge ("É" files under "E").
    var initial: String {
        LibraryShelf.fold(title).first.map { String($0).uppercased() } ?? ""
    }
}

/// What one tab of the library shows: its pieces, searched, filtered and ordered as asked, or on the Composers and Playlists
/// tabs their lines until one is picked; and whether the index at the list's edge leads into it (a list ordered from A).
struct LibraryPage: Equatable, Sendable {
    let pieces: [LibraryEntry]
    /// The composers' or playlists' lines, on their tab before one is picked; nil otherwise.
    let groups: [LibraryGroup]?
    let indexed: Bool

    static func of(_ entries: [LibraryEntry], _ query: LibraryQuery, tab: LibraryTab, picked: LibraryGroup?, locale: Locale) -> LibraryPage {
        let fromA = query.sort == .title && query.ascending
        switch tab {
        case .all, .recent:
            return LibraryPage(pieces: LibraryShelf.apply(LibraryShelf.pieces(of: tab, entries), query, locale: locale), groups: nil, indexed: fromA)
        case .composers, .playlists:
            // The composer or playlist is picked here, not by a filter; search still narrows.
            let searched = LibraryShelf.apply(entries, LibraryQuery(search: query.search, sort: query.sort, ascending: query.ascending, withoutNotes: query.withoutNotes), locale: locale)
            let composers = tab == .composers
            if let picked {
                let pieces = composers ? LibraryShelf.pieces(by: picked.name, searched) : LibraryShelf.pieces(on: picked.name ?? "", searched)
                return LibraryPage(pieces: pieces, groups: nil, indexed: fromA)
            }
            let groups = composers ? LibraryShelf.composerRows(searched, locale: locale) : LibraryShelf.playlistRows(searched, locale: locale)
            return LibraryPage(pieces: [], groups: groups, indexed: true)
        case .sessions:
            let sessions = LibraryShelf.pieces(of: .sessions, entries)
            return LibraryPage(pieces: LibraryShelf.apply(sessions, LibraryQuery(search: query.search, sort: .recent), locale: locale), groups: nil, indexed: false)
        case .online:
            // The projects saved online are not pieces here: the tab lists them itself (`LibraryShelf.online`).
            return LibraryPage(pieces: [], groups: nil, indexed: false)
        }
    }

    var isEmpty: Bool { groups?.isEmpty ?? pieces.isEmpty }

    /// Each line's initial, in the order shown, for the index.
    var initials: [String] { groups?.map(\.initial) ?? pieces.map(\.initial) }

    /// The id of the line at a place in the list, to bring it into view.
    func id(at index: Int) -> AnyHashable {
        if let groups { return AnyHashable(groups[index].id) }
        return AnyHashable(pieces[index].id)
    }
}

/// The library's search, filters and order (version 0.2), the web app's rules (src/MotiffLab.Web/Projects/LibraryShelf.cs):
/// pure functions over the cards, so the shelf narrows as the player types without reading a project file again. Search
/// ignores letter case and accents ("fur elise" finds "Für Elise", "ежи" finds "Ёжик", and "и" finds "й").
enum LibraryShelf {
    /// How many pieces Recent lists, the last opened first.
    static let recentCount = 20

    /// The latin capitals of the index at the list's edge; Cyrillic ones follow when a title starts with one.
    static let latin = (UInt8(ascii: "A")...UInt8(ascii: "Z")).map { String(UnicodeScalar($0)) }
    static let cyrillic = "АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩЭЮЯ".map(String.init)

    /// The pieces of a tab: all of them, the last opened, or those with a session, before search, filters and order.
    static func pieces(of tab: LibraryTab, _ entries: [LibraryEntry]) -> [LibraryEntry] {
        switch tab {
        case .all, .composers, .playlists: entries
        case .recent: Array(entries.sorted { $0.modified > $1.modified }.prefix(recentCount))
        case .sessions: entries.filter(\.inSession).sorted { $0.modified > $1.modified }
        case .online: []
        }
    }

    /// The projects saved online that are not here, narrowed by the search as the pieces are, from A.
    static func online(_ projects: [OnlineProject], search: String, locale: Locale) -> [OnlineProject] {
        let words = fold(search).split(whereSeparator: \.isWhitespace).map(String.init)
        return projects
            .filter { project in words.allSatisfy { fold(project.title).contains($0) } }
            .sorted { compare($0.title, $1.title, locale: locale) == .orderedAscending }
    }

    /// The title a piece's properties keep: what the player typed, or nil (the file's own) when it is empty or just what
    /// the file says, so a title the field starts with is never fixed as the player's (#479).
    static func playersTitle(_ typed: String, file: String) -> String? {
        guard let typed = clean(typed) else { return nil }
        return typed == clean(file) ? nil : typed
    }

    /// The composers of the pieces given, in the language's order, each once whatever its letter case and with how many
    /// pieces; the pieces without a composer under one line at the end.
    static func composerRows(_ entries: [LibraryEntry], locale: Locale) -> [LibraryGroup] {
        let named = composers(entries, locale: locale).map { name in
            LibraryGroup(name: name, count: entries.filter { same($0.composer, name) }.count)
        }
        let without = entries.filter { $0.composer == nil }.count
        return named + (without > 0 ? [LibraryGroup(name: nil, count: without)] : [])
    }

    /// The playlists of the pieces given, in the language's order, each once whatever its letter case and with how many
    /// pieces are on it.
    static func playlistRows(_ entries: [LibraryEntry], locale: Locale) -> [LibraryGroup] {
        playlists(entries, locale: locale).map { name in LibraryGroup(name: name, count: pieces(on: name, entries).count) }
    }

    /// The pieces of one line of the Composers tab: the composer's, whatever the letter case, or those without one.
    static func pieces(by composer: String?, _ entries: [LibraryEntry]) -> [LibraryEntry] {
        entries.filter { entry in composer.map { same(entry.composer, $0) } ?? (entry.composer == nil) }
    }

    /// The pieces on a playlist, whatever the letter case.
    static func pieces(on playlist: String, _ entries: [LibraryEntry]) -> [LibraryEntry] {
        entries.filter { entry in entry.playlists.contains { same($0, playlist) } }
    }

    static func apply(_ entries: [LibraryEntry], _ query: LibraryQuery, locale: Locale) -> [LibraryEntry] {
        let ordered = order(filter(entries, query), query.sort, locale: locale)
        return query.ascending ? ordered : ordered.reversed()
    }

    private static func filter(_ entries: [LibraryEntry], _ query: LibraryQuery) -> [LibraryEntry] {
        let words = fold(query.search).split(whereSeparator: \.isWhitespace).map(String.init)
        return entries.filter { entry in
            words.allSatisfy { entry.folded.contains($0) }
                && (query.composer.map { same(entry.composer, $0) } ?? true)
                && (query.tag.map { tag in entry.tags.contains { same($0, tag) } } ?? true)
                && (query.playlist.map { playlist in entry.playlists.contains { same($0, playlist) } } ?? true)
                && (!query.withoutNotes || entry.withoutNotes)
        }
    }

    private static func order(_ found: [LibraryEntry], _ sort: LibrarySort, locale: Locale) -> [LibraryEntry] {
        switch sort {
        case .recent:
            return found.sorted { $0.modified > $1.modified }
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

    /// The playlists on the shelf, each once whatever its letter case, in the language's order.
    static func playlists(_ entries: [LibraryEntry], locale: Locale) -> [String] {
        distinct(entries.flatMap(\.playlists), locale: locale)
    }

    /// The query with a composer, tag or playlist that no piece has any more taken off, written as the shelf now writes it.
    static func kept(_ query: LibraryQuery, composers: [String], tags: [String], playlists: [String] = []) -> LibraryQuery {
        var kept = query
        kept.composer = query.composer.flatMap { picked in composers.first { same($0, picked) } }
        kept.tag = query.tag.flatMap { picked in tags.first { same($0, picked) } }
        kept.playlist = query.playlist.flatMap { picked in playlists.first { same($0, picked) } }
        return kept
    }

    /// The letters of the index at the list's edge: A to Z, and the Cyrillic capitals when a line starts with one.
    static func indexLetters(_ initials: [String]) -> [String] {
        latin + (initials.contains { cyrillic.contains($0) } ? cyrillic : [])
    }

    /// Where a letter of the index leads in a list ordered from A, given each line's initial: the first line filed under
    /// it, else the first after it, else the last line; nil for an empty list.
    static func target(_ letter: String, in initials: [String], locale: Locale) -> Int? {
        let found = initials.firstIndex { initial in
            initial == letter || compare(initial, letter, locale: locale) == .orderedDescending
        }
        return found ?? (initials.isEmpty ? nil : initials.count - 1)
    }

    /// The composers in the library that hold what is typed, to pick instead of typing on; none once one is typed whole.
    static func composerSuggestions(_ typed: String, _ composers: [String]) -> [String] {
        guard let wanted = clean(typed).map(fold), !composers.contains(where: { fold($0) == wanted }) else { return [] }
        return Array(composers.filter { fold($0).contains(wanted) }.prefix(5))
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

    /// The piece's tags (or playlists) with one more at the end, cleaned; a name already there in any letter case is not
    /// added again, and one the library has in another letter case is taken as the library writes it.
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

/// A kind of source a piece really has, in the order the library shows them (v0.5.6, #533 section 10; music-core's
/// `LibrarySources`, which the web reads): PDF, notes, recording, then the others. A new kind is one more case here, one
/// more line in `of` and one more icon in `SourceIcons`.
enum LibrarySource: CaseIterable, Equatable, Sendable {
    case pdf, notes, recording, photos, midi

    /// The piece's sources from its summary (no change to the project file): only what is there, each once, in order. A
    /// recording heard into notes keeps no sound, so it shows its notes only; notes read from a PDF piece the library hides
    /// behind them show that PDF too.
    static func of(_ summary: PieceSummary?) -> [LibrarySource] {
        guard let summary else { return [] }
        var has: Set<LibrarySource> = []
        if summary.readFrom != nil { has.insert(.pdf) }
        if summary.hasNotes { has.insert(.notes) }
        for original in summary.originals {
            switch original.kind {
            case "pdf": has.insert(.pdf)
            case "images": has.insert(.photos)
            case "audio": has.insert(.recording)
            // The file the notes were imported from is the notes; another MIDI file is a version of its own.
            case "midi" where original.id != OriginalEntry.sourceFileId: has.insert(.midi)
            default: break
            }
        }
        return allCases.filter(has.contains)
    }

    /// Its icon in the kit.
    var icon: String {
        switch self {
        case .pdf: "pages"
        case .notes: "notes"
        case .recording: "mic"
        case .photos: "camera"
        case .midi: "roll"
        }
    }

    /// Its name, for the tooltip and VoiceOver.
    func label(_ text: UIText) -> String {
        switch self {
        case .pdf: text["library.source.pdf"]
        case .notes: text["library.source.notes"]
        case .recording: text["library.source.recording"]
        case .photos: text["library.source.photos"]
        case .midi: text["library.source.midi"]
        }
    }
}
