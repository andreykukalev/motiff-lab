import SwiftUI

/// «Распознать» on an original's row while it has no Notes yet (moved out of `SourcesSheet.swift` in v0.10): it opens the
/// recognise panel under the row; Pro when the scan server is Motiff Lab's, as it always is for a recording (v0.10,
/// finding 26: «Распознать · Pro» right on the recording's row).
struct SourcesRecogniseButton: View {
    let text: UIText
    let pro: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text["sources.sheet.recognise"])
                if pro { ProTag() }
            }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
    }
}

/// «Распознать ноты» under the row (moved out of `SourcesSheet.swift` in v0.10): where the pages or the recording's sound
/// go, then Send (the player's yes) and Cancel; recognised again (the same heading, v0.10 finding 27), the notes there are
/// and their corrections stay beside. A recording's sound goes only to Motiff Lab's scan server, which is Pro (v0.10,
/// finding 26). Send is `SourcesController.recognise`, the one path of recognising an original.
struct SourcesRecognisePanel: View {
    @ObservedObject var sources: SourcesController
    let original: SourceLayers.Original
    let row: SourcesRow
    /// The original whose recognise panel is open: Send done and Cancel close it.
    @Binding var recognising: String?
    /// Why an original was not recognised, by its id.
    @Binding var problems: [String: String]
    @EnvironmentObject private var account: Account
    /// The library's recognition: one at a time (Andrei, 2026-11-25), so a page original is not sent while it goes on.
    @Environment(\.backgroundReading) private var background
    /// With «Четыре движка», what the iPad reads itself, what goes to the server and about how long (v1.5 i-7).
    @State private var planned: String?

    private var text: UIText { row.text }

    /// What the panel says before Send: what recognising needs first (nil when it can go), where it goes, and Send's word.
    struct Offer {
        var blocker: String?
        var line = ""
        var send = ""
    }

    private var offer: Offer {
        // E-sources-1: while another original is being recognised, this one waits, and the panel says which.
        if let other = sources.readingOther(than: original) { return Offer(blocker: other) }
        if original.isRecording {
            return Offer(blocker: RecordingChooser.blocker(account, text)?.words, line: Self.hearLine(sources.layers, text: text), send: text["sources.sheet.sendRecording"])
        }
        return Self.pagesOffer(max(1, original.pages ?? 1), busy: background.busy, address: row.address, hosted: row.hosted, jobs: row.jobs, account: account, text: text)
    }

    /// What sending `pages` pages of a PDF to the scan server at `address` says before the player's yes (ADR 0011
    /// section 2): what it needs first, the library's recognition (`busy`), a server, and on Motiff Lab's (`hosted`) sign-in
    /// and Pro; else where the pages go and Send's word. Also Check's «Распознать заново с отметками» for its pages that
    /// leave the iPad (`Workspace/ScoreMarkedReading`).
    static func pagesOffer(_ pages: Int, busy: String?, address: URL?, hosted: Bool, jobs: Bool, account: Account, text: UIText) -> Offer {
        if let busy { return Offer(blocker: text.format("recognition.busy", busy)) }
        guard let address else { return Offer(blocker: SourcesController.noScanServer(text)) }
        if hosted && account.player == nil { return Offer(blocker: text["scan.signInPrompt"]) }
        if hosted && !account.allows(.scan) { return Offer(blocker: text["scan.proPrompt"]) }
        let line = hosted
            ? SourceLayers.counted(jobs ? "sources.sheet.readHostedJob" : "sources.sheet.readHosted", pages, text: text)
            : SourceLayers.counted("sources.sheet.readOwn", pages, address.absoluteString, text: text)
        return Offer(line: line, send: SourceLayers.counted("sources.sheet.send", pages, text: text))
    }

    /// Where a recording's sound goes and what its notes become: Notes under the recording beside the working notes, or,
    /// on a piece with no notes yet, the piece's own notes, which it opens on (v1.1, finding 29).
    static func hearLine(_ layers: SourceLayers, text: UIText) -> String {
        text[layers.working == nil ? "sources.sheet.hearHostedFirst" : "sources.sheet.hearHosted"]
    }

    var body: some View {
        let offer = self.offer
        let earlier = original.notes.compactMap { sources.layers.notes($0) }.last
        let busy = sources.reading == original.id
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text["sources.sheet.readHeading"]).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                if row.isPro(original) { ProTag() }
            }
            if let blocker = offer.blocker {
                SourcesCaption(words: blocker, colour: "ink-2")
            } else {
                SourcesCaption(words: offer.line, colour: "ink-2")
                if let planned { SourcesCaption(words: planned, colour: "ink-2") }
                if let earlier {
                    SourcesCaption(words: text.format("sources.sheet.readKeeps", earlier.at.map { SourceLayers.shortDate($0, text) } ?? "", text.count("sources.sheet.edits", earlier.edits)), colour: "ink-2")
                }
                HStack(spacing: CGFloat.ds("s-2")) {
                    Button(busy ? sources.progress ?? text["import.scanBusy"] : offer.send) {
                        Task { await send() }
                    }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(busy)
                    Button(text["common.cancel"]) { recognising = nil }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        .disabled(busy)
                }
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        // Before Send, as the PDF's own panel (ScanServer.jobPlanLine): only for pages, with nothing blocking them (E-scan-5);
        // asked again when what blocks them changes, so the line comes back once another recognition ends (E-sources-1).
        .task(id: [original.id, offer.blocker ?? ""]) {
            guard let address = Self.timedBy(jobs: row.jobs, recording: original.isRecording, blocked: self.offer.blocker != nil, address: row.address) else { return }
            planned = await sources.jobPlanLine(original, address: address)
        }
    }

    /// The scan server to ask how long an original takes before Send: only with «Четыре движка», for pages rather than a
    /// recording, with nothing blocking them and an address set; nil asks nothing (E-scan-5).
    static func timedBy(jobs: Bool, recording: Bool, blocked: Bool, address: URL?) -> URL? {
        jobs && !recording && !blocked ? address : nil
    }

    /// Send: the panel closes once the notes are in, else says why under the row.
    private func send() async {
        problems[original.id] = nil
        if let problem = await sources.recognise(original, address: row.address, account: account) {
            problems[original.id] = problem
        } else {
            recognising = nil
        }
    }
}
