import SwiftUI
import UIKit
import XCTest

@testable import MotiffLab

/// VIEW-02 over many switches (#681): a piece with notes and a PDF kept inside it goes from its notes to its PDF and back
/// again and again, in one window as the app has one, each view opened in the other's place as the library opens it
/// (LibraryView.switchView, openFile, show). Two ways:
/// - the screens' objects: the score's workspace with its controllers and its two web views, drawn, watching the Pencil
///   over the window and appearing as the score's view makes them, handed to the PDF by the bar's switch
///   (ScoreWorkspaceLife.swift); and the PDF's controllers with its pages' view, made, watched and attached as PdfPages
///   makes them, handed back to the notes. Once the last has gone none of them is alive.
/// - the real screens, ScoreScreen and PdfScreen, hosted in the window and swapped as the library's
///   `navigationDestination(item:)` with `.id(piece.id)` swaps them, once before the PDF's screen has read its pages:
///   once the last has gone, none of their controllers is alive (the Debug probe's counts, ScreensProbe). The library's
///   part of a switch is run here, not the bar's: the screens are swapped as handed over, without saving first.
/// In both, the window has as many gesture recognizers after each visit to the notes as after the first.
/// Not seen here: memory in WebKit's processes or in music-core, a Release build, the roll's drawer and the pencil palette
/// opened, and the time a switch takes (MotiffLabScreens/SwitchUITests and the device see that).
@MainActor
final class SwitchReleaseTests: XCTestCase {
    private static let rounds = 5
    private static let frame = CGRect(x: 0, y: 0, width: 1024, height: 1366)
    /// The project file switched, and the project's id, by which its views are kept on the device.
    private var file: URL?
    private var project: String?
    /// The pieces opened in music-core, closed again whether or not the switches closed them.
    private var opened: [Int32] = []
    /// What each visited screen made, by name, and whether it is still alive.
    private var made: [(name: String, alive: () -> Bool)] = []
    /// The window every screen shows in.
    private var window: UIWindow?

    override func tearDown() async throws {
        window?.rootViewController = nil
        window?.isHidden = true
        window = nil
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
        let window = UIWindow(frame: Self.frame)
        window.isHidden = false
        self.window = window
        var notesLanding: Landing?
        var recognizers: [Int] = []
        for round in 1...Self.rounds {
            let pdfLanding = try await visitNotes(file, at: notesLanding, in: window, round: round)
            recognizers.append(window.gestureRecognizers?.count ?? 0)
            notesLanding = try await visitPdf(file, at: pdfLanding, in: window, round: round)
        }
        XCTAssertEqual(made.count, Self.rounds * 17, "every screen's objects are watched")
        XCTAssertEqual(recognizers, Array(repeating: recognizers[0], count: Self.rounds), "the window's gesture recognizers grow with each visit")
        await expectAllLetGo()
    }

    func testEveryScreenShownIsLetGo() async throws {
        let file = try await pieceWithNotesAndPdf()
        let shown = ShownPiece()
        let window = hostingWindow(shown)
        let before = ProbeCounts.shared.snapshot()
        var recognizers: [Int] = []
        for round in 1...3 {
            try await host(open(file, at: nil, asPdf: false), in: shown, round: round)
            recognizers.append(window.gestureRecognizers?.count ?? 0)
            try await host(open(file, at: .page(1), asPdf: true), in: shown, round: round)
        }
        // Back to the notes, opened before, as soon as the PDF's screen begins to read its pages: before it reads its
        // sources and sets their way back.
        let notes = try await open(file, at: nil, asPdf: false)
        try await host(open(file, at: .page(1), asPdf: true), in: shown, round: 4, ready: false)
        try await host(notes, in: shown, round: 4)
        recognizers.append(window.gestureRecognizers?.count ?? 0)
        if let last = shown.piece { await ClosedPieces.close(last.id) }
        shown.piece = nil
        XCTAssertEqual(recognizers, Array(repeating: recognizers[0], count: recognizers.count), "the window's gesture recognizers grow with each visit")
        await expectAllLetGo()
        let after = ProbeCounts.shared.snapshot()
        for key in ["score", "roll", "pdf", "player"] {
            XCTAssertLessThanOrEqual(after[key] ?? 0, before[key] ?? 0, "a visited screen's \(key) is still alive")
        }
    }

