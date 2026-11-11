import SwiftUI

/// A category of bookmarks (facade `bookmarks`): Practise, Performance, Structure, Teacher or the player's own, named in
/// the piece's language, with its ink.
struct BookmarkCategory: Identifiable, Equatable, Sendable {
    var id: String
    var name: String
    /// "graphite", "carmine", "forest", "ochre" or "amber".
    var ink: String
    var builtIn: Bool

    /// The ink's colour from the design kit.
    var color: Color { ink == "amber" ? .ds("amber") : .ds("ink-\(ink)") }
}

/// A passage the player has named and filed (0018 section 5): its title, its category, where it is, and how many ideas
/// were kept about passages inside it.
struct Bookmark: Identifiable, Equatable, Sendable {
    var id: String
    var title: String
    var category: String
    var onset: Double
    var end: Double
    var firstBar: Int
    var lastBar: Int
    var ideas: Int

    init?(answer: [String: Any]) {
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        let span = answer["span"] as? [String: Any]
        let bars = answer["bars"] as? [String: Any]
        guard let id = answer["id"] as? String, let onset = number(span?["onset"]), let end = number(span?["end"]) else { return nil }
        self.id = id
        title = answer["title"] as? String ?? ""
        category = answer["category"] as? String ?? "practise"
        self.onset = onset
        self.end = end
        firstBar = Int(number(bars?["first"]) ?? 0)
        lastBar = Int(number(bars?["last"]) ?? 0)
        ideas = Int(number(answer["ideas"]) ?? 0)
    }

    /// "bars 9–12", or "bar 9".
    func bars(_ text: UIText) -> String {
        firstBar == lastBar
            ? text.format("bars.one", String(firstBar))
            : text.format("bars.range", String(firstBar), String(lastBar))
    }
}

/// The piece's bookmarks and their categories, as music-core answers every bookmark call: the bookmarks in the player's
/// own order, the categories in theirs.
struct BookmarkShelf: Equatable, Sendable {
    static let structure = "structure"

    var categories: [BookmarkCategory]
    var bookmarks: [Bookmark]

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil,
              let categories = object["categories"] as? [[String: Any]],
              let bookmarks = object["bookmarks"] as? [[String: Any]]
        else { return nil }
        self.categories = categories.compactMap { category in
            guard let id = category["id"] as? String else { return nil }
            return BookmarkCategory(id: id, name: category["name"] as? String ?? id, ink: category["ink"] as? String ?? "graphite", builtIn: category["builtIn"] as? Bool ?? true)
        }
        self.bookmarks = bookmarks.compactMap(Bookmark.init(answer:))
    }

    /// The bookmarks by where they are in the piece, as the web app's score order has them.
    var inScoreOrder: [Bookmark] {
        bookmarks.sorted { $0.onset != $1.onset ? $0.onset < $1.onset : $0.end < $1.end }
    }

    func category(of bookmark: Bookmark) -> BookmarkCategory? {
        categories.first { $0.id == bookmark.category } ?? categories.first
    }
}

/// The bookmarks of the open piece and the Contents drawer (0018 section 5, the web app's PlaybackLab.Bookmarks.cs): the
/// card that makes or changes one, the drawer's search, order and folded categories, the pages and the sections of the form it shows.
/// Every change goes to music-core, which keeps the bookmarks with the piece (project file version 12 on) and answers
/// with all of them, so the drawer and the brackets on the score always show what music-core holds.
@MainActor
final class ContentsController: ObservableObject {
    /// The search and the order show once there are more bookmarks than this (testing item 21).
    static let toolsFrom = 3

    /// The bookmark being made from the menu (`id` nil) or changed in the drawer.
    struct Draft: Equatable {
        var id: String?
        var title: String
        var category: String
    }

    /// A category with the entries the drawer lists under it.
    struct CategoryEntries: Identifiable {
        var category: BookmarkCategory
        var entries: [Bookmark]
        var id: String { category.id }
    }

