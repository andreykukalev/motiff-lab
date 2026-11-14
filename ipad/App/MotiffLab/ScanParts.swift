import PDFKit
import SwiftUI

/// A score sent a page a request (v0.5, issue #479; v0.5.6, #533 section 1; music-core's `ScanParts`): the PDF is cut into
/// one-page PDFs, each that page as it is, sent one after another to the same server, so each answer is a real step of
/// the progress (`ScanProgress`); music-core joins what they read (`scan.join`), as the server joins its pages, so each
/// page of the PDF still starts in the notes where it did. Nothing changes on the server, and any server, an older one
/// on the player's own computer too, reads a one-page PDF.
enum ScanParts {
    /// The most pages a score may have to be recognised (Andrei, 2026-11-13).
    static let mostPages = 100
    /// The pages a server is taken to read in one request when a plan is asked for without saying.
    static let safePages = 4
    /// The pages the iPad sends a request, as the web does (music-core's `ScanParts.PagesARequest`).
    static let pagesARequest = 1

    /// The parts, pages from 1: consecutive, in order, each as full as it can be. One part when the PDF fits.
    static func plan(pages: Int, maxPages: Int) -> [ClosedRange<Int>] {
        let size = maxPages < 1 ? safePages : maxPages
        let count = max(1, pages)
        return stride(from: 1, through: count, by: size).map { $0...min(count, $0 + size - 1) }
    }

    /// The pages of the document as a PDF of their own; nil when one cannot be copied.
    static func part(of document: PDFDocument, _ pages: ClosedRange<Int>) -> Data? {
        let part = PDFDocument()
        for number in pages {
            guard let page = document.page(at: number - 1)?.copy() as? PDFPage else { return nil }
            part.insert(page, at: part.pageCount)
        }
        return part.dataRepresentation()
    }
}

/// Hears how far a score's notes are read, on the main actor.
typealias ScanProgressListener = @MainActor @Sendable (ScanProgress) -> Void

/// How far the notes of one score are read (v0.5.6, #533 section 1), as music-core's `PageProgress`: the pages whose notes
/// are back are done, the page sent is current, the rest wait; nothing moves by time alone. The time left is told only
/// from the pages already read, timed from send to answer: their mean times the pages not back yet, none before the first.
struct ScanProgress: Equatable, Sendable {
    /// Where one page is.
    enum State: Equatable, Sendable {
        case done, current, waiting
    }

    let pages: Int
    /// The pages whose notes are back.
    private(set) var done = 0
    /// How long each page read took, from send to answer, in milliseconds.
    private(set) var milliseconds: [Int] = []

    /// Who hears the progress of a score read in this task: Recognise notes' panel sets it around its send.
    @TaskLocal static var listener: ScanProgressListener? = nil

    init(pages: Int) {
        self.pages = max(1, pages)
    }

    /// The page being read, from 1; nil once every page is back.
    var current: Int? { done < pages ? done + 1 : nil }

    func state(_ page: Int) -> State {
        page <= done ? .done : page == done + 1 ? .current : .waiting
    }

    /// Each page's state, page 1 first.
    var states: [State] { (1...pages).map(state) }

    /// The current page's notes came back after `milliseconds`: the next page is current.
    func read(after milliseconds: Int) -> ScanProgress {
        guard done < pages else { return self }
        var next = self
        next.done += 1
        next.milliseconds.append(max(0, milliseconds))
        return next
    }

    /// About how many minutes are left, at least one; nil before the first page is back, or once all are.
    var minutesLeft: Int? { Self.minutes(measured: milliseconds, pagesLeft: pages - done) }

    /// About how many minutes `pagesLeft` pages take from the pages `measured`: their mean, rounded up, at least one.
    static func minutes(measured: [Int], pagesLeft: Int) -> Int? {
        guard !measured.isEmpty, pagesLeft > 0 else { return nil }
        let left = measured.reduce(0, +) * pagesLeft / measured.count
        return max(1, (left + 59_999) / 60_000)
    }

    /// «Страница 3 из 8», or that all are read.
    func line(_ text: UIText) -> String {
        current.map { text.format("scan.pageOf", String($0), String(pages)) } ?? text.format("scan.allRead", String(pages))
    }

    /// The line, and about how long is left once a page has been timed.
    func caption(_ text: UIText) -> String {
        minutesLeft.map { line(text) + " · " + text.format("recognition.left", String($0)) } ?? line(text)
    }

    /// The milliseconds since `start`, as a page is timed.
    static func since(_ start: Date) -> Int { Int((Date().timeIntervalSince(start) * 1000).rounded()) }
}

