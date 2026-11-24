import PDFKit
import SwiftUI

/// What a scan server read from a PDF: the MusicXML, the engine that read it and how many pages it read.
struct ScanResult: Sendable {
    let musicXml: Data
    let engine: String
    let pages: Int
    /// The server's report on each page (`X-Page-Report`, numbers only), when it sends one.
    var report: String?
    /// A PDF read in parts (issue #479): each part's report, in order, when every part sent one.
    var partReports: [String]?
    /// A PDF read by a scan job (decision record 0035): its pages no engine read, from 1, left out of the notes.
    var unread: [Int] = []

    /// The reports on the pages as `document.read` takes them: one header per request, in order.
    var reports: [String]? { partReports ?? report.map { [$0] } }
}

/// Sheet music scanning on the iPad (0011, 0014, 0023), as the web app's `ScanService` does it: the PDF goes to the scan
/// server, which reads it with homr and gives MusicXML back. In a build with sign-in, Motiff Lab's hosted server is the
/// choice until the player gives their own server's address or turns scanning off in Settings; it reads only for a
/// signed-in player with Pro. In a build without sign-in, scanning is off until the player gives an address. The PDF goes
/// only to that address, and only when the player sends it; the server deletes it once read, or, sent as a scan job
/// (`ScanJobs.swift`, the experiment `jobsKey`), once the app has fetched what it read, and at the latest after 24 hours.
@MainActor
final class ScanServer: ObservableObject {
    static let shared = ScanServer()

    /// Where the player's choice is kept on the device: an address, or `off`.
    static let addressKey = "motiff.scan.address"
    static let off = "off"
    /// The address the service's README starts it on.
    static let suggested = "http://localhost:8765"
    /// The largest file the scan server reads (src/backend/omr `MAX_UPLOAD_MB`, 50 there and on Motiff Lab's server): a larger
    /// one is not sent, so it is not refused after the whole upload (the web app's `ScanService.MaxUploadBytes`).
    static let maxUploadBytes = 50 * 1024 * 1024
    /// «Движок распознавания» (v0.5.8; a setting since v0.6.5, homr by default as before): each scan names the engine
    /// chosen in `engineChoice` (homr or copisteria) to the server, so the two can be compared on the player's own pages.
    static let engineChoice = "scan.engine"

    /// The engine to name to the server: the one chosen; «Авто» is never named: the pages it sends go to the server's own
    /// engine (`VectorPages.swift`), nor nothing, before the settings are read.
    static func chosenEngine(_ settings: DeviceSettings) -> String? {
        let chosen = settings[engineChoice]
        return chosen.isEmpty || chosen == auto ? nil : chosen
    }

    /// The experiment «Распознавание в фоне» (version 1.3, spec part 5): scans go as jobs to the server's four engines
    /// (`ScanJobs.swift`); off, to `/recognise` as before.
    static let jobsKey = "experimental.scanJobs"

    static func jobsOn(_ settings: DeviceSettings) -> Bool { settings.isOn(jobsKey) }

    /// Whether scan jobs are on, in the settings of this iPad.
    static var jobs: Bool { jobsOn(DeviceSettings.shared) }

    /// True when the player chose «Авто»: the pages a notation program drew are read on the device.
    static func choseAuto(_ settings: DeviceSettings) -> Bool {
        settings[engineChoice] == auto
    }

