import XCTest
@testable import MotiffLab

/// Piece DNA's «Добавить закладки «Структура»» says what it did and where (Andrei, v0.8 finding 6): before, «3» and then
/// «0». music-core files one Structure bookmark per section of the form that has none, so a second press adds none.
final class DnaBookmarksTests: XCTestCase {
    func testTheFirstPressSaysHowManyAndWhereTheyAre() throws {
        let ru = try XCTUnwrap(DnaBookmarks.said(added: 3, UIText(language: "ru")))
        XCTAssertTrue(ru.hasPrefix("Добавлены 3 закладки «Структура»"), ru)
        XCTAssertTrue(ru.hasSuffix("В оглавлении: «Закладки» → «Структура»."), ru)
        let en = try XCTUnwrap(DnaBookmarks.said(added: 1, UIText(language: "en")))
        XCTAssertTrue(en.hasPrefix("1 Structure bookmark added"), en)
        XCTAssertTrue(en.hasSuffix("In the contents: Bookmarks › Structure."), en)
    }

    func testASecondPressSaysNoneWasAddedAndWhereTheyAreNotZero() throws {
        for language in ["ru", "en"] {
            let text = UIText(language: language)
            let said = try XCTUnwrap(DnaBookmarks.said(added: 0, text))
            XCTAssertEqual(said, text["dna.bookmarksAlready"] + " " + text["dna.bookmarksWhere"])
            XCTAssertFalse(said.contains("dna."), "said in \(language)")
            XCTAssertFalse(said.contains("0"), said)
        }
    }

    func testNothingIsSaidWhenNothingCouldBeAdded() {
        XCTAssertNil(DnaBookmarks.said(added: nil, UIText(language: "ru")), "no piece open, or music-core refused")
    }
}
