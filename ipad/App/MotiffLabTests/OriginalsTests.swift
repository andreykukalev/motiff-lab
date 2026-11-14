import FileProvider
import UIKit
import XCTest
@testable import MotiffLab

/// Local-first originals on the iPad (version 0.3): the badge from the devices' marks and this iPad's link, the link to a
/// file in Files kept beside the projects and checked by size and date before its fingerprint, and «Привязать оригинал»
/// through music-core (`sources.relink`, `sources.held`), which writes only the iPad's mark into the project file.
final class OriginalsTests: XCTestCase {
    private var folder: URL!

    override func setUpWithError() throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("OriginalsTests-\(UUID().uuidString)")
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: folder)
    }

    private func original(inFile: Bool = false, held: [String] = [], kind: String = "pdf") -> OriginalEntry {
        OriginalEntry(
            id: kind == "pdf" ? "src_pdf" : "perf_1", kind: kind, shown: "Nocturne.pdf", fileName: "Nocturne.pdf",
            sha256: String(repeating: "a", count: 64), bytes: 1000, pages: 3, kept: "file", inFile: inFile, held: held
        )
    }

    private func link(changed: Bool = false) -> OriginalLink {
        OriginalLink(
            bookmark: Data([1, 2, 3]), fileName: "Nocturne.pdf", sha256: String(repeating: "a", count: 64),
            bytes: 1000, modified: Date(timeIntervalSince1970: 1_000_000), changed: changed, linkedAt: Date(timeIntervalSince1970: 1_000_000)
        )
    }

    func testTheBadgeSaysWhereTheBytesAreAsThisIpadSeesThem() {
        XCTAssertEqual(OriginalBadge.of(original(inFile: true, held: ["web"]), link: nil), .here)
        XCTAssertEqual(OriginalBadge.of(original(inFile: true), link: link(changed: true)), .here, "a copy in the piece is here whatever the link says")
        XCTAssertEqual(OriginalBadge.of(original(held: ["web"]), link: link()), .linked)
        XCTAssertEqual(OriginalBadge.of(original(held: ["ipad", "web"]), link: link(changed: true)), .changed)
        XCTAssertEqual(OriginalBadge.of(original(held: ["web"]), link: nil), .inBrowser)
        XCTAssertEqual(OriginalBadge.of(original(held: ["ipad", "web"]), link: nil), .inBrowser, "another kind of device comes first")
        XCTAssertEqual(OriginalBadge.of(original(held: ["ipad"]), link: nil), .otherIpad, "an iPad's mark with no link here is another iPad's")
        XCTAssertEqual(OriginalBadge.of(original(), link: nil), .nowhere)
        XCTAssertTrue(OriginalBadge.here.isHere)
        XCTAssertTrue(OriginalBadge.linked.isHere)
        XCTAssertFalse(OriginalBadge.changed.isHere)
    }

    func testTheBadgesAreWordedInBothLanguages() {
        let badges: [OriginalBadge] = [.here, .linked, .changed, .otherIpad, .inBrowser, .nowhere]
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for badge in badges {
                XCTAssertFalse(badge.text(text).hasPrefix("originals."), "\(language): \(badge)")
            }
            for problem in [LinkProblem.missing, .offline, .unreadable] {
                XCTAssertFalse(problem.text(text).hasPrefix("originals."), "\(language): \(problem)")
            }
        }
        let ru = UIText(language: "ru")
        XCTAssertEqual(OriginalBadge.inBrowser.text(ru), "Только в браузере на компьютере")
        XCTAssertEqual(OriginalBadge.linked.text(ru), "Ссылка на файл в «Файлах»")
        XCTAssertEqual(OriginalBadge.changed.text(ru), "Оригинал изменился")
        XCTAssertEqual(OriginalBadge.nowhere.text(ru), ru["originals.nowhere"], "the web's own words")
    }

    func testTheLibraryLineNamesTheFirstOriginalNotHere() {
        let ru = UIText(language: "ru")
        let away = OriginalAway.of([original(held: ["web"])]) { _ in nil }
        XCTAssertEqual(away, OriginalAway(kind: "pdf", badge: .inBrowser))
        XCTAssertEqual(away?.text(ru), "PDF: только в браузере на компьютере")
        let linkedPdf = OriginalAway.of([original(held: ["web"]), original(held: ["web"], kind: "audio")]) { $0.kind == "pdf" ? self.link() : nil }
        XCTAssertEqual(linkedPdf?.kind, "audio", "a linked PDF is here; the recording is not")
        XCTAssertNil(OriginalAway.of([original(inFile: true)]) { _ in nil })
        let midi = OriginalEntry(id: "src_file", kind: "midi", shown: "a.mid", fileName: "a.mid", sha256: nil, bytes: nil, pages: nil, kept: "file", inFile: false, held: [])
        XCTAssertNil(OriginalAway.of([midi]) { _ in nil }, "a MIDI file is the notes, not an original to show")
        XCTAssertEqual(OriginalAway(kind: "pdf", badge: .changed).text(UIText(language: "en")), "PDF: the original has changed")
    }

    func testOriginalsAreReadFromTheFacadesAnswer() throws {
        let answer = #"{"originals": [{"id": "src_pdf", "kind": "pdf", "shown": "Nocturne", "fileName": "Nocturne.pdf", "sha256": null, "bytes": 12, "pages": null, "durationMs": null, "kept": "file", "inFile": false, "held": [{"device": "web", "at": "2026-11-12T01:00:00Z"}]}]}"#
        let list = OriginalEntry.list(facadeObject(answer)?["originals"])
        let pdf = try XCTUnwrap(list.first)
        XCTAssertEqual(list.count, 1)
        XCTAssertEqual(pdf.shown, "Nocturne")
        XCTAssertNil(pdf.sha256)
        XCTAssertNil(pdf.pages)
        XCTAssertEqual(pdf.bytes, 12)
        XCTAssertEqual(pdf.held, ["web"])
        XCTAssertTrue(pdf.isDocument)
        XCTAssertTrue(pdf.isShown)
    }

    func testALinkIsTheSameFileWhileItsSizeAndDateAreAndItsFingerprintDecidesOtherwise() {
        let kept = link()
        let date = Date(timeIntervalSince1970: 1_000_000)
        XCTAssertEqual(kept.check(bytes: 1000, modified: date), .same)
        XCTAssertEqual(kept.check(bytes: 1001, modified: date), .unknown)
        XCTAssertEqual(kept.check(bytes: 1000, modified: date.addingTimeInterval(60)), .unknown)
        XCTAssertEqual(kept.check(bytes: nil, modified: date), .unknown)

        // Touched but the same bytes: the new size and date are the original's from now on.
        let touched = kept.verified(sha256: kept.sha256, bytes: 1000, modified: date.addingTimeInterval(60))
        XCTAssertFalse(touched.changed)
        XCTAssertEqual(touched.check(bytes: 1000, modified: date.addingTimeInterval(60)), .same)

        // Another file: changed, and not read again while it stays as it is.
        let other = kept.verified(sha256: String(repeating: "b", count: 64), bytes: 2000, modified: date.addingTimeInterval(120))
        XCTAssertTrue(other.changed)
        XCTAssertEqual(other.check(bytes: 2000, modified: date.addingTimeInterval(120)), .changed)
        XCTAssertEqual(other.check(bytes: 1000, modified: date), .same, "back to the original's size and date")
        XCTAssertFalse(other.verified(sha256: kept.sha256, bytes: 1000, modified: date).changed, "the original back again")
    }

    func testLinksAreKeptBesideTheProjectsByProjectAndOriginal() throws {
        let kept = link()
        XCTAssertNil(OriginalLinks.load(project: "p_1", original: "src_pdf", in: folder))
        OriginalLinks.save(kept, project: "p_1", original: "src_pdf", in: folder)
        XCTAssertEqual(OriginalLinks.load(project: "p_1", original: "src_pdf", in: folder), kept)
        XCTAssertNil(OriginalLinks.load(project: "p_2", original: "src_pdf", in: folder))
        XCTAssertEqual(OriginalLinks.url(project: "p_1", original: "src_pdf", in: folder).lastPathComponent, "src_pdf.json")
        XCTAssertEqual(OriginalLinks.safe("../a b/c"), "___a_b_c")
        OriginalLinks.remove(project: "p_1", original: "src_pdf", in: folder)
        XCTAssertNil(OriginalLinks.load(project: "p_1", original: "src_pdf", in: folder))
    }

    func testNoConnectionAndAMissingFileAreToldApart() {
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSURLErrorDomain, code: NSURLErrorNotConnectedToInternet)), .offline)
        let unreachable = NSError(domain: NSFileProviderErrorDomain, code: NSFileProviderError.Code.serverUnreachable.rawValue)
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSCocoaErrorDomain, code: NSFileReadUnknownError, userInfo: [NSUnderlyingErrorKey: unreachable])), .offline)
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSCocoaErrorDomain, code: NSFileReadNoSuchFileError)), .missing)
        XCTAssertEqual(LinkProblem.of(NSError(domain: NSCocoaErrorDomain, code: NSFileReadCorruptFileError)), .unreadable)
    }

    func testTheSamePdfIsKeptInThePieceAgainAndTheIpadWritesOnlyItsOwnMark() throws {
        // A piece with a PDF kept in it, as the online copy has it: notes, and the PDF only described, held by the iPad.
        let example = try XCTUnwrap(KitPiece.example)
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en"}"#)))
        let first = Int32(opened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: first) }
        let pdf = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            context.beginPage()
        }
        XCTAssertNil(facadeObject(MusicCore.call(piece: first, "sources.document", json(["pdf": pdf.base64EncodedString(), "pages": 1])))?["error"])
        let saved = MusicCore.call(piece: first, "project.save")
        let copy = try XCTUnwrap(facadeObject(MusicCore.call(piece: 0, "project.online", json(["file": saved])))?["file"] as? String)

        let online = try XCTUnwrap(facadeObject(MusicCore.open(fileName: "Elise.motiff.json", data: Data(copy.utf8), request: #"{"language": "en"}"#)))
        let piece = Int32(online["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: piece) }
        let sources = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources")))
        XCTAssertNotNil(sources["projectId"] as? String)
        let described = try XCTUnwrap(OriginalEntry.list(sources["originals"]).first(where: \.isDocument))
        XCTAssertFalse(described.inFile)
        XCTAssertEqual(described.held, ["ipad"])
        XCTAssertEqual(described.sha256, CloudSync.hash(pdf))
        XCTAssertEqual(OriginalBadge.of(described, link: nil), .otherIpad)

        // Its own mark taken back and set again, as a link keeps it, with no PDF in the piece.
        let gone = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.held", json(["id": "src_pdf", "held": false]))))
        XCTAssertEqual(OriginalEntry.list(gone["originals"]).first?.held, [])
        let linked = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.held", json(["id": "src_pdf", "held": true]))))
        XCTAssertEqual(OriginalEntry.list(linked["originals"]).first?.held, ["ipad"])
        XCTAssertTrue(linked["document"] is NSNull)

        // Another PDF is refused; the same one is kept in the piece, the same original.
        let other = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 842, height: 595), format: UIGraphicsPDFRendererFormat()).pdfData { context in
            context.beginPage()
            context.beginPage()
        }
        let refused = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.relink", json(["pdf": other.base64EncodedString()]))))
        XCTAssertEqual(refused["linked"] as? Bool, false)
        let kept = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "sources.relink", json(["pdf": pdf.base64EncodedString(), "pages": 1]))))
        XCTAssertEqual(kept["linked"] as? Bool, true)
        XCTAssertEqual(SourcesInfo(answer: MusicCore.call(piece: piece, "sources"))?.document?.pages, 1)
        let after = OriginalEntry.list(kept["originals"]).filter(\.isDocument)
        XCTAssertEqual(after.count, 1, "no second original")
        XCTAssertEqual(after.first?.inFile, true)
    }

    private func json(_ object: [String: Any]) -> String {
        String(decoding: (try? JSONSerialization.data(withJSONObject: object)) ?? Data("{}".utf8), as: UTF8.self)
    }
}