    /// What the player chose: their own server's address, `off`, or nil for the hosted server where there is one.
    @Published private(set) var chosen: String?
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        chosen = defaults.string(forKey: Self.addressKey)
    }

    /// The address scanning uses: the player's own server, else the hosted one (nil in a build without sign-in), or nil
    /// once scanning is turned off.
    func address(hosted: URL?) -> URL? {
        switch chosen {
        case Self.off?: nil
        case let text?: Self.parse(text) ?? hosted
        case nil: hosted
        }
    }

    /// Keeps the player's own server, or turns scanning off with nil.
    func choose(_ address: URL?) {
        chosen = address?.absoluteString ?? Self.off
        defaults.set(chosen, forKey: Self.addressKey)
    }

    /// Goes back to Motiff Lab's server, in a build with sign-in.
    func chooseHosted() {
        chosen = nil
        defaults.removeObject(forKey: Self.addressKey)
    }

    /// An absolute http or https address with a host and no path, query or user name; nil otherwise.
    nonisolated static func parse(_ text: String?) -> URL? {
        guard let text = text?.trimmingCharacters(in: .whitespaces), let parts = URLComponents(string: text),
              parts.scheme == "http" || parts.scheme == "https", let host = parts.host, !host.isEmpty,
              parts.path.isEmpty || parts.path == "/", parts.query == nil, parts.fragment == nil, parts.user == nil
        else { return nil }
        var clean = parts
        clean.path = "/"
        return clean.url
    }

    /// Motiff Lab's scan server, in a build with sign-in.
    static func hosted(_ account: Account) -> URL? {
        account.options.flatMap { parse($0.scan) }
    }

    /// True when the address is this device, so the PDF does not leave it.
    nonisolated static func isLocal(_ address: URL) -> Bool {
        ["localhost", "127.0.0.1", "::1"].contains(address.host ?? "")
    }

    /// The engine the server runs, such as "homr 0.7.0", or why it cannot be reached.
    nonisolated static func check(_ address: URL, session: URLSession = .shared) async -> Result<String, ScanProblem> {
        await health(address, session: session).map(\.engine)
    }

    /// What the server says of itself on `/health`, or why it cannot be reached.
    nonisolated static func health(_ address: URL, session: URLSession = .shared) async -> Result<ScanHealth, ScanProblem> {
        var request = URLRequest(url: address.appendingPathComponent("health"))
        request.timeoutInterval = 10
        do {
            let (body, response) = try await session.data(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200,
                  let object = try? JSONSerialization.jsonObject(with: body) as? [String: Any], let health = ScanHealth(object)
            else { return .failure(.notAScanServer) }
            return .success(health)
        } catch {
            return .failure(.unreachable(address))
        }
    }

    /// Sends the PDF to be read: the MusicXML and engine, or why it could not be read. To the hosted server it goes with
    /// the player's Scan token and a tester's plan choice; to any other address with nothing of the player's. A PDF of
    /// more than one page, up to 100, goes a page a request and their notes are joined (v0.5.6, #533 section 1,
    /// `ScanParts.swift`); with «Авто», the pages a notation program drew are read on the device and only the rest are sent
    /// (decision record 0029, `VectorPages.swift`); `progress`, or the task's `ScanProgress.listener`, hears as each page is sent and read.
    static func recognise(fileName: String, data: Data, address: URL, account: Account, progress: ScanProgressListener? = nil) async -> Result<ScanResult, ScanProblem> {
        let report = progress ?? ScanProgress.listener
        // A file PDFKit cannot open goes whole, as before: the server says what is wrong with it.
        guard data.starts(with: Data("%PDF".utf8)), let document = PDFDocument(data: data), !document.isLocked, document.pageCount > 0 else {
            return await whole(fileName: fileName, data: data, address: address, account: account, report: report)
        }
        let pages = document.pageCount
        guard pages <= ScanParts.mostPages else { return .failure(.tooManyPages(pages)) }
        let target = ScanTarget(address: address, account: account)
        if jobs, let byJob = await readByJob(fileName: fileName, data: data, pages: pages, to: target, hear: { report?($0.progress) }) {
            // The notes are back here: the job goes from the server now (lead's decision, version 1.3).
            if case .success(let read) = byJob, let job = read.job { await deleteJob(job.id, to: target) }
            return byJob.map { $0.result }
        }
        if choseAuto(DeviceSettings.shared), let vector = await vectorPages(data) {
            return await recogniseAuto(fileName: fileName, document: document, vector: vector, address: address, account: account, report: report)
        }
        guard pages > 1 else { return await whole(fileName: fileName, data: data, address: address, account: account, report: report) }
        return await recognisePages(fileName: fileName, document: document, address: address, account: account, report: report)
    }

    /// One request for the whole file, as one page of the progress.
    private static func whole(fileName: String, data: Data, address: URL, account: Account, report: ScanProgressListener?) async -> Result<ScanResult, ScanProblem> {
        let progress = ScanProgress(pages: 1)
        report?(progress)
        let start = Date()
        let answer = await recognisePage(fileName: fileName, data: data, address: address, account: account).result
        if case .success = answer { report?(progress.read(after: ScanProgress.since(start))) }
        return answer
    }

    /// Sends one file to be read, as `recognise`, with the server's page report (`X-Page-Report`), which comes with a page
    /// that could not be read too (v0.4, «Фото страниц»).
    static func recognisePage(fileName: String, data: Data, address: URL, account: Account) async -> (result: Result<ScanResult, ScanProblem>, report: String?) {
        guard data.count <= maxUploadBytes else { return (.failure(.tooLarge), nil) }
        let boundary = "motiff-\(UUID().uuidString)"
        var request = URLRequest(url: address.appendingPathComponent("recognise"))
        request.httpMethod = "POST"
        request.timeoutInterval = 600
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        guard await sign(&request, address: address, account: account) else { return (.failure(.signIn), nil) }
        let engine = chosenEngine(DeviceSettings.shared)
        request.httpBody = form(engine.map { [("engine", $0)] } ?? [(String, String)](), fileName: fileName, data: data, boundary: boundary)
        let hostedServer = address == hosted(account)
        do {
            let (answer, response) = try await URLSession.shared.data(for: request)
            let http = response as? HTTPURLResponse
            let report = http?.value(forHTTPHeaderField: "X-Page-Report")
            switch http?.statusCode ?? 0 {
            case 200..<300:
                let engine = http?.value(forHTTPHeaderField: "X-Engine") ?? "unknown engine"
                let pages = Int(http?.value(forHTTPHeaderField: "X-Pages") ?? "") ?? 1
                return (.success(ScanResult(musicXml: answer, engine: engine, pages: pages, report: report)), report)
            case let status:
                return (.failure(problem(status, answer, hosted: hostedServer, engine: engine)), report)
            }
        } catch {
            return (.failure(problem(error, address: address)), nil)
        }
    }

    /// Signs a request to Motiff Lab's server with the player's Scan token and a tester's plan choice, and one to any other
    /// address with nothing of the player's; false when the player has no token for Motiff Lab's server.
    static func sign(_ request: inout URLRequest, address: URL, account: Account) async -> Bool {
        guard address == hosted(account) else { return true }
        guard let options = account.options, let token = await account.token(options.scanScope) else { return false }
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        account.addPlanChoice(to: &request)
        return true
    }

    /// The multipart form the scan server takes: the fields, then the file.
    nonisolated static func form(_ fields: [(String, String)], fileName: String, data: Data, boundary: String) -> Data {
        var body = Data()
        for (name, value) in fields {
            body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n".utf8))
        }
        let file = fileName.replacingOccurrences(of: "\"", with: "")
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"\(file)\"\r\nContent-Type: application/octet-stream\r\n\r\n".utf8))
        body.append(data)
        body.append(Data("\r\n--\(boundary)--\r\n".utf8))
        return body
    }

    /// Why the server refused a request, from its status and answer: Pro on Motiff Lab's server, an engine it does not have,
    /// its own message, or that the address is not a scan server.
    static func problem(_ status: Int, _ answer: Data, hosted: Bool, engine: String?) -> ScanProblem {
        if status == 403 && hosted { return .needsPro }
        let problem = try? JSONSerialization.jsonObject(with: answer) as? [String: Any]
        if let engine, problem?["code"] as? String == "engine" { return .server(UIText.current.format("scan.engineMissing", engine)) }
        if let message = problem?["message"] as? String, !message.isEmpty { return .server(message) }
        return status == 404 ? .notAScanServer : .server("The scan server could not read this file.")
    }

    /// Why a request did not reach the server or was not answered in time.
    nonisolated static func problem(_ error: Error, address: URL) -> ScanProblem {
        (error as? URLError)?.code == .timedOut ? .server("The scan server took too long to answer.") : .unreachable(address)
    }
}

