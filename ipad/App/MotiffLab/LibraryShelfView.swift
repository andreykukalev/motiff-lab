import SwiftUI

/// The library's shelf (version 0.2, ux-prototype/v2/screens/lib-1-list.png and lib-3-sessions.png): the tabs, then the
/// list of pieces with its columns, the composers or the playlists with their pieces, the sessions of current work as
/// cards, or the pieces saved online that are not here (#479); and a way back to everything when the search or a filter
/// finds nothing.
struct LibraryShelfView: View {
    let page: LibraryPage
    let summaries: [URL: PieceSummary]
    let composers: [String]
    let tags: [String]
    let playlists: [String]
    @Binding var tab: LibraryTab
    @Binding var query: LibraryQuery
    @Binding var picked: LibraryGroup?
    let text: UIText
    /// Beside a playlist's name: «Держать на этом iPad» (version 0.5, experiment «Импорт папки»).
    var playlistAccessory: ((String) -> AnyView)?
    /// The pieces picked to recognise and each piece's recognition, and «Без нот» among the filters (version 0.5).
    var marks: LibraryMarks?
    /// The pieces saved online that are not on this iPad, for their tab; nil or none hides it.
    var online: OnlineTab?
    let open: (URL) -> Void
    let edit: (LibraryEntry) -> Void
    let delete: (URL) -> Void

    /// The sessions' columns.
    static let columns = [GridItem(.adaptive(minimum: 320), spacing: CGFloat.ds("s-6"), alignment: .top)]

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            LibraryTabs(tab: $tab, query: $query, composers: composers, tags: tags, playlists: playlists, text: text, withoutNotes: marks != nil, online: !(online?.projects.isEmpty ?? true))
            if tab == .composers || tab == .playlists, let picked {
                HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-3")) {
                    BackChip(label: LibraryTabs.label(tab, text), text: text) { self.picked = nil }
                    Text(picked.name ?? text["library.noComposer"])
                        .dsType(.title)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                    if tab == .playlists, let name = picked.name, let playlistAccessory {
                        Spacer(minLength: CGFloat.ds("s-4"))
                        playlistAccessory(name)
                    }
                }
            }
            if tab == .online {
                let found = LibraryShelf.online(online?.projects ?? [], search: query.search, locale: Locale(identifier: text.language))
                if found.isEmpty {
                    empty
                } else if let online {
                    OnlineList(projects: found, text: text, get: online.get)
                }
            } else if page.isEmpty {
                empty
            } else if let rows = page.groups {
                GroupList(rows: rows, text: text) { picked = $0 }
            } else if tab == .sessions {
                LazyVGrid(columns: Self.columns, alignment: .leading, spacing: CGFloat.ds("s-6")) {
                    ForEach(page.pieces) { entry in
                        PieceCard(entry: entry, summary: summaries[entry.url], text: text) {
                            open(entry.url)
                        } edit: {
                            edit(entry)
                        } delete: {
                            delete(entry.url)
                        }
                    }
                }
            } else {
                LibraryList(entries: page.pieces, query: $query, text: text) { entry in
                    open(entry.url)
                } edit: { entry in
                    edit(entry)
                } delete: { entry in
                    delete(entry.url)
                }
                .environment(\.libraryMarks, marks ?? LibraryMarks())
            }
        }
    }

    /// Nothing to show: when the search or a filter narrows the shelf, the way back to all of it; with no playlists yet, how
    /// a piece goes on one; another empty tab says nothing.
    @ViewBuilder
    private var empty: some View {
        if tab == .playlists && !query.narrows {
            Text(text["library.noPlaylists"])
                .dsType(.body)
                .foregroundStyle(Color.ds("ink-2"))
                .padding(.top, CGFloat.ds("s-4"))
        } else if query.narrows {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                Text(text["library.noneMatch"])
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink-2"))
                Button(text["library.clearFilters"]) { query = query.cleared }
                    .buttonStyle(DSButtonStyle(kind: .secondary))
            }
            .padding(.top, CGFloat.ds("s-4"))
        }
    }
}
