import XCTest
@testable import MotiffLab

/// «Очистить тестовую библиотеку» (Andrei, 2026-11-13): only the Test environment's own files go, never Main's and never
/// anything else; the online copies only on develop's own API. And Settings · About says which build this is.
final class TestLibraryTests: XCTestCase {
    private let documents = URL(fileURLWithPath: "/container/Documents", isDirectory: true)
    private let support = URL(fileURLWithPath: "/container/Library/Application Support", isDirectory: true)

    private func url(_ path: String) -> URL { URL(fileURLWithPath: path) }

    func testOnlyTestsProjectFilesAndRecordsAreDeleted() {
        let listed = [
            url("/container/Documents/Test/Für Elise.motiff.json"),
            url("/container/Documents/Test/Sonata 2.MOTIFF.JSON"),
            url("/container/Documents/Test/notes.pdf"),
            url("/container/Documents/Test/../Main piece.motiff.json"),
            url("/container/Documents/Main piece.motiff.json"),
            url("/container/Documents/Test/Inner/Deep.motiff.json"),
            url("/elsewhere/Test/Other.motiff.json"),
        ]
        let targets = TestLibrary.targets(listed: listed, documents: documents, support: support).map(\.path)
        XCTAssertEqual(targets, [
            "/container/Documents/Test/Für Elise.motiff.json",
            "/container/Documents/Test/Sonata 2.MOTIFF.JSON",
            "/container/Library/Application Support/sync-test.json",
            "/container/Library/Application Support/LibraryImport/test",
        ])
    }

    func testNothingOfMainIsEverDeleted() {
        for path in [
            "/container/Documents",
            "/container/Documents/Test",
            "/container/Documents/Piece.motiff.json",
            "/container/Library/Application Support",
            "/container/Library/Application Support/sync.json",
            "/container/Library/Application Support/LibraryImport",
            "/container/Library/Application Support/LibraryImport/main",
            "/container/Library/Application Support/LibraryImport/main/folders.json",
            "/container/Library/Application Support/OriginalLinks",
            "/container/Library/Application Support/LibraryImport/test/../main",
        ] {
            XCTAssertFalse(TestLibrary.deletes(url(path), documents: documents, support: support), path)
        }
        XCTAssertEqual(TestLibrary.importRecords(in: support).path, "/container/Library/Application Support/LibraryImport/test")
    }

    func testClearingDeletesTheFilesOnDiskAndLeavesMainsAndTheRest() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("TestLibraryTests-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let documents = root.appendingPathComponent("Documents", isDirectory: true)
        let support = root.appendingPathComponent("Support", isDirectory: true)
        let test = documents.appendingPathComponent("Test", isDirectory: true)
        let kept = support.appendingPathComponent("LibraryImport/test/Kept", isDirectory: true)
        let mainImport = support.appendingPathComponent("LibraryImport/main", isDirectory: true)
        for folder in [test, kept, mainImport] {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        }
        let files = [
            test.appendingPathComponent("A.motiff.json"), test.appendingPathComponent("B.motiff.json"), test.appendingPathComponent("player.pdf"),
            documents.appendingPathComponent("Main.motiff.json"), support.appendingPathComponent("sync.json"),
            support.appendingPathComponent("sync-test.json"), kept.appendingPathComponent("p_1.pdf"), mainImport.appendingPathComponent("folders.json"),
        ]
        for file in files { try Data("{}".utf8).write(to: file) }

        let done = TestLibrary.clearFiles(documents: documents, support: support)

        XCTAssertEqual(done.pieces, 2)
        XCTAssertEqual(done.failed, 0)
        let left = files.filter { FileManager.default.fileExists(atPath: $0.path) }.map(\.lastPathComponent)
        XCTAssertEqual(left, ["player.pdf", "Main.motiff.json", "sync.json", "folders.json"])
        XCTAssertFalse(FileManager.default.fileExists(atPath: support.appendingPathComponent("LibraryImport/test").path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: test.path), "the folder itself stays, empty of pieces")
    }

