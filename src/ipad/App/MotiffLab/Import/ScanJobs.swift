import Foundation
import PDFKit

/// What a scan server says of itself on `/health` (decision record 0035 section 2): the engine an upload without one gets,
/// and, since version 1.3, each engine's seconds a page as measured there and its queue, for the estimate before sending.
struct ScanHealth: Equatable, Sendable {
    /// Such as "homr 0.7.0".
    let engine: String
    /// Each engine's seconds a page; empty from a server that takes no jobs.
    var secondsPerPage: [String: Double] = [:]
    /// The units (a page read by one engine) waiting in its queue, and how many it reads at once.
    var queue = 0
    var workers = 1

    init(engine: String, secondsPerPage: [String: Double] = [:], queue: Int = 0, workers: Int = 1) {
        self.engine = engine
        self.secondsPerPage = secondsPerPage
        self.queue = queue
        self.workers = max(1, workers)
    }

    /// The answer of `/health` read; nil when it names no engine, as no scan server's does.
    init?(_ object: [String: Any]) {
        guard let name = object["engine"] as? String, !name.isEmpty else { return nil }
        engine = "\(name) \(object["version"] as? String ?? "")".trimmingCharacters(in: .whitespaces)
        for entry in object["engines"] as? [[String: Any]] ?? [] {
            if let id = entry["engine"] as? String, let seconds = (entry["secondsPerPage"] as? NSNumber)?.doubleValue {
                secondsPerPage[id] = seconds
            }
        }
        let jobs = object["jobs"] as? [String: Any]
        queue = (jobs?["queue"] as? NSNumber)?.intValue ?? 0
        workers = max(1, (jobs?["workers"] as? NSNumber)?.intValue ?? 1)
    }

    /// Whether the server takes scan jobs (version 1.3): only such a server times its engines.
    var takesJobs: Bool { !secondsPerPage.isEmpty }

    /// About how many minutes `pages` pages take there, at least one: every engine reads each page, after the units already
    /// queued (each at the engines' mean time), as many at once as the server reads, and never less than its slowest
    /// engine takes for a page; nil for no page or no timed engine.
    func minutes(pages: Int) -> Int? {
        guard pages > 0, takesJobs, let slowest = secondsPerPage.values.max() else { return nil }
        let all = secondsPerPage.values.reduce(0, +)
        let ahead = Double(queue) * all / Double(secondsPerPage.count)
        let seconds = max(slowest, (Double(pages) * all + ahead) / Double(workers))
        return max(1, Int((seconds / 60).rounded(.up)))
    }
}

/// A scan job's state as `GET /jobs/{id}` gives it (decision record 0035 section 2).
struct ScanJobStatus: Equatable, Sendable {
    struct Page: Equatable, Sendable {
        let page: Int
        /// Each engine's state: "waiting", "reading", "read", "retrying" or "not_read".
        let engines: [String: String]

        /// Every engine is done with the page, whether it read it or not.
        var finished: Bool { engines.values.allSatisfy { $0 == "read" || $0 == "not_read" } }
    }

    /// "queued", "running", "done" or "failed".
    let state: String
    let pages: [Page]
    /// The server's estimate of the seconds left.
    let etaSeconds: Int

    init(state: String, pages: [Page], etaSeconds: Int) {
        self.state = state
        self.pages = pages
        self.etaSeconds = etaSeconds
    }

    init?(_ object: [String: Any]) {
        guard let state = object["state"] as? String else { return nil }
        self.state = state
        pages = (object["pages"] as? [[String: Any]] ?? []).compactMap { page in
            guard let number = page["page"] as? Int else { return nil }
            return Page(page: number, engines: page["engines"] as? [String: String] ?? [:])
        }
        etaSeconds = (object["etaSeconds"] as? NSNumber)?.intValue ?? 0
    }

    /// Every unit has finished: the readings can be fetched.
    var finished: Bool { state == "done" || state == "failed" }

