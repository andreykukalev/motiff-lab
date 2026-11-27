import PDFKit
import SwiftUI

/// The pages of a scan's notes that carry the player's marks «что не так» (v1.6 c-14b3): each page of the notes (`check`'s
/// "pageStarts") one of whose bars a mark lies on, with its page in the PDF (the notes' "readPages", else the same number)
/// and the bars on it marked as read badly.
enum MarkedPages {
    /// The marked pages; nil when the notes' pages and the PDF's disagree, so a page read again could be the wrong one: the
    /// notes name the PDF pages they were read from (`read`) for another number of pages, or name none and the PDF has
    /// another number of pages (`pdfPages`) than they have.
    static func of(_ marks: [ReadingMarkEntry], starts: [CheckPageStart], read: [Int], pdfPages: Int) -> [ScanMarkedPages.Page]? {
        guard read.isEmpty ? pdfPages == starts.count : read.count == starts.count else { return nil }
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

/// What a reading with marks reads, found before the player's yes (`MarkedReading.plan`): the PDF the working notes were
/// read from, its bytes as kept in the piece, and which of its marked pages stay on this iPad and which go to the server.
struct MarkedPlan {
    var original: SourceLayers.Original
    var pdf: Data
    var pages: ScanMarkedPages.Plan
}

/// «Распознать заново с отметками» (v1.6 c-14b3, the 1.6 plan's decision 15, «Как напечатано» step 3): the working notes'
/// marked pages read again (`ScanMarkedPages`), put into their music by music-core with the marks (`scan.join` with the
/// project file), and kept as new Notes of their PDF beside the working ones (`sources.notes.recognise`, which answers
/// what became of the marks, "marked", told in the toast as "carried" is). The working notes stay as they are; the player
/// makes the new ones working in Sources. One recognition at a time: the run holds `SourcesController.reading` for its PDF
/// (`setReading`), as Sources' «Распознать» does; it stops with the screen, its job deleted (E-scan-20). Owned by the
/// screen's workspace (`ScoreWorkspace.markedReading`), shown in Check (`MarkedReadingCheck`).
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

    /// What reading the marked pages of `check`'s notes again would read, or why they cannot be: the PDF not kept in the
    /// piece here, the notes' pages and the PDF's disagreeing, or no mark on a page.
    func plan(_ check: CheckShelf, sources: SourcesController, text: UIText) async -> Result<MarkedPlan, SourcesController.Refusal> {
        let layers = sources.layers
        guard let original = layers.original(layers.notes(layers.working)?.original), original.isPages, sources.canRecognise(original) else {
            return .failure(SourcesController.Refusal(words: text["markedReading.noPdf"]))
        }
        let pdf: Data
        switch await sources.keptPdf(original) {
        case .failure(let refusal): return .failure(refusal)
        case .success(let kept): pdf = kept.data
        }
        let count = PDFDocument(data: pdf)?.pageCount ?? 0
        guard let pages = MarkedPages.of(check.marks, starts: check.pageStarts, read: check.readPages, pdfPages: count) else {
            return .failure(SourcesController.Refusal(words: text["markedReading.pagesDiffer"]))
        }
        guard !pages.isEmpty else { return .failure(SourcesController.Refusal(words: text["markedReading.noPages"])) }
        return .success(MarkedPlan(original: original, pdf: pdf, pages: await ScanMarkedPages.plan(pages, pdf: pdf, pdfPages: count)))
    }

    /// Reads the pages of `plan` again, unless a reading goes on, holding `sources.reading` for its run (a recognition in
    /// Sources meanwhile waits, and this one waits for one there); `target` is the scan server, or why none can read,
    /// asked only when a page has to go there (E-scan-19).
    func start(_ plan: MarkedPlan, check: CheckShelf, sources: SourcesController, target: Result<ScanTarget, ScanProblem>, text: UIText) {
        guard !busy else { return }
        busy = true
        problem = nil
        task = Task {
            let why = await sources.setReading(plan.original) {
                await self.readAgain(plan, check: check, sources: sources, target: target, text: text)
            }
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

    private func readAgain(_ plan: MarkedPlan, check: CheckShelf, sources: SourcesController, target: Result<ScanTarget, ScanProblem>, text: UIText) async -> String? {
        guard let file = await sources.snapshot(), let score = MarkedPages.workingMusic(file) else { return sources.status ?? text["pdf.readNone"] }
        let reading = RecognitionJob(
            kept: nil, keep: { [weak self] made in self?.job = made }, hear: { [weak self] status in self?.progress = status.progress },
            wanted: { !Task.isCancelled }, ready: { !Task.isCancelled }
        )
        let read: ScanMarkedPages.Reading
        switch await ScanMarkedPages.read(plan.pages, pdf: plan.pdf, to: target, job: reading, text: text) {
        case .failure(let problem): return problem.text(text)
        case .success(let done): read = done
        }
        let request = facadeRequest(["score": score, "replace": read.replace, "file": file, "original": plan.original.id, "language": text.language])
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(.scanJoin, request) }
        guard let joined = facadeObject(answer), joined["error"] == nil, let music = joined["score"] as? String else {
            return facadeObject(answer)?["message"] as? String ?? text["pdf.readNone"]
        }
        // Only the pages read again have bars to check with their readings and crops (E-scan-21).
        ScanCrops.keepReview(joined, pages: check.readPages.isEmpty ? Array(1...max(1, check.pageStarts.count)) : check.readPages, pdf: plan.pdf)
        let engine = read.engines.isEmpty ? check.engine ?? "" : read.engines.joined(separator: " + ")
        let fields: [String: Any] = [
            "original": plan.original.id, "musicXml": Data(music.utf8).base64EncodedString(), "engine": engine,
            "pages": max(1, plan.original.pages ?? read.pdfPages), "now": SourcesController.now(),
        ]
        guard !Task.isCancelled else { return nil }
        let toast = text.count("markedReading.toast", plan.pages.pages.count)
        return await sources.keepRecognised(.sourcesNotesRecognise, SourcesController.Recognised(fields: fields, dropped: nil, toast: toast))
    }
}

/// «Распознать заново с отметками» under the marks in Check (v1.6 c-14b3). Before the player's yes it says where the
/// pages go (ADR 0011 section 2): those the vector reader reads stay on this iPad, the others go to the scan server chosen
/// in Settings as Sources says it (`SourcesRecognisePanel.pagesOffer`), by a scan job of the whole file with only those
/// pages read («Четыре движка»), Pro on Motiff Lab's; or why it waits or cannot: another recognition (here, in Sources or
/// the library), the PDF not kept in the piece, its pages disagreeing with the notes'. While the pages are read, how far;
/// after, why not (E-scan-17, E-scan-19).
struct MarkedReadingCheck: View {
    @ObservedObject var reading: MarkedReading
    @ObservedObject var checkModel: CheckModel
    @ObservedObject var sources: SourcesController
    let text: UIText
    @EnvironmentObject private var account: Account
    @ObservedObject private var scan = ScanServer.shared
    /// The library's recognition: one at a time (Andrei, 2026-11-25), as Sources' «Распознать».
    @Environment(\.backgroundReading) private var background
    /// What the reading would read, or why it cannot, found again when the marks or the working notes change; nil while
    /// it is being found.
    @State private var plan: Result<MarkedPlan, SourcesController.Refusal>?

    /// What the button says before the player's yes: why it waits or cannot (nil when it can go; empty while the plan is
    /// found), where the pages go, and whether that is Pro.
    private struct Offer {
        var blocker: String?
        var line = ""
        var pro = false
    }

    var body: some View {
        let hosted = ScanServer.hosted(account)
        let address = scan.address(hosted: hosted)
        let offer = offered(address: address, hosted: address != nil && address == hosted)
        let said = offer.blocker ?? offer.line
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Button {
                guard case .some(.success(let planned)) = plan else { return }
                reading.start(planned, check: checkModel.check, sources: sources, target: target(address), text: text)
            } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Text(text["markedReading.readAgain"])
                    if offer.pro { ProTag() }
                }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            .disabled(reading.busy || offer.blocker != nil)
            .accessibilityHint(text["markedReading.readAgainHint"])
            if let progress = reading.progress {
                ScanProgressBar(progress: progress, text: text)
            } else if !reading.busy, !said.isEmpty {
                caption(said, colour: "ink-2")
            }
            if let problem = reading.problem, !problem.isEmpty {
                caption(problem, colour: "warning")
            }
        }
        .padding(.top, CGFloat.ds("s-1"))
        .task(id: checkModel.check.marks.map(\.id) + [sources.layers.working ?? ""]) {
            plan = nil
            plan = await reading.plan(checkModel.check, sources: sources, text: text)
        }
    }