    @Published private(set) var shelf: BookmarkShelf?
    @Published var draft: Draft?
    /// The sections of the form music-core finds in the piece, for the Contents' structure: the same sections, letters and
    /// bars as Piece DNA, read from the one structure of the piece (#347), whatever level the brackets show.
    @Published private(set) var sections: [DnaMap.Section] = []
    @Published var search = ""
    /// The player's own order rather than the score's.
    @Published var myOrder = false
    @Published var folded: Set<String> = []
    @Published private(set) var pages: [ScoreController.PageOutline] = []
    @Published private(set) var problem: String?
    /// The bookmarks changed: the score draws their brackets again.
    var changed: (@MainActor (BookmarkShelf?) -> Void)?

    private var piece: Int32?
    /// The category the last bookmark went into: the next one starts there.
    private var lastCategory = "practise"

    func load(piece: Int32) async {
        self.piece = piece
        await call("bookmarks", [:])
    }

    /// Bookmark in the menu: the card over the selection, its title pre-filled.
    func start(title: String) {
        draft = Draft(id: nil, title: title, category: lastCategory)
    }

    /// The pen beside an entry: the card in its place, to rename it, move it to another category or delete it.
    func edit(_ bookmark: Bookmark) {
        draft = Draft(id: bookmark.id, title: bookmark.title, category: bookmark.category)
    }

    func cancel() {
        draft = nil
    }

    /// Done on the card: a new bookmark over the selection, or the one being changed; a blank title keeps the bars.
    func save(over selection: ScoreSelection?, defaultTitle: String) async {
        guard let draft else { return }
        self.draft = nil
        let title = draft.title.trimmingCharacters(in: .whitespacesAndNewlines)
        if let id = draft.id {
            var request: [String: Any] = ["id": id, "category": draft.category]
            if !title.isEmpty { request["title"] = title }
            await call("bookmark.change", request)
        } else if let selection {
            lastCategory = draft.category
            let span: [String: Any] = ["onset": Int(selection.onset), "end": Int(selection.end)]
            await call("bookmark.add", ["span": span, "title": title.isEmpty ? defaultTitle : title, "category": draft.category, "now": Self.now()])
        }
    }

    func delete(_ id: String) async {
        draft = nil
        await call("bookmark.remove", ["id": id])
    }

    /// Add Structure bookmarks: one for each section of the form music-core found, named and placed by it, as Piece DNA adds them.
    func addStructure() async {
        await call("bookmark.form", ["now": Self.now()])
    }

    /// Add Structure bookmarks from Piece DNA (testing item 22): one per section of the form, "Section A′, bars 17–32";
    /// answers how many were added.
    func addForm() async -> Int {
        let before = shelf?.bookmarks.count ?? 0
        await call("bookmark.form", ["now": Self.now()])
        return (shelf?.bookmarks.count ?? 0) - before
    }

    /// Whether the drawer offers the detected form as Structure bookmarks: the piece has sections and none is filed yet.
    func canAddStructure(sections: Int) -> Bool {
        sections > 0 && !(shelf?.bookmarks.contains { $0.category == BookmarkShelf.structure } ?? false)
    }

    /// The bookmarks the drawer lists, by category in the categories' order: each in score order or the player's own,
    /// matching the search in its title or its category's name.
    var groups: [CategoryEntries] {
        guard let shelf else { return [] }
        let ordered = myOrder ? shelf.bookmarks : shelf.inScoreOrder
        let words = search.trimmingCharacters(in: .whitespaces)
        return shelf.categories.compactMap { category in
            let entries = ordered.filter { bookmark in
                bookmark.category == category.id
                    && (words.isEmpty || bookmark.title.localizedCaseInsensitiveContains(words) || category.name.localizedCaseInsensitiveContains(words))
            }
            return entries.isEmpty ? nil : CategoryEntries(category: category, entries: entries)
        }
    }

    func toggleFold(_ id: String) {
        if folded.contains(id) { folded.remove(id) } else { folded.insert(id) }
    }

    /// The Contents' pages: each page's picture, drawn by the score's page at about twice the thumbnail's width.
    func loadPages(from score: ScoreController) async {
        pages = await score.outline(width: 180)
    }

