import Foundation
import PDFKit

/// One piece of a batch sent to be recognised: its project file by name in the library's folder, its title and pages, and
/// what came of it.
struct RecognitionItem: Codable, Equatable, Identifiable, Sendable {
    enum State: String, Codable, Sendable {
        case queued, reading, done, failed
    }

    var file: String
    var title: String
    var pages: Int
    var state = State.queued
    /// Why its notes were not read, in the player's words.
    var reason: String?
    /// Its scan job on the server while it is read by one (version 1.3, decision record 0035 section 6): kept here, on the
    /// device, so a relaunch asks for the same job again; gone once the notes are saved.
    var job: ScanJobRecord?
    /// The pages no engine read, from 1: the notes have the others.
    var unread: [Int]?
    /// The PDF pages the notes have, in order, from 1 (v1.3 i-1b): where a page read again goes among them.
    var read: [Int]?

    var id: String { file }
}

/// What one piece's reading came to: the pages sent, those no engine read, the PDF pages the notes have, and why a page
/// read again was not put in (v1.3 i-1b).
struct RecognitionRead: Equatable {
    var pages = 0
    var unread: [Int] = []
    var read: [Int] = []
    var why: String?
}

/// How the batch follows one piece's scan job: the job kept from before, where a new one's id is kept, who hears its
/// state, whether it is still waited for, and when its readings may be fetched (once the piece is not open on screen).
struct RecognitionJob {
    var kept: ScanJobRecord?
    var keep: (ScanJobRecord) -> Void
    var hear: (ScanJobStatus) -> Void
    var wanted: () -> Bool
    var ready: () async -> Bool
}

/// «Распознать ноты» for several pieces at once (version 0.5, the approved screens import-ipad-4-consent.png and
/// import-ipad-5-status.png): the PDF-only pieces the player picked, sent one at a time to the scan server they agreed to
/// for the batch, the notes put into each same piece. Kept in Application Support after each piece, so it is paused,
/// stopped and carried on after a relaunch.
struct RecognitionBatch: Codable, Equatable, Sendable {
    var id = UUID().uuidString
    /// The scan server the player agreed to send these PDFs to.
    var address: String
    /// Motiff Lab's hosted server, with its daily limit of pages.
    var hosted: Bool
    var items: [RecognitionItem]
    var paused = false
    /// "limit", "offline", "signIn", "needsPro" or "unreachable" when the app paused it.
    var stopped: String?
    var startedAt = Date()

    var done: Int { items.filter { $0.state == .done || $0.state == .failed }.count }
    var read: Int { items.filter { $0.state == .done }.count }
    var failed: Int { items.filter { $0.state == .failed }.count }
    var finished: Bool { !items.contains { $0.state == .queued || $0.state == .reading } }
    var pagesLeft: Int { items.filter { $0.state == .queued || $0.state == .reading }.reduce(0) { $0 + max(1, $1.pages) } }
}

/// The batch beside the folder import's records: `recognition.json`.
enum RecognitionStore {
    static func load(in root: URL = ImportFolders.root) -> RecognitionBatch? {
        guard let data = try? Data(contentsOf: root.appendingPathComponent("recognition.json")) else { return nil }
        return try? ImportFolders.decoder.decode(RecognitionBatch.self, from: data)
    }

    static func save(_ batch: RecognitionBatch?, in root: URL = ImportFolders.root) {
        let url = root.appendingPathComponent("recognition.json")
        guard let batch else {
            try? FileManager.default.removeItem(at: url)
            return
        }
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        if let data = try? ImportFolders.encoder.encode(batch) { try? data.write(to: url, options: .atomic) }
    }
}

/// Motiff Lab's hosted scan server reads 1000 pages a day for each player (Andrei, 2026-11-13; 100 before), counted on this
/// device by player and day (the player's own server has no limit).
enum DailyPages {
    static let limit = 1000
    /// About how long the scan server takes for a page, for the estimates shown.
    static let secondsPerPage = 30

