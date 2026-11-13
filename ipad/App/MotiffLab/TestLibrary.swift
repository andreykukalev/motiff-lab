import SwiftUI

/// «Очистить всё» (Andrei, 2026-11-13): before a new testing cycle the Test environment starts from an empty library
/// everywhere, to import again and see what changed. Settings · About offers it only while the iPad works in Test, in a
/// build that knows Test (infra/README.md, "A fresh testing cycle"). Signed in, it deletes the player's data in the test
/// database, and their other devices clear their own test library at their next sync (`TestReset`). On the iPad it deletes
/// only the app's own files of the Test environment, never Main's and never anything outside the app: the player's PDFs,
/// recordings and folders added by link stay where they are.
enum TestLibrary {
    /// What the clear came to.
    enum Outcome: Equatable {
        /// The pieces deleted on this iPad, and the files that would not go.
        case cleared(pieces: Int, failed: Int)
        /// Nothing was deleted: the iPad is not in Test, an import or a recognition runs, or the online copies did not go.
        case refused(String)
    }

    /// The player's copies in the Test database, as the API's POST /me/reset answered.
    enum Online: Equatable {
        case signedOut, deleted, failed, notDevelop
    }

    /// Test's folder-import records: `LibraryImport/test` (`ImportFolders.root` while the iPad works in Test), with the
    /// folders added, the import job, the recognition batch, the playlists kept on the iPad and their copies (`Kept`).
    static func importRecords(in support: URL) -> URL {
        support.appendingPathComponent("LibraryImport", isDirectory: true).appendingPathComponent(ServerEnvironment.test.rawValue, isDirectory: true)
    }

    /// Whether clearing may delete the file or folder: one of Test's project files (a `.motiff.json` directly in
    /// Documents/Test), Test's sync records (`sync-test.json`) or Test's folder-import records. Nothing of Main (the project
    /// files in Documents itself, `sync.json`, `LibraryImport/main`), nothing else in Documents/Test, nothing outside.
    static func deletes(_ url: URL, documents: URL, support: URL) -> Bool {
        let path = url.standardizedFileURL.path
        let records = [ServerEnvironment.test.syncRecords(in: support), importRecords(in: support)]
        if records.contains(where: { $0.standardizedFileURL.path == path }) { return true }
        let folder = ServerEnvironment.test.projectsFolder(in: documents)
        guard folder.standardizedFileURL.path != documents.standardizedFileURL.path else { return false }
        return ProjectFiles.isProject(url.lastPathComponent) && ProjectFiles.isInFolder(url, folder: folder)
    }

    /// What clearing deletes, given the files listed in Documents/Test: Test's project files among them, then its records.
    static func targets(listed: [URL], documents: URL, support: URL) -> [URL] {
        (listed + [ServerEnvironment.test.syncRecords(in: support), importRecords(in: support)])
            .filter { deletes($0, documents: documents, support: support) }
    }

    /// Whether the player's copies may be deleted online: only in Test, and only when Test's API is develop's own
    /// (`ca-motifflab-api-dev.…`, not Main's address), so nothing in the players' database is ever asked to go.
    static func deletesOnline(_ setup: EnvironmentSetup?, in environment: ServerEnvironment) -> Bool {
        guard environment == .test, let setup, let test = setup.test,
              let host = URL(string: test.api)?.host?.lowercased(), host != URL(string: setup.main.api)?.host?.lowercased()
        else { return false }
        return host.hasPrefix("ca-motifflab-api-dev.")
    }

    /// Settings shows the clear only while the iPad works in Test, in a build that knows Test.
    @MainActor
    static func offered(_ account: Account) -> Bool {
        account.environment == .test && account.setup?.test != nil
    }

    /// Deletes Test's own files on this iPad: the project files in Documents/Test (regular files only) and its records.
    static func clearFiles(
        documents: URL = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0],
        support: URL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0],
        files: FileManager = .default
    ) -> (pieces: Int, failed: Int) {
        let folder = ServerEnvironment.test.projectsFolder(in: documents)
        let listed = ((try? files.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.isRegularFileKey])) ?? [])
            .filter { (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true }
        var pieces = 0
        var failed = 0
        for url in targets(listed: listed, documents: documents, support: support) where files.fileExists(atPath: url.path) {
            if (try? files.removeItem(at: url)) == nil {
                failed += 1
            } else if ProjectFiles.isProject(url.lastPathComponent) {
                pieces += 1
            }
        }
        return (pieces, failed)
    }

    /// The whole clear, after the player confirmed it: the online copies first (so the sync brings nothing back), then the
    /// files on this iPad; the library then reads itself again (`Account.libraryCleared`).
    @MainActor
    static func clear(_ account: Account) async -> Outcome {
        guard offered(account) else { return .refused("testLibrary.notTest") }
        guard !ImportRunner.shared.running, !RecognitionQueue.shared.running else { return .refused("testLibrary.busy") }
        switch await account.deleteTestCopies() {
        case .failed: return .refused("testLibrary.onlineFailed")
        case .notDevelop: return .refused("testLibrary.notDevelop")
        case .signedOut, .deleted: break
        }
        guard offered(account) else { return .refused("testLibrary.notTest") }
        let done = clearLocal(account)
        return .cleared(pieces: done.pieces, failed: done.failed)
    }
}

