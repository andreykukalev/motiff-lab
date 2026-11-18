import Foundation

/// What Piece DNA's «Добавить закладки «Структура»» did, in words (Andrei, v0.8 finding 6: «3», then «0», said neither what
/// happened nor where): music-core files one Structure bookmark per section of the form that has none yet
/// (`bookmark.form`), so a second press adds none. The words say how many were added, or that every section already has
/// one, and where they are: the contents, Bookmarks › Structure. Nothing is said when nothing could be added (no piece
/// open, or music-core refused).
enum DnaBookmarks {
    static func said(added: Int?, _ text: UIText) -> String? {
        guard let added else { return nil }
        let what = added > 0 ? text.count("dna.bookmarksAdded", added) : text["dna.bookmarksAlready"]
        return what + " " + text["dna.bookmarksWhere"]
    }
}