    private func offered(address: URL?, hosted: Bool) -> Offer {
        // E-sources-1: one recognition at a time; while this one goes, its progress says so.
        if !reading.busy, let other = SourcesController.readingOther(sources.reading, than: nil, in: sources.layers, text: text) {
            return Offer(blocker: other)
        }
        let planned: MarkedPlan
        switch plan {
        case .none: return Offer(blocker: "")
        case .some(.failure(let refusal)): return Offer(blocker: refusal.words)
        case .some(.success(let found)): planned = found
        }
        let here = planned.pages.hereLine(text)
        let server = planned.pages.server.count
        guard server > 0 else { return Offer(blocker: background.busy.map { text.format("recognition.busy", $0) }, line: here) }
        let sending = SourcesRecognisePanel.pagesOffer(server, busy: background.busy, address: address, hosted: hosted, jobs: true, account: account, text: text)
        if let blocker = sending.blocker { return Offer(blocker: blocker, pro: hosted) }
        // A bar read badly is read again by the other engines of a job (E-scan-19).
        guard ScanServer.jobs else { return Offer(blocker: text["markedReading.needsJobs"], pro: hosted) }
        return Offer(line: [here, sending.line].filter { !$0.isEmpty }.joined(separator: " "), pro: hosted)
    }

    /// The scan server chosen in Settings, which the pages that leave this iPad go to.
    private func target(_ address: URL?) -> Result<ScanTarget, ScanProblem> {
        guard let address else { return .failure(.server(SourcesController.noScanServer(text))) }
        return .success(ScanTarget(address: address, account: account))
    }

    private func caption(_ words: String, colour: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds(colour)).fixedSize(horizontal: false, vertical: true)
    }
}