    static func key(player: String, day: Date) -> String {
        let parts = Calendar(identifier: .gregorian).dateComponents([.year, .month, .day], from: day)
        return String(format: "motiff.recognise.hosted.%@.%04d-%02d-%02d", player, parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
    }

    static func used(player: String, on day: Date = Date(), defaults: UserDefaults = .standard) -> Int {
        defaults.integer(forKey: key(player: player, day: day))
    }

    static func add(_ pages: Int, player: String, on day: Date = Date(), defaults: UserDefaults = .standard) {
        defaults.set(used(player: player, on: day, defaults: defaults) + pages, forKey: key(player: player, day: day))
    }

    /// About how long a batch takes, in minutes: half a minute a page, as the scan server reads.
    static func minutes(_ pages: Int) -> Int { max(1, (pages * secondsPerPage + 59) / 60) }
}

/// Why one piece's notes were not read, and whether the batch stops there for it (no connection, no sign-in, no Pro, the
/// server not answering).
struct RecognitionFailure: Error, Equatable {
    let reason: String
    var stop: String?
    /// The piece was opened on screen while its notes were read: they are not written, and it is read again once closed.
    var open = false
}

/// Runs the batch in the background of the library: one piece at a time, the batch kept after each, the library told to
/// read the piece again once its notes are in. A piece whose PDF is in a folder is read there, as opening it does.
@MainActor
final class RecognitionQueue: ObservableObject {
    static let shared = RecognitionQueue()

    @Published private(set) var batch: RecognitionBatch?
    @Published private(set) var running = false
    /// Grows as each piece is done, so the library reads its pieces again.
    @Published private(set) var revision = 0
    /// How far the piece being read is, by its project file's name (issue #479 item 10); nil between pieces.
    @Published private(set) var progress: (file: String, progress: ScanProgress)?
    /// The scan job of the piece being read, as its server last told it: its pages and their engines (version 1.3).
    @Published private(set) var job: (file: String, status: ScanJobStatus)?
    /// How long each page this batch read here took, from send to answer (v0.5.6, #533 section 1), for the time left.
    private(set) var measured: [Int] = []
    var account: Account?
    var root = ImportFolders.root
    var links = OriginalLinks.folder
    var projects: () -> URL = { ProjectFiles.folder }
    var defaults = UserDefaults.standard
    /// What the scan jobs' requests go by: the app's session, a stub's in tests.
    var session = URLSession.shared
    private var restored = false
    /// The project file of the piece open on screen. The batch leaves it queued and never writes it: closing the screen
    /// saves the piece over anything written meanwhile. It is read once the screen closes.
    private(set) var open: String?
    /// The batch stopped only at the open piece, to go on once it closes.
    private var waiting = false
    /// After a relaunch, only the pieces whose scan job goes on on the server are followed, until «Продолжить».
    private var jobsOnly = false

    /// About how many minutes the batch's pages left take, from the pages timed so far, the piece being read's own among
    /// them; nil before the first page is back (v0.5.6, #533 section 1).
    var minutesLeft: Int? {
        guard let batch else { return nil }
        let current = progress?.progress
        // A scan job's server tells its own estimate (version 1.3).
        if let told = current?.told { return told }
        return ScanProgress.minutes(measured: measured + (current?.milliseconds ?? []), pagesLeft: batch.pagesLeft - (current?.done ?? 0))
    }

    /// A batch left unfinished when the app was closed, waiting for «Продолжить».
    var interrupted: Bool { batch.map { !$0.finished && !$0.paused } == true && !running }

    /// Reads the batch kept on the device, once, and starts following at once the pieces whose scan job goes on on the
    /// server (spec part 5: after a relaunch the app asks again); every other piece waits for «Продолжить», as before.
    func restoreAndFollowJobs() {
        guard !restored else { return }
        restored = true
        batch = RecognitionStore.load(in: root)
        for index in batch?.items.indices ?? 0..<0 where batch?.items[index].state == .reading {
            batch?.items[index].state = .queued
        }
        // A scan job goes on on the server while the app is closed: its state is asked again at once (spec part 5).
        if let batch, !batch.paused, batch.items.contains(where: { $0.job != nil && $0.state == .queued }) {
            jobsOnly = true
            Task { await run() }
        }
    }

    /// The pieces the player agreed to send to `address`: added to the batch going to the same server, else a new batch.
    func start(_ items: [RecognitionItem], address: URL, hosted: Bool) {
        if var current = batch, !current.finished, current.address == address.absoluteString {
            let known = Set(current.items.map(\.file))
            current.items += items.filter { !known.contains($0.file) }
            batch = current
        } else {
            batch = RecognitionBatch(address: address.absoluteString, hosted: hosted, items: items)
            measured = []
        }
        jobsOnly = false
        RecognitionStore.save(batch, in: root)
        Task { await run() }
    }

    /// A piece the folder import just added, when the player asked for its notes for the whole import.
    func append(_ url: URL, address: URL, hosted: Bool) async {
        guard let summary = await PieceSummary.read(url), !summary.hasNotes else { return }
        start([Self.item(url, summary)], address: address, hosted: hosted)
    }

