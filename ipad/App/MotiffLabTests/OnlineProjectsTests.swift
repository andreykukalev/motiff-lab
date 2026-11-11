import XCTest
@testable import MotiffLab

/// The API as these tests answer it: each URL's status, body and headers ("PUT <url>" for a method other than GET), and
/// every request the app sent with its body, kept in order. A URL with no answer fails as a missing connection does.
final class StubAPI: URLProtocol {
    struct Answer {
        var status: Int
        var body: Data
        var headers: [String: String] = [:]
    }

    private static let lock = NSLock()
    private static var answers: [String: Answer] = [:]
    private static var sent: [URLRequest] = []
    private static var sentBodies: [Data] = []

    static func reset(_ given: [String: Answer] = [:]) {
        lock.lock()
        defer { lock.unlock() }
        answers = given
        sent = []
        sentBodies = []
    }

    /// Adds or replaces answers while a test runs.
    static func answer(_ given: [String: Answer]) {
        lock.lock()
        defer { lock.unlock() }
        answers.merge(given) { _, new in new }
    }

    static var requests: [URLRequest] {
        lock.lock()
        defer { lock.unlock() }
        return sent
    }

    /// The bodies of the requests, in the same order (empty for a GET).
    static var bodies: [Data] {
        lock.lock()
        defer { lock.unlock() }
        return sentBodies
    }

    static func session() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubAPI.self]
        return URLSession(configuration: configuration)
    }

    override class func canInit(with request: URLRequest) -> Bool { true }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        Self.lock.lock()
        Self.sent.append(request)
        Self.sentBodies.append(request.httpBody ?? Self.read(request.httpBodyStream))
        let method = request.httpMethod ?? "GET"
        let answer = request.url.flatMap { url in
            method == "GET" ? Self.answers[url.absoluteString] : Self.answers["\(method) \(url.absoluteString)"]
        }
        Self.lock.unlock()
        guard let answer, let url = request.url,
              let response = HTTPURLResponse(
                  url: url, statusCode: answer.status, httpVersion: nil,
                  headerFields: answer.headers.merging(["Content-Type": "application/json"]) { given, _ in given })
        else {
            client?.urlProtocol(self, didFailWithError: URLError(.notConnectedToInternet))
            return
        }
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: answer.body)
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}

    private static func read(_ stream: InputStream?) -> Data {
        guard let stream else { return Data() }
        stream.open()
        defer { stream.close() }
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 16_384)
        while stream.hasBytesAvailable {
            let count = stream.read(&buffer, maxLength: buffer.count)
            if count <= 0 { break }
            data.append(buffer, count: count)
        }
        return data
    }
}

/// The projects saved online on the iPad (#335): listed when they are not here, downloaded as a new project file that
/// keeps its id, never listed again or replaced once here, and only ever read: every request is a GET.
@MainActor
final class OnlineProjectsTests: XCTestCase {
    private var folder: URL!
    private var account: Account!

