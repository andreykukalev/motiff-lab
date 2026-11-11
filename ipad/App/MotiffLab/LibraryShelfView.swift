import SwiftUI

/// The library's shelf (version 0.2, ux-prototype/v2/screens/lib-1-list.png and lib-3-sessions.png): the tabs, then the
/// list of pieces with its columns, the composers with their pieces, or the sessions of current work as cards; and a way
/// back to everything when the search or a filter finds nothing.
struct LibraryShelfView: View {
    let page: LibraryPage
    let summaries: [URL: PieceSummary]
    let composers: [String]
    let tags: [String]
    @Binding var tab: LibraryTab
    @Binding var query: LibraryQuery
    @Binding var picked: ComposerRow?
    let text: UIText
    let open: (URL) -> Void
    let edit: (LibraryEntry) -> Void
    let delete: (URL) -> Void

    /// The sessions' columns.
    static let columns = [GridItem(.adaptive(minimum: 320), spacing: CGFloat.ds("s-6"), alignment: .top)]

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            LibraryTabs(tab: $tab, query: $query, composers: composers, tags: tags, text: text)
            if tab == .composers, let picked {
                HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-3")) {
                    BackChip(label: text["library.tabComposers"], text: text) { self.picked = nil }
                    Text(picked.name ?? text["library.noComposer"])
                        .dsType(.title)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                }
            }
            if page.isEmpty {
                empty
            } else if let rows = page.composers {
                ComposerList(rows: rows, text: text) { picked = $0 }
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
            }
        }
    }

    /// Nothing to show: when the search or a filter narrows the shelf, the way back to all of it; an empty tab says nothing.
    @ViewBuilder
    private var empty: some View {
        if query.narrows {
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
