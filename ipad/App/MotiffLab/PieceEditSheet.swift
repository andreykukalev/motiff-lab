import SwiftUI

/// The title, composer and tags of one piece, edited from its card in the library (version 0.2), as on the web
/// (src/MotiffLab.Web/Projects/PieceEditSheet.razor). What is typed is the player's; a field left empty shows what the file
/// says again, named in its placeholder. Tags are added by typing a new one or picking one the library already has, and
/// taken off one by one. Nothing is kept until Save, which music-core writes into the project file (`project.metadata`).
struct PieceEditSheet: View {
    let entry: LibraryEntry
    let libraryTags: [String]
    let text: UIText
    let saved: () async -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var title: String
    @State private var composer: String
    @State private var tags: [String]
    @State private var newTag = ""
    /// What the file says, shown as each field's placeholder: read again from the file once the sheet is open.
    @State private var fileTitle: String
    @State private var fileComposer: String?
    @State private var saving = false
    @State private var problem: String?
    @FocusState private var titleFocused: Bool

    init(entry: LibraryEntry, summary: PieceSummary?, libraryTags: [String], text: UIText, saved: @escaping () async -> Void) {
        self.entry = entry
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
            VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
                header
                field(text["library.fieldTitle"]) {
                    TextField(fileTitle, text: $title)
                        .focused($titleFocused)
                }
                field(text["library.fieldComposer"]) {
                    TextField(fileComposer ?? text["library.composerNone"], text: $composer)
                }
                Text(text["library.emptyFromFile"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                tagsSection
                if let problem {
                    Text(problem)
                        .dsType(.label)
                        .foregroundStyle(Color.ds("danger"))
                        .fixedSize(horizontal: false, vertical: true)
                }
                HStack(spacing: CGFloat.ds("s-3")) {
                    Button(text["library.save"]) { Task { await save() } }
                        .buttonStyle(DSButtonStyle(kind: .primary))
                        .disabled(saving)
                    Button(text["library.cancel"]) { dismiss() }
                        .buttonStyle(DSButtonStyle(kind: .quiet))
                }
            }
            .padding(CGFloat.ds("s-6"))
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.large])
        .interactiveDismissDisabled(saving)
        .task {
            titleFocused = true
            if let file = await PieceDetailsFile.fromFile(entry.url) {
                if file.titleFrom == "file", let named = file.title { fileTitle = named }
                fileComposer = file.composerFrom == "file" ? file.composer : nil
            }
        }
    }

    private var header: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                Text(text["library.details"]).overline()
                Text(entry.title)
                    .dsType(.title)
                    .foregroundStyle(Color.ds("ink"))
                    .accessibilityAddTraits(.isHeader)
            }
            Spacer()
            SheetCloseButton(text: text)
        }
    }

    /// The piece's tags with a way to take each off; a new one typed, or one of the library's picked.
    private var tagsSection: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["library.fieldTags"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            if !tags.isEmpty {
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    ForEach(tags, id: \.self) { tag in
                        HStack(spacing: 0) {
                            Text(tag)
                                .dsType(.label)
                                .foregroundStyle(Color.ds("ink"))
                                .padding(.leading, CGFloat.ds("s-3"))
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
                        .background(Color.ds("accent-tint"), in: Capsule())
                        .overlay(Capsule().strokeBorder(Color.ds("accent")))
                    }
                }
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                TextField(text["library.newTag"], text: $newTag)
                    .submitLabel(.done)
                    .onSubmit(addTyped)
                    .modifier(FieldLook())
                    .accessibilityLabel(text["library.newTag"])
                Button(text["library.addTag"], action: addTyped)
                    .buttonStyle(DSButtonStyle(kind: .secondary))
                    .disabled(LibraryShelf.clean(newTag) == nil)
            }
            let suggestions = LibraryShelf.suggestions(libraryTags, excluding: tags, typed: newTag)
            if !suggestions.isEmpty {
                Text(text["library.libraryTags"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    ForEach(suggestions, id: \.self) { tag in
                        Button {
                            tags = LibraryShelf.adding(tag, to: tags)
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

    private func field<Input: View>(_ caption: String, @ViewBuilder _ input: () -> Input) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(caption)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            input()
                .modifier(FieldLook())
                .accessibilityLabel(caption)
        }
    }

    private func addTyped() {
        tags = LibraryShelf.adding(newTag, to: tags, library: libraryTags)
        newTag = ""
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

/// A one-line field in the kit's look (ds.css `.field input`): body type on the surface, a strong hairline round it.
private struct FieldLook: ViewModifier {
    func body(content: Content) -> some View {
        content
            .dsType(.body)
            .foregroundStyle(Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-3"))
            .frame(height: CGFloat.ds("h-touch"))
            .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
    }
}