    /// As the bars and lines of a score being read show it: the pages finished, and the server's minutes left.
    var progress: ScanProgress {
        ScanProgress(pages: pages.count, done: pages.filter(\.finished).count, minutesLeft: finished ? nil : max(1, (etaSeconds + 59) / 60))
    }
}

/// A scan job the app sent, kept on the device (`RecognitionItem.job`) until what it read is saved (0035 section 6).
struct ScanJobRecord: Codable, Equatable, Sendable {
    let id: String
    /// The pages it reads, from 1.
    let pages: [Int]
}

/// A PDF's pages before they are sent as a scan job (decision records 0029 «Авто», 0035 section 6): those the vector reader
/// read on this iPad, and those that go to the server; a page with no music is in neither.
struct ScanJobPlan: Sendable {
    /// Page number to its MusicXML, read on the device.
    var device: [Int: String] = [:]
    /// Page number to its page report as the vector reader wrote it (`X-Page-Report`'s form), for the pages read here.
    var reports: [Int: String] = [:]
    /// The vector reader's name ("vector 1"), when it read a page.
    var engine: String?
    var server: [Int] = []

    /// The PDF's pages as the vector reader leaves them; all of them for the server when it cannot open the file.
    static func of(_ data: Data, pages: Int) async -> ScanJobPlan {
        guard let vector = await ScanServer.vectorPages(data) else { return ScanJobPlan(server: Array(1...max(1, pages))) }
        var plan = ScanJobPlan()
        for page in vector.pages {
            if let musicXml = page.musicXml {
                plan.device[page.number] = musicXml
                plan.reports[page.number] = page.report
            } else if page.kind == "scan" {
                plan.server.append(page.number)
            }
        }
        plan.engine = plan.device.isEmpty ? nil : vector.engine
        return plan
    }

    /// «4 страницы прочитаны на iPad. 2 страницы отправим на распознавание, около 3 минут.»
    func line(_ text: UIText, minutes: Int?) -> String {
        var parts: [String] = []
        if !device.isEmpty { parts.append(text.count("scanJob.readHere", device.count)) }
        if !server.isEmpty {
            let about = minutes.map { text.format("scanJob.about", text.count("scanJob.minutes", $0)) } ?? ""
            parts.append(text.count("scanJob.toServer", server.count) + about + ".")
        }
        return parts.joined(separator: " ")
    }
}

/// Scan jobs (decision record 0035): how often their state is asked, and what their result gives music-core to merge.
enum ScanJobs {
    /// The order the readings of a page go to the merge in, which settles a tie last: JSON keeps no order, so a fixed one.
    static let engineOrder = ["homr", "copisteria", "audiveris", "transcoda"]

    /// The engines named, in `engineOrder`, any other after them by name.
    static func ordered<Names: Sequence>(_ names: Names) -> [String] where Names.Element == String {
        let last = engineOrder.count
        return names.sorted { (engineOrder.firstIndex(of: $0) ?? last, $0) < (engineOrder.firstIndex(of: $1) ?? last, $1) }
    }

    /// A status a job's state is asked again after: the server busy (429) or failing (5xx).
    static func isMiss(_ status: Int) -> Bool {
        status == 429 || (500..<600).contains(status)
    }

    /// The seconds before a job's state is asked again (spec part 2a: every 2–3 s while the app is open): 2.5 s while the
    /// server answers, twice as long after each ask in a row it did not answer, at most a minute.
    static func pause(misses: Int) -> Double {
        min(60, 2.5 * pow(2, Double(min(max(0, misses), 6))))
    }