    func testOnlineCopiesGoOnlyFromDevelopsOwnApiInTest() {
        let main = AccountOptions(
            authority: "https://motifflab.ciamlogin.com/tenant", clientId: "client", apiScope: "api://api/Projects.ReadWrite",
            scanScope: "api://api/Scan", api: "https://ca-motifflab-api.example.io", scan: "https://ca-motifflab-scan.example.io")
        var test = main
        test.api = "https://ca-motifflab-api-dev.example.io"
        let setup = EnvironmentSetup(main: main, test: test, buildDefault: .test)
        XCTAssertTrue(TestLibrary.deletesOnline(setup, in: .test))
        XCTAssertFalse(TestLibrary.deletesOnline(setup, in: .main))
        XCTAssertFalse(TestLibrary.deletesOnline(EnvironmentSetup(main: main, test: nil, buildDefault: .main), in: .test))
        XCTAssertFalse(TestLibrary.deletesOnline(nil, in: .test))
        var same = main
        same.scan = test.scan
        XCTAssertFalse(TestLibrary.deletesOnline(EnvironmentSetup(main: main, test: same, buildDefault: .test), in: .test), "Test on Main's API")
        var other = main
        other.api = "https://api.test.example"
        XCTAssertFalse(TestLibrary.deletesOnline(EnvironmentSetup(main: main, test: other, buildDefault: .test), in: .test), "not develop's API")
        XCTAssertTrue(TestLibrary.deletesOnline(EnvironmentSetup.load(), in: .test), "the app's own Account.json: Test is develop's API")
    }

    @MainActor
    func testTheClearIsOfferedOnlyInTest() {
        let defaults = UserDefaults(suiteName: "TestLibraryTests") ?? .standard
        defaults.removePersistentDomain(forName: "TestLibraryTests")
        let main = AccountOptions(
            authority: "https://motifflab.ciamlogin.com/tenant", clientId: "client", apiScope: "s", scanScope: "", api: "https://a.example", scan: "")
        var test = main
        test.api = "https://ca-motifflab-api-dev.example"
        XCTAssertTrue(TestLibrary.offered(Account(setup: EnvironmentSetup(main: main, test: test, buildDefault: .test), defaults: defaults)))
        XCTAssertFalse(TestLibrary.offered(Account(setup: EnvironmentSetup(main: main, test: test, buildDefault: .main), defaults: defaults)))
        XCTAssertFalse(TestLibrary.offered(Account(options: main, defaults: defaults)), "a build that knows only Main")
        XCTAssertFalse(TestLibrary.offered(Account(setup: nil, defaults: defaults)))
    }

    func testAboutSaysTheTagOrTheBranchAndCommitElseDev() {
        let tagged = BuildInfo.read(["MotiffBuildVersion": "v0.5.2", "MotiffBuildCommit": "1a2b3c4", "MotiffBuildDate": "2026-11-13T12:05:00Z",
                                     "CFBundleShortVersionString": "0.5.2", "CFBundleVersion": "812"])
        XCTAssertEqual(tagged.version, "v0.5.2")
        XCTAssertEqual(tagged.commit, "1a2b3c4")
        XCTAssertEqual(tagged.built, ISO8601DateFormatter().date(from: "2026-11-13T12:05:00Z"))
        XCTAssertNotNil(tagged.builtText("ru"))
        XCTAssertEqual(BuildInfo.read(["MotiffBuildVersion": "develop 1a2b3c4"]).version, "develop 1a2b3c4")

        let xcode = BuildInfo.read(["MotiffBuildVersion": "", "MotiffBuildCommit": "", "MotiffBuildDate": "",
                                    "CFBundleShortVersionString": "0.1.0", "CFBundleVersion": "1"])
        XCTAssertEqual(xcode.version, "0.1.0 (1) · dev")
        XCTAssertNil(xcode.built)
        XCTAssertNil(xcode.builtText("en"))
        XCTAssertEqual(BuildInfo.read([:]).version, "dev")
    }
}