    /// The detected sections of the form, read from the map of the piece without changing the level the score shows.
    func loadSections() async {
        guard let piece else { return }
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "dna") }
        sections = DnaMap(answer: answer)?.form ?? []
    }

    private func call(_ name: String, _ request: [String: Any]) async {
        guard let piece else { return }
        let body = (try? JSONSerialization.data(withJSONObject: request)).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece, name, body) }
        guard let read = BookmarkShelf(answer: answer) else {
            problem = facadeObject(answer)?["message"] as? String
            return
        }
        problem = nil
        shelf = read
        changed?(read)
    }

    private static func now() -> String {
        ISO8601DateFormatter().string(from: Date())
    }
}

/// The bookmark card (0018 section 5, the web app's `.bookmark-card`): the title, pre-filled with the bars, the category
/// chips in their inks, and Done; over the selection for a new bookmark, in the drawer for one being changed.
struct BookmarkCard: View {
    @ObservedObject var contents: ContentsController
    let text: UIText
    let save: () -> Void
    @FocusState private var titleFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                Text(text["bookmark.title"]).overline()
                TextField(text["bookmark.title"], text: Binding(get: { contents.draft?.title ?? "" }, set: { contents.draft?.title = $0 }))
                    .focused($titleFocused)
                    .submitLabel(.done)
                    .onSubmit(save)
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink"))
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .frame(height: CGFloat.ds("h-control"))
                    .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
            }
            FlowLayout(spacing: CGFloat.ds("s-2")) {
                ForEach(contents.shelf?.categories ?? []) { category in
                    let chosen = contents.draft?.category == category.id
                    Button {
                        contents.draft?.category = category.id
                    } label: {
                        HStack(spacing: 6) {
                            Circle().fill(category.color).frame(width: 10, height: 10)
                            Text(category.name)
                        }
                    }
                    .buttonStyle(DSChipStyle(on: chosen))
                    .accessibilityAddTraits(chosen ? .isSelected : [])
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["bookmark.category"])
            HStack(spacing: CGFloat.ds("s-2")) {
                if let id = contents.draft?.id {
                    Button {
                        Task { await contents.delete(id) }
                    } label: {
                        HStack(spacing: 6) {
                            Icon(name: "trash", size: CGFloat.ds("icon-s"))
                            Text(text["bookmark.delete"])
                        }
                    }
                    .buttonStyle(DSButtonStyle(kind: .danger, compact: true))
                }
                Spacer(minLength: 0)
                Button(text["bookmark.cancel"]) { contents.cancel() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button(text["bookmark.done"], action: save)
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
        }
        .onAppear { titleFocused = true }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(contents.draft?.id == nil ? text["bookmark.new"] : text["bookmark.edit"])
    }
}

/// The Contents drawer (0018 section 5, design/kit/ds.css `.sheet.left`, `.entry`): over the left of the page in
/// landscape and from the bottom in portrait. Contents lists the player's bookmarks by category, to search, sort, fold, go
/// to, change or delete; Notes lists the margin notes, the highlights and the bars with ink; Pages shows the pages with the
/// phrases that start on each and a tick for each bookmark.
/// The Contents (testing item 21): one sheet read top to bottom, the pages, the detected structure, the bookmarks by
/// category and the notes and marks, as the web app's Contents; the chips at the top scroll to a section rather than
/// replace the sheet.
struct ContentsDrawer: View {
    @ObservedObject var contents: ContentsController
    @ObservedObject var score: ScoreController
    let text: UIText
    let close: () -> Void

    private enum Part: Hashable {
        case pages, structure, bookmarks, notes
    }

