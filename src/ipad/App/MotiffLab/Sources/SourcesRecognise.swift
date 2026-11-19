import SwiftUI

/// «Распознать» on an original's row while it has no Notes yet (moved out of `SourcesSheet.swift` in v0.10): it opens the
/// recognise panel under the row; Pro when the scan server is Motiff Lab's.
struct SourcesRecogniseButton: View {
    let text: UIText
    let hosted: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text["sources.sheet.recognise"])
                if hosted { ProTag() }
            }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
    }
}

/// «Распознать ноты» under the row (moved out of `SourcesSheet.swift` in v0.10): where the pages go, then Send (the
/// player's yes) and Cancel; recognised again, the notes there are and their corrections stay beside. Send is
/// `SourcesController.recognise`, the one path of recognising an original.
struct SourcesRecognisePanel: View {
    @ObservedObject var sources: SourcesController
    let original: SourceLayers.Original
    let row: SourcesRow
    /// The original whose recognise panel is open: Send done and Cancel close it.
    @Binding var recognising: String?
    /// Why an original was not recognised, by its id.
    @Binding var problems: [String: String]
    @EnvironmentObject private var account: Account

    private var text: UIText { row.text }

    var body: some View {
        let pages = max(1, original.pages ?? 1)
        let earlier = original.notes.compactMap { sources.layers.notes($0) }.last
        let busy = sources.reading == original.id
        let hosted = row.hosted
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text[earlier == nil ? "sources.sheet.readHeading" : "sources.sheet.recogniseAgain"]).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                if hosted { ProTag() }
            }
            if let address = row.address {
                if hosted && account.player == nil {
                    SourcesCaption(words: text["scan.signInPrompt"], colour: "ink-2")
                } else if hosted && !account.allows(.scan) {
                    SourcesCaption(words: text["scan.proPrompt"], colour: "ink-2")
                } else {
                    SourcesCaption(words: hosted
                        ? SourceLayers.counted("sources.sheet.readHosted", pages, text: text)
                        : SourceLayers.counted("sources.sheet.readOwn", pages, address.absoluteString, text: text), colour: "ink-2")
                    if let earlier {
                        SourcesCaption(words: text.format("sources.sheet.readKeeps", earlier.at.map { SourceLayers.shortDate($0, text) } ?? "", text.count("sources.sheet.edits", earlier.edits)), colour: "ink-2")
                    }
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Button(busy ? text["import.scanBusy"] : SourceLayers.counted("sources.sheet.send", pages, text: text)) {
                            Task { await send(address: address) }
                        }
                        .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                        .disabled(busy)
                        Button(text["common.cancel"]) { recognising = nil }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                            .disabled(busy)
                    }
                }
            } else {
                SourcesCaption(words: text["pdf.readNone"], colour: "ink-2")
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    /// Send: the panel closes once the notes are in, else says why under the row.
    private func send(address: URL) async {
        problems[original.id] = nil
        if let problem = await sources.recognise(original, address: address, account: account) {
            problems[original.id] = problem
        } else {
            recognising = nil
        }
    }
}
