import Foundation

/// A piece a sync pass resolved with a conflict (v1.6 c-17b): what both devices changed differently, by where in the
/// project file music-core says it is (the "in" of `project.resolve`'s "conflicts"), and the previous copy that keeps the
/// other device's. Only what the library tells the player; the merge itself is music-core's (`ProjectMerge`).
struct SyncConflict: Equatable, Sendable {
    /// The piece's title, as the library shows it.
    let title: String
    /// The title of the previous copy kept beside.
    let copyTitle: String
    /// Where music-core found the conflicts: "workspace.observations", "notes.edits", "originals.marks.ink", ….
    let places: [String]

    /// What the player is told (`sync.conflict`): "«Für Elise» изменили на двух устройствах по-разному (идеи, пометки).
    /// Версия, сохранённая раньше, лежит рядом: «Für Elise (прежняя копия, …)»."
    func line(_ text: UIText) -> String {
        let found = Set(places.map { Self.kind($0) })
        let kinds = Self.kinds.map { $0.key }.filter { found.contains($0) }.map { text[$0] }
        return text.format("sync.conflict", title, kinds.joined(separator: ", "), copyTitle)
    }

    /// What the player calls a place in the file, by the first of its path's starts that matches, in this order (bookmarks
    /// on a PDF's pages before the marks there, a Notes' marks before the Notes); a place no start names, such as a field
    /// a later build added, is «другое». The starts are path segments, as the core's "in" joins them with dots.
    private static let kinds: [(key: String, starts: [[String]])] = [
        ("sync.inIdeas", [["workspace", "observations"]]),
        ("sync.inMotifs", [["workspace", "motifs"], ["workspace", "variations"]]),
        ("sync.inDrafts", [["workspace", "drafts"]]),
        ("sync.inResumePoints", [["workspace", "resumePoints"]]),
        ("sync.inBookmarks", [["workspace", "bookmarks"], ["originals", "marks", "bookmarks"]]),
        ("sync.inMarks", [["workspace", "marks"], ["originals", "marks"], ["notes", "marks"]]),
        ("sync.inPhrases", [["workspace", "phrases"]]),
        ("sync.inNotes", [["notes"]]),
        ("sync.inDetails", [["workspace", "metadata"], ["workspace", "settings"], ["workspace", "tempo"], ["workspace", "assembly"], ["workspace", "hiddenViews"]]),
        ("sync.inOther", []),
    ]

    static func kind(_ place: String) -> String {
        let path = place.split(separator: ".").map(String.init)
        return kinds.first { kind in kind.starts.contains { path.starts(with: $0) } }?.key ?? "sync.inOther"
    }
}