    // MARK: - The two screens' objects

    /// The notes' screen: its workspace made, its page drawn and watching the Pencil over the window, appearing, then
    /// handed to the PDF by the bar's switch (the library closing the piece as the PDF opens) and gone. Answers where the
    /// PDF opens.
    private func visitNotes(_ file: URL, at landing: Landing?, in window: UIWindow, round: Int) async throws -> Landing {
        let piece = try await open(file, at: landing, asPdf: false)
        let workspace = ScoreWorkspace(piece: piece)
        let views = [workspace.score.webView, workspace.roll.webView]
        for view in views {
            view.frame = window.bounds
            window.addSubview(view)
        }
        // As the score's view is updated (ScoreWebView.updateUIView).
        workspace.score.show(piece, dark: false)
        workspace.score.watchStylus(in: window)
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
        _ = await until(seconds: 15) { handed != nil }
        workspace.disappeared()
        views.forEach { $0.removeFromSuperview() }
        let name = "round \(round) notes:"
        watch("\(name) workspace", workspace)
        let controllers: [(String, AnyObject)] = [
            ("score", workspace.score), ("roll", workspace.roll), ("player", workspace.player), ("insights", workspace.insights),
            ("assistant", workspace.assistant), ("contents", workspace.contents), ("originals", workspace.originals),
            ("sources", workspace.sources), ("score's web view", workspace.score.webView), ("roll's web view", workspace.roll.webView),
        ]
        for (part, object) in controllers { watch("\(name) \(part)", object) }
        return try XCTUnwrap(handed, "round \(round): the notes are handed to the PDF")
    }

    /// The PDF's screen: its pages read and shown in their view as PdfPages makes it (watched, the pen attached), its
    /// sources and originals read as PdfScreen reads them, then handed to the notes as its switch does (the piece saved, the
    /// library closing it as the notes open) and gone. Answers where the notes open.
    private func visitPdf(_ file: URL, at landing: Landing, in window: UIWindow, round: Int) async throws -> Landing {
        let piece = try await open(file, at: landing, asPdf: true)
        let other = try XCTUnwrap(piece.other, "round \(round): the PDF has its notes to go to")
        let pdf = PdfController()
        pdf.family = piece.family
        let sources = SourcesController(layers: SourcesHandover.layers(for: piece.file))
        let originals = OriginalsController()
        await pdf.load(piece: piece.id, text: UIText.current, page: piece.landing?.page)
        let document = try XCTUnwrap(pdf.document, "round \(round): the PDF's pages are read")
        // As PdfPages.makeUIView makes the pages' view.
        let pages = BarPdfView(frame: window.bounds)
        pages.document = document
        window.addSubview(pages)
        pdf.view = pages
        let coordinator = PdfPages.Coordinator(controller: pdf)
        coordinator.watch(pages)
        pdf.attach(to: pages)
        await sources.load(piece: piece.id, file: piece.file)
        SourcesHandover.drop(piece.file)
        await originals.load(piece)
        SourcesHandover.give(sources.layers, to: other.file)
        try await ProjectFiles.save(piece: piece.id, to: file)
        await ClosedPieces.close(piece.id)
        XCTAssertTrue(ClosedPieces.take(piece.id), "round \(round): the PDF's screen goes after its piece was handed over")
        pages.removeFromSuperview()
        let name = "round \(round) PDF:"
        let parts: [(String, AnyObject)] = [
            ("controller", pdf), ("sources", sources), ("originals", originals), ("document", document), ("pages' view", pages),
            ("pages' coordinator", coordinator),
        ]
        for (part, object) in parts { watch("\(name) \(part)", object) }
        return .tick(other.pages.start(of: max(1, pdf.page)))
    }