    static func item(_ url: URL, _ summary: PieceSummary?) -> RecognitionItem {
        let pages = summary?.originals.first(where: \.isDocument)?.pages ?? summary?.documentPages.count ?? 1
        return RecognitionItem(file: url.lastPathComponent, title: summary?.title ?? ProjectFiles.title(of: url.lastPathComponent), pages: max(1, pages))
    }

    func pause() {
        batch?.paused = true
        RecognitionStore.save(batch, in: root)
    }

    func resume() {
        jobsOnly = false
        batch?.paused = false
        batch?.stopped = nil
        RecognitionStore.save(batch, in: root)
        Task { await run() }
    }

    /// «Повторить» for the pieces whose notes were not read: each sent again, as a new scan job when it is read by one.
    func retryFailed() {
        guard batch != nil else { return }
        for index in batch?.items.indices ?? 0..<0 where batch?.items[index].state == .failed {
            batch?.items[index].state = .queued
            batch?.items[index].reason = nil
            batch?.items[index].job = nil
        }
        resume()
    }

    /// «Повторить» on a piece's «стр. 3 не распознана» (v1.3 i-1b): only those pages are sent again, as a new scan job, and
    /// put into its notes; the piece keeps its notes meanwhile.
    func retryUnread(_ file: String) {
        guard let index = batch?.items.firstIndex(where: { $0.file == file && $0.state == .done && $0.unread != nil }) else { return }
        batch?.items[index].state = .queued
        batch?.items[index].job = nil
        batch?.items[index].reason = nil
        resume()
    }

    /// Stops: the pieces not read yet stay without notes; those read keep theirs. Their scan jobs are deleted on the server.
    func stop() {
        if let batch, let address = URL(string: batch.address), let account {
            let jobs = batch.items.compactMap(\.job)
            let target = ScanTarget(address: address, account: account)
            Task { for job in jobs { await ScanServer.deleteJob(job.id, to: target) } }
        }
        batch = nil
        measured = []
        RecognitionStore.save(nil, in: root)
        revision += 1
    }

    /// The piece now open on screen (nil when none); a piece left for it is read once another is open or none.
    func opened(_ url: URL?) {
        let now = url?.lastPathComponent
        guard now != open else { return }
        open = now
        if waiting {
            waiting = false
            Task { await run() }
        }
    }

    /// The next piece to read: the first queued, except the one open on screen; with `jobsOnly`, the first with a scan job.
    static func next(in batch: RecognitionBatch, open: String?, jobsOnly: Bool = false) -> Int? {
        batch.items.firstIndex { $0.state == .queued && $0.file != open && (!jobsOnly || $0.job != nil) }
    }

    /// What is happening to the piece's notes in the batch, if it is in it.
    func status(of url: URL) -> RecognitionItem? {
        batch?.items.first { $0.file == url.lastPathComponent }
    }

    private func run() async {
        guard !running, let account else { return }
        running = true
        defer {
            running = false
            waiting = batch.map { !$0.paused && $0.items.contains { $0.state == .queued } } ?? false
            revision += 1
        }
        while let current = batch, !current.paused, let index = Self.next(in: current, open: open, jobsOnly: jobsOnly) {
            guard let address = URL(string: current.address) else { return stop() }
            let item = current.items[index]
            if current.hosted, let player = account.player, DailyPages.used(player: player, defaults: defaults) + item.pages > DailyPages.limit {
                batch?.paused = true
                batch?.stopped = "limit"
                return RecognitionStore.save(batch, in: root)
            }
            batch?.items[index].state = .reading
            RecognitionStore.save(batch, in: root)
            let file = item.file
            let outcome = await Self.recognise(
                projects().appendingPathComponent(file), address: address, account: account, links: links, folders: root,
                // Heard only on the way without a job (a job's `hear` sets both), so no job's line stays.
                progress: { [weak self] in
                    self?.job = nil
                    self?.progress = (file, $0)
                },
                job: follow(item, index: index, batch: current.id),
                again: item.unread, read: item.read, session: session,
                isOpen: { self.open == file }
            )
            if let timed = progress, timed.file == file { measured += timed.progress.milliseconds }
            progress = nil
            job = nil
            guard batch?.id == current.id, batch?.items.indices.contains(index) == true else { return }
            switch outcome {
            case .success(let read):
                batch?.items[index].state = .done
                batch?.items[index].job = nil
                batch?.items[index].unread = read.unread.isEmpty ? nil : read.unread
                batch?.items[index].read = read.read
                batch?.items[index].reason = read.why
                if current.hosted, let player = account.player { DailyPages.add(read.pages, player: player, defaults: defaults) }
            case .failure(let failure) where failure.open:
                batch?.items[index].state = .queued
            case .failure(let failure):
                // Pages read again that did not work leave the piece with its notes and the line with «Повторить» (v1.3 i-1b).
                let again = item.unread != nil
                batch?.items[index].state = failure.stop == nil ? (again ? .done : .failed) : .queued
                batch?.items[index].reason = failure.stop == nil ? failure.reason : nil
                // A job that did not work is no longer waited for; one the batch stopped at is asked again on «Продолжить».
                if failure.stop == nil, let sent = batch?.items[index].job {
                    batch?.items[index].job = nil
                    Task { await ScanServer.deleteJob(sent.id, to: ScanTarget(address: address, account: account)) }
                }
                if let stop = failure.stop {
                    batch?.paused = true
                    batch?.stopped = stop
                }
            }
            RecognitionStore.save(batch, in: root)
            revision += 1
        }
    }