/// «Очистить всё» across the player's devices (Andrei, 2026-11-13): pressing it on one device deletes the player's data in
/// the test database (the API's POST /me/reset, src/MotiffLab.Api/TestReset.cs), which records a reset stamp; develop's
/// API then sends the stamp with every list of projects. Each device keeps the stamp it last applied, per environment, in
/// its preferences (never among the files the clear deletes). A newer stamp in a list means the player cleared everything
/// from another device: the iPad clears its own test library before the pass syncs anything (`applyReset`), and its saves
/// say which stamp they come after, so the API refuses one from before the reset. Only in Test on develop's own API.
enum TestReset {
    /// The player's last reset stamp, on the list of projects (milliseconds since 1970, the server's clock).
    static let header = "Motiff-Reset"
    /// The stamp this iPad last applied, sent with a save.
    static let seenHeader = "Motiff-Reset-Seen"

    /// The stamp in a header's value: a whole number from 0; nil for anything else.
    static func stamp(_ value: String?) -> Int64? {
        guard let value = value?.trimmingCharacters(in: .whitespaces), !value.isEmpty, value.count <= 15,
              value.allSatisfy({ $0.isASCII && $0.isNumber })
        else { return nil }
        return Int64(value)
    }

    /// Whether the list's stamp is newer than the one applied here, so the test library here is to be cleared.
    static func isDue(_ stamp: Int64?, applied: Int64) -> Bool {
        guard let stamp else { return false }
        return stamp > applied
    }

    /// Whether resets reach the iPad in the environment: as for deleting the online copies, only Test on develop's API.
    static func applies(_ setup: EnvironmentSetup?, in environment: ServerEnvironment) -> Bool {
        TestLibrary.deletesOnline(setup, in: environment)
    }

    /// Where the stamp applied in the environment is kept, in the preferences.
    static func key(_ environment: ServerEnvironment) -> String { "ml-test-reset.\(environment.rawValue)" }

    /// The last stamp applied in the environment; 0 when none.
    static func applied(_ defaults: UserDefaults, in environment: ServerEnvironment) -> Int64 {
        stamp(defaults.string(forKey: key(environment))) ?? 0
    }

    /// Keeps the stamp as applied in the environment; an older one never replaces a newer one.
    static func record(_ stamp: Int64, in defaults: UserDefaults, environment: ServerEnvironment) {
        guard stamp > applied(defaults, in: environment) else { return }
        defaults.set(String(stamp), forKey: key(environment))
    }
}

extension TestLibrary {
    /// Applies a reset made on another device, found by a sync pass (`OnlineProjects.Listing.resetDue`): Test's own files
    /// on this iPad go, exactly as the clear deletes them, and the stamp is kept. Nothing is deleted online (the player's
    /// data there went with the reset). False when it did not run: the iPad left Test, or an import or a recognition is
    /// running; the stamp then stays unapplied, so the next pass tries again and still sends nothing meanwhile.
    @MainActor
    static func applyReset(_ account: Account, stamp: Int64) -> Bool {
        guard offered(account), !ImportRunner.shared.running, !RecognitionQueue.shared.running else { return false }
        _ = clearLocal(account)
        account.resetApplied(stamp)
        return true
    }

    /// Test's own files on this iPad, and anything an import or a recognition keeps of them in memory.
    @MainActor
    fileprivate static func clearLocal(_ account: Account) -> (pieces: Int, failed: Int) {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let records = importRecords(in: support).standardizedFileURL
        // A job or a batch kept in memory for Test's records is forgotten too (never one of Main's).
        if ImportRunner.shared.root.standardizedFileURL == records { ImportRunner.shared.stop() }
        if RecognitionQueue.shared.root.standardizedFileURL == records { RecognitionQueue.shared.stop() }
        let done = clearFiles(support: support)
        account.libraryWasCleared()
        return done
    }
}

/// The clear in Settings · About: the button, its confirmation and what it came to.
struct TestLibraryPanel: View {
    @ObservedObject var account: Account
    let text: UIText
    @State private var asking = false
    @State private var clearing = false
    @State private var outcome: TestLibrary.Outcome?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Button(text["testLibrary.clear"]) { asking = true }
                .buttonStyle(DSButtonStyle(kind: .danger, compact: true))
                .disabled(clearing)
            Text(text["testLibrary.hint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            if clearing { ProgressView() }
            if let outcome {
                Text(message(outcome)).dsType(.caption).foregroundStyle(Color.ds(isProblem(outcome) ? "danger" : "ink-2"))
            }
        }
        .alert(text["testLibrary.confirmTitle"], isPresented: $asking) {
            Button(text["testLibrary.confirm"], role: .destructive) {
                clearing = true
                outcome = nil
                Task {
                    outcome = await TestLibrary.clear(account)
                    clearing = false
                }
            }
            Button(text["common.cancel"], role: .cancel) {}
        } message: {
            Text(text["testLibrary.confirmMessage"])
        }
    }

    private func message(_ outcome: TestLibrary.Outcome) -> String {
        switch outcome {
        case .cleared(let pieces, 0): text.count("testLibrary.cleared", pieces)
        case .cleared(_, let failed): text.count("testLibrary.clearedFailed", failed)
        case .refused(let key): text[key]
        }
    }

    private func isProblem(_ outcome: TestLibrary.Outcome) -> Bool {
        if case .cleared(_, 0) = outcome { return false }
        return true
    }
}