    // MARK: - The real screens

    /// The window with the library's place for a screen in it.
    private func hostingWindow(_ shown: ShownPiece) -> UIWindow {
        let scene = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first
        let window = scene.map { UIWindow(windowScene: $0) } ?? UIWindow(frame: Self.frame)
        window.frame = Self.frame
        window.rootViewController = UIHostingController(rootView: ScreenPlace(shown: shown, account: Account(setup: nil)))
        window.isHidden = false
        self.window = window
        return window
    }

    /// A piece opened in one of its views shown in the place of the one shown, as the library's `show` does: the one
    /// shown is closed as its screen gives way. Waits for the new screen to be ready as the switch test does (the notes
    /// drawn and shown, the PDF's pages read), or, not `ready`, only for it to begin; then watches its controllers.
    private func host(_ piece: OpenPiece, in shown: ShownPiece, round: Int, ready: Bool = true) async throws {
        if let earlier = shown.piece { await ClosedPieces.close(earlier.id) }
        shown.piece = piece
        let name = "round \(round) \(piece.isDocument ? "PDF" : "notes") screen:"
        if piece.isDocument {
            let shows = await until(seconds: 30, every: ready ? 50 : 1) {
                guard let pdf = ScreensProbe.pdf, pdf.piece == piece.id else { return false }
                return !ready || pdf.document != nil
            }
            XCTAssertTrue(shows, "\(name) not shown")
            if let pdf = ScreensProbe.pdf { watch("\(name) controller", pdf) }
            return
        }
        let shows = await until(seconds: 30) {
            guard let score = ScreensProbe.score, score.piece?.id == piece.id else { return false }
            return !ready || (score.rendered && score.webView.alpha > 0.99)
        }
        XCTAssertTrue(shows, "\(name) not shown")
        let parts: [(String, AnyObject?)] = [
            ("score", ScreensProbe.score), ("roll", ScreensProbe.roll), ("player", ScreensProbe.player), ("assistant", ScreensProbe.assistant),
        ]
        for case let (part, object?) in parts { watch("\(name) \(part)", object) }
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

    /// Nothing a visited screen made is still alive, once what lets go of it later has run.
    private func expectAllLetGo() async {
        _ = await until(seconds: 10) { !made.contains { $0.alive() } }
        XCTAssertEqual(made.filter { $0.alive() }.map(\.name), [], "what the visited screens made is still alive")
    }

    /// Waits, at most so long, for a condition to hold, looking again every so many milliseconds, and says whether it did.
    private func until(seconds: Double, every milliseconds: Int = 50, _ condition: () -> Bool) async -> Bool {
        for _ in 0..<Int(seconds * 1000) / milliseconds where !condition() {
            try? await Task.sleep(for: .milliseconds(milliseconds))
        }
        return condition()
    }
}

/// The piece the library shows (LibraryView's `piece`): set, its screen is pushed; another, its screen is made again in
/// the place of the one before; nil, it is popped.
@MainActor
private final class ShownPiece: ObservableObject {
    @Published var piece: OpenPiece?
}

/// The library's place for a piece's screen, as LibraryView has it: `navigationDestination(item:)` with `.id(piece.id)`.
/// The screens' own switch is not pressed here, so what they would call does nothing.
private struct ScreenPlace: View {
    @ObservedObject var shown: ShownPiece
    let account: Account

    var body: some View {
        NavigationStack {
            Color.clear.navigationDestination(item: $shown.piece) { piece in
                Group {
                    if piece.isDocument {
                        PdfScreen(piece: piece, switchView: { _ in }, openRead: { _, _ in })
                    } else {
                        ScoreScreen(piece: piece, switchView: { _ in }, reopen: { _ in })
                    }
                }
                .id(piece.id)
            }
        }
        .environmentObject(account)
        .environmentObject(DeviceSettings.shared)
    }
}
