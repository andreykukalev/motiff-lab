import XCTest
@testable import MotiffLab

/// The switch between the Main and Test environments (Andrei, 2026-11-12): Account.json knows both, a version build starts
/// in Test and a build from main in Main, only a version build or a tester is offered the switch, each environment keeps
/// its own library and sync records on the iPad, and Main is read only in a version build until writing is allowed.
@MainActor
final class EnvironmentTests: XCTestCase {
    private var defaults: UserDefaults!

    override func setUp() async throws {
        defaults = try XCTUnwrap(UserDefaults(suiteName: "EnvironmentTests"))
        defaults.removePersistentDomain(forName: "EnvironmentTests")
    }

    private static let main = AccountOptions(
        authority: "https://motifflab.ciamlogin.com/tenant", clientId: "client", apiScope: "api://api/Projects.ReadWrite",
        scanScope: "api://api/Scan", api: "https://api.main.example", scan: "https://scan.main.example")

    private func setup(starting buildDefault: ServerEnvironment, test: Bool = true) -> EnvironmentSetup {
        var options = Self.main
        options.api = "https://api.test.example"
        options.scan = "https://scan.test.example"
        return EnvironmentSetup(main: Self.main, test: test ? options : nil, buildDefault: buildDefault)
    }

    func testTheAppKnowsMainAndTestWithOneSignIn() throws {
        let setup = try XCTUnwrap(EnvironmentSetup.load(), "Account.json is in the app and complete")
        let test = try XCTUnwrap(setup.test, "Account.json has Test's servers")
        XCTAssertEqual(setup.main, AccountOptions.load(), "Main is the Account block, the published web app's values")
        XCTAssertEqual(URL(string: setup.main.api)?.host?.hasPrefix("ca-motifflab-api."), true)
        XCTAssertEqual(URL(string: test.api)?.host?.hasPrefix("ca-motifflab-api-dev."), true)
        XCTAssertEqual(URL(string: test.scan)?.host?.hasPrefix("ca-motifflab-scan-dev."), true)
        XCTAssertEqual([test.authority, test.clientId, test.apiScope, test.scanScope],
                       [setup.main.authority, setup.main.clientId, setup.main.apiScope, setup.main.scanScope], "the same tenant and registrations")
        XCTAssertEqual(setup.buildDefault, .main, "the committed file starts in Main; ipad/build-version.sh sets Test for a version")
    }