    /// How the batch follows the item's scan job: one kept from before, else a new one with scan jobs on (version 1.3);
    /// nil to send the PDF to `/recognise` as before. A new job's id is kept in the batch at once, so a relaunch finds it.
    private func follow(_ item: RecognitionItem, index: Int, batch id: String) -> RecognitionJob? {
        guard item.job != nil || ScanServer.jobs else { return nil }
        let file = item.file
        let wanted = { [weak self] in self?.batch?.id == id && self?.batch?.paused == false }
        return RecognitionJob(
            kept: item.job,
            keep: { [weak self] made in
                guard let self, self.batch?.id == id, self.batch?.items.indices.contains(index) == true else { return }
                self.batch?.items[index].job = made
                RecognitionStore.save(self.batch, in: self.root)
            },
            hear: { [weak self] status in
                self?.job = (file, status)
                self?.progress = (file, status.progress)
            },
            wanted: wanted,
            ready: { [weak self] in
                // The screen saves the open piece over anything written meanwhile: its readings wait until it closes.
                while wanted(), self?.open == file { try? await Task.sleep(nanoseconds: 2_000_000_000) }
                return wanted()
            }
        )
    }

    /// One piece's PDF read by the scan server and its notes put into the same piece (`document.read`), as «Распознать
    /// ноты» on its PDF does: the pages sent and those no engine read, or why not. `isOpen` says, just before the notes
    /// are written, whether the piece is open on screen now; `progress` hears as each page is sent and read; `job`, the scan
    /// job it is read by (version 1.3), deleted on the server once the notes are written, its requests going by `session`;
    /// `again`, the pages of a piece with notes no engine read, read again into its notes (v1.3 i-1b).
    static func recognise(_ url: URL, address: URL, account: Account, links: URL, folders: URL, progress: ScanProgressListener? = nil, job: RecognitionJob? = nil, again unread: [Int]? = nil, read notes: [Int]? = nil, session: URLSession = .shared, isOpen: () -> Bool) async -> Result<RecognitionRead, RecognitionFailure> {
        let text = UIText.current
        guard let summary = await PieceSummary.read(url), let project = try? Data(contentsOf: url) else {
            return .failure(RecognitionFailure(reason: text["recognition.failMissing"]))
        }
        if summary.hasNotes {
            guard let unread, !unread.isEmpty else { return .success(RecognitionRead()) }
            guard let job, let notes else { return .failure(RecognitionFailure(reason: text["pdf.readNone"])) }
            let pages = max(1, summary.originals.first(where: \.isDocument)?.pages ?? summary.documentPages.count)
            return await readAgain(unread, read: notes, pages: pages, url: url, project: project, to: ScanTarget(address: address, account: account, session: session), text: text, job: job, isOpen: isOpen)
        }
        var pdf: Data?
        if AwayPdf.isAway(summary) {
            switch await AwayPdf.read(url, summary, links: links, folders: folders) {
            case .success(let found): pdf = found.data
            case .failure(let reason): return .failure(RecognitionFailure(reason: reason.text(text), stop: reason == .offline ? "offline" : nil))
            }
        }
        guard case .success(let open) = await OpenPiece.open(fileName: url.lastPathComponent, data: project, language: text.language, pdf: pdf), open.isDocument else {
            return .failure(RecognitionFailure(reason: text["pdf.unreadable"]))
        }
        let piece = open.id
        let outcome = await read(piece, url: url, to: ScanTarget(address: address, account: account, session: session), text: text, progress: progress, job: job, isOpen: isOpen)
        _ = await MusicCore.run { MusicCore.close(piece: piece) }
        return outcome
    }

