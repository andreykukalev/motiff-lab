import PDFKit
import SwiftUI

/// A score longer than the scan server reads in one request, sent in parts (v0.5, issue #479; music-core's `ScanParts`): the
/// server says how many pages it reads at a time (`maxPages` on `/health`; 4 on Motiff Lab's server, 0014), and the PDF is
/// cut into consecutive parts of at most that many pages, each a PDF of those pages as they are, sent one after another to
/// the same server. music-core joins what they read (`scan.join`), as the server joins its pages, so each page of the PDF
/// still starts in the notes where it did. Nothing changes on the server.
enum ScanParts {
    /// The most pages a score may have to be recognised (Andrei, 2026-11-13).
    static let mostPages = 100
    /// The pages any scan server reads in one request: used when it does not say, and a PDF this short goes whole unasked.
    static let safePages = 4

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

/// How far the notes of one score are read (issue #479 item 10): its pages, the parts it goes in, the part being read and
/// since when. The time is a rough guess, half a minute a page as the server reads (`DailyPages`), so the bar moves while a
/// part is read and never shows the score done before its notes are back.
struct ScanProgress: Equatable, Sendable {
    let pages: Int
    let parts: [ClosedRange<Int>]
    /// The part being read, from 1.
    var part = 1
    /// When it was sent.
    var sent = Date()

    /// Who hears the progress of a score read in this task: Recognise notes' panel sets it around its send.
    @TaskLocal static var listener: ScanProgressListener? = nil

    /// The pages of the parts already read.
    var pagesDone: Int { parts.prefix(max(0, part - 1)).reduce(0) { $0 + $1.count } }

    /// The share done by `now`, from 0 to just under 1: the parts read and most of the part being read.
    func fraction(at now: Date) -> Double {
        min(0.99, seconds(at: now) / Double(max(1, pages) * DailyPages.secondsPerPage))
    }

    /// About how many minutes are left, at least one.
    func minutesLeft(at now: Date) -> Int {
        max(1, Int(((Double(pages * DailyPages.secondsPerPage) - seconds(at: now)) / 60).rounded(.up)))
    }

    /// «Часть 2 из 3 · страницы 5–8», or «6 страниц» for a PDF read in one request.
    func line(_ text: UIText) -> String {
        guard parts.count > 1, parts.indices.contains(part - 1) else { return text.count("recognition.pages", pages) }
        let range = parts[part - 1]
        return text.format("scan.progressPart", String(part), String(parts.count), String(range.lowerBound), String(range.upperBound))
    }

    private func seconds(at now: Date) -> Double {
        let size = parts.indices.contains(part - 1) ? parts[part - 1].count : pages
        let reading = max(0, now.timeIntervalSince(sent))
        return Double(pagesDone * DailyPages.secondsPerPage) + min(reading, Double(size * DailyPages.secondsPerPage) * 0.95)
    }
}

/// The progress of the score a panel sends, for its bar.
@MainActor
final class ScanFeed: ObservableObject {
    @Published var progress: ScanProgress?
}

/// While a score's notes are read: a bar, the part or pages being read and about how long is left, moving every few seconds.
struct ScanProgressBar: View {
    let progress: ScanProgress
    let text: UIText

    var body: some View {
        TimelineView(.periodic(from: .now, by: 3)) { context in
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                ProgressView(value: progress.fraction(at: context.date))
                    .tint(Color.ds("ink-2"))
                    .accessibilityLabel(text["scan.progressLabel"])
                    .accessibilityValue(progress.line(text))
                Text(progress.line(text) + " · " + text.format("recognition.left", String(progress.minutesLeft(at: context.date))))
                    .monospacedDigit()
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

extension ScanServer {
    /// How many pages of a PDF the server reads in one request (`maxPages` on `/health`); `ScanParts.safePages` when it does
    /// not say or does not answer in time.
    nonisolated static func maxPages(_ address: URL, session: URLSession = .shared) async -> Int {
        var request = URLRequest(url: address.appendingPathComponent("health"))
        request.timeoutInterval = 10
        guard let answer = try? await session.data(for: request), (answer.1 as? HTTPURLResponse)?.statusCode == 200,
              let object = try? JSONSerialization.jsonObject(with: answer.0) as? [String: Any],
              let most = (object["maxPages"] as? NSNumber)?.intValue, most > 0
        else { return ScanParts.safePages }
        return most
    }

    /// The parts of the PDF sent one after another and their notes joined by music-core, or the first part's problem.
    static func recogniseParts(fileName: String, document: PDFDocument, plan: [ClosedRange<Int>], address: URL, account: Account, report: ScanProgressListener?) async -> Result<ScanResult, ScanProblem> {
        let text = UIText.current
        let name = (fileName as NSString).deletingPathExtension
        var read: [String] = []
        var reports: [String] = []
        var engine = ""
        for (index, pages) in plan.enumerated() {
            report?(ScanProgress(pages: document.pageCount, parts: plan, part: index + 1))
            guard let part = ScanParts.part(of: document, pages) else { return .failure(.server(text["pdf.unreadable"])) }
            let sent = await recognisePage(fileName: "\(name) \(pages.lowerBound)-\(pages.upperBound).pdf", data: part, address: address, account: account)
            switch sent.result {
            case .failure(let problem):
                return .failure(problem.inPart(pages, text))
            case .success(let scanned):
                read.append(String(decoding: scanned.musicXml, as: UTF8.self))
                engine = scanned.engine
                if let header = scanned.report { reports.append(header) }
            }
        }
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
