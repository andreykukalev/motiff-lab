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
        XCTAssertFalse(variant.names(rest: "rest").isEmpty)
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
        XCTAssertEqual(image.size, CGSize(width: 100, height: 140), "the box on the server's image, at its pixels")
        for point in [CGPoint(x: 50, y: 70), CGPoint(x: 3, y: 3), CGPoint(x: 96, y: 136)] {
            XCTAssertLessThan(brightness(image, at: point), 0.2, "the filled rectangle at \(point)")
        }
    }

    /// How bright one pixel of an image is, from 0 (black) to 1 (white).
    private func brightness(_ image: UIImage, at point: CGPoint) -> CGFloat {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let one = UIGraphicsImageRenderer(size: CGSize(width: 1, height: 1), format: format).image { _ in image.draw(at: CGPoint(x: -point.x, y: -point.y)) }
        guard let data = one.cgImage?.dataProvider?.data, let bytes = CFDataGetBytePtr(data) else { return 1 }
        return CGFloat(bytes[1]) / 255
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
        XCTAssertEqual(variant.names(rest: "rest"), "G5 A5")
        let rest = try XCTUnwrap(ScanVariant(try json(#"{"engines": ["homr"], "staves": [{"part": 1, "staff": 1, "notes": []}, {"part": 1, "staff": 2, "notes": [{"onset": 0, "pitch": 48, "length": 1920, "name": "C3"}]}]}"#)))
        XCTAssertEqual(rest.names(rest: UIText(language: "ru")["scanReview.rest"]), "пауза · C3")
        XCTAssertEqual(rest.names(rest: UIText(language: "en")["scanReview.rest"]), "rest · C3")
        XCTAssertNil(ScanVariant(try json(#"{"staves": []}"#)))
    }

    func testABarTheEnginesDisagreeOnIsKnownByTheReasonCheckWrites() throws {
        let shelf = try XCTUnwrap(CheckShelf(answer: #"{"flags": [{"bar": 2, "reason": "enginesDisagree", "name": "Engines disagree", "text": "", "resolved": false, "span": {"onset": 1920, "end": 3840}}], "open": 1}"#))

        XCTAssertEqual(shelf.openBars.first?.flags.first?.reason, CheckShelf.enginesDisagree)
    }

    func testAVariantsUndoHoldsOnlyUntilAnotherEditOrOtherNotes() {
        var choice = ScanVariantChoice()
        choice.chose(bar: 4, engines: ["audiveris"], notes: "n1")
        XCTAssertEqual(choice.engines(in: 4), ["audiveris"])
        XCTAssertNil(choice.engines(in: 5))
        choice.notesRead("n1")
        XCTAssertEqual(choice.bar, 4, "the score engraved again on the same notes keeps it")
        choice.notesRead("n2")
        XCTAssertNil(choice.bar, "other working notes end it")
        choice.chose(bar: 4, engines: ["audiveris"], notes: "n1")
        choice.edited()
        XCTAssertNil(choice.engines(in: 4), "another edit ends it, so its Undo cannot take back an assistant's draft")
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
        for key in ["scanReview.rest", "scanReview.page", "scanReview.cropLabel", "scanReview.variants", "scanReview.variantHint", "scanReview.none", "scanReview.missing", "scanReview.chosen", "scanReview.undo"] {
            for text in [UIText(language: "en"), UIText(language: "ru")] {
                XCTAssertNotEqual(text[key], key, "\(text.language): \(key)")
            }
        }
    }
}
