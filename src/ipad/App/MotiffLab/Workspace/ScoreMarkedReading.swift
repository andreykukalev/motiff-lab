import SwiftUI

/// The pages of a scan's notes that carry the player's marks «что не так» (v1.6 c-14b3): each page of the notes (`check`'s
/// "pageStarts") one of whose bars a mark lies on, with its page in the PDF (the notes' "readPages", else the same number)
/// and the bars on it marked as read badly. None when the notes' pages and the PDF's pages kept with them disagree.
enum MarkedPages {
    static func of(_ marks: [ReadingMarkEntry], starts: [CheckPageStart], read: [Int]) -> [ScanMarkedPages.Page] {
        guard read.isEmpty || read.count == starts.count else { return [] }
        return starts.enumerated().compactMap { index, start -> ScanMarkedPages.Page? in
            let bars = start.bar...max(start.bar, start.lastBar)
            let on = marks.filter { $0.lies(on: bars) }
            guard !on.isEmpty else { return nil }
            let bad = Set(on.filter { $0.kind == "badlyRead" }.flatMap { max($0.bar, bars.lowerBound)...min($0.last, bars.upperBound) })
            return ScanMarkedPages.Page(place: start.page, pdf: read.isEmpty ? index + 1 : read[index], bad: bad.sorted())
        }
    }

    /// The working notes' music in a project file (`project.save`'s answer, the notes layer of project file 18), as text:
    /// what the pages read again are put into (`scan.join`'s "score"); nil when the file has none.
    static func workingMusic(_ file: String) -> String? {
        guard let object = facadeObject(file), let working = (object["workspace"] as? [String: Any])?["working"] as? String,
              let notes = (object["notes"] as? [[String: Any]])?.first(where: { $0["id"] as? String == working }),
              let music = notes["music"] as? String, let data = Data(base64Encoded: music) else { return nil }
        return String(data: data, encoding: .utf8)
    }
}

/// «Распознать заново с отметками» (v1.6 c-14b3, the 1.6 plan's decision 15, «Как напечатано» step 3): the working notes'
/// marked pages read again (`ScanMarkedPages`), put into their music by music-core with the marks (`scan.join` with the
/// project file), and kept as new Notes of their PDF beside the working ones (`sources.notes.recognise`, which answers
/// what became of the marks, "marked", told in the toast as "carried" is). The working notes stay as they are; the player
/// makes the new ones working in Sources. One reading at a time; it stops with the screen, its job deleted (E-scan-20).
/// Owned by the screen's workspace (`ScoreWorkspace.markedReading`), shown in Check (`MarkedReadingCheck`).
@MainActor
final class MarkedReading: ObservableObject {
    /// The pages are being read: a second tap does nothing (E-scan-17).
    @Published private(set) var busy = false
    /// How far a scan job is, while one reads the pages.
    @Published private(set) var progress: ScanProgress?
    /// Why the last reading gave no new Notes, in the player's words; nil when it did or none was made.
    @Published private(set) var problem: String?
    private var task: Task<Void, Never>?
    /// The scan job sent, deleted once the reading is over, whatever came of it.
    private var job: ScanJobRecord?

    /// Reads the marked pages of `check`'s notes again, unless a reading goes on; `target` is the scan server, or why
    /// none can read, asked only when a page has to go there (E-scan-19).
    func start(_ check: CheckShelf, sources: SourcesController, target: Result<ScanTarget, ScanProblem>, text: UIText) {
        guard !busy else { return }
        busy = true
        problem = nil
        task = Task {
            let why = await readAgain(check, sources: sources, target: target, text: text)
            if let job, case .success(let server) = target {
                // Not in this task, which the screen may have cancelled: the server forgets the pages either way.
                Task { await ScanServer.deleteJob(job.id, to: server) }
            }
            job = nil
            progress = nil
            problem = why
            busy = false
        }
    }

    /// The screen goes: the reading stops and nothing of it is kept (E-scan-20).
    func stop() {
        task?.cancel()
    }

