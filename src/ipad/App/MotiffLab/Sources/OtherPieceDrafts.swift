import Foundation

/// Another piece's drafts on the «В черновик» sheet (v1.2, collage, the plan's answer 5; drafts spec «Коллаж»): a project
/// file of the folder other than the open piece's, the title its library line shows, and its drafts as music-core reads
/// them from the file (`project.drafts` on piece 0, which writes nothing). The sheet groups them by that title; a fragment
/// added to one of them goes into that file (`SourcesController.addToDraft`), never into this piece.
struct OtherPieceDrafts: Identifiable, Equatable, Sendable {
    /// What music-core read from a project file, kept by its bytes: its summary (the project's id, its title) and its drafts.
    struct Read: Sendable {
        let summary: PieceSummary?
        let drafts: [SourceLayers.Draft]
    }

    let file: URL
    let title: String
    let drafts: [SourceLayers.Draft]
    var id: URL { file }

    /// The pieces of `folder` with drafts, the most recently changed first: every project file but the open piece's (`open`,
    /// and any copy of its project, `project`, whose file music-core refuses while the piece is open here). Each file is read
    /// through music-core only when its bytes changed since it was last read in this `language`.
    @MainActor
    static func list(in folder: URL = ProjectFiles.folder, except open: URL?, project: String?, language: String) async -> [OtherPieceDrafts] {
        let files = ProjectFiles.list(in: folder).map(\.url).filter { $0.standardizedFileURL != open?.standardizedFileURL }
        let read = await cache(language).values(files)
        return files.compactMap { file -> OtherPieceDrafts? in
            guard let kept = read[file], !kept.drafts.isEmpty, project == nil || kept.summary?.id != project else { return nil }
            let fileTitle = ProjectFiles.title(of: file.lastPathComponent)
            return OtherPieceDrafts(file: file, title: kept.summary?.shownTitle(else: fileTitle) ?? fileTitle, drafts: kept.drafts)
        }
    }

    /// A project file's bytes read by music-core in `language` (`project.drafts`); nil when they are not a project file it
    /// can read.
    static func read(_ data: Data, language: String) async -> Read? {
        guard let file = String(data: data, encoding: .utf8) else { return nil }
        let request = facadeRequest(["file": file, "language": language])
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "project.drafts", request) }
        guard let read = FacadeAnswer.decode(PieceDraftsAnswer.self, answer, of: "project.drafts") else { return nil }
        return Read(summary: PieceSummary(answer: answer), drafts: read.drafts.map { SourceLayers.Draft($0) })
    }

    /// The files read so far, in the language they were read in (a draft's caption is music-core's words).
    @MainActor private static var kept: (language: String, cache: ProjectFileCache<Read>)?

    @MainActor
    private static func cache(_ language: String) -> ProjectFileCache<Read> {
        if let kept, kept.language == language { return kept.cache }
        let cache = ProjectFileCache<Read> { await read($0, language: language) }
        kept = (language, cache)
        return cache
    }
}
