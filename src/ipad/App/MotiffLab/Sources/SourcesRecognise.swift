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
    private struct Offer {
        var blocker: String?
        var line = ""
        var send = ""
    }

    private var offer: Offer {
        if original.isRecording {
            return Offer(blocker: RecordingChooser.blocker(account, text)?.words, line: Self.hearLine(sources.layers, text: text), send: text["sources.sheet.sendRecording"])
        }
        let pages = max(1, original.pages ?? 1)
        if let busy = background.busy { return Offer(blocker: text.format("recognition.busy", busy)) }
        guard let address = row.address else { return Offer(blocker: text["pdf.readNone"]) }
        if row.hosted && account.player == nil { return Offer(blocker: text["scan.signInPrompt"]) }
        if row.hosted && !account.allows(.scan) { return Offer(blocker: text["scan.proPrompt"]) }
        let line = row.hosted
            ? SourceLayers.counted(row.jobs ? "sources.sheet.readHostedJob" : "sources.sheet.readHosted", pages, text: text)
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
        // Before Send, as the PDF's own panel (ScanServer.jobPlanLine): only for pages, with nothing blocking them (E-scan-5).
        .task(id: original.id) {
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

extension SourcesController {
    /// What recognising an original gives music-core to keep as its new Notes: the call's fields, a field to send again
    /// without when music-core refuses it ("badRequest"), and the toast once the notes are kept.
    struct Recognised {
        var fields: [String: Any]
        var dropped: String?
        var toast: String
    }

    /// Why an original was not recognised, in the player's words.
    struct Refusal: Error {
        let words: String
    }

    /// A toast's text, then music-core's line of what became of the player's work, an answer's object's "text", when it
    /// says something: the corrections on notes recognised again («Как напечатано», step 2: "carried"), the bookmarks and
    /// phrases on notes made working (v1.5 c-11b: "followed").
    nonisolated static func toastText(_ done: String, adding part: Any?) -> String {
        guard let line = (part as? [String: Any])?["text"] as? String, !line.isEmpty else { return done }
        return "\(done) · \(line)"
    }
}