    override func setUp() async throws {
        StubAPI.reset()
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("OnlineProjectsTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let defaults = try XCTUnwrap(UserDefaults(suiteName: "OnlineProjectsTests"))
        defaults.removePersistentDomain(forName: "OnlineProjectsTests")
        account = Account(options: try XCTUnwrap(AccountOptions.load()), defaults: defaults)
        // A tester's choice, which every request to the API carries (Account.request).
        await account.choose(.free)
    }

    override func tearDown() async throws {
        try? FileManager.default.removeItem(at: folder)
        StubAPI.reset()
    }

    /// As the library makes it, with the token "t" standing for the player's token for ApiScope.
    private func online(signedIn: Bool = true) -> OnlineProjects {
        let account = account!
        return OnlineProjects(session: StubAPI.session()) { path in
            guard signedIn else { return nil }
            return await account.request(path: path, token: "t")
        }
    }

    private func url(_ path: String) throws -> String {
        try XCTUnwrap(account.request(path: path, token: "t")?.url?.absoluteString)
    }

    /// A project file of the kit's piece saved under the id, as music-core writes it.
    private func projectFile(id: String) throws -> Data {
        let example = try XCTUnwrap(KitPiece.example)
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en", "id": "\#(id)"}"#)))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0, "\(opened)")
        defer { MusicCore.close(piece: piece) }
        let file = MusicCore.call(piece: piece, "project.save", #"{"now": "2026-11-09T12:00:00Z"}"#)
        XCTAssertNil(facadeObject(file)?["error"], file)
        return Data(file.utf8)
    }

    private func assertOnlyReadWithTheToken(file: StaticString = #filePath, line: UInt = #line) {
        for request in StubAPI.requests {
            XCTAssertEqual(request.httpMethod ?? "GET", "GET", "the iPad only reads online", file: file, line: line)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer t", file: file, line: line)
            XCTAssertEqual(request.value(forHTTPHeaderField: Account.planHeader), "free", file: file, line: line)
        }
    }

    // Criterion 1: the library's section lists the projects saved online whose id is in no project file here, titled
    // from their summaries as the web titles them.
    func testTheProjectsSavedOnlineThatAreNotHereAreListedByTheirTitles() async throws {
        let rows = #"""
        [{"id":"p_here","fileName":"Here.mid","motifCount":0,"variationCount":0,"lastOpened":"2026-11-09T10:00:00+00:00","resumeCount":0,"ideaCount":0,"savedAt":"2026-11-09T10:00:00+00:00"},
         {"id":"p_online","fileName":"Für Elise.musicxml","motifCount":2,"variationCount":1,"lastOpened":"2026-11-09T11:00:00+00:00","resumeCount":1,"ideaCount":3,"savedAt":"2026-11-09T11:00:00+00:00"}]
        """#
        StubAPI.reset([try url("projects"): .init(status: 200, body: Data(rows.utf8))])
        let answer = await online().list()
        let listed = try XCTUnwrap(answer)
        XCTAssertEqual(listed.map(\.id), ["p_here", "p_online"])
        let shown = OnlineProjects.notHere(listed, here: ["p_here", "p_elsewhere"])
        XCTAssertEqual(shown.map(\.id), ["p_online"])
        XCTAssertEqual(shown.first?.title, "Für Elise")
        XCTAssertEqual(StubAPI.requests.map { $0.url?.absoluteString }, [try url("projects")])
        assertOnlyReadWithTheToken()

        XCTAssertEqual(UIText(language: "en")["home.onlineOnly"], "Saved online, not on this device", "the web's own string")
        XCTAssertEqual(UIText(language: "ru")["home.onlineOnly"], "Сохранены онлайн, но не на этом устройстве")
        XCTAssertNotEqual(UIText(language: "en")["home.getHere"], "home.getHere")
        XCTAssertEqual(OnlineProject.title(of: "piece.motiff.json"), "piece")
        XCTAssertEqual(OnlineProject.title(of: " Sonata.mid "), "Sonata")
        XCTAssertEqual(OnlineProject.title(of: "Op. 27.2"), "Op. 27.2")
        XCTAssertEqual(OnlineProject(id: "p_1", fileName: "").title, "p_1")
    }

    // Criterion 2: a tap downloads the project file with GET /projects/{id}, keeps it as a new .motiff.json named by its
    // title, its bytes and so its project id unchanged, and it opens as any project file does.
    func testATapDownloadsTheProjectAsANewProjectFileThatKeepsItsId() async throws {
        let file = try projectFile(id: "p_online")
        StubAPI.reset([try url("projects/p_online"): .init(status: 200, body: file)])
        let project = OnlineProject(id: "p_online", fileName: "Für Elise.musicxml")

        let kept = try await online().keep(project, in: folder).get()
        XCTAssertEqual(kept.lastPathComponent, "Für Elise.motiff.json")
        XCTAssertTrue(ProjectFiles.isInFolder(kept, folder: folder))
        XCTAssertEqual(try Data(contentsOf: kept), file, "kept as the API sent it")
        let summary = await PieceSummary.read(kept)
        XCTAssertEqual(summary?.id, "p_online")
        XCTAssertEqual(ProjectFiles.list(in: folder).map(\.url.lastPathComponent), ["Für Elise.motiff.json"])
        XCTAssertEqual(StubAPI.requests.map { $0.url?.absoluteString }, [try url("projects/p_online")])
        assertOnlyReadWithTheToken()

        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: kept.lastPathComponent, data: try Data(contentsOf: kept))))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: piece) }
        XCTAssertGreaterThan(piece, 0, "\(opened)")
        XCTAssertEqual((opened["project"] as? [String: Any])?["id"] as? String, "p_online", "an ordinary project here")
    }

    // Criterion 3: a project already here is not listed again, a tap on it never downloads it or replaces its file, and a
    // different project of the same title is kept beside a file of that name.
    func testAProjectAlreadyHereIsNotListedAgainAndItsFileIsNeverReplaced() async throws {
        let mine = try projectFile(id: "p_online")
        let here = folder.appendingPathComponent("Mine.motiff.json")
        try mine.write(to: here)
        let ids = Set(await ProjectFiles.list(in: folder).asyncIds())
        XCTAssertEqual(ids, ["p_online"])
        let listed = [OnlineProject(id: "p_online", fileName: "Für Elise.musicxml"), OnlineProject(id: "p_other", fileName: "Mine.mid")]
        XCTAssertEqual(OnlineProjects.notHere(listed, here: ids).map(\.id), ["p_other"])

        StubAPI.reset([
            try url("projects/p_online"): .init(status: 200, body: try projectFile(id: "p_online")),
            try url("projects/p_other"): .init(status: 200, body: try projectFile(id: "p_other")),
        ])
        let same = try await online().keep(listed[0], in: folder).get()
        XCTAssertEqual(same.lastPathComponent, here.lastPathComponent, "its own file")
        XCTAssertTrue(StubAPI.requests.isEmpty, "not downloaded again")
        XCTAssertEqual(try Data(contentsOf: here), mine, "not replaced")

        let other = try await online().keep(listed[1], in: folder).get()
        XCTAssertEqual(other.lastPathComponent, "Mine 2.motiff.json")
        XCTAssertEqual(try Data(contentsOf: here), mine, "a file of the same name is not replaced")
        let otherSummary = await PieceSummary.read(other)
        XCTAssertEqual(otherSummary?.id, "p_other")
        assertOnlyReadWithTheToken()
    }

    // Criteria 4 and 5: signed out, offline or when the API refuses, nothing is listed, nothing is written and the reason
    // is one of the web's lines; what is not this project's file is not kept.
    func testWithoutSignInOrConnectionNothingIsListedOrWritten() async throws {
        let project = OnlineProject(id: "p_online", fileName: "Für Elise.musicxml")
        let signedOutList = await online(signedIn: false).list()
        XCTAssertNil(signedOutList)
        let signedOut = await online(signedIn: false).keep(project, in: folder)
        XCTAssertEqual(signedOut.failure, .signedOut)
        XCTAssertTrue(StubAPI.requests.isEmpty, "signed out, nothing is sent")

        let offlineList = await online().list()
        XCTAssertNil(offlineList, "no answer: offline")
        let offline = await online().keep(project, in: folder)
        XCTAssertEqual(offline.failure, .offline)

        StubAPI.reset([
            try url("projects"): .init(status: 401, body: Data()),
            try url("projects/p_online"): .init(status: 404, body: Data(#"{"message":"There is no such project."}"#.utf8)),
            try url("projects/p_wrong"): .init(status: 200, body: try projectFile(id: "p_someone")),
            try url("projects/p_broken"): .init(status: 200, body: Data("not a project".utf8)),
            try url("projects/p_down"): .init(status: 503, body: Data()),
        ])
        let refusedList = await online().list()
        XCTAssertNil(refusedList)
        let missing = await online().keep(project, in: folder)
        XCTAssertEqual(missing.failure, .notFound)
        let wrong = await online().keep(OnlineProject(id: "p_wrong", fileName: "Wrong.mid"), in: folder)
        XCTAssertEqual(wrong.failure, .problem, "another project's file is not kept under this one")
        let broken = await online().keep(OnlineProject(id: "p_broken", fileName: "Broken.mid"), in: folder)
        XCTAssertEqual(broken.failure, .problem)
        let down = await online().keep(OnlineProject(id: "p_down", fileName: "Down.mid"), in: folder)
        XCTAssertEqual(down.failure, .problem)
        XCTAssertTrue(ProjectFiles.list(in: folder).isEmpty, "the library is as it was")
        assertOnlyReadWithTheToken()

        let text = UIText(language: "en")
        for failure in [OnlineProjects.Failure.signedOut, .notFound, .offline, .problem] {
            XCTAssertNotEqual(text[failure.key], failure.key, "the web's own line for \(failure)")
        }
    }
}

private extension Result {
    var failure: Failure? {
        if case .failure(let failure) = self { return failure }
        return nil
    }
}

private extension Array where Element == ProjectFiles.Entry {
    /// The project ids in these files, as the library reads them.
    func asyncIds() async -> [String] {
        var ids: [String] = []
        for entry in self {
            if let id = await PieceSummary.read(entry.url)?.id { ids.append(id) }
        }
        return ids
    }
}
