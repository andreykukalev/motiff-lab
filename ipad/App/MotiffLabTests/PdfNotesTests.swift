import XCTest
@testable import MotiffLab

/// PDF | Notes whole whenever both views are on the iPad (#495 BUG-02), and a PDF's pages resting below the floating bar
/// where the notes' pages do (#495 UX-12).
final class PdfNotesTests: XCTestCase {
    /// A piece's summary as music-core's answers carry it (`project`, contract 4).
    private func summary(_ project: String) throws -> PieceSummary {
        try XCTUnwrap(PieceSummary(answer: #"{"project": \#(project)}"#))
    }

    func testAFileWithNotesAndAPdfIsItsOwnOtherViewWhateverTheShelfSays() throws {
        let file = URL(fileURLWithPath: "/shelf/Silent Ocean.motiff.json")
        let own = try summary(#"{"id": "p_1", "hasNotes": true, "document": true, "documentPages": [0, 7680]}"#)
        // The library has no line for the file under this URL: the PDF view showed only «PDF» then.
        let other = try XCTUnwrap(OtherView.of(file, own: own, in: [:]), "the PDF of a piece with notes offers its notes")
        XCTAssertEqual(other.file, file, "the same file in its other view")
        XCTAssertEqual(other.pages.start(of: 2), 7680)
        var stale = own
        stale.hasNotes = false
        stale.documentPages = []
        XCTAssertEqual(OtherView.of(file, own: own, in: [file: stale])?.pages.start(of: 2), 7680, "nor does a line read before the notes")
        XCTAssertNil(OtherView.of(file, in: [:]), "with neither, there is no other view")

        let pdfOnly = try summary(#"{"id": "p_2", "hasNotes": false, "document": true}"#)
        XCTAssertNil(OtherView.of(file, own: pdfOnly, in: [:]), "a PDF with no notes yet: no switch, Recognise instead")
        // Notes whose PDF is linked in a folder (version 0.5): the file holds no PDF, so the switch goes through Sources.
        let linked = try summary(#"{"id": "p_3", "hasNotes": true, "document": false}"#)
        XCTAssertNil(OtherView.of(file, own: linked, in: [:]))
    }

    func testNotesReadIntoAPieceOfTheirOwnStillFindTheirPdf() throws {
        // Notes read between 2026-11-05 and 2026-11-07 name the PDF piece; their own word finds it on the shelf too.
        let notesFile = URL(fileURLWithPath: "/shelf/Nocturne.motiff.json")
        let pdfFile = URL(fileURLWithPath: "/shelf/Nocturne PDF.motiff.json")
        let notes = try summary(#"{"id": "p_notes", "hasNotes": true, "readFrom": "p_pdf", "readFromPages": [0, 5760]}"#)
        let pdf = try summary(#"{"id": "p_pdf", "hasNotes": false, "document": true}"#)
        XCTAssertEqual(OtherView.of(notesFile, own: notes, in: [pdfFile: pdf])?.file, pdfFile)
        XCTAssertEqual(OtherView.of(notesFile, own: notes, in: [pdfFile: pdf])?.pages.start(of: 2), 5760)
    }

    @MainActor
    func testAPdfLinkedHereIsOfferedFromTheScoresFirstFrame() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("PdfNotesTests-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        let own = try summary(#"""
        {"id": "p_1", "hasNotes": true, "document": false, "originals": [{"id": "src_pdf", "kind": "pdf", "fileName": "Santiago.pdf",
         "sha256": "\#(String(repeating: "a", count: 64))", "kept": "link", "inFile": false, "held": [{"device": "ipad"}]}]}
        """#)
        XCTAssertFalse(OriginalsController(opened: own, folder: folder).canOpenLinkedPdf, "no link on this iPad: no PDF to offer")
        let link = OriginalLink(
            bookmark: Data(), fileName: "Santiago.pdf", sha256: String(repeating: "a", count: 64), linkedAt: Date(), folderId: "f1", path: "Santiago.pdf"
        )
        OriginalLinks.save(link, project: "p_1", original: "src_pdf", in: folder)
        // Before `load` has asked music-core again: the switch has its PDF at once, and keeps it.
        XCTAssertTrue(OriginalsController(opened: own, folder: folder).canOpenLinkedPdf, "linked here: PDF | Notes is whole at once")
        var changed = link
        changed.changed = true
        OriginalLinks.save(changed, project: "p_1", original: "src_pdf", in: folder)
        XCTAssertFalse(OriginalsController(opened: own, folder: folder).canOpenLinkedPdf, "the file in the folder is another one now")
        XCTAssertFalse(OriginalsController().canOpenLinkedPdf, "nothing said yet, nothing offered")
    }

    func testThePdfsPagesRestBelowTheBarWhereTheNotesPagesDo() {
        // The bar's 12 pt margin and 44 pt height (s-3, h-touch), then the notes' gap of 16 pt (s-4): score.css `--pad-top`.
        let top = PageRest.pageTop(barBottom: 56, gap: 16)
        XCTAssertEqual(top, 72)
        XCTAssertEqual(PageRest.room(pageTop: top, firstPage: 16), 56, "PDFKit's page break above page 1 counts in the room")
        XCTAssertEqual(PageRest.room(pageTop: top, firstPage: 120), 0, "never less than none")
        let lowest = -PageRest.room(pageTop: top, firstPage: 16)
        XCTAssertEqual(PageRest.offset(page: 16, pageTop: top, lowest: lowest, highest: 5_000), lowest, "page 1 brought to the top rests where the top of the document does")
        XCTAssertEqual(PageRest.offset(page: 1_200, pageTop: top, lowest: lowest, highest: 5_000), 1_128, "a later page's top 72 pt below the view's top")
        XCTAssertEqual(PageRest.offset(page: 6_000, pageTop: top, lowest: lowest, highest: 5_000), 5_000, "the last pages stop at the end")
        XCTAssertEqual(PageRest.offset(page: 600, pageTop: top, lowest: lowest, highest: -200), lowest, "a short document stays at its top")
        XCTAssertEqual(PageRest.pageTop(barBottom: 56), 56 + CGFloat.ds("s-4"), "the gap is the kit's s-4")
    }
}
