import XCTest
@testable import MotiffLab

/// Piece DNA's «Добавить закладки «Структура»» says what it did and where (Andrei, v0.8 finding 6): before, «3» and then
/// «0». music-core files one Structure bookmark per section of the form that has none, so a second press adds none.
final class DnaBookmarksTests: XCTestCase {
    func testTheFirstPressSaysHowManyAndWhereTheyAre() {
        let ru = DnaBookmarks.said(added: 3, UIText(language: "ru"))
        XCTAssertTrue(ru.hasPrefix("Добавлены 3 закладки «Структура»"), ru)
        XCTAssertTrue(ru.contains("в оглавлении"), ru)
        let en = DnaBookmarks.said(added: 1, UIText(language: "en"))
        XCTAssertTrue(en.hasPrefix("1 Structure bookmark added"), en)
        XCTAssertTrue(en.contains("in the contents"), en)
    }

    func testASecondPressSaysEverySectionHasItsBookmarkNotZero() {
        for language in ["ru", "en"] {
            let text = UIText(language: language)
            let said = DnaBookmarks.said(added: 0, text)
            XCTAssertEqual(said, text["dna.bookmarksAlready"])
            XCTAssertNotEqual(said, "dna.bookmarksAlready", "said in \(language)")
            XCTAssertFalse(said.contains("0"), said)
        }
    }
}
