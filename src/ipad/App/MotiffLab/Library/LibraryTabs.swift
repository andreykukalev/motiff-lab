import SwiftUI

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
                SteadyWord(text: label(each), on: on, size: 17, offWeight: 400, relativeTo: .body)
                    .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
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

    /// The Composer, Tag and Playlist filters, on the lists of pieces only; «Без нот» on every tab of pieces.
    @ViewBuilder
    private var filters: some View {
        let lists = tab == .all || tab == .recent
        let notes = tab != .online
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
