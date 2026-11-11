import SwiftUI

/// A piece's properties (version 0.2, Andrei's approved ux-prototype/v2/screens/lib-4-props.png): its title, its composer,
/// with the composers already in the library offered while typing, and its tags as chips, each taken off by its ✕ and a
/// new one added with «+ tag» (or picked from the library's). What is typed is the player's; a field left empty shows what
/// the file says again, named in its placeholder. Done keeps it: music-core writes it into the project file
/// (`project.metadata`); a swipe down leaves it as it was.
struct PiecePropertiesSheet: View {
    let entry: LibraryEntry
    let composers: [String]
    let libraryTags: [String]
    let text: UIText
    let saved: () async -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var title: String
    @State private var composer: String
    @State private var tags: [String]
    @State private var newTag = ""
    @State private var addingTag = false
    /// What the file says, shown as each field's placeholder: read again from the file once the sheet is open.
    @State private var fileTitle: String
    @State private var fileComposer: String?
    @State private var saving = false
    @State private var problem: String?
    @FocusState private var focus: Field?

    private enum Field: Hashable { case title, composer, tag }

    init(entry: LibraryEntry, summary: PieceSummary?, composers: [String], libraryTags: [String], text: UIText, saved: @escaping () async -> Void) {
        self.entry = entry
        self.composers = composers
        self.libraryTags = libraryTags
        self.text = text
        self.saved = saved
        _title = State(initialValue: summary?.playersTitle ?? "")
        _composer = State(initialValue: summary?.playersComposer ?? "")
        _tags = State(initialValue: summary?.tags ?? [])
        let named = summary?.titleFrom == "file" ? summary?.title : nil
        _fileTitle = State(initialValue: named ?? ProjectFiles.title(of: entry.url.lastPathComponent))
        _fileComposer = State(initialValue: summary?.composerFrom == "file" ? summary?.composer : nil)
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                header
                field(text["library.fieldTitle"]) {
                    TextField(fileTitle, text: $title)
                        .focused($focus, equals: .title)
                        .modifier(FieldLook(on: focus == .title))
                }
                field(text["library.fieldComposer"]) {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                        TextField(fileComposer ?? text["library.composerNone"], text: $composer)
                            .focused($focus, equals: .composer)
                            .autocorrectionDisabled()
                            .modifier(FieldLook(on: focus == .composer))
                        if focus == .composer {
                            composerSuggestions
                        }
                    }
                }
                field(text["library.fieldTags"]) {
                    tagChips
                }
                if let problem {
                    Text(problem)
                        .dsType(.label)
                        .foregroundStyle(Color.ds("danger"))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(CGFloat.ds("s-8"))
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.large])
        .interactiveDismissDisabled(saving)
        .task {
            focus = .title
            if let file = await PieceDetailsFile.fromFile(entry.url) {
                if file.titleFrom == "file", let named = file.title { fileTitle = named }
                fileComposer = file.composerFrom == "file" ? file.composer : nil
            }
        }
    }

    /// "Piece properties", and Done.
    private var header: some View {
        HStack(alignment: .center) {
            Text(text["library.properties"])
                .dsType(.title)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            Spacer()
            Button(text["library.done"]) { Task { await save() } }
                .buttonStyle(DSButtonStyle(kind: .primary))
                .disabled(saving)
        }
    }

    /// The library's composers holding what is typed, each marked as already in the library; a tap takes it.
    @ViewBuilder
    private var composerSuggestions: some View {
        let offered = LibraryShelf.composerSuggestions(composer, composers)
        if !offered.isEmpty {
            VStack(spacing: 0) {
                ForEach(offered, id: \.self) { name in
                    Button {
                        composer = name
                        focus = nil
                    } label: {
                        HStack {
                            Text(name)
                                .dsType(.body)
                                .foregroundStyle(Color.ds("ink"))
                            Spacer(minLength: CGFloat.ds("s-4"))
                            Text(text["library.inLibrary"])
                                .dsType(.caption)
                                .foregroundStyle(Color.ds("ink-2"))
                        }
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .frame(minHeight: CGFloat.ds("h-touch"))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(CGFloat.ds("s-1"))
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")))
            .dsShadow("shadow-menu")
        }
    }

    /// The piece's tags as chips with ✕, then «+ tag», which becomes a field; while typing, the library's tags that match.
    private var tagChips: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            FlowLayout(spacing: CGFloat.ds("s-2")) {
                ForEach(tags, id: \.self) { tag in
                    HStack(spacing: 0) {
                        Text(tag)
                            .dsType(.label)
                            .foregroundStyle(Color.ds("ink"))
                            .padding(.leading, CGFloat.ds("s-4"))
                        Button {
                            tags.removeAll { $0 == tag }
                        } label: {
                            Icon(name: "close", size: CGFloat.ds("icon-s"))
                                .foregroundStyle(Color.ds("ink-2"))
                                .frame(width: CGFloat.ds("h-control"), height: CGFloat.ds("h-control"))
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel(text.format("library.removeTag", tag))
                    }
                    .background(Color.ds("surface-2"), in: Capsule())
                    .overlay(Capsule().strokeBorder(Color.ds("hairline-strong")))
                }
                if addingTag {
                    TextField(text["library.newTag"], text: $newTag)
                        .focused($focus, equals: .tag)
                        .submitLabel(.done)
                        .onSubmit(addTyped)
                        .autocorrectionDisabled()
                        .dsType(.label)
                        .padding(.horizontal, CGFloat.ds("s-4"))
                        .frame(width: 200, height: CGFloat.ds("h-control"))
                        .overlay(Capsule().strokeBorder(Color.ds("accent")))
                        .accessibilityLabel(text["library.newTag"])
                } else {
                    Button {
                        addingTag = true
                        focus = .tag
                    } label: {
                        HStack(spacing: CGFloat.ds("s-1")) {
                            Icon(name: "plus", size: CGFloat.ds("icon-s"))
                            Text(text["library.tagChip"])
                        }
                        .dsType(.label)
                        .foregroundStyle(Color.ds("ink-2"))
                        .padding(.horizontal, CGFloat.ds("s-4"))
                        .frame(height: CGFloat.ds("h-control"))
                        .overlay(Capsule().strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
                        .contentShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(text["library.newTag"])
                }
            }
            if addingTag {
                let offered = LibraryShelf.suggestions(libraryTags, excluding: tags, typed: newTag)
                if !offered.isEmpty {
                    FlowLayout(spacing: CGFloat.ds("s-2")) {
                        ForEach(offered, id: \.self) { tag in
                            Button {
                                tags = LibraryShelf.adding(tag, to: tags)
                                newTag = ""
                            } label: {
                                HStack(spacing: CGFloat.ds("s-1")) {
                                    Icon(name: "plus", size: CGFloat.ds("icon-s"))
                                    Text(tag)
                                }
                            }
                            .buttonStyle(DSChipStyle())
                            .frame(minHeight: CGFloat.ds("h-control"))
                            .contentShape(Rectangle())
                            .accessibilityLabel(text.format("library.addTagNamed", tag))
                        }
                    }
                }
            }
        }
    }

    private func field<Input: View>(_ caption: String, @ViewBuilder _ input: () -> Input) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(caption)
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
                .accessibilityHidden(true)
            input()
                .accessibilityLabel(caption)
        }
    }

    private func addTyped() {
        tags = LibraryShelf.adding(newTag, to: tags, library: libraryTags)
        newTag = ""
        focus = .tag
    }

    private func save() async {
        saving = true
        problem = nil
        defer { saving = false }
        let kept = LibraryShelf.adding(newTag, to: tags, library: libraryTags)
        do {
            try await PieceDetailsFile.save(entry.url, title: LibraryShelf.clean(title), composer: LibraryShelf.clean(composer), tags: kept)
            await saved()
            dismiss()
        } catch {
            problem = (error as? ProjectError)?.message ?? error.localizedDescription
        }
    }
}

/// A one-line field in the kit's look (ds.css `.field input`): body type on the paper, a strong hairline round it, the
/// accent's ring while it is typed in.
private struct FieldLook: ViewModifier {
    var on = false

    func body(content: Content) -> some View {
        content
            .dsType(.body)
            .foregroundStyle(Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(height: CGFloat.ds("h-touch"))
            .background(Color.ds("paper"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(on ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: on ? 2 : 1))
    }
}
