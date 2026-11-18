import UIKit
import XCTest

@testable import MotiffLab

/// VIEW-02 over many switches (#681): a piece with notes and a PDF kept inside it goes from its notes to its PDF and back
/// again and again, each view opened in the other's place as the library opens it (LibraryView.switchView, openFile,
/// show), through the real objects of both screens: the score's workspace with its controllers, its two web views and
/// their pages, drawn, appearing as the view's three tasks make it, handed to the PDF by the bar's switch and gone
/// (ScoreWorkspaceLife.swift); and the PDF's controllers as PdfScreen makes and loads them, handed back to the notes and
/// gone. Once the last screen has gone, nothing a visited screen made is still alive: a screen kept by anything would keep
/// its web views and its engraved pages with it, switch after switch (v0.6.4 spec item 11). What SwiftUI keeps of a view
/// is not seen here; the switch test (MotiffLabScreens/SwitchUITests) and the device see that.
@MainActor
final class SwitchReleaseTests: XCTestCase {
    private static let rounds = 5
    /// The project file switched, and the project's id, by which its views are kept on the device.
    private var file: URL?
    private var project: String?
    /// The pieces opened in music-core, closed again whether or not the switches closed them.
    private var opened: [Int32] = []
    /// What each visited screen made, by name, and whether it is still alive.
    private var made: [(name: String, alive: () -> Bool)] = []

    override func tearDown() async throws {
        for piece in opened {
            _ = ClosedPieces.take(piece)
            MusicCore.close(piece: piece)
        }
        opened = []
        if let file {
            try? FileManager.default.removeItem(at: file)
            SourcesHandover.drop(file)
        }
        if let project {
            for key in [ViewStates.notesKey(project), ViewStates.pdfKey(project), ViewStates.pdfPageKey(project)] {
                UserDefaults.standard.removeObject(forKey: key)
            }
        }
        made = []
    }

    func testEveryScreenVisitedIsLetGo() async throws {
        let file = try await pieceWithNotesAndPdf()
        var notesLanding: Landing?
        for round in 1...Self.rounds {
            let pdfLanding = try await visitNotes(file, at: notesLanding, round: round)
            notesLanding = try await visitPdf(file, at: pdfLanding, round: round)
        }
        for _ in 0..<100 where made.contains(where: { $0.alive() }) {
            try await Task.sleep(for: .milliseconds(100))
        }
        XCTAssertEqual(made.count, Self.rounds * 17, "every screen's objects are watched")
        XCTAssertEqual(made.filter { $0.alive() }.map(\.name), [], "what the visited screens made is still alive")
    }

    // MARK: - The two screens

    /// The notes' screen: its workspace made, its page drawn, appearing, then handed to the PDF by the bar's switch (the
    /// library closing the piece as the PDF opens) and gone. Answers where the PDF opens.
    private func visitNotes(_ file: URL, at landing: Landing?, round: Int) async throws -> Landing {
        let piece = try await open(file, at: landing, asPdf: false)
        let workspace = ScoreWorkspace(piece: piece)
        let views = [workspace.score.webView, workspace.roll.webView]
        let window = UIWindow(frame: CGRect(x: 0, y: 0, width: 1024, height: 1366))
        for view in views {
            view.frame = window.bounds
            window.addSubview(view)
        }
        window.isHidden = false
        workspace.score.show(piece, dark: false)
        let drawn = await workspace.score.whenRendered()
        XCTAssertTrue(drawn, "round \(round): the notes are drawn")
        await workspace.appeared()
        await workspace.loadOriginals()
        await workspace.loadSources(reopen: { _ in }, pick: { _ in })
        var handed: Landing?
        let toPdf = workspace.toPdf { landing in
            await ClosedPieces.close(piece.id)
            handed = landing
        }
        XCTAssertNotNil(toPdf, "round \(round): the notes have their PDF to go to")
        toPdf?()
        for _ in 0..<300 where handed == nil {
            try await Task.sleep(for: .milliseconds(50))
        }
        workspace.disappeared()
        views.forEach { $0.removeFromSuperview() }
        window.isHidden = true
        let name = "round \(round) notes:"
        watch("\(name) workspace", workspace)
        let controllers: [(String, AnyObject)] = [
            ("score", workspace.score), ("roll", workspace.roll), ("player", workspace.player), ("insights", workspace.insights),
            ("assistant", workspace.assistant), ("contents", workspace.contents), ("originals", workspace.originals),
            ("sources", workspace.sources), ("score's web view", workspace.score.webView), ("roll's web view", workspace.roll.webView),
            ("score's page", workspace.score.scorePage), ("roll's page", workspace.roll.page),
        ]
        for (part, object) in controllers { watch("\(name) \(part)", object) }
        return try XCTUnwrap(handed, "round \(round): the notes are handed to the PDF")
    }

