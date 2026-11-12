import Foundation

/// What the scan server said of one page (`X-Page-Report`, services/omr): numbers only. music-core works out the page's
/// quality from them (`scan.join`); the iPad reads them only to know a page was reported on and how far it was turned.
struct PageReportEntry: Equatable, Sendable {
    var rotation: Int
    var sharpness: Double
    var contrast: Int
    var staves: Int
    var measures: Int

    /// The pages of a header: a JSON list of objects; nil when the header is not one.
    static func parse(_ header: String?) -> [PageReportEntry]? {
        guard let header, let data = header.data(using: .utf8),
              let list = try? JSONSerialization.jsonObject(with: data) as? [Any] else { return nil }
        var pages: [PageReportEntry] = []
        for item in list {
            guard let page = item as? [String: Any] else { return nil }
            func number(_ name: String) -> Double { (page[name] as? NSNumber)?.doubleValue ?? 0 }
            pages.append(PageReportEntry(
                rotation: Int(number("rotation")), sharpness: number("sharpness"), contrast: Int(number("contrast")),
                staves: Int(number("staves")), measures: Int(number("measures"))
            ))
        }
        return pages
    }
}

/// Where one photographed page is on its way to notes: waiting, at the scan server, read, or not read and why.
enum PageReading: Equatable, Sendable {
    case waiting
    case sending
    /// The MusicXML the server read, and its `X-Page-Report` header when it sent one.
    case read(musicXml: String, report: String?)
    case failed(message: String, report: String?)

    var isDone: Bool {
        if case .read = self { return true }
        return false
    }

    var isFailed: Bool {
        if case .failed = self { return true }
        return false
    }
}

/// One page as music-core joined it (`scan.join`): its bars, how many are doubtful, and the page's quality by its report.
struct JoinedPage: Equatable, Sendable {
    var bars: Int
    var doubtful: Int
    /// "good", "weak" or "poor"; nil without reports.
    var quality: String?
}

/// The pages of a photographed piece read one request at a time (v0.4, «Фото страниц»): each page's state, the request to
/// join the pages read, and what music-core answered. The page reports the join took go on to `document.read`, which keeps
/// them with the notes (`scan.pages`), so Check tells the pages' quality after the piece is opened again.
struct PhotoReadings: Equatable, Sendable {
    var pages: [PageReading]
    /// The score music-core joined, and each photographed page's place in it (nil for a page not in it).
    var joined: String?
    var joinedPages: [Int: JoinedPage] = [:]
    /// The photographed pages that went into `joined`, in order.
    var joinedFrom: [Int] = []
    /// The page reports music-core took with that join, one per page of `joined`; nil when the join went without them.
    var joinedReports: [String]?

    init(count: Int) {
        pages = Array(repeating: .waiting, count: count)
    }

    var done: Int { pages.filter(\.isDone).count }
    var isBusy: Bool { pages.contains(.sending) }
    var allTried: Bool { !pages.contains(.waiting) && !isBusy }

    /// «готово 3 из 6».
    func progress(_ text: UIText) -> String {
        text.format("photos.progress", String(done), String(pages.count))
    }

    /// The pages read, from 1, in order.
    var readPages: [Int] { pages.indices.filter { pages[$0].isDone }.map { $0 + 1 } }

    /// The `scan.join` request: the pages read joined afresh, or, when only `again` was read again since the last join,
    /// that page put in place of its earlier reading (`replace`). The reports go with it when every page read has one.
    func joinRequest(again: Int? = nil) -> [String: Any]? {
        let read = readPages
        guard !read.isEmpty else { return nil }
        var request: [String: Any] = [:]
        if let again, let joined, joinedFrom == read, let place = read.firstIndex(of: again), case .read(let xml, _) = pages[again - 1] {
            request["score"] = joined
            request["replace"] = [["page": place + 1, "musicxml": xml]]
        } else {
            request["pages"] = read.compactMap { page -> String? in
                if case .read(let xml, _) = pages[page - 1] { return xml }
                return nil
            }
        }
        let reports = read.compactMap { page -> String? in
            guard case .read(_, let report) = pages[page - 1], PageReportEntry.parse(report)?.count == 1 else { return nil }
            return report
        }
        if reports.count == read.count { request["reports"] = reports }
        return request
    }

    /// Takes music-core's answer to the join of `from`, sent with `reports` (the request's); false when it is an error.
    mutating func joined(_ answer: [String: Any], from read: [Int], reports: [String]? = nil) -> Bool {
        guard answer["error"] == nil, let score = answer["score"] as? String, let list = answer["pages"] as? [[String: Any]],
              list.count == read.count else { return false }
        joined = score
        joinedFrom = read
        joinedReports = reports.flatMap { $0.count == read.count ? $0 : nil }
        joinedPages = [:]
        for (index, entry) in list.enumerated() {
            joinedPages[read[index]] = JoinedPage(
                bars: (entry["bars"] as? NSNumber)?.intValue ?? 0,
                doubtful: (entry["doubtful"] as? NSNumber)?.intValue ?? 0,
                quality: entry["quality"] as? String
            )
        }
        return true
    }