    var body: some View {
        ScrollViewReader { reader in
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Text(text["bar.contents"]).overline()
                    Spacer(minLength: 0)
                    Button(action: close) {
                        Icon(name: "close").frame(width: 32, height: 32)
                    }
                    .buttonStyle(BarActionStyle())
                    .accessibilityLabel(text["drawer.close"])
                }
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        chip(text["contents.tabPages"], .pages, reader)
                        if !contents.sections.isEmpty { chip(text["contents.structure"], .structure, reader) }
                        chip(text["contents.bookmarks"], .bookmarks, reader)
                        chip(text["contents.tabNotes"], .notes, reader)
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["contents.sections"])
                ScrollView {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                        part(contents.pages.isEmpty ? text["contents.tabPages"] : text.format("contents.category", text["contents.tabPages"], String(contents.pages.count))) {
                            PagesStrip(contents: contents, score: score, text: text)
                        }
                        .id(Part.pages)
                        if !contents.sections.isEmpty {
                            part(text["contents.structureFound"]) {
                                StructureChips(contents: contents, score: score, text: text)
                            }
                            .id(Part.structure)
                        }
                        part(bookmarksTitle) {
                            ContentsList(contents: contents, score: score, text: text, close: close)
                        }
                        .id(Part.bookmarks)
                        part(text["contents.notesHead"]) {
                            NotesList(score: score, text: text)
                        }
                        .id(Part.notes)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.bottom, CGFloat.ds("s-4"))
                }
                .scrollDismissesKeyboard(.interactively)
            }
        }
        .padding(.top, CGFloat.ds("s-4"))
        .padding(.bottom, CGFloat.ds("s-3"))
        .padding(.horizontal, CGFloat.ds("s-5"))
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["bar.contents"])
        .task {
            await contents.loadPages(from: score)
            await contents.loadSections()
        }
    }

    private var bookmarksTitle: String {
        let count = contents.shelf?.bookmarks.count ?? 0
        return count > 0 ? text.format("contents.category", text["contents.bookmarks"], String(count)) : text["contents.bookmarks"]
    }

    private func chip(_ label: String, _ part: Part, _ reader: ScrollViewProxy) -> some View {
        Button(label) {
            withAnimation(.easeOut(duration: 0.2)) { reader.scrollTo(part, anchor: .top) }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
    }

    private func part<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(title).overline().accessibilityAddTraits(.isHeader)
            content()
        }
    }
}

/// The form as music-core detects it, lettered as Piece DNA letters it: a chip per section selects its bars; dashed and grey until added as Structure
/// bookmarks, with the button that adds them.
struct StructureChips: View {
    @ObservedObject var contents: ContentsController
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        let found = contents.canAddStructure(sections: contents.sections.count)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            FlowRow(spacing: CGFloat.ds("s-2")) {
                ForEach(contents.sections) { section in
                    Button {
                        Task { await score.select(section.selectableOnset, section.selectableEnd) }
                    } label: {
                        Text(text.format("contents.sentence", section.label, section.firstBar == section.lastBar ? String(section.firstBar) : "\(section.firstBar)–\(section.lastBar)"))
                            .dsType(.label)
                            .foregroundStyle(found ? Color.ds("ink-2") : Color.ds("ink"))
                            .padding(.horizontal, CGFloat.ds("s-3"))
                            .frame(height: 32)
                            .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")).strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: found ? [3, 2] : [])))
                    }
                    .buttonStyle(.plain)
                }
            }
            if found {
                Button(text["contents.addStructure"]) { Task { await contents.addStructure() } }
                    .buttonStyle(DSButtonStyle(kind: .quiet))
                    .accessibilityHint(text["contents.addStructureHint"])
            }
        }
    }
}

/// Chips that wrap onto the next line when the row is full.
struct FlowRow: Layout {
    var spacing: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(width: proposal.width ?? .infinity, subviews)
        return CGSize(width: proposal.width ?? rows.width, height: rows.height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let rows = arrange(width: bounds.width, subviews)
        for (index, point) in rows.points.enumerated() {
            subviews[index].place(at: CGPoint(x: bounds.minX + point.x, y: bounds.minY + point.y), proposal: .unspecified)
        }
    }

