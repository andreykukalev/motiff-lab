import UIKit
import XCTest

@testable import MotiffLab

/// What the scan tests share (`ScanJobsTests`, and the baselines of 1.3 in `tests/ipad/Baseline/Scan*BaselineTests`): the
/// scan server answered here (`StubAPI`) and what it was asked, an engine's reading of a page, a PDF piece and pictures of
/// pages, a piece's file changed and its notes read, the experiment «Распознавание в фоне» turned on and back, and a
/// bounded wait. Only public-domain or synthetic pages.
@MainActor
enum ScanSupport {
    nonisolated static let server = URL(string: "http://scan.test/")!

    /// The Mutopia Promenade (public domain, LilyPond) of the vector reader's fixtures, where it lies in the repository.
    static var promenade: URL {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("backend/Unit/MotiffLab.MusicCore.Tests/Import/Vector/Fixtures/promenade-a4.pdf")
    }

    static func answer(_ status: Int, _ body: String) -> StubAPI.Answer {
        StubAPI.Answer(status: status, body: Data(body.utf8))
    }

    /// A 200 with `object` as its JSON body.
    static func json(_ object: [String: Any]) -> StubAPI.Answer {
        StubAPI.Answer(status: 200, body: (try? JSONSerialization.data(withJSONObject: object)) ?? Data())
    }

    /// `/health` of a server of 1.3: four engines timed there, nothing queued, four at once.
    static var health: StubAPI.Answer {
        let engines: [[String: Any]] = [("homr", 75), ("copisteria", 90), ("audiveris", 70), ("transcoda", 125)].map { ["engine": $0.0, "secondsPerPage": $0.1] }
        return json(["engine": "homr", "version": "0.7.0", "engines": engines, "jobs": ["queue": 0, "workers": 4]])
    }

    /// `GET /jobs/{id}/result`: each page read by homr as `page(step)`, or by no engine (a nil step).
    static func result(_ id: String, _ pages: [(page: Int, step: String?)]) -> StubAPI.Answer {
        let entries: [[String: Any]] = pages.map { entry in
            let homr: [String: Any] = entry.step.map { ["state": "read", "version": "0.7.0", "musicxml": page($0)] as [String: Any] } ?? ["state": "not_read"]
            return ["page": entry.page, "engines": ["homr": homr]]
        }
        return json(["id": id, "pages": entries])
    }

    /// How many requests of `method` to `path` on the stub server were sent.
    static func sent(_ method: String, _ path: String) -> Int {
        StubAPI.requests.filter { ($0.httpMethod ?? "GET") == method && $0.url?.absoluteString == server.absoluteString + path }.count
    }

    /// A page of one bar with one whole note on `step` 4, as an engine would read it.
    static func page(_ step: String) -> String {
        """
        <?xml version="1.0" encoding="UTF-8"?><score-partwise version="3.1"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>\
        <part id="P1"><measure number="1"><attributes><divisions>1</divisions><key><fifths>0</fifths></key><time><beats>4</beats><beat-type>4</beat-type></time>\
        <clef><sign>G</sign><line>2</line></clef></attributes><note><pitch><step>\(step)</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note></measure></part></score-partwise>
        """
    }

    /// A PDF of `pages` pictures of pages, 1000 × 400 points each: a photo of the page (no text, no lines), white with a
    /// black block where `dark` is.
    static func scannedPdf(pages: Int, dark: CGRect = CGRect(x: 100, y: 80, width: 100, height: 140)) -> Data {
        let size = CGSize(width: 1000, height: 400)
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let photo = UIGraphicsImageRenderer(size: size, format: format).image { context in
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            UIColor.black.setFill()
            context.fill(dark)
        }
        return UIGraphicsPDFRenderer(bounds: CGRect(origin: .zero, size: size)).pdfData { context in
            for _ in 0..<pages {
                context.beginPage()
                photo.draw(in: CGRect(origin: .zero, size: size))
            }
        }
    }

    /// A PDF piece of `pdf`, `pages` pages, with no notes yet, saved in `folder` as the library keeps it.
    static func pdfPiece(in folder: URL, pdf: Data = Data("%PDF-1.4\n%%EOF\n".utf8), pages: Int = 2) throws -> URL {
        let request = #"{"id": "p_pages", "photos": \#(pages), "now": "2026-11-24T18:00:00Z"}"#
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: "Pages.pdf", data: pdf, request: request)))
        let piece = try XCTUnwrap((opened["piece"] as? NSNumber)?.int32Value, "\(opened)")
        defer { MusicCore.close(piece: piece) }
        let url = folder.appendingPathComponent("Pages.motiff.json")
        try Data(MusicCore.call(piece: piece, "project.save", "{}").utf8).write(to: url)
        return url
    }

    /// The piece of the file at `url` opened, `call` made on it and the file saved, as the score's screen saves a change.
    static func change(_ url: URL, _ call: String, _ request: String) throws {
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: url.lastPathComponent, data: try Data(contentsOf: url), request: #"{"language": "en"}"#)))
        let piece = try XCTUnwrap((opened["piece"] as? NSNumber)?.int32Value)
        defer { MusicCore.close(piece: piece) }
        let answer = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, call, request)))
        XCTAssertNil(answer["error"], "\(call): \(answer)")
        try Data(MusicCore.call(piece: piece, "project.save", "{}").utf8).write(to: url, options: .atomic)
    }

    /// The pitches the piece of the file at `url` plays in `bar`, its corrections on, from the lowest; none for a bar it
    /// does not have.
    static func pitches(_ url: URL, bar: Int) throws -> [Int] {
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: url.lastPathComponent, data: try Data(contentsOf: url), request: #"{"language": "en"}"#)))
        let piece = try XCTUnwrap((opened["piece"] as? NSNumber)?.int32Value)
        defer { MusicCore.close(piece: piece) }
        let answer = facadeObject(MusicCore.call(piece: piece, "corrections", #"{"bar": \#(bar)}"#))
        return (answer?["notes"] as? [[String: Any]] ?? []).compactMap { $0["pitch"] as? Int }.sorted()
    }

    /// Turns «Распознавание в фоне» on in this iPad's settings, as its switch does; answers what it was, for `restore`.
    static func jobsOn() async -> String {
        let before = ScanServer.jobs ? "on" : "off"
        await DeviceSettings.shared.update(["set": ["id": ScanServer.jobsKey, "value": "on"]])
        XCTAssertTrue(ScanServer.jobs, "the experiment on")
        return before
    }

    /// The experiment as it was before `jobsOn`; nothing when it was not turned on.
    static func restore(_ before: String?) async {
        guard let before else { return }
        await DeviceSettings.shared.update(["set": ["id": ScanServer.jobsKey, "value": before]])
    }

    /// Waits, `seconds` at most, for what the queue or a controller does in a task of its own.
    static func wait(_ what: String, seconds: Int = 15, until done: () -> Bool) async throws {
        for _ in 0..<(seconds * 20) where !done() { try await Task.sleep(for: .milliseconds(50)) }
        XCTAssertTrue(done(), what)
    }
}