    /// A page read again or newly read: the join no longer holds it as it is.
    mutating func set(_ page: Int, _ reading: PageReading) {
        guard pages.indices.contains(page - 1) else { return }
        pages[page - 1] = reading
    }

    /// The bars each joined page holds, numbered from 1 in the order joined: where to look for a page's doubtful bars.
    var barRanges: [Int: ClosedRange<Int>] {
        var ranges: [Int: ClosedRange<Int>] = [:]
        var next = 1
        for page in joinedFrom {
            guard let bars = joinedPages[page]?.bars, bars > 0 else { continue }
            ranges[page] = next...(next + bars - 1)
            next += bars
        }
        return ranges
    }

    /// The summary after reading, one line a page: what was read and what is in doubt.
    func summary(_ text: UIText) -> [PageSummaryLine] {
        pages.indices.map { index in
            let page = index + 1
            switch pages[index] {
            case .failed(let message, _):
                return PageSummaryLine(page: page, text: text.format("photos.pageFailed", String(page), message), retake: true, unsure: true)
            case .read:
                guard let joined = joinedPages[page] else { return PageSummaryLine(page: page, text: text.format("photos.pageRead", String(page)), retake: false) }
                return PageSummaryLine.of(page, joined, text)
            case .waiting, .sending:
                return PageSummaryLine(page: page, text: text.format("photos.pageWaiting", String(page)), retake: false)
            }
        }
    }
}

/// One line of the summary: «Страница 4: 9 из 12 тактов под вопросом», and whether to offer «Переснять страницу».
struct PageSummaryLine: Equatable, Sendable, Identifiable {
    var page: Int
    var text: String
    var retake: Bool
    /// Some of the page's bars are in doubt, or the page was not read.
    var unsure = false
    var id: Int { page }

    static func of(_ page: Int, _ joined: JoinedPage, _ text: UIText) -> PageSummaryLine {
        let form = UIText.pluralForm(text.language, joined.bars)
        let number = String(page), bars = String(joined.bars), doubtful = String(joined.doubtful)
        switch joined.quality {
        case "poor":
            return PageSummaryLine(page: page, text: text.format("photos.pagePoor.\(form)", number, doubtful, bars), retake: true, unsure: true)
        case "weak":
            return PageSummaryLine(page: page, text: text.format("photos.pageWeak.\(form)", number, doubtful, bars), retake: true, unsure: true)
        default:
            let key = joined.doubtful > 0 ? "photos.pageDoubtful.\(form)" : "photos.pageClean.\(form)"
            return PageSummaryLine(page: page, text: text.format(key, number, doubtful, bars), retake: false, unsure: joined.doubtful > 0)
        }
    }
}

/// `document.read` (facade 0020): the notes a scan server read, put into the PDF piece, with the server's page reports
/// (v0.4, within contract 12) when there are some: one per page of the notes, each an `X-Page-Report` header's text, as
/// `scan.join` takes them. music-core keeps them with the notes (`scan.pages`) and flags the bars of a poor page
/// (`PageQuality`); reports it refuses as not one per page (`badRequest`) are left out, and the notes read without them.
enum DocumentRead {
    static func request(musicXml: Data, engine: String, pages: Int, reports: [String]?, language: String, now: Date) -> [String: Any] {
        var request: [String: Any] = [
            "musicXml": musicXml.base64EncodedString(),
            "engine": engine,
            "pages": pages,
            "language": language,
            "now": ISO8601DateFormatter().string(from: now),
        ]
        if let reports, !reports.isEmpty { request["reports"] = reports }
        return request
    }

    /// music-core's answer to the request on the PDF piece, sent again without the reports when it refused them.
    @MainActor
    static func call(piece: Int32, _ request: [String: Any]) async -> String {
        let answer = await send(piece, request)
        guard request["reports"] != nil, facadeObject(answer)?["error"] as? String == "badRequest" else { return answer }
        var without = request
        without["reports"] = nil
        return await send(piece, without)
    }

    @MainActor
    private static func send(_ piece: Int32, _ request: [String: Any]) async -> String {
        guard let body = try? JSONSerialization.data(withJSONObject: request) else { return "" }
        let json = String(decoding: body, as: UTF8.self)
        return await MusicCore.run { MusicCore.call(piece: piece, "document.read", json) }
    }
}
