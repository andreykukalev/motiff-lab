import SwiftUI

/// The Sources sheet's dialogs (moved out of `SourcesSheet.swift` in v0.10): the warning before an original is deleted,
/// with or without its notes; «Удалить ноты…»; and an original picked in Files kept where it lies or copied to this iPad.
struct SourcesDialogs: ViewModifier {
    @ObservedObject var sources: SourcesController
    @ObservedObject var originals: OriginalsController
    let text: UIText
    @Binding var deleting: SourceLayers.Original?
    @Binding var deletingNotes: SourceLayers.Notes?

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