    /// `scan.join`'s pages (forms of c-1a and c-1b) from the pages read on the device and a job's result: each page in
    /// order, as its MusicXML when read here, else as the readings of the engines that read it, with the page's evidence when
    /// the server gave one. A page no engine read is left out, as «Авто» leaves out a page with no music, and named in
    /// `unread`; `engines` names the engines that read, with their versions, as they first come. `reports` gives each page
    /// joined its page report, as `/recognise` and «Авто» pass them on: the device's, else that of the first engine in
    /// order that read it; nil unless every page has one. `numbers` names each page's number in the PDF, in the same order.
    static func joined(device: [Int: String], reports deviceReports: [Int: String] = [:], server: [[String: Any]]) -> (pages: [Any], engines: [String], unread: [Int], reports: [String]?, numbers: [Int]) {
        var pages: [Int: Any] = device.mapValues { $0 as Any }
        var reports = deviceReports
        var engines: [String] = []
        var unread: [Int] = []
        for entry in server.sorted(by: { ($0["page"] as? Int ?? 0) < ($1["page"] as? Int ?? 0) }) {
            guard let number = entry["page"] as? Int else { continue }
            let read = entry["engines"] as? [String: [String: Any]] ?? [:]
            var readings: [[String: String]] = []
            for name in ordered(read.keys) {
                guard let reading = read[name], reading["state"] as? String == "read", let musicXml = reading["musicxml"] as? String else { continue }
                readings.append(["engine": name, "musicxml": musicXml])
                if reports[number] == nil, let report = reading["report"] as? [String: Any],
                   let written = try? JSONSerialization.data(withJSONObject: [report], options: [.sortedKeys]) {
                    reports[number] = String(decoding: written, as: UTF8.self)
                }
                let named = [name, reading["version"] as? String].compactMap { $0 }.joined(separator: " ")
                if !engines.contains(named) { engines.append(named) }
            }
            guard !readings.isEmpty else {
                unread.append(number)
                continue
            }
            var page: [String: Any] = ["readings": readings]
            if let evidence = entry["evidence"] { page["evidence"] = evidence }
            pages[number] = page
        }
        let order = pages.keys.sorted()
        let told = order.compactMap { reports[$0] }
        return (order.compactMap { pages[$0] }, engines, unread.sorted(), told.count == order.count ? told : nil, order)
    }
}

extension ScanServer {
    /// The line before sending (0035 section 6): what the iPad read itself, what goes to the server, about how long; nil
    /// when the file is not a PDF or the server takes no jobs.
    static func jobPlanLine(data: Data?, address: URL?, text: UIText) async -> String? {
        guard let data, let address, data.starts(with: Data("%PDF".utf8)), let document = PDFDocument(data: data) else { return nil }
        guard case .success(let server) = await health(address), server.takesJobs else { return nil }
        let plan = await ScanJobPlan.of(data, pages: document.pageCount)
        return plan.line(text, minutes: server.minutes(pages: plan.server.count))
    }