    private static func read(_ piece: Int32, url: URL, to target: ScanTarget, text: UIText, progress: ScanProgressListener?, job: RecognitionJob?, isOpen: () -> Bool) async -> Result<RecognitionRead, RecognitionFailure> {
        let document = await MusicCore.run { PieceCore(piece: piece).send(.document) }
        guard let object = facadeObject(document), let base64 = object["pdf"] as? String, let data = Data(base64Encoded: base64) else {
            return .failure(RecognitionFailure(reason: text["pdf.unreadable"]))
        }
        let name = (object["fileName"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "score.pdf"
        let pages = max(1, PDFDocument(data: data)?.pageCount ?? 1)
        var sent: ScanJobRecord?
        let scanning: Result<ScanResult, ScanProblem>
        if let job, pages <= ScanParts.mostPages,
           let byJob = await ScanServer.readByJob(fileName: name, data: data, pages: pages, to: target, job: job.kept, keep: job.keep, hear: job.hear, wanted: job.wanted, ready: job.ready) {
            scanning = byJob.map { $0.result }
            if case .success(let read) = byJob { sent = read.job }
        } else {
            scanning = await ScanServer.recognise(fileName: name, data: data, address: target.address, account: target.account, progress: progress)
        }
        switch scanning {
        case .failure(.stopped):
            return .failure(RecognitionFailure(reason: "", open: true))
        case .failure(let problem):
            return .failure(RecognitionFailure(reason: problem.text(text), stop: stop(problem)))
        case .success(let scanned):
            let request = DocumentRead.request(musicXml: scanned.musicXml, engine: scanned.engine, pages: pages, reports: nil, language: text.language, now: Date())
            let answer = await DocumentRead.call(piece: piece, request)
            guard let read = facadeObject(answer), read["error"] == nil, let file = read["file"] as? String else {
                return .failure(RecognitionFailure(reason: facadeObject(answer)?["message"] as? String ?? text["pdf.readNone"]))
            }
            if isOpen() { return .failure(RecognitionFailure(reason: "", open: true)) }
            do {
                try Data(file.utf8).write(to: url, options: .atomic)
            } catch {
                return .failure(RecognitionFailure(reason: error.localizedDescription))
            }
            // Saved: the job goes from the server now, not after its 10 minutes' grace (lead's decision, version 1.3).
            if let sent { await ScanServer.deleteJob(sent.id, to: target) }
            return .success(RecognitionRead(pages: pages, unread: scanned.unread, read: scanned.read))
        }
    }

    /// The pages of a piece with notes no engine read, read again and put into its notes (`ScanPageAgain`, v1.3 i-1b), its
    /// PDF of `pages` pages having `read` in the notes: the file written unless the piece is open on screen now, then the job
    /// deleted; the pages sent, those still unread and why.
    private static func readAgain(_ unread: [Int], read notes: [Int], pages: Int, url: URL, project: Data, to target: ScanTarget, text: UIText, job: RecognitionJob, isOpen: () -> Bool) async -> Result<RecognitionRead, RecognitionFailure> {
        guard case .success(let open) = await OpenPiece.open(fileName: url.lastPathComponent, data: project, language: text.language, asPdf: true), open.isDocument else {
            return .failure(RecognitionFailure(reason: text["pdf.unreadable"]))
        }
        let piece = open.id
        let document = await MusicCore.run { PieceCore(piece: piece).send(.document) }
        _ = await MusicCore.run { MusicCore.close(piece: piece) }
        guard let base64 = facadeObject(document)?["pdf"] as? String, let data = Data(base64Encoded: base64) else {
            return .failure(RecognitionFailure(reason: text["pdf.unreadable"]))
        }
        switch await ScanPageAgain.read(unread, read: notes, pages: pages, file: url, pdf: data, to: target, job: job, text: text) {
        case .failure(.stopped):
            return .failure(RecognitionFailure(reason: "", open: true))
        case .failure(let problem):
            return .failure(RecognitionFailure(reason: problem.text(text), stop: stop(problem)))
        case .success(let outcome):
            if isOpen() { return .failure(RecognitionFailure(reason: "", open: true)) }
            do {
                try Data(outcome.file.utf8).write(to: url, options: .atomic)
            } catch {
                return .failure(RecognitionFailure(reason: error.localizedDescription))
            }
            await ScanServer.deleteJob(outcome.job.id, to: target)
            return .success(RecognitionRead(pages: unread.count, unread: outcome.unread, read: outcome.read, why: outcome.why))
        }
    }

    /// The problems that stop the batch rather than fail one piece: every piece after would meet them too.
    private static func stop(_ problem: ScanProblem) -> String? {
        switch problem {
        case .signIn: "signIn"
        case .needsPro: "needsPro"
        case .unreachable, .notAScanServer: "unreachable"
        case .tooLarge, .tooManyPages, .server, .stopped: nil
        }
    }
}