    /// The PDF's screen: its pages, sources and originals read as PdfScreen reads them, then handed to the notes as its
    /// switch does (the piece saved, the library closing it as the notes open) and gone. Answers where the notes open.
    private func visitPdf(_ file: URL, at landing: Landing, round: Int) async throws -> Landing {
        let piece = try await open(file, at: landing, asPdf: true)
        let other = try XCTUnwrap(piece.other, "round \(round): the PDF has its notes to go to")
        let pdf = PdfController()
        let sources = SourcesController(layers: SourcesHandover.layers(for: piece.file))
        let originals = OriginalsController()
        await pdf.load(piece: piece.id, text: UIText.current, page: piece.landing?.page)
        XCTAssertNotNil(pdf.document, "round \(round): the PDF's pages are read")
        await sources.load(piece: piece.id, file: piece.file)
        SourcesHandover.drop(piece.file)
        await originals.load(piece)
        SourcesHandover.give(sources.layers, to: other.file)
        try await ProjectFiles.save(piece: piece.id, to: file)
        await ClosedPieces.close(piece.id)
        XCTAssertTrue(ClosedPieces.take(piece.id), "round \(round): the PDF's screen goes after its piece was handed over")
        watch("round \(round) PDF: controller", pdf)
        watch("round \(round) PDF: sources", sources)
        watch("round \(round) PDF: originals", originals)
        watch("round \(round) PDF: document", try XCTUnwrap(pdf.document))
        return .tick(other.pages.start(of: max(1, pdf.page)))
    }

    // MARK: - Helpers

    /// The project file opened in one of its views, as the library opens it in another screen's place.
    private func open(_ file: URL, at landing: Landing?, asPdf: Bool) async throws -> OpenPiece {
        let data = try Data(contentsOf: file)
        var piece = try await OpenPiece.open(fileName: file.lastPathComponent, data: data, language: "en", asPdf: asPdf).get()
        opened.append(piece.id)
        piece.file = file
        piece.other = OtherView.of(file, own: piece.own, in: [:])
        piece.landing = ViewStates.landing(landing, for: piece)
        project = piece.own?.id
        return piece
    }

    /// A project file with the kit's notes read into a PDF kept inside it, as Read the notes leaves one.
    private func pieceWithNotesAndPdf() async throws -> URL {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let pages = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842)).pdfData { context in
            for _ in 1...3 { context.beginPage() }
        }
        let document = try await OpenPiece.open(fileName: "Switched.pdf", data: pages, language: "en").get()
        let read = DocumentRead.request(musicXml: example.data, engine: "homr", pages: 3, reports: nil, language: "en", now: Date())
        let answer = await DocumentRead.call(piece: document.id, read)
        _ = await MusicCore.run { MusicCore.close(piece: document.id) }
        let project = try XCTUnwrap(facadeObject(answer)?["file"] as? String, "the kit's notes are read into the PDF's piece")
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("switch-\(UUID().uuidString).motiff.json")
        try Data(project.utf8).write(to: url, options: .atomic)
        file = url
        return url
    }

    /// Watches what a screen made: alive or not, without keeping it.
    private func watch(_ name: String, _ object: AnyObject) {
        made.append((name, { [weak object] in object != nil }))
    }
}