    /// A PDF read by a scan job (0035 section 6): the pages the vector reader reads here stay here; the others go as one
    /// job (`kept` resumes one sent before, `keep` hears a new one's id to keep on the device), whose state is asked until
    /// it is done (`hear` hears each), its readings fetched once `ready` says so, and merged with the device's pages by
    /// music-core (`scan.join`); the merge's bars to check are kept on this iPad with their variants and crops
    /// (`ScanCrops.keepReview`, v1.3 i-2). The job (`readJob`) stays on the server: the caller deletes it once the notes are saved. Nil, before
    /// anything is sent, when the server takes no jobs: the caller sends the PDF to `/recognise` as before.
    static func readByJob(
        fileName: String, data: Data, pages: Int, to target: ScanTarget, job kept: ScanJobRecord? = nil,
        keep: (ScanJobRecord) -> Void = { _ in }, hear: (ScanJobStatus) -> Void = { _ in },
        wanted: () -> Bool = { !Task.isCancelled }, ready: () async -> Bool = { !Task.isCancelled },
        pause: (Double) async -> Void = { try? await Task.sleep(nanoseconds: UInt64($0 * 1_000_000_000)) }
    ) async -> Result<(result: ScanResult, job: ScanJobRecord?), ScanProblem>? {
        if kept == nil {
            guard case .success(let server) = await health(target.address, session: target.session), server.takesJobs else { return nil }
        }
        let plan = await ScanJobPlan.of(data, pages: pages)
        var read: [[String: Any]] = []
        var job = kept
        if kept != nil || !plan.server.isEmpty {
            switch await readJob(fileName: fileName, data: data, pages: plan.server, to: target, job: kept, keep: keep, hear: hear, wanted: wanted, ready: ready, pause: pause) {
            case .failure(let problem): return .failure(problem)
            case .success(let done):
                read = done.pages
                job = done.job
            }
        }
        let text = UIText.current
        let joined = ScanJobs.joined(device: plan.device, reports: plan.reports, server: read)
        guard !joined.pages.isEmpty else { return .failure(.server(text["pdf.readNone"])) }
        let json = facadeRequest(["pages": joined.pages])
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "scan.join", json) }
        guard let object = facadeObject(answer), object["error"] == nil, let score = object["score"] as? String else {
            return .failure(.server(text["import.scanPagesNotJoined"]))
        }
        ScanCrops.keepReview(object, pages: joined.numbers, pdf: data)
        let engines = [plan.engine].compactMap { $0 } + joined.engines
        let result = ScanResult(musicXml: Data(score.utf8), engine: engines.joined(separator: " + "), pages: pages, partReports: joined.reports, unread: joined.unread)
        return .success((result, job))
    }

    /// The PDF's `pages` read by a scan job: `kept`, one sent before, else a new one (`keep` hears its id to keep on the
    /// device), asked until it is done (`hear` hears each state) and its pages fetched once `ready` says so: each page's
    /// engines and evidence as `GET /jobs/{id}/result` gives them. The job stays on the server: the caller deletes it.
    static func readJob(
        fileName: String, data: Data, pages: [Int], to target: ScanTarget, job kept: ScanJobRecord?, keep: (ScanJobRecord) -> Void,
        hear: (ScanJobStatus) -> Void, wanted: () -> Bool, ready: () async -> Bool,
        pause: (Double) async -> Void = { try? await Task.sleep(nanoseconds: UInt64($0 * 1_000_000_000)) }
    ) async -> Result<(pages: [[String: Any]], job: ScanJobRecord), ScanProblem> {
        let job: ScanJobRecord
        if let kept {
            job = kept
        } else {
            switch await submitJob(fileName: fileName, data: data, pages: pages, to: target) {
            case .failure(let problem): return .failure(problem)
            case .success(let id):
                job = ScanJobRecord(id: id, pages: pages)
                keep(job)
            }
        }
        return await waitForJob(job, to: target, hear: hear, wanted: wanted, ready: ready, pause: pause).map { read in (pages: read, job: job) }
    }

    /// `POST /jobs` with the pages to read: the job's id, or why not.
    static func submitJob(fileName: String, data: Data, pages: [Int], to target: ScanTarget) async -> Result<String, ScanProblem> {
        guard data.count <= maxUploadBytes else { return .failure(.tooLarge) }
        let boundary = "motiff-\(UUID().uuidString)"
        var request = URLRequest(url: target.address.appendingPathComponent("jobs"))
        request.httpMethod = "POST"
        request.timeoutInterval = 120
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        guard await target.sign(&request) else { return .failure(.signIn) }
        request.httpBody = form([("pages", pages.map(String.init).joined(separator: ","))], fileName: fileName, data: data, boundary: boundary)
        switch answered(await send(request, to: target), to: target) {
        case .failure(let problem): return .failure(problem)
        case .success(let answer):
            let object = try? JSONSerialization.jsonObject(with: answer) as? [String: Any]
            guard let id = object?["id"] as? String, !id.isEmpty else { return .failure(.notAScanServer) }
            return .success(id)
        }
    }

    /// `DELETE /jobs/{id}`: the server forgets the job's pages and readings. Its answer changes nothing here.
    static func deleteJob(_ id: String, to target: ScanTarget) async {
        _ = await jobCall(["jobs", id], method: "DELETE", to: target)
    }

    /// Asks the job's state every few seconds, longer while the server does not answer or is busy (no answer, a 5xx or a
    /// 429 is a miss, `ScanJobs.pause`), until every unit has finished, then fetches its pages once `ready`; `.stopped`
    /// once `wanted` or `ready` says no. Any other refusal ends it: a 404 is a job the server no longer has.
    private static func waitForJob(_ job: ScanJobRecord, to target: ScanTarget, hear: (ScanJobStatus) -> Void, wanted: () -> Bool, ready: () async -> Bool, pause: (Double) async -> Void) async -> Result<[[String: Any]], ScanProblem> {
        var misses = 0
        while wanted() {
            let answer = await jobCall(["jobs", job.id], to: target)
            if answer.isMiss {
                misses += 1
            } else {
                misses = 0
                switch answered(answer, to: target) {
                case .failure(let problem): return .failure(problem)
                case .success(let body):
                    let object = try? JSONSerialization.jsonObject(with: body) as? [String: Any]
                    guard let status = object.flatMap({ ScanJobStatus($0) }) else { return .failure(.notAScanServer) }
                    hear(status)
                    if status.finished { return await jobPages(job, to: target, ready: ready) }
                }
            }
            await pause(ScanJobs.pause(misses: misses))
        }
        return .failure(.stopped)
    }

    /// `GET /jobs/{id}/result`'s pages, once the app may take them; a miss is `.unreachable`, which stops a batch with the
    /// job kept, to be asked again on «Продолжить».
    private static func jobPages(_ job: ScanJobRecord, to target: ScanTarget, ready: () async -> Bool) async -> Result<[[String: Any]], ScanProblem> {
        guard await ready() else { return .failure(.stopped) }
        let answer = await jobCall(["jobs", job.id, "result"], to: target)
        if answer.isMiss { return .failure(.unreachable(target.address)) }
        switch answered(answer, to: target) {
        case .failure(let problem): return .failure(problem)
        case .success(let body):
            let object = try? JSONSerialization.jsonObject(with: body) as? [String: Any]
            guard let pages = object?["pages"] as? [[String: Any]] else { return .failure(.notAScanServer) }
            return .success(pages)
        }
    }

    /// A request on a job, signed for Motiff Lab's server as an upload is.
    private static func jobCall(_ path: [String], method: String = "GET", to target: ScanTarget) async -> ScanJobAnswer {
        var request = URLRequest(url: path.reduce(target.address) { $0.appendingPathComponent($1) })
        request.httpMethod = method
        request.timeoutInterval = 30
        guard await target.sign(&request) else { return .notSignedIn }
        return await send(request, to: target)
    }

    private static func send(_ request: URLRequest, to target: ScanTarget) async -> ScanJobAnswer {
        guard let answer = try? await target.session.data(for: request) else { return .unanswered }
        return .answered(status: (answer.1 as? HTTPURLResponse)?.statusCode ?? 0, body: answer.0)
    }

    /// An answer as the player's words: its body for a 2xx, else why not.
    private static func answered(_ answer: ScanJobAnswer, to target: ScanTarget) -> Result<Data, ScanProblem> {
        switch answer {
        case .notSignedIn: return .failure(.signIn)
        case .unanswered: return .failure(.unreachable(target.address))
        case .answered(let status, let body):
            return (200..<300).contains(status) ? .success(body) : .failure(problem(status, body, hosted: target.hosted, engine: nil))
        }
    }
}

/// What a request on a scan job came to.
enum ScanJobAnswer {
    /// The player has no token for Motiff Lab's server: nothing was sent.
    case notSignedIn
    /// No answer, or none in time.
    case unanswered
    case answered(status: Int, body: Data)

    /// The state is asked again after it, the pause growing: no answer, or the server busy or failing.
    var isMiss: Bool {
        switch self {
        case .notSignedIn: false
        case .unanswered: true
        case .answered(let status, _): ScanJobs.isMiss(status)
        }
    }
}
