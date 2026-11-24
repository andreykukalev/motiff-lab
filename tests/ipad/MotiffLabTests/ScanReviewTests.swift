import UIKit
import XCTest
@testable import MotiffLab

/// A scan's bars to check on the iPad (version 1.3 i-2, decision record 0035 section 6): what `scan.join` answers of them
/// kept on this iPad only, by the notes' fingerprint, with each bar's crop of the PDF page where the server's evidence
/// placed it, turned as the server turned the page; the variant sent back to music-core as it came; the words.
@MainActor
final class ScanReviewTests: XCTestCase {
    private var root: URL!

    override func setUp() {
        super.setUp()
        root = FileManager.default.temporaryDirectory.appendingPathComponent("scan-review-\(UUID().uuidString)", isDirectory: true)
    }

    /// A JSON object as music-core writes it.
    private func json(_ text: String) throws -> [String: Any] {
        try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any])
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: root)
        super.tearDown()
    }

    func testTheJoinAnswersBarsToCheckAreKeptByTheNotesFingerprintWithTheirVariants() throws {
        let (_, text) = try XCTUnwrap(FacadeAnswerFixturesTests.answers("scan.join.readings").first)
        let answer = try XCTUnwrap(facadeObject(text))
        let fingerprint = try XCTUnwrap(answer["fingerprint"] as? String)

        ScanCrops.keepReview(answer, pages: [1, 2, 3], pdf: Data(), in: root)

        let review = try XCTUnwrap(ScanReviewStore.load(fingerprint, in: root))
        XCTAssertFalse(review.bars.isEmpty, "the fixture's merge has bars to check")
        XCTAssertTrue(review.bars.allSatisfy { !$0.cropped }, "no PDF, no crop")
        let variant = try XCTUnwrap(review.bars.lazy.flatMap(\.variants).first)
        XCTAssertFalse(variant.engines.isEmpty)
        XCTAssertFalse(variant.names.isEmpty)
        XCTAssertNil(ScanReviewStore.load("another", in: root), "notes read elsewhere have none")
    }

    func testAnAnswerWithNoBarToCheckKeepsNothing() throws {
        ScanCrops.keepReview(try json(#"{"fingerprint": "f1", "pages": [{"merge": {"bars": [{"bar": 1, "level": "sure"}]}}]}"#), pages: [1], pdf: Data(), in: root)

        XCTAssertNil(ScanReviewStore.load("f1", in: root))
    }

    func testABarsBoxOnATurnedPageIsFoundOnThePageAsThePdfShowsIt() {
        func rect(_ rotated: Int) -> CGRect { ScanCrop(box: [100, 80, 200, 220], width: 1000, height: 400, rotated: rotated).pageRect }
        let cases: [(Int, CGRect, UIImage.Orientation)] = [
            (0, CGRect(x: 0.1, y: 0.2, width: 0.1, height: 0.35), .up),
            (90, CGRect(x: 0.2, y: 0.8, width: 0.35, height: 0.1), .right),
            (180, CGRect(x: 0.8, y: 0.45, width: 0.1, height: 0.35), .down),
            (270, CGRect(x: 0.45, y: 0.1, width: 0.35, height: 0.1), .left),
        ]
        for (rotated, expected, orientation) in cases {
            let found = rect(rotated)
            XCTAssertEqual(found.minX, expected.minX, accuracy: 1e-9, "\(rotated)")
            XCTAssertEqual(found.minY, expected.minY, accuracy: 1e-9, "\(rotated)")
            XCTAssertEqual(found.width, expected.width, accuracy: 1e-9, "\(rotated)")
            XCTAssertEqual(found.height, expected.height, accuracy: 1e-9, "\(rotated)")
            XCTAssertEqual(ScanCrop(box: [0, 0, 1, 1], width: 10, height: 10, rotated: rotated).orientation, orientation)
        }
        XCTAssertEqual(ScanCrop(box: [100, 80, 200, 220], width: 1000, height: 400, rotated: 90).pageSize, CGSize(width: 400, height: 1000))
    }

    func testABarsCropIsCutFromItsPdfPageAndTurnedAsTheServersImage() throws {
        // A page 400 wide and 1000 high the server turned a quarter clockwise: its image is 1000 wide and 400 high.
        let pdf = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 400, height: 1000)).pdfData { context in
            context.beginPage()
            UIColor.black.setFill()
            context.fill(CGRect(x: 80, y: 800, width: 140, height: 100))
        }
        let answer = try json("""
        {"fingerprint": "f2", "pages": [{"merge": {"bars": [
         {"bar": 5, "level": "check", "variants": [], "crop": {"box": [100, 80, 200, 220], "width": 1000, "height": 400, "rotated": 90}}]}}]}
        """)

        ScanCrops.keepReview(answer, pages: [1], pdf: pdf, in: root)

        XCTAssertEqual(ScanReviewStore.load("f2", in: root)?.bar(5)?.cropped, true)
        let image = try XCTUnwrap(ScanReviewStore.crop("f2", bar: 5, in: root).flatMap { UIImage(data: $0) })
        XCTAssertEqual(image.size.width / image.size.height, 100.0 / 140.0, accuracy: 0.05, "as wide and high as the box on the server's image")
    }

    func testAVariantGoesBackToMusicCoreAsItCame() throws {
        let variant = try XCTUnwrap(ScanVariant(try json("""
        {"engines": ["copisteria"], "staves": [{"part": 1, "staff": 1, "notes": [{"onset": 0, "pitch": 79, "length": 480, "name": "G5"}, {"onset": 480, "pitch": 81, "length": 480, "name": "A5"}]}]}
        """)))

        let fields = variant.fields(bar: 2)
        XCTAssertEqual(fields["bar"] as? Int, 2)
        let staff = try XCTUnwrap((fields["staves"] as? [[String: Any]])?.first)
        XCTAssertEqual(staff["part"] as? Int, 1)
        XCTAssertEqual((staff["notes"] as? [[String: Int]])?.map { $0["pitch"] }, [79, 81])
        XCTAssertEqual(variant.names, "G5 A5")
        XCTAssertNil(ScanVariant(try json(#"{"staves": []}"#)))
    }

    func testCheckNamesTheWorkingNotesByTheirFingerprint() {
        XCTAssertEqual(CheckShelf(answer: #"{"flags": [], "open": 0, "fingerprint": "abc"}"#)?.fingerprint, "abc")
        XCTAssertNil(CheckShelf(answer: #"{"flags": [], "open": 0}"#)?.fingerprint)
    }

    func testTheJoinedPagesNameTheirPdfPages() {
        let joined = ScanJobs.joined(device: [3: "V"], server: [["page": 1, "engines": ["homr": ["state": "read", "musicxml": "X"]]], ["page": 2, "engines": [String: Any]()]])

        XCTAssertEqual(joined.numbers, [1, 3])
        XCTAssertEqual(joined.unread, [2])
    }

    func testThePanelsWordsAreInBothLanguages() {
        for key in ["scanReview.page", "scanReview.cropLabel", "scanReview.variants", "scanReview.variantHint", "scanReview.none", "scanReview.missing", "scanReview.chosen", "scanReview.undo"] {
            for text in [UIText(language: "en"), UIText(language: "ru")] {
                XCTAssertNotEqual(text[key], key, "\(text.language): \(key)")
            }
        }
    }
}
