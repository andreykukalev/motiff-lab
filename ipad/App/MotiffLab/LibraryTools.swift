import SwiftUI

extension LibraryEntry {
    /// The card of a project file: the title the player set or the one the score names, else the file's name; the
    /// composer the player set or the score names; and the player's tags.
    static func of(_ entry: ProjectFiles.Entry, _ summary: PieceSummary?) -> LibraryEntry {
        let named = summary?.titleFrom == "player" || summary?.titleFrom == "file"
        return LibraryEntry(
            url: entry.url,
            title: named ? summary?.title ?? entry.title : entry.title,
            composer: summary?.composer,
            tags: summary?.tags ?? [],
            modified: entry.modified
        )
    }
}

/// The library's search, order, view and filters (version 0.2), for a shelf of a couple of hundred pieces, as on the web:
/// search finds words in the title, the composer and the tags whatever their case and accents; the order and the view
/// are two segmented choices; a composer and a tag are picked as chips, one of each, each row scrolling sideways; the
/// count says how many pieces show.
struct LibraryTools: View {
    @Binding var query: LibraryQuery
    let layout: LibraryLayout
    let composers: [String]
    let tags: [String]
    let shown: Int
    let total: Int
    let text: UIText
    let choose: (LibraryLayout) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            searchField
            HStack(spacing: CGFloat.ds("s-4")) {
                SegmentedChoice(options: LibrarySort.allCases.map { ($0, sortLabel($0)) }, chosen: query.sort, label: text["library.sort"]) {
                    query.sort = $0
                }
                Spacer(minLength: CGFloat.ds("s-2"))
                SegmentedChoice(options: [(LibraryLayout.cards, text["library.viewCards"]), (LibraryLayout.list, text["library.viewList"])], chosen: layout, label: text["library.view"], choose: choose)
            }
            if !composers.isEmpty {
                chips(text["library.byComposer"], composers, picked: query.composer, icon: nil) { query.composer = $0 }
            }
            if !tags.isEmpty {
                chips(text["library.byTag"], tags, picked: query.tag, icon: "tag") { query.tag = $0 }
            }
            Text(query.narrows ? text.format("library.shownOf", String(shown), String(total)) : text.count("library.pieces", total))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
        }
    }

    private var searchField: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Icon(name: "search")
                .foregroundStyle(Color.ds("ink-2"))
            TextField(text["library.searchHint"], text: $query.search)
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
                .submitLabel(.search)
                .autocorrectionDisabled()
                .accessibilityLabel(text["library.search"])
            if !query.search.isEmpty {
                Button {
                    query.search = ""
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
        .frame(maxWidth: 560)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
    }

    /// A row of chips under its caption, scrolling sideways: a tap picks one, a tap on the one picked lets it go.
    private func chips(_ caption: String, _ values: [String], picked: String?, icon: String?, pick: @escaping (String?) -> Void) -> some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(caption)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                ForEach(values, id: \.self) { value in
                    let on = picked?.caseInsensitiveCompare(value) == .orderedSame
                    Button {
                        pick(on ? nil : value)
                    } label: {
                        HStack(spacing: CGFloat.ds("s-1")) {
                            if let icon { Icon(name: icon, size: CGFloat.ds("icon-s")) }
                            Text(value)
                        }
                    }
                    .buttonStyle(DSChipStyle(on: on))
                    .frame(minHeight: CGFloat.ds("h-control"))
                    .contentShape(Rectangle())
                    .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(caption)
    }

    private func sortLabel(_ sort: LibrarySort) -> String {
        switch sort {
        case .recent: text["library.sortRecent"]
        case .title: text["library.sortTitle"]
        case .composer: text["library.sortComposer"]
        }
    }
}

/// A piece's ⋯ (version 0.2), the same on a card and on a line of the list: its title, composer and tags, and Delete, which
/// asks once more.
struct PieceMenu: View {
    let title: String
    let text: UIText
    let edit: () -> Void
    let delete: () -> Void
    @State private var confirming = false

    var body: some View {
        Menu {
            Button(action: edit) {
                Text(text["library.editDetails"])
            }
            Button(role: .destructive) {
                confirming = true
            } label: {
                Text(text["home.delete"])
            }
        } label: {
            Icon(name: "more", size: CGFloat.ds("icon-l"))
                .foregroundStyle(Color.ds("ink-2"))
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                .contentShape(Rectangle())
        }
        .accessibilityLabel(text.format("library.moreFor", title))
        .confirmationDialog(text["home.deleteForGood"], isPresented: $confirming, titleVisibility: .hidden) {
            Button(text["home.deleteForGood"], role: .destructive, action: delete)
            Button(text["home.keep"], role: .cancel) {}
        }
    }
}

/// One line of the compact list (version 0.2), for a shelf of a couple of hundred pieces: the title, the composer and
/// what kind of piece, the player's tags and when it was last opened. A tap opens the piece where it was left, as a
/// card's Resume does.
struct LibraryRow: View {
    let entry: LibraryEntry
    let summary: PieceSummary?
    let text: UIText
    let open: () -> Void
    let edit: () -> Void
    let delete: () -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(action: open) {
                HStack(spacing: CGFloat.ds("s-4")) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(entry.title)
                            .font(DesignTokens.font(display: true, weight: 500, size: 17, relativeTo: .body))
                            .foregroundStyle(Color.ds("ink"))
                            .lineLimit(1)
                        if !line.isEmpty {
                            Text(line)
                                .dsType(.label)
                                .foregroundStyle(Color.ds("ink-2"))
                                .lineLimit(1)
                        }
                    }
                    Spacer(minLength: CGFloat.ds("s-2"))
                    PieceTagsLine(tags: entry.tags, most: 3)
                    Text(entry.modified.formatted(.relative(presentation: .named).locale(Locale(identifier: text.language))))
                        .dsType(.label)
                        .foregroundStyle(Color.ds("ink-2"))
                        .lineLimit(1)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(text.format(summary?.latest == nil ? "library.openLabel" : "library.resumeLabel", entry.title))
            PieceMenu(title: entry.title, text: text, edit: edit, delete: delete)
        }
        .padding(.leading, CGFloat.ds("s-4"))
        .padding(.trailing, CGFloat.ds("s-1"))
        .padding(.vertical, CGFloat.ds("s-2"))
    }

    /// "Beethoven · Scanned": the composer, and what kind of piece when it is not a file the player opened.
    private var line: String {
        [entry.composer, PieceCard.kind(summary, text)].compactMap { $0 }.joined(separator: " · ")
    }
}

/// The player's own tags on a card or a line (ds.css `.tag.own`), the first few and how many more.
struct PieceTagsLine: View {
    let tags: [String]
    var most = Int.max

    var body: some View {
        if !tags.isEmpty {
            HStack(spacing: CGFloat.ds("s-1")) {
                ForEach(tags.prefix(most), id: \.self) { tag in
                    Tag(text: tag, kind: .own)
                }
                if tags.count > most {
                    Tag(text: "+\(tags.count - most)", kind: .own)
                }
            }
        }
    }
}
