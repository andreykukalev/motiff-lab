import SwiftUI

extension LibraryEntry {
    /// The line of a project file: the title the player set or the one the score names, else the file's name; the composer
    /// the player set or the score names; the player's tags and playlists; and whether there is work on it to come back to.
    static func of(_ entry: ProjectFiles.Entry, _ summary: PieceSummary?) -> LibraryEntry {
        let named = summary?.titleFrom == "player" || summary?.titleFrom == "file"
        var line = LibraryEntry(
            url: entry.url,
            title: named ? summary?.title ?? entry.title : entry.title,
            composer: summary?.composer,
            tags: summary?.tags ?? [],
            playlists: summary?.playlists ?? [],
            modified: entry.modified,
            inSession: summary.map { !$0.hasNotes || $0.latest != nil } ?? false
        )
        line.originalAway = OriginalAway.of(summary)
        if let summary, !summary.hasNotes, summary.document || AwayPdf.isAway(summary) {
            line.withoutNotes = true
            line.pages = max(1, summary.originals.first(where: \.isDocument)?.pages ?? summary.documentPages.count)
        }
        return line
    }
}

/// The library's tabs (ux-prototype/v2/screens/lib-1-list.png): All pieces, Composers, Recent, Playlists and Sessions, and
/// «Нет на этом iPad» while a piece saved online is not here (#479), the one shown underlined in the accent; on the lists of
/// pieces, the Composer, Tag and Playlist filters as menus at the right.
struct LibraryTabs: View {
    @Binding var tab: LibraryTab
    @Binding var query: LibraryQuery
    let composers: [String]
    let tags: [String]
    let playlists: [String]
    let text: UIText
    /// «Без нот» among the filters (version 0.5, experiment «Импорт папки»).
    var withoutNotes = false
    /// Whether a piece saved online is not on this iPad, for its tab.
    var online = false

    var body: some View {
        // The filters beside the tabs while they fit, in portrait under them.
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .bottom, spacing: CGFloat.ds("s-6")) {
                tabs
                Spacer(minLength: CGFloat.ds("s-4"))
                filters.padding(.bottom, CGFloat.ds("s-2"))
            }
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                HStack(alignment: .bottom, spacing: CGFloat.ds("s-6")) { tabs }
                filters
            }
        }
        .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
    }

    private var tabs: some View {
        ForEach(LibraryTab.tabs(online: online), id: \.self) { each in
            let on = each == tab
            Button {
                tab = each
            } label: {
                Text(label(each))
                    .font(DesignTokens.font(weight: on ? 600 : 400, size: 17, relativeTo: .body))
                    .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                    .lineLimit(1)
                    .fixedSize()
                    .padding(.vertical, CGFloat.ds("s-3"))
                    .overlay(alignment: .bottom) {
                        if on { Rectangle().fill(Color.ds("accent")).frame(height: 2) }
                    }
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(on ? .isSelected : [])
        }
    }

    /// The Composer, Tag and Playlist filters, on the lists of pieces only; «Без нот» on every tab of pieces, with the
    /// experiment on.
    @ViewBuilder
    private var filters: some View {
        let lists = tab == .all || tab == .recent
        let notes = withoutNotes && tab != .online
        if lists || notes {
            HStack(spacing: CGFloat.ds("s-3")) {
                if lists && !composers.isEmpty {
                    LibraryFilter(caption: text["library.byComposer"], all: text["library.anyComposer"], values: composers, picked: $query.composer)
                }
                if lists && !tags.isEmpty {
                    LibraryFilter(caption: text["library.byTag"], all: text["library.anyTag"], values: tags, picked: $query.tag)
                }
                if lists && !playlists.isEmpty {
                    LibraryFilter(caption: text["library.byPlaylist"], all: text["library.anyPlaylist"], values: playlists, picked: $query.playlist)
                }
                if notes {
                    Button(text["recognition.withoutNotes"]) { query.withoutNotes.toggle() }
                        .buttonStyle(DSChipStyle(on: query.withoutNotes))
                        .accessibilityAddTraits(query.withoutNotes ? .isSelected : [])
                }
            }
        }
    }

    private func label(_ tab: LibraryTab) -> String {
        LibraryTabs.label(tab, text)
    }

    static func label(_ tab: LibraryTab, _ text: UIText) -> String {
        switch tab {
        case .all: text["library.tabAll"]
        case .composers: text["library.tabComposers"]
        case .recent: text["library.tabRecent"]
        case .playlists: text["library.tabPlaylists"]
        case .sessions: text["library.tabSessions"]
        case .online: text["library.tabOnline"]
        }
    }
}

