import SwiftUI

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
                        SourceIcons(sources: entry.sources, text: text)
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

/// What a piece holds, at a glance (v0.5.6, #533 section 10): a small neutral icon for each source it really has, in the
/// fixed order of `LibrarySource`, named for VoiceOver and in a tooltip; nothing for a piece with none.
struct SourceIcons: View {
    let sources: [LibrarySource]
    let text: UIText

    var body: some View {
        if !sources.isEmpty {
            HStack(spacing: CGFloat.ds("s-1")) {
                ForEach(sources, id: \.self) { source in
                    Icon(name: source.icon, size: CGFloat.ds("icon-s"))
                        .help(source.label(text))
                }
            }
            .foregroundStyle(Color.ds("ink-2"))
            .fixedSize()
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(Self.label(sources, text))
        }
    }

    /// «Источники: PDF, ноты, запись».
    static func label(_ sources: [LibrarySource], _ text: UIText) -> String {
        text.format("library.sources", sources.map { $0.label(text) }.joined(separator: ", "))
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