/// Where a scan goes, and how a request to it is signed: to Motiff Lab's server with the player's Scan token, to any other
/// address with nothing of the player's (`ScanServer.sign`).
@MainActor
struct ScanTarget {
    let address: URL
    let account: Account
    /// What the job's requests go by: the app's session, a stub's in tests.
    var session = URLSession.shared

    var hosted: Bool { address == ScanServer.hosted(account) }

    func sign(_ request: inout URLRequest) async -> Bool {
        await ScanServer.sign(&request, address: address, account: account)
    }
}

/// Why a scan did not give notes, in the player's words.
enum ScanProblem: Error, Equatable {
    case notAScanServer
    case unreachable(URL)
    case signIn
    case needsPro
    /// Larger than the scan server reads; nothing was sent.
    case tooLarge
    /// A PDF of more pages than a score may have (`ScanParts.mostPages`); nothing was sent.
    case tooManyPages(Int)
    case server(String)
    /// A scan job no longer waited for: its batch was paused or stopped, or its panel closed; it is kept on the server.
    case stopped

    func text(_ text: UIText) -> String {
        switch self {
        case .notAScanServer: "Something answers at this address, but it is not a Motiff Lab scan server."
        case .unreachable(let address): "The scan server at \(address.absoluteString) does not answer. Check that it is running (see Settings), and that this iPad may reach it."
        case .signIn: text["scan.signIn"]
        case .needsPro: text["scan.needsPro"]
        case .tooLarge: text["import.scanTooLarge"] + " " + text["import.scanTooLargeTip"]
        case .tooManyPages(let pages): text.format("scan.tooManyPages", String(pages), String(ScanParts.mostPages))
        case .server(let message): message
        case .stopped: ""
        }
    }

