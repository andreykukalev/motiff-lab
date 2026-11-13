import SwiftUI

/// «Очистить тестовую библиотеку» (Andrei, 2026-11-13): before a new testing cycle the Test environment starts from an
/// empty library, to import again and see what changed. Settings · About offers it only while the iPad works in Test, in
/// a build that knows Test (infra/README.md, "A fresh testing cycle"; the test database is emptied by
/// infra/azure/reset-dev-data.ps1). It deletes only the app's own files of the Test environment, never Main's and never
/// anything outside the app: the player's PDFs, recordings and folders added by link stay where they are.
enum TestLibrary {
    /// What the clear came to.
    enum Outcome: Equatable {
        /// The pieces deleted on this iPad, and the files that would not go.
        case cleared(pieces: Int, failed: Int)
        /// Nothing was deleted: the iPad is not in Test, an import or a recognition runs, or the online copies did not go.
        case refused(String)
    }

    /// The player's copies in the Test database, as the API's DELETE /me answered.
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
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let records = importRecords(in: support).standardizedFileURL
        // A job or a batch kept in memory for Test's records is forgotten too (never one of Main's).
        if ImportRunner.shared.root.standardizedFileURL == records { ImportRunner.shared.stop() }
        if RecognitionQueue.shared.root.standardizedFileURL == records { RecognitionQueue.shared.stop() }
        let done = clearFiles(support: support)
        account.libraryWasCleared()
        return .cleared(pieces: done.pieces, failed: done.failed)
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