    private func readAgain(_ check: CheckShelf, sources: SourcesController, target: Result<ScanTarget, ScanProblem>, text: UIText) async -> String? {
        let layers = sources.layers
        guard let working = layers.working, let id = layers.notes(working)?.original,
              let original = layers.originals.first(where: { $0.id == id }), original.isPages, sources.canRecognise(original) else {
            return text["markedReading.noPdf"]
        }
        let pages = MarkedPages.of(check.marks, starts: check.pageStarts, read: check.readPages)
        guard !pages.isEmpty else { return text["markedReading.noPages"] }
        guard let file = await sources.snapshot(), let score = MarkedPages.workingMusic(file) else { return sources.status ?? text["pdf.readNone"] }
        let pdf: Data
        switch await sources.keptPdf(original) {
        case .failure(let refusal): return refusal.words
        case .success(let kept): pdf = kept.data
        }
        let reading = RecognitionJob(
            kept: nil, keep: { [weak self] made in self?.job = made }, hear: { [weak self] status in self?.progress = status.progress },
            wanted: { !Task.isCancelled }, ready: { !Task.isCancelled }
        )
        let read: ScanMarkedPages.Reading
        switch await ScanMarkedPages.read(pages, pdf: pdf, to: target, job: reading, text: text) {
        case .failure(let problem): return problem.text(text)
        case .success(let done): read = done
        }
        let request = facadeRequest(["score": score, "replace": read.replace, "file": file, "original": original.id, "language": text.language])
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(.scanJoin, request) }
        guard let joined = facadeObject(answer), joined["error"] == nil, let music = joined["score"] as? String else {
            return facadeObject(answer)?["message"] as? String ?? text["pdf.readNone"]
        }
        // Only the pages read again have bars to check with their readings and crops (E-scan-21).
        ScanCrops.keepReview(joined, pages: check.readPages.isEmpty ? Array(1...max(1, check.pageStarts.count)) : check.readPages, pdf: pdf)
        let engine = read.engines.isEmpty ? check.engine ?? "" : read.engines.joined(separator: " + ")
        let fields: [String: Any] = [
            "original": original.id, "musicXml": Data(music.utf8).base64EncodedString(), "engine": engine,
            "pages": max(1, original.pages ?? read.pdfPages), "now": SourcesController.now(),
        ]
        guard !Task.isCancelled else { return nil }
        return await sources.keepRecognised(.sourcesNotesRecognise, SourcesController.Recognised(fields: fields, dropped: nil, toast: text.count("markedReading.toast", pages.count)))
    }
}

/// «Распознать заново с отметками» under the marks in Check (v1.6 c-14b3): Pro on Motiff Lab's scan server, as Sources'
/// «Распознать»; while a recognition goes on in the library, why it waits; while the pages are read, how far; after, why
/// not. The scan server is the one chosen in Settings, by a scan job («Четыре движка»), and only when a page has to go there
/// (E-scan-19).
struct MarkedReadingCheck: View {
    @ObservedObject var reading: MarkedReading
    @ObservedObject var checkModel: CheckModel
    let sources: SourcesController
    let text: UIText
    @EnvironmentObject private var account: Account
    @ObservedObject private var scan = ScanServer.shared
    /// The library's recognition: one at a time (Andrei, 2026-11-25), as Sources' «Распознать».
    @Environment(\.backgroundReading) private var background

    var body: some View {
        let hosted = ScanServer.hosted(account)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Button {
                reading.start(checkModel.check, sources: sources, target: target(hosted: hosted), text: text)
            } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Text(text["markedReading.readAgain"])
                    if hosted != nil && scan.address(hosted: hosted) == hosted { ProTag() }
                }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            .disabled(reading.busy || background.busy != nil)
            .accessibilityHint(text["markedReading.readAgainHint"])
            if let busy = background.busy, !reading.busy {
                caption(text.format("recognition.busy", busy), colour: "ink-2")
            }
            if let progress = reading.progress {
                ScanProgressBar(progress: progress, text: text)
            }
            if let problem = reading.problem, !problem.isEmpty {
                caption(problem, colour: "warning")
            }
        }
        .padding(.top, CGFloat.ds("s-1"))
    }

    /// Where the pages go when one has to leave this iPad, or why none can: no server chosen, Motiff Lab's without sign-in
    /// or Pro, or scan jobs off, since a bar read badly is read again by the other engines of a job.
    private func target(hosted: URL?) -> Result<ScanTarget, ScanProblem> {
        guard let address = scan.address(hosted: hosted) else { return .failure(.server(SourcesController.noScanServer(text))) }
        if address == hosted && account.player == nil { return .failure(.server(text["scan.signInPrompt"])) }
        if address == hosted && !account.allows(.scan) { return .failure(.server(text["scan.proPrompt"])) }
        guard ScanServer.jobs else { return .failure(.server(text["markedReading.needsJobs"])) }
        return .success(ScanTarget(address: address, account: account))
    }

    private func caption(_ words: String, colour: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds(colour)).fixedSize(horizontal: false, vertical: true)
    }
}
