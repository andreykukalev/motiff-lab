import SwiftUI

/// The Sources sheet's dialogs (moved out of `SourcesSheet.swift` in v0.10): the warning before an original is deleted,
/// with or without its notes; «Удалить ноты…»; a version's new name; and an original picked in Files kept where it lies or
/// copied to this iPad. A draft's are `DraftDialogs` (v1.1).
struct SourcesDialogs: ViewModifier {
    @ObservedObject var sources: SourcesController
    @ObservedObject var originals: OriginalsController
    let text: UIText
    @Binding var deleting: SourceLayers.Original?
    @Binding var deletingNotes: SourceLayers.Notes?
    /// The version whose name is being changed («Переименовать…»).
    @Binding var renaming: SourceLayers.Notes?
    let rename: (SourceLayers.Notes, String) -> Void
    @State private var name = ""

    private var layers: SourceLayers { sources.layers }

    func body(content: Content) -> some View {
        content
            .sheet(item: $deleting) { original in
                DeleteOriginalDialog(layers: layers, original: original, text: text) { withNotes in
                    deleting = nil
                    Task { await sources.remove(original, withNotes: withNotes) }
                } cancel: {
                    deleting = nil
                }
                .fittedSheet()
            }
            .confirmationDialog(
                deletingNotes.map { text.format("sources.sheet.deleteNotesTitle", layers.title(of: $0, text)) } ?? "",
                isPresented: Binding(get: { deletingNotes != nil }, set: { if !$0 { deletingNotes = nil } }),
                titleVisibility: .visible,
                presenting: deletingNotes
            ) { notes in
                Button(text["sources.sheet.deleteNotesButton"], role: .destructive) { Task { await sources.remove(notes) } }
                Button(text["common.cancel"], role: .cancel) {}
            } message: { _ in
                Text(text["sources.sheet.deleteNotesLine"])
            }
            .alert(
                renaming.map { text.format("sources.sheet.renameTitle", layers.title(of: $0, text, underOriginal: true)) } ?? "",
                isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } }),
                presenting: renaming
            ) { notes in
                TextField(text["performance.name"], text: $name).submitLabel(.done)
                Button(text["sources.sheet.renameButton"]) { rename(notes, name) }
                Button(text["common.cancel"], role: .cancel) {}
            }
            .onChange(of: renaming) { _, notes in name = notes?.name ?? "" }
            .confirmationDialog(
                text["originals.linkOrCopy"],
                isPresented: Binding(get: { originals.choice != nil }, set: { if !$0 { originals.choice = nil } }),
                titleVisibility: .visible,
                presenting: originals.choice
            ) { picked in
                if picked.bookmark != nil {
                    Button(text["originals.keepWhere"]) { Task { await originals.keepLink(picked); await sources.refresh() } }
                }
                Button(text["originals.copyToIpad"]) { Task { await originals.keepCopy(picked); await sources.refresh() } }
                Button(text["common.cancel"], role: .cancel) {}
            } message: { _ in
                Text(text["originals.linkOrCopyHint"])
            }
    }
}

/// A draft's dialogs (v1.1): its new name («Переименовать…»), and the question before it is deleted, which says that only
/// the draft goes, never its sources or their notes (0034, section 1).
struct DraftDialogs: ViewModifier {
    @ObservedObject var sources: SourcesController
    let text: UIText
    @Binding var renaming: SourceLayers.Draft?
    @Binding var deleting: SourceLayers.Draft?
    @State private var name = ""

    func body(content: Content) -> some View {
        content
            .alert(
                renaming.map { text.format("sources.sheet.renameTitle", $0.name) } ?? "",
                isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } }),
                presenting: renaming
            ) { draft in
                TextField(text["performance.name"], text: $name).submitLabel(.done)
                Button(text["sources.sheet.renameButton"]) { Task { await sources.renameDraft(draft, to: name) } }
                Button(text["common.cancel"], role: .cancel) {}
            }
            .onChange(of: renaming) { _, draft in name = draft?.name ?? "" }
            .confirmationDialog(
                deleting.map { text.format("drafts.deleteTitle", $0.name) } ?? "",
                isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }),
                titleVisibility: .visible,
                presenting: deleting
            ) { draft in
                Button(text["drafts.deleteButton"], role: .destructive) { Task { await sources.removeDraft(draft) } }
                Button(text["common.cancel"], role: .cancel) {}
            } message: { _ in
                Text(text["drafts.deleteLine"])
            }
    }
}
