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

    var id: String { file }
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
    /// How long each page this batch read here took, from send to answer (v0.5.6, #533 section 1), for the time left.
    private(set) var measured: [Int] = []
    var account: Account?
    var root = ImportFolders.root
    var links = OriginalLinks.folder
    var projects: () -> URL = { ProjectFiles.folder }
    var defaults = UserDefaults.standard
    private var restored = false
    /// The project file of the piece open on screen. The batch leaves it queued and never writes it: closing the screen
    /// saves the piece over anything written meanwhile. It is read once the screen closes.
    private(set) var open: String?
    /// The batch stopped only at the open piece, to go on once it closes.
    private var waiting = false

    /// About how many minutes the batch's pages left take, from the pages timed so far, the piece being read's own among
    /// them; nil before the first page is back (v0.5.6, #533 section 1).
    var minutesLeft: Int? {
        guard let batch else { return nil }
        let current = progress?.progress
        return ScanProgress.minutes(measured: measured + (current?.milliseconds ?? []), pagesLeft: batch.pagesLeft - (current?.done ?? 0))
    }

    /// A batch left unfinished when the app was closed, waiting for «Продолжить».
    var interrupted: Bool { batch.map { !$0.finished && !$0.paused } == true && !running }

    func restore() {
        guard !restored else { return }
        restored = true
        batch = RecognitionStore.load(in: root)
        for index in batch?.items.indices ?? 0..<0 where batch?.items[index].state == .reading {
            batch?.items[index].state = .queued
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
        batch?.paused = false
        batch?.stopped = nil
        RecognitionStore.save(batch, in: root)
        Task { await run() }
    }

    /// Stops: the pieces not read yet stay without notes; those read keep theirs.
    func stop() {
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

    /// The next piece to read: the first queued, except the one open on screen.
    static func next(in batch: RecognitionBatch, open: String?) -> Int? {
        batch.items.firstIndex { $0.state == .queued && $0.file != open }
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
        while let current = batch, !current.paused, let index = Self.next(in: current, open: open) {
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
                progress: { [weak self] in self?.progress = (file, $0) },
                isOpen: { self.open == file }
            )
            if let timed = progress, timed.file == file { measured += timed.progress.milliseconds }
            progress = nil
            guard batch?.id == current.id, batch?.items.indices.contains(index) == true else { return }
            switch outcome {
            case .success(let pages):
                batch?.items[index].state = .done
                if current.hosted, let player = account.player { DailyPages.add(pages, player: player, defaults: defaults) }
            case .failure(let failure) where failure.open:
                batch?.items[index].state = .queued
            case .failure(let failure):
                batch?.items[index].state = failure.stop == nil ? .failed : .queued
                batch?.items[index].reason = failure.stop == nil ? failure.reason : nil
                if let stop = failure.stop {
                    batch?.paused = true
                    batch?.stopped = stop
                }
            }
            RecognitionStore.save(batch, in: root)
            revision += 1
        }
    }

    /// One piece's PDF read by the scan server and its notes put into the same piece (`document.read`), as «Распознать
    /// ноты» on its PDF does: the pages sent, or why not. `isOpen` says, just before the notes are written, whether the piece
    /// is open on screen now; `progress` hears as each page is sent and read.
    static func recognise(_ url: URL, address: URL, account: Account, links: URL, folders: URL, progress: ScanProgressListener? = nil, isOpen: () -> Bool) async -> Result<Int, RecognitionFailure> {
        let text = UIText.current
        guard let summary = await PieceSummary.read(url), let project = try? Data(contentsOf: url) else {
            return .failure(RecognitionFailure(reason: text["recognition.failMissing"]))
        }
        if summary.hasNotes { return .success(0) }
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
        let outcome = await read(piece, url: url, address: address, account: account, text: text, progress: progress, isOpen: isOpen)
        _ = await MusicCore.run { MusicCore.close(piece: piece) }
        return outcome
    }

    private static func read(_ piece: Int32, url: URL, address: URL, account: Account, text: UIText, progress: ScanProgressListener?, isOpen: () -> Bool) async -> Result<Int, RecognitionFailure> {
        let document = await MusicCore.run { MusicCore.call(piece: piece, "document") }
        guard let object = facadeObject(document), let base64 = object["pdf"] as? String, let data = Data(base64Encoded: base64) else {
            return .failure(RecognitionFailure(reason: text["pdf.unreadable"]))
        }
        let name = (object["fileName"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "score.pdf"
        let pages = max(1, PDFDocument(data: data)?.pageCount ?? 1)
        switch await ScanServer.recognise(fileName: name, data: data, address: address, account: account, progress: progress) {
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
            return .success(pages)
        }
    }

    /// The problems that stop the batch rather than fail one piece: every piece after would meet them too.
    private static func stop(_ problem: ScanProblem) -> String? {
        switch problem {
        case .signIn: "signIn"
        case .needsPro: "needsPro"
        case .unreachable, .notAScanServer: "unreachable"
        case .tooLarge, .tooManyPages, .server: nil
        }
    }
}