    private func arrange(width: CGFloat, _ subviews: Subviews) -> (points: [CGPoint], width: CGFloat, height: CGFloat) {
        var points: [CGPoint] = []
        var x: CGFloat = 0, y: CGFloat = 0, line: CGFloat = 0, widest: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0, x + size.width > width {
                x = 0
                y += line + spacing
                line = 0
            }
            points.append(CGPoint(x: x, y: y))
            x += size.width + spacing
            line = max(line, size.height)
            widest = max(widest, x - spacing)
        }
        return (points, widest, y + line)
    }
}

/// The Contents' bookmarks: search and the order once there are more than three, then the bookmarks by category.
struct ContentsList: View {
    @ObservedObject var contents: ContentsController
    @ObservedObject var score: ScoreController
    let text: UIText
    let close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            if (contents.shelf?.bookmarks.count ?? 0) > ContentsController.toolsFrom {
                TextField(text["contents.search"], text: $contents.search)
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink"))
                    .submitLabel(.search)
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .frame(height: CGFloat.ds("h-control"))
                    .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
                    .accessibilityLabel(text["contents.search"])
                SegmentedChoice(options: [(false, text["contents.scoreOrder"]), (true, text["contents.myOrder"])], chosen: contents.myOrder, label: text["contents.order"]) {
                    contents.myOrder = $0
                }
            }
            if (contents.shelf?.bookmarks ?? []).isEmpty {
                Text(text["contents.empty"])
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
            } else if contents.groups.isEmpty {
                Text(text["contents.noMatch"])
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
            } else {
                ForEach(contents.groups) { group in
                    section(group)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// A category's heading, which folds it, and its entries.
    @ViewBuilder private func section(_ group: ContentsController.CategoryEntries) -> some View {
        let folded = contents.folded.contains(group.id)
        Button {
            contents.toggleFold(group.id)
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Circle().fill(group.category.color).frame(width: 10, height: 10)
                Text(text.format("contents.category", group.category.name, String(group.entries.count)))
                    .dsType(.overline)
                    .textCase(.uppercase)
                    .tracking(0.66)
                    .foregroundStyle(folded ? Color.ds("ink-3") : Color.ds("ink-2"))
                Spacer(minLength: 0)
            }
            .frame(height: 32)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(.top, CGFloat.ds("s-2"))
        .accessibilityHint(text.format("contents.fold", group.category.name))
        .accessibilityAddTraits(.isHeader)
        if !folded {
            VStack(alignment: .leading, spacing: 2) {
                ForEach(group.entries) { bookmark in
                    if contents.draft?.id == bookmark.id {
                        BookmarkCard(contents: contents, text: text) {
                            Task { await contents.save(over: nil, defaultTitle: bookmark.title) }
                        }
                        .padding(CGFloat.ds("s-3"))
                        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                    } else {
                        entry(bookmark)
                    }
                }
            }
        }
    }

    /// An entry (ds.css `.entry`): a tap goes there and selects it, a second tap focuses on it; the pen changes it.
    private func entry(_ bookmark: Bookmark) -> some View {
        let on = score.isShown(bookmark.id)
        return HStack(spacing: CGFloat.ds("s-1")) {
            Button {
                if score.goToBookmark(bookmark.id) { close() }
            } label: {
                VStack(alignment: .leading, spacing: 2) {
                    Text(bookmark.title)
                        .dsType(.body)
                        .foregroundStyle(Color.ds("ink"))
                        .multilineTextAlignment(.leading)
                    Text(details(bookmark))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                .padding(.horizontal, CGFloat.ds("s-3"))
                .padding(.vertical, CGFloat.ds("s-2"))
                .frame(maxWidth: .infinity, minHeight: CGFloat.ds("h-touch"), alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityHint(text["contents.entryHint"])
            .accessibilityAddTraits(on ? .isSelected : [])
            Button {
                contents.edit(bookmark)
            } label: {
                Icon(name: "pen").frame(width: 36, height: 36)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text.format("bookmark.editOne", bookmark.title))
        }
        .background(on ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay {
            if on { RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("accent")) }
        }
    }

    /// "bars 9–12 · 2 ideas".
    private func details(_ bookmark: Bookmark) -> String {
        let bars = bookmark.bars(text)
        return bookmark.ideas > 0 ? "\(bars) · \(text.count("contents.ideas", bookmark.ideas))" : bars
    }
}

/// The Contents' pages: a strip of small pictures on paper, the one being read outlined in the accent, with the phrases
/// that start on each and a tick in its category's ink for each bookmark that starts there; a tap brings the page into view.
struct PagesStrip: View {
    @ObservedObject var contents: ContentsController
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        if contents.pages.isEmpty {
            Text(text["contents.pagesStrip"])
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
        } else {
            ScrollView(.horizontal, showsIndicators: false) {
                LazyHStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                    ForEach(contents.pages) { page in
                        thumbnail(page).frame(width: 84)
                    }
                }
                .padding(.vertical, CGFloat.ds("s-1"))
            }
        }
    }

    private func thumbnail(_ page: ScoreController.PageOutline) -> some View {
        let reading = score.pages.first <= page.page && page.page <= score.pages.last
        return Button {
            score.showPage(page.page)
        } label: {
            VStack(spacing: CGFloat.ds("s-1")) {
                ZStack(alignment: .topTrailing) {
                    Color.ds("paper")
                    if let image = page.image {
                        Image(uiImage: image).resizable().scaledToFit()
                    }
                    HStack(spacing: 3) {
                        ForEach(bookmarks(on: page)) { bookmark in
                            RoundedRectangle(cornerRadius: 1)
                                .fill(contents.shelf?.category(of: bookmark)?.color ?? Color.ds("ink-graphite"))
                                .frame(width: 4, height: 12)
                        }
                    }
                    .padding(4)
                }
                .aspectRatio(210.0 / 297.0, contentMode: .fit)
                .clipShape(RoundedRectangle(cornerRadius: 3))
                .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(Color.ds("hairline")))
                .overlay {
                    if reading { RoundedRectangle(cornerRadius: 4).strokeBorder(Color.ds("accent"), lineWidth: 2).padding(-3) }
                }
                .dsShadow("shadow-paper")
                Text(caption(page))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
            }
            .padding(CGFloat.ds("s-1"))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text.format("contents.pageLabel", String(page.page)))
        .accessibilityAddTraits(reading ? .isSelected : [])
    }

    private func bookmarks(on page: ScoreController.PageOutline) -> [Bookmark] {
        (contents.shelf?.bookmarks ?? []).filter { page.start <= $0.onset && $0.onset < page.end }
    }

    /// "p. 2 · phrases 3–5", the phrases named while the brackets show phrases.
    private func caption(_ page: ScoreController.PageOutline) -> String {
        let number = text.format("contents.page", String(page.page))
        guard let phrases = score.phrases, phrases.level == "phrase" else { return number }
        let starting = phrases.units.filter { page.start <= $0.onset && $0.onset < page.end }.map(\.number)
        guard let first = starting.first, let last = starting.last else { return number }
        let named = first == last ? text.format("contents.pagePhrase", String(first)) : text.format("contents.pagePhrases", String(first), String(last))
        return "\(number) · \(named)"
    }
}

/// A choice of two or three (ds.css `.seg`): the chosen one on the accent's strong tint.
struct SegmentedChoice<Value: Hashable>: View {
    let options: [(Value, String)]
    let chosen: Value
    let label: String
    let choose: (Value) -> Void

    var body: some View {
        HStack(spacing: 0) {
            ForEach(options.indices, id: \.self) { index in
                let value = options[index].0
                let name = options[index].1
                let on = value == chosen
                Button {
                    choose(value)
                } label: {
                    Text(name)
                        .font(DesignTokens.font(weight: on ? 600 : 500, size: 13, relativeTo: .callout))
                        .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                        .lineLimit(1)
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .frame(maxHeight: .infinity)
                        .background(on ? Color.ds("accent-tint-strong") : .clear, in: Capsule())
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .padding(2)
        .frame(height: CGFloat.ds("h-compact"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(label)
    }
}