/// The progress of the score a panel sends, for its bar.
@MainActor
final class ScanFeed: ObservableObject {
    @Published var progress: ScanProgress?
}

/// A bar of segments, a page or a piece each (v0.5.6, #533 section 1): done filled, waiting empty, the current one marked
/// as going on (a slow pulse, still with Reduce Motion) and filled only by what is really done of it. Past `denseAbove`
/// segments they touch, so each stays wide enough to see.
struct ProgressSegments: View {
    struct Segment: Equatable {
        var state: ScanProgress.State
        /// For a current piece of a batch: the share of its pages read.
        var filled = 0.0
    }

    let segments: [Segment]
    var denseAbove = 40
    var height = CGFloat.ds("s-1")
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let dense = segments.count > denseAbove
        HStack(spacing: dense ? 0 : 2) {
            ForEach(segments.indices, id: \.self) { index in
                segment(segments[index], radius: dense ? 0 : height / 2)
            }
        }
        .frame(height: height)
        .accessibilityHidden(true)
    }

    @ViewBuilder
    private func segment(_ segment: Segment, radius: CGFloat) -> some View {
        if segment.state == .current {
            bar(segment, radius: radius)
                .phaseAnimator(reduceMotion ? [1.0] : [1.0, 0.45]) { content, phase in
                    content.opacity(phase)
                } animation: { _ in
                    Animation.ds("t-slow").speed(0.25)
                }
        } else {
            bar(segment, radius: radius)
        }
    }

    private func bar(_ segment: Segment, radius: CGFloat) -> some View {
        let shape = RoundedRectangle(cornerRadius: radius)
        return shape
            .fill(Color.ds(segment.state == .done ? "ink-2" : segment.state == .current ? "hairline-strong" : "hairline"))
            .overlay(alignment: .leading) {
                if segment.state == .current, segment.filled > 0 {
                    GeometryReader { box in
                        Rectangle().fill(Color.ds("ink-2")).frame(width: box.size.width * min(1, segment.filled))
                    }
                }
            }
            .clipShape(shape)
            .frame(maxWidth: .infinity)
    }
}

/// While a score's notes are read: a segment a page, «Страница 3 из 8», and about how long is left once a page is timed.
struct ScanProgressBar: View {
    let progress: ScanProgress
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            ProgressSegments(segments: progress.states.map { ProgressSegments.Segment(state: $0) })
            Text(progress.caption(text))
                .monospacedDigit()
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(text["scan.progressLabel"])
        .accessibilityValue(progress.caption(text))
        .accessibilityAddTraits(.updatesFrequently)
    }
}

extension ScanServer {
    /// The PDF's pages sent one a request and their notes joined by music-core, or the first page's problem; `report`
    /// hears as each page is sent and as its notes come back.
    static func recognisePages(fileName: String, document: PDFDocument, address: URL, account: Account, report: ScanProgressListener?) async -> Result<ScanResult, ScanProblem> {
        let text = UIText.current
        let name = (fileName as NSString).deletingPathExtension
        var progress = ScanProgress(pages: document.pageCount)
        var read: [String] = []
        var reports: [String] = []
        var engine = ""
        let plan = ScanParts.plan(pages: document.pageCount, maxPages: ScanParts.pagesARequest)
        for pages in plan {
            report?(progress)
            guard let part = ScanParts.part(of: document, pages) else { return .failure(.server(text["pdf.unreadable"])) }
            let start = Date()
            let sent = await recognisePage(fileName: "\(name) \(pages.lowerBound).pdf", data: part, address: address, account: account)
            switch sent.result {
            case .failure(let problem):
                return .failure(problem.onPage(pages.lowerBound, text))
            case .success(let scanned):
                progress = progress.read(after: ScanProgress.since(start))
                read.append(String(decoding: scanned.musicXml, as: UTF8.self))
                engine = scanned.engine
                if let header = scanned.report { reports.append(header) }
            }
        }
        report?(progress)
        guard let body = try? JSONSerialization.data(withJSONObject: ["pages": read]) else { return .failure(.server(text["import.scanPagesNotJoined"])) }
        let json = String(decoding: body, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "scan.join", json) }
        guard let object = facadeObject(answer), object["error"] == nil, let score = object["score"] as? String else {
            return .failure(.server(text["import.scanPagesNotJoined"]))
        }
        let all = reports.count == plan.count ? reports : nil
        return .success(ScanResult(musicXml: Data(score.utf8), engine: engine, pages: document.pageCount, report: nil, partReports: all))
    }
}
