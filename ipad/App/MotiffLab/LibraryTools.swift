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
        return line
    }
}

/// The library's tabs (ux-prototype/v2/screens/lib-1-list.png): All pieces, Composers, Recent, Playlists and Sessions, the
/// one shown underlined in the accent; on the lists of pieces, the Composer, Tag and Playlist filters as menus at the right.
struct LibraryTabs: View {
    @Binding var tab: LibraryTab
    @Binding var query: LibraryQuery
    let composers: [String]
    let tags: [String]
    let playlists: [String]
    let text: UIText

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
        ForEach(LibraryTab.allCases, id: \.self) { each in
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

    /// The Composer, Tag and Playlist filters, on the lists of pieces only.
    @ViewBuilder
    private var filters: some View {
        if tab == .all || tab == .recent {
            HStack(spacing: CGFloat.ds("s-3")) {
                if !composers.isEmpty {
                    LibraryFilter(caption: text["library.byComposer"], all: text["library.anyComposer"], values: composers, picked: $query.composer)
                }
                if !tags.isEmpty {
                    LibraryFilter(caption: text["library.byTag"], all: text["library.anyTag"], values: tags, picked: $query.tag)
                }
                if !playlists.isEmpty {
                    LibraryFilter(caption: text["library.byPlaylist"], all: text["library.anyPlaylist"], values: playlists, picked: $query.playlist)
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
                LibraryLine(entry: entry, text: text, openedWidth: Self.openedWidth) {
                    open(entry)
                } edit: {
                    edit(entry)
                } delete: {
                    delete(entry)
                }
                .id(entry.id)
            }
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

/// One piece in the list: its title in the serif, its composer, its playlists, and when it was last opened.
struct LibraryLine: View {
    let entry: LibraryEntry
    let text: UIText
    let openedWidth: CGFloat
    let open: () -> Void
    let edit: () -> Void
    let delete: () -> Void

    var body: some View {
        Button(action: open) {
            HStack(spacing: CGFloat.ds("s-4")) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(entry.title)
                        .font(DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3))
                        .foregroundStyle(Color.ds("ink"))
                        .lineLimit(1)
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
        .modifier(PieceActions(text: text, edit: edit, delete: delete))
        .accessibilityElement(children: .combine)
        .accessibilityAction(named: text["library.properties"], edit)
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
/// case and accents.
struct LibrarySearchField: View {
    @Binding var search: String
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Icon(name: "search")
                .foregroundStyle(Color.ds("ink-2"))
            TextField(text["library.searchHint"], text: $search)
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
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
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
    }
}

/// What a long press on a piece offers, on a line of the list and on a session's card: its properties, and Delete, which
/// asks once more.
struct PieceActions: ViewModifier {
    let text: UIText
    let edit: () -> Void
    let delete: () -> Void
    @State private var confirming = false

    func body(content: Content) -> some View {
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
            .confirmationDialog(text["home.deleteForGood"], isPresented: $confirming, titleVisibility: .hidden) {
                Button(text["home.deleteForGood"], role: .destructive, action: delete)
                Button(text["home.keep"], role: .cancel) {}
            }
    }
}