/// A filter as a menu (ds.css `.chip` with a chevron): its caption, or the value picked, in the accent's tint while one is.
struct LibraryFilter: View {
    let caption: String
    let all: String
    let values: [String]
    @Binding var picked: String?

    var body: some View {
        Menu {
            Button(all) { picked = nil }
            ForEach(values, id: \.self) { value in
                Button {
                    picked = value
                } label: {
                    if picked?.caseInsensitiveCompare(value) == .orderedSame {
                        Label(value, systemImage: "checkmark")
                    } else {
                        Text(value)
                    }
                }
            }
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(picked ?? caption)
                    .lineLimit(1)
                Icon(name: "chevd", size: CGFloat.ds("icon-s"))
            }
            .dsType(.label)
            .foregroundStyle(Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(minHeight: CGFloat.ds("h-control"))
            .background(picked == nil ? Color.ds("surface") : Color.ds("accent-tint"), in: Capsule())
            .overlay(Capsule().strokeBorder(picked == nil ? Color.ds("hairline-strong") : Color.ds("accent")))
            .contentShape(Capsule())
        }
        .accessibilityLabel(caption)
        .accessibilityValue(picked ?? all)
    }
}

/// The list of pieces (lib-1): Title, Composer, Playlists and Opened as columns, a heading ordering the list by its column
/// (Playlists does not), the arrow on the one in use; a line per piece, a tap opening it where it was left, a long press
/// offering its properties and Delete.
struct LibraryList: View {
    let entries: [LibraryEntry]
    @Binding var query: LibraryQuery
    let text: UIText
    let open: (LibraryEntry) -> Void
    let edit: (LibraryEntry) -> Void
    let delete: (LibraryEntry) -> Void
    /// The pieces picked to recognise and each piece's recognition (version 0.5).
    @Environment(\.libraryMarks) private var marks
    /// The line whose swiped actions are uncovered, one at a time (#479).
    @State private var swiped: URL?

    /// The width of the Opened column.
    private static let openedWidth: CGFloat = 160

    var body: some View {
        LazyVStack(alignment: .leading, spacing: 0) {
            HStack(spacing: CGFloat.ds("s-4")) {
                heading(text["library.fieldTitle"], .title)
                heading(text["library.fieldComposer"], .composer)
                Text(text["library.fieldPlaylists"]).overline()
                    .frame(maxWidth: .infinity, minHeight: CGFloat.ds("h-compact"), alignment: .leading)
                heading(text["library.colOpened"], .recent, trailing: true)
                    .frame(width: Self.openedWidth, alignment: .trailing)
            }
            .padding(.horizontal, CGFloat.ds("s-4"))
            .padding(.vertical, CGFloat.ds("s-3"))
            .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
            ForEach(entries) { entry in
                LibraryLine(entry: entry, text: text, openedWidth: Self.openedWidth, marks: marks, swiped: swipe(entry)) {
                    // A tap while another line shows its actions covers them, as in Messages.
                    if swiped == nil { open(entry) } else { swiped = nil }
                } edit: {
                    edit(entry)
                } delete: {
                    delete(entry)
                }
                .id(entry.id)
            }
        }
    }

    /// Whether a line's actions are uncovered: opening one covers the others'.
    private func swipe(_ entry: LibraryEntry) -> Binding<Bool> {
        Binding {
            swiped == entry.url
        } set: { on in
            if on { swiped = entry.url } else if swiped == entry.url { swiped = nil }
        }
    }