    /// The problem as one page of a PDF read a page a request met it: the server's own words say which page.
    func onPage(_ page: Int, _ text: UIText) -> ScanProblem {
        guard case .server(let message) = self else { return self }
        return .server(text.format("scan.pageFailed", String(page), message))
    }
}

/// Settings' scan server, in a build without Motiff Lab's server (the web app's AccountPanel `#scan`): the address of the
/// player's own server, saved and checked, or scanning turned off.
struct ScanPanel: View {
    @ObservedObject var scan: ScanServer
    let text: UIText
    @State private var address = ""
    @State private var status: String?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["settings.scanTitle"]).dsType(.label).foregroundStyle(Color.ds("ink"))
            Text(text["settings.scanWhat"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            TextField(ScanServer.suggested, text: $address)
                .keyboardType(.URL)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.done)
                .onSubmit { Task { await save() } }
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(height: CGFloat.ds("h-control"))
                .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
                .accessibilityLabel(text["settings.scanAddress"])
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["settings.scanSave"]) { Task { await save() } }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                if current != nil {
                    Button(text["settings.scanTurnOff"]) {
                        scan.choose(nil)
                        status = nil
                    }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                }
            }
            Text(status ?? current.map { text.format("settings.scanStatusUses", $0.absoluteString) } ?? text["settings.scanStatusOff"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .accessibilityAddTraits(.updatesFrequently)
            if let current, !ScanServer.isLocal(current) {
                Text(text["settings.scanElsewhere"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
        .onAppear { address = current?.absoluteString ?? "" }
    }

    private var current: URL? { scan.address(hosted: nil) }

    private func save() async {
        guard let parsed = ScanServer.parse(address) else {
            status = text["settings.scanBadAddress"]
            return
        }
        scan.choose(parsed)
        status = text["settings.scanChecking"]
        switch await ScanServer.check(parsed) {
        case .success(let engine): status = text.format("settings.scanConnected", engine)
        case .failure(let problem): status = text.format("settings.scanSavedBut", problem.text(text))
        }
    }
}

/// Read the notes from a PDF piece (0011, 0023; the web app's RecognisePanel): the PDF goes to the scan server, and the
/// notes it reads become a separate piece; the PDF piece stays as it is (Andrei, 2026-11-05). What leaves the device is one
/// line naming the server set in Settings, and Recognise notes sends it: pressing the button is the player's yes, with no
/// switch to turn on each time (issue #479 item 17). While it is read, a segment a page says which pages are read and
/// which is being read, and, once a page is timed, about how long is left (v0.5.6, #533 section 1); its sheet is as tall
/// as it is (`fittedSheet`). On Motiff Lab's server it needs an account and Pro, offered here.
struct ReadNotesPanel: View {
    let address: URL?
    let hosted: Bool
    let signedIn: Bool
    let pro: Bool
    let text: UIText
    /// The panel's heading; Read the notes unless given.
    var heading: String? = nil
    /// What becomes of the notes read, under Send; that they go into this piece unless given.
    var keeps: String? = nil
    /// The key of the line on what is sent and to where ({0} the address), in place of the PDF's.
    var line: String? = nil
    let settings: () -> Void
    /// Sends the PDF and puts the notes into the piece, or adds them as a version; answers why not, or nil once done.
    let read: () async -> String?
    /// The PDF to be sent: with scan jobs on (decision record 0035 section 6), the panel says before sending which pages the
    /// iPad read itself, which go to the server and about how long they take (`ScanServer.jobPlanLine`).
    var pdf: Data? = nil
    @State private var reading = false
    @State private var problem: String?
    @State private var planned: String?
    @StateObject private var feed = ScanFeed()

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(heading ?? text["pdf.readNotes"]).overline().accessibilityAddTraits(.isHeader)
            if let address {
                if hosted && !signedIn {
                    caption(text["scan.signInPrompt"])
                    Button(text["settings.signIn"], action: settings).buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                } else if hosted && !pro {
                    HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                        Tag(text: "Pro", kind: .saved)
                        caption(text["scan.proPrompt"])
                    }
                } else {
                    caption(line.map { text.format($0, address.absoluteString) } ?? sentTo(address))
                    if let planned { caption(planned) }
                    Button {
                        Task { await send() }
                    } label: {
                        Text(reading ? text["import.scanBusy"] : text["pdf.readSend"])
                    }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(reading)
                    if reading, let progress = feed.progress {
                        ScanProgressBar(progress: progress, text: text)
                    }
                    caption(keeps ?? text["pdf.readKeeps"])
                }
            } else {
                caption(text["import.scanSetUp"])
                Button(text["import.scanSetUpLink"], action: settings).buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
            if let problem {
                Text(problem).dsType(.caption).foregroundStyle(Color.ds("danger")).fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface"))
        .task { if ScanServer.jobs { planned = await ScanServer.jobPlanLine(data: pdf, address: address, text: text) } }
    }

    private func caption(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
    }

    /// Where Recognise notes sends the PDF: Motiff Lab's server, or the address set in Settings; as a scan job, kept there
    /// until the iPad has fetched what was read, at most 24 hours (decision record 0035 section 4).
    private func sentTo(_ address: URL) -> String {
        let job = ScanServer.jobs
        if hosted { return text[job ? "pdf.readLineHostedJob" : "pdf.readLineHosted"] }
        let here = ScanServer.isLocal(address)
        return text.format(here ? (job ? "pdf.readLineHereJob" : "pdf.readLineHere") : (job ? "pdf.readLineJob" : "pdf.readLine"), address.absoluteString)
    }

    /// Sends the PDF on the player's press, with the pages as they go to the bar.
    @MainActor
    private func send() async {
        reading = true
        problem = nil
        let feed = feed
        problem = await ScanProgress.$listener.withValue({ feed.progress = $0 }) {
            await read()
        }
        feed.progress = nil
        reading = false
    }
}