    func testAccountJsonIsReadWithItsDefault() throws {
        let file = #"""
        {"Account": {"Authority": "https://motifflab.ciamlogin.com/t", "ClientId": "c", "ApiScope": "api://a/Projects.ReadWrite",
          "ScanScope": "api://a/Scan", "Api": "https://api.main.example", "Scan": "https://scan.main.example"},
         "Environments": {"Default": "test", "Test": {"Api": "https://api.test.example", "Scan": "https://scan.test.example"}}}
        """#
        let read = try XCTUnwrap(EnvironmentSetup.read(Data(file.utf8)))
        XCTAssertEqual(read.buildDefault, .test)
        XCTAssertEqual(read.options(.test).api, "https://api.test.example")
        XCTAssertEqual(read.options(.test).scan, "https://scan.test.example")
        XCTAssertEqual(read.options(.main).api, "https://api.main.example")

        // A file from before the switch knows only Main: it starts there, whatever it says.
        let old = #"{"Account": {"Authority": "https://motifflab.ciamlogin.com/t", "ClientId": "c", "ApiScope": "s", "ScanScope": "", "Api": "https://api.main.example", "Scan": ""}}"#
        let only = try XCTUnwrap(EnvironmentSetup.read(Data(old.utf8)))
        XCTAssertNil(only.test)
        XCTAssertEqual(only.buildDefault, .main)
        XCTAssertEqual(EnvironmentSetup(main: Self.main, test: nil, buildDefault: .test).buildDefault, .main)
        XCTAssertNil(EnvironmentSetup.read(Data("{}".utf8)), "no sign-in, no environments")
    }

    func testAVersionBuildStartsInTestAndABuildFromMainInMain() {
        let version = setup(starting: .test)
        XCTAssertTrue(version.isVersionBuild)
        XCTAssertEqual(version.current(chosen: nil), .test)
        XCTAssertEqual(version.current(chosen: "main"), .main, "the choice made in Settings")
        XCTAssertEqual(version.current(chosen: "nonsense"), .test)

        let released = setup(starting: .main)
        XCTAssertFalse(released.isVersionBuild)
        XCTAssertEqual(released.current(chosen: nil), .main)
        XCTAssertNotEqual(version.choiceKey, released.choiceKey, "a build from main does not take a version build's choice")
        XCTAssertEqual(setup(starting: .main, test: false).current(chosen: "test"), .main, "no Test servers, no Test")
    }

    func testOnlyAVersionBuildOrATesterIsOfferedTheSwitch() {
        let version = setup(starting: .test)
        XCTAssertTrue(version.offersChoice(current: .test, isTester: false))
        XCTAssertTrue(version.offersChoice(current: .main, isTester: false))

        let released = setup(starting: .main)
        XCTAssertFalse(released.offersChoice(current: .main, isTester: false), "a player never sees it")
        XCTAssertTrue(released.offersChoice(current: .main, isTester: true))
        XCTAssertTrue(released.offersChoice(current: .test, isTester: false), "away from Main, the way back stays")
        XCTAssertFalse(setup(starting: .main, test: false).offersChoice(current: .main, isTester: true))
    }

    func testMainIsReadOnlyInAVersionBuildUntilWritingIsAllowed() {
        let version = setup(starting: .test)
        XCTAssertFalse(version.writes(.main, mainAllowed: false))
        XCTAssertTrue(version.writes(.main, mainAllowed: true))
        XCTAssertTrue(version.writes(.test, mainAllowed: false))
        let released = setup(starting: .main)
        XCTAssertTrue(released.writes(.main, mainAllowed: false), "a build from main writes to Main")
        XCTAssertTrue(released.writes(.test, mainAllowed: false))
    }

    func testEachEnvironmentHasItsOwnLibraryAndSyncRecords() throws {
        let documents = FileManager.default.temporaryDirectory.appendingPathComponent("EnvironmentTests-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: documents) }
        let main = ServerEnvironment.main.projectsFolder(in: documents)
        let test = ServerEnvironment.test.projectsFolder(in: documents)
        XCTAssertEqual(main, documents, "the pieces already on the iPad stay Main's")
        XCTAssertEqual(test, documents.appendingPathComponent("Test", isDirectory: true))
        try FileManager.default.createDirectory(at: test, withIntermediateDirectories: true)
        try Data("{}".utf8).write(to: main.appendingPathComponent("Real.motiff.json"))
        try Data("{}".utf8).write(to: test.appendingPathComponent("Trial.motiff.json"))
        XCTAssertEqual(ProjectFiles.list(in: main).map(\.title), ["Real"], "Main's library never lists Test's folder")
        XCTAssertEqual(ProjectFiles.list(in: test).map(\.title), ["Trial"])
        XCTAssertFalse(ProjectFiles.isInFolder(test.appendingPathComponent("Trial.motiff.json"), folder: main))

        let support = documents.appendingPathComponent("Support")
        XCTAssertEqual(ServerEnvironment.main.syncRecords(in: support).lastPathComponent, "sync.json", "Main keeps the records it had")
        XCTAssertEqual(ServerEnvironment.test.syncRecords(in: support).lastPathComponent, "sync-test.json")
    }

    func testSwitchingChangesTheServersAndKeepsTheSignIn() async throws {
        let account = Account(setup: setup(starting: .test), defaults: defaults)
        XCTAssertEqual(account.environment, .test)
        XCTAssertEqual(account.request(path: "projects", token: "t")?.url?.absoluteString, "https://api.test.example/projects")
        XCTAssertTrue(account.writes)

        await account.switchEnvironment(to: .main)
        XCTAssertEqual(account.environment, .main)
        XCTAssertEqual(account.request(path: "projects", token: "t")?.url?.absoluteString, "https://api.main.example/projects")
        XCTAssertEqual(account.options?.clientId, "client", "the same sign-in")
        XCTAssertFalse(account.writes, "Main is read only in a version build")
        XCTAssertFalse(account.online.writable)
        XCTAssertEqual(Account(setup: setup(starting: .test), defaults: defaults).environment, .main, "kept on this device")
        XCTAssertEqual(Account(setup: setup(starting: .main), defaults: defaults).environment, .main)
    }

    func testWritingToMainIsAllowedForThisBuildOnly() async throws {
        let account = Account(setup: setup(starting: .test), defaults: defaults)
        await account.switchEnvironment(to: .main)
        account.allowMainWrites(true)
        XCTAssertTrue(account.writes)
        XCTAssertTrue(account.online.writable)
        XCTAssertTrue(Account(setup: setup(starting: .test), defaults: defaults).mainWritesAllowed, "kept for this build")
        XCTAssertFalse(EnvironmentSetup.mainWritesAllowed(defaults, for: "0.4 (999)"), "the next build starts read only")
        account.allowMainWrites(false)
        XCTAssertFalse(account.writes)
    }

    func testReadOnlySendsNoSave() async throws {
        StubAPI.reset(["PUT https://api.main.example/projects/p_1": .init(status: 204, body: Data(), headers: ["ETag": "\"e1\""])])
        defer { StubAPI.reset() }
        let account = Account(setup: setup(starting: .test), defaults: defaults)
        await account.switchEnvironment(to: .main)
        let online = OnlineProjects(session: StubAPI.session(), request: { path in await account.request(path: path, token: "t") }, writable: false)
        let sent = await online.upload("p_1", file: "{}", ifMatch: nil)
        XCTAssertEqual(sent, .failed(.problem))
        XCTAssertTrue(StubAPI.requests.isEmpty, "nothing left the iPad")
        let profile = await account.saveProfile(name: "A", photo: nil)
        XCTAssertNotNil(profile, "the profile is not saved either")
    }
}