    /// A column's heading, in the overline's small capitals, which orders the list by its column.
    private func heading(_ name: String, _ column: LibrarySort, trailing: Bool = false) -> some View {
        let on = query.sort == column
        return Button {
            query = query.sorted(by: column)
        } label: {
            HStack(spacing: CGFloat.ds("s-1")) {
                Text(name).overline()
                if on { Text(query.ascending ? "↑" : "↓").overline() }
            }
            .frame(maxWidth: trailing ? nil : .infinity, alignment: trailing ? .trailing : .leading)
            .frame(minHeight: CGFloat.ds("h-compact"))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// One piece in the list: its title in the serif, its composer, its playlists, and when it was last opened. While the player
/// chooses pieces to recognise, a piece that can be picked has its tick, and its title and composer typed over in place
/// (version 0.5.1).
struct LibraryLine: View {
    let entry: LibraryEntry
    let text: UIText
    let openedWidth: CGFloat
    var marks = LibraryMarks()
    /// Whether its swiped actions are uncovered (#479).
    var swiped: Binding<Bool>?
    let open: () -> Void
    let edit: () -> Void
    let delete: () -> Void

    var body: some View {
        if let chosen = marks.chosen, entry.withoutNotes {
            choosing(chosen)
        } else {
            line
        }
    }

    /// The line while choosing: the tick a button of its own, the title and composer fields.
    private func choosing(_ chosen: Set<URL>) -> some View {
        HStack(spacing: CGFloat.ds("s-4")) {
            Button {
                marks.toggle(entry)
            } label: {
                CheckBox(on: chosen.contains(entry.url)).frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
            }
            .buttonStyle(.plain)
            .accessibilityLabel(entry.title)
            .accessibilityAddTraits(chosen.contains(entry.url) ? .isSelected : [])
            HStack(spacing: CGFloat.ds("s-2")) {
                LineField(value: entry.title, label: text["library.fieldTitle"], serif: true) { marks.rename(entry, "title", $0) }
                if let item = marks.status(entry.url) { RecognitionTag(item: item, text: text) }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            LineField(value: entry.composer ?? "", label: text["library.fieldComposer"], serif: false) { marks.rename(entry, "composer", $0) }
                .frame(maxWidth: .infinity, alignment: .leading)
            Text(entry.playlists.joined(separator: ", "))
                .dsType(.body)
                .foregroundStyle(Color.ds("ink-2"))
                .lineLimit(1)
                .frame(maxWidth: .infinity, alignment: .leading)
            Color.clear.frame(width: openedWidth, height: 1)
        }
        .padding(.horizontal, CGFloat.ds("s-4"))
        .frame(minHeight: CGFloat.ds("h-touch") + CGFloat.ds("s-4"))
        .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
    }

    private var line: some View {
        Button {
            if marks.chosen != nil { marks.toggle(entry) } else { open() }
        } label: {
            HStack(spacing: CGFloat.ds("s-4")) {
                if let chosen = marks.chosen {
                    CheckBox(on: chosen.contains(entry.url)).frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch")).opacity(entry.withoutNotes ? 1 : 0)
                }
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(entry.title)
                            .font(DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3))
                            .foregroundStyle(Color.ds("ink"))
                            .lineLimit(1)
                        if let item = marks.status(entry.url) { RecognitionTag(item: item, text: text) }
                    }
                    if let away = entry.originalAway {
                        OriginalPlaceLabel(label: away.text(text), badge: away.badge)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Text(entry.composer ?? "")
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(entry.playlists.joined(separator: ", "))
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(entry.modified.formatted(.relative(presentation: .named).locale(Locale(identifier: text.language))))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                    .frame(width: openedWidth, alignment: .trailing)
            }
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(minHeight: CGFloat.ds("h-touch") + CGFloat.ds("s-4"))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
        .modifier(PieceActions(text: text, edit: edit, delete: delete, swiped: marks.chosen == nil ? swiped : nil))
        .accessibilityElement(children: .combine)
        .accessibilityAction(named: text["library.properties"], edit)
    }
}

/// A title or composer typed over in place on a library line: what the piece says until the player types, kept when they
/// press Done or leave the field; an empty field gives the file's own back.
struct LineField: View {
    let value: String
    let label: String
    let serif: Bool
    let commit: (String) -> Void
    @State private var typed: String?
    @FocusState private var focused: Bool

    var body: some View {
        TextField(label, text: Binding { typed ?? value } set: { typed = $0 })
            .font(serif ? DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3) : DesignTokens.font(weight: 400, size: 15, relativeTo: .body))
            .foregroundStyle(serif ? Color.ds("ink") : Color.ds("ink-2"))
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .submitLabel(.done)
            .focused($focused)
            .onSubmit(keep)
            .onChange(of: focused) { _, on in if !on { keep() } }
            .accessibilityLabel(label)
    }

    private func keep() {
        guard let typed else { return }
        self.typed = nil
        if typed != value { commit(typed) }
    }
}

/// The index at the list's right edge (lib-1): a tap on a letter, or a finger run down them, brings the first title under it
/// into view.
struct LibraryIndex: View {
    let letters: [String]
    let text: UIText
    let jump: (String) -> Void
    @State private var last: String?

    var body: some View {
        VStack(spacing: 0) {
            ForEach(letters, id: \.self) { letter in
                Text(letter)
                    .font(DesignTokens.font(weight: 600, size: 11, relativeTo: .caption2))
                    .foregroundStyle(Color.ds("ink-3"))
                    .frame(width: CGFloat.ds("s-6"), height: 17)
            }
        }
        .padding(.vertical, CGFloat.ds("s-2"))
        .contentShape(Rectangle())
        .gesture(
            DragGesture(minimumDistance: 0)
                .onChanged { drag in
                    let row = Int((drag.location.y - CGFloat.ds("s-2")) / 17)
                    let letter = letters[max(0, min(letters.count - 1, row))]
                    if letter != last {
                        last = letter
                        jump(letter)
                    }
                }
                .onEnded { _ in last = nil }
        )
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(text["library.index"])
        .accessibilityAdjustableAction { direction in
            let at = last.flatMap { letters.firstIndex(of: $0) } ?? -1
            let next = direction == .increment ? at + 1 : at - 1
            guard letters.indices.contains(next) else { return }
            last = letters[next]
            jump(letters[next])
        }
    }
}

/// The Composers or Playlists tab: the library's composers or playlists from A, each with how many pieces it holds, and the
/// pieces without a composer last; a tap shows its pieces.
struct GroupList: View {
    let rows: [LibraryGroup]
    let text: UIText
    let pick: (LibraryGroup) -> Void

    var body: some View {
        LazyVStack(alignment: .leading, spacing: 0) {
            ForEach(rows) { row in
                Button {
                    pick(row)
                } label: {
                    HStack(spacing: CGFloat.ds("s-4")) {
                        Text(row.name ?? text["library.noComposer"])
                            .font(DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3))
                            .foregroundStyle(row.name == nil ? Color.ds("ink-2") : Color.ds("ink"))
                            .lineLimit(1)
                        Spacer(minLength: CGFloat.ds("s-4"))
                        Text(text.count("library.pieces", row.count))
                            .dsType(.label)
                            .foregroundStyle(Color.ds("ink-2"))
                        Icon(name: "fwd", size: CGFloat.ds("icon-s"))
                            .foregroundStyle(Color.ds("ink-3"))
                    }
                    .padding(.horizontal, CGFloat.ds("s-4"))
                    .frame(minHeight: CGFloat.ds("h-touch") + CGFloat.ds("s-4"))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
                .id(row.id)
            }
        }
    }
}

/// The library's search (lib-1, at the top beside Add): words found in the title, the composer and the tags, whatever their
/// case and accents. The library puts the keyboard away when the list scrolls or empty space is tapped (#479), through
/// `focus`.
struct LibrarySearchField: View {
    @Binding var search: String
    let text: UIText
    var focus: FocusState<Bool>.Binding

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Icon(name: "search")
                .foregroundStyle(Color.ds("ink-2"))
            TextField(text["library.searchHint"], text: $search)
                .focused(focus)
                .dsType(.body)
                .foregroundStyle(Color.ds(FieldLook.ink))
                .tint(Color.ds("accent"))
                .submitLabel(.search)
                .autocorrectionDisabled()
                .accessibilityLabel(text["library.search"])
            if !search.isEmpty {
                Button {
                    search = ""
                } label: {
                    Icon(name: "close", size: CGFloat.ds("icon-s"))
                        .foregroundStyle(Color.ds("ink-2"))
                        .frame(width: CGFloat.ds("h-control"), height: CGFloat.ds("h-control"))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(text["library.clearSearch"])
            }
        }
        .padding(.leading, CGFloat.ds("s-3"))
        .frame(height: CGFloat.ds("h-touch"))
        .frame(minWidth: 160, maxWidth: 360)
        .background(Color.ds(FieldLook.background), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
    }
}

/// What a long press on a piece offers, on a line of the list and on a session's card: its properties, and Delete, which
/// asks once more. A line of the list offers the same under a swipe to the left (`LineSwipe`, #479).
struct PieceActions: ViewModifier {
    let text: UIText
    let edit: () -> Void
    let delete: () -> Void
    /// On a line of the list: whether its swiped actions are uncovered; nil on a card, which has no swipe.
    var swiped: Binding<Bool>?
    @State private var confirming = false

    func body(content: Content) -> some View {
        Group {
            if let swiped {
                menu(content).modifier(LineSwipe(text: text, open: swiped, edit: edit) { confirming = true })
            } else {
                menu(content)
            }
        }
        .confirmationDialog(text["home.deleteForGood"], isPresented: $confirming, titleVisibility: .hidden) {
            Button(text["home.deleteForGood"], role: .destructive, action: delete)
            Button(text["home.keep"], role: .cancel) {}
        }
    }

    private func menu(_ content: Content) -> some View {
        content
            .contextMenu {
                Button(action: edit) {
                    Text(text["library.properties"])
                }
                Button(role: .destructive) {
                    confirming = true
                } label: {
                    Text(text["home.delete"])
                }
            }
    }
}
