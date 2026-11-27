import SwiftUI

/// The library's projects online (0014, #335, version 0.2): one sync pass of the pieces here with the player's other
/// devices and what it resolved, the projects saved online that are not here, their tab, Get it here, and a piece deleted
/// here and online. Signed out, offline or when the API cannot be reached there are none, and nothing is said: the
/// library is about the projects on this iPad.
extension LibraryView {
    /// The tab «Нет на этом iPad», while a project saved online is not here.
    var onlineTab: OnlineTab? {
        guard !online.isEmpty else { return nil }
        return OnlineTab(projects: online) { project in Task { await getHere(project) } }
    }

    /// One sync pass (version 0.2), then the projects saved online whose id is still in no project file here (0014),
    /// such as one deleted here; none while signed out, offline or when the API cannot be reached, and then nothing is
    /// said: the library is about the projects on this iPad.
    func refreshOnline() async {
        guard account.player != nil else {
            online = []
            return
        }
        if syncOn && !syncing {
            syncing = true
            let language = text.language
            let previous = text.format("sync.previousCopy", "{0}", "%@")
            // One environment for the whole pass: its folder, its records and its servers (the requests stop on a switch).
            let environment = account.environment
            let outcome = await CloudSync(online: account.online, folder: environment.projectsFolder, recordsURL: environment.syncRecordsURL) { made in
                previous.replacingOccurrences(of: "%@", with: Self.when(made, language: language))
            }.run()
            syncing = false
            // Switched meanwhile: the pass stopped, and the environment now chosen gets one of its own.
            guard environment == account.environment else { return await refreshOnline() }
            overQuota = outcome.overQuota
            // Kept until the player hides them or a later pass resolves others (E-sync-4).
            if !outcome.conflicts.isEmpty { conflicts = outcome.conflicts }
            if outcome.changed { await library.reload() }
        }
        let listing = await account.online.listing()
        // Everything was cleared from another device (`TestReset`; the pass above then sent nothing): Test's library here
        // goes too, and the library reads itself again (`account.libraryCleared`).
        if let stamp = listing?.resetDue, TestLibrary.applyReset(account, stamp: stamp) {
            library.showProblem(text["testLibrary.clearedElsewhere"])
            return
        }
        let listed = listing?.projects ?? []
        // A piece deleted here whose online copy is still to go is not offered to get back (E-sync-2).
        let deleting = CloudSync.deletingHere(recordsURL: account.environment.syncRecordsURL)
        online = OnlineProjects.notHere(listed, here: Set(library.summaries.values.compactMap(\.id)).union(deleting))
    }

    /// The library's Delete: the piece's file goes here, and its online copy is marked to go with the pass started now
    /// (the 1.6 plan's decision 14), unless it changed online since (decision 26). A file that would not go stays, marked
    /// for nothing.
    func deleteHereAndOnline(_ url: URL) {
        let id = library.summaries[url]?.id
        library.delete(url)
        guard let id, library.summaries[url] == nil else { return }
        CloudSync.markDeletedHere(id, recordsURL: account.environment.syncRecordsURL)
        Task { await refreshOnline() }
    }

    /// The pieces the last pass resolved with a conflict, a line each, and «Скрыть».
    var conflictLines: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            ForEach(conflicts.indices, id: \.self) { index in
                Text(conflicts[index].line(text))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            Button(text["sync.hide"]) { conflicts = [] }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        }
    }

    /// "11.11.2026, 20:40" in the interface's language, for a previous copy's title.
    private static func when(_ date: Date, language: String) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: language)
        formatter.dateStyle = .short
        formatter.timeStyle = .short
        return formatter.string(from: date)
    }

    /// Get it here (#335): downloads a project saved online as a new project file, which the library opens.
    func getHere(_ project: OnlineProject) async {
        await library.openDownloaded {
            switch await account.online.keep(project) {
            case .success(let url):
                online.removeAll { $0.id == project.id }
                return .success(url)
            case .failure(let failure):
                return .failure(ProjectError(message: text[failure.key]))
            }
        }
    }
}
