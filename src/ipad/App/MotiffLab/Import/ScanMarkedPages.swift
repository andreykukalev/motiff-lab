import PDFKit

/// «Распознать заново с отметками» (v1.6 c-14b3, the 1.6 plan's decision 15, «Как напечатано» step 3): the pages of a PDF's
/// notes that carry the player's marks «что не так», and only those, read again for `scan.join`'s "replace". A page the
/// vector reader reads stays on this iPad; the others go to the scan server as one job, with the bars marked as read
/// badly, which only the other engines read otherwise. What the pages give is put into the notes by music-core; nothing
/// here decides what a mark does.
@MainActor
enum ScanMarkedPages {
    /// A page of the notes to read again: its place among the notes' pages, its page in the PDF (both from 1), and its
    /// bars the player marked as read badly, as the notes number them.
    struct Page: Equatable, Sendable {
        var place: Int
        var pdf: Int
        var bad: [Int] = []
    }

    /// What the pages read again give: `scan.join`'s "replace", in the pages' order, the engines that read them, and how
    /// many pages the PDF has.
    struct Reading {
        var replace: [[String: Any]]
        var engines: [String]
        var pdfPages: Int
    }

    /// Which of `pages` stay on this iPad, read by the vector reader (`device`, by PDF page), and which go to the server:
    /// a page with a bar read badly always goes, as the vector reader would read it as before (E-scan-19).
    static func split(_ pages: [Page], device: [Int: String]) -> (device: [Page], server: [Page]) {
        let here = pages.filter { $0.bad.isEmpty && device[$0.pdf] != nil }
        return (here, pages.filter { page in !here.contains(page) })
    }

    /// `pages` of the PDF `data` read again: those the vector reader reads here, the others by a scan job to `target`
    /// (asked only when one goes; its problem refuses the reading), whose id `job.keep` hears for the caller to delete it.
    /// A page no engine read again fails the whole reading, so no page keeps a reading its marks were put into already
    /// (E-scan-18).
    static func read(_ pages: [Page], pdf data: Data, to target: Result<ScanTarget, ScanProblem>, job: RecognitionJob, text: UIText) async -> Result<Reading, ScanProblem> {
        let count = PDFDocument(data: data)?.pageCount ?? (pages.map(\.pdf).max() ?? 1)
        let plan = await ScanJobPlan.of(data, pages: count)
        let (device, server) = split(pages, device: plan.device)
        var read: [[String: Any]] = []
        if !server.isEmpty {
            let sent: ScanTarget
            switch target {
            case .failure(let problem): return .failure(problem)
            case .success(let chosen): sent = chosen
            }
            let numbers = server.map(\.pdf)
            switch await ScanServer.takesJobs(sent, pages: numbers, hear: job.hear) {
            case .failure(let problem): return .failure(problem)
            case .success(false): return .failure(.server(text["scanMarked.noJobs"]))
            case .success(true): break
            }
            switch await ScanServer.readJob(fileName: "score.pdf", data: data, pages: numbers, to: sent, job: nil, keep: job.keep, hear: job.hear, wanted: job.wanted, ready: job.ready) {
            case .failure(let problem): return .failure(problem)
            case .success(let done): read = done.pages
            }
        }
        let here = plan.device.filter { number, _ in device.contains { $0.pdf == number } }
        let joined = ScanJobs.joined(device: here, server: read)
        let missing = pages.map(\.pdf).filter { !joined.numbers.contains($0) }
        guard missing.isEmpty else { return .failure(.server(text.format("scanMarked.notRead", missing.map(String.init).joined(separator: ", ")))) }
        let replace: [[String: Any]] = zip(joined.numbers, joined.pages).compactMap { number, page -> [String: Any]? in
            guard let marked = pages.first(where: { $0.pdf == number }) else { return nil }
            var entry = (page as? [String: Any]) ?? ["musicxml": page]
            entry["page"] = marked.place
            if !marked.bad.isEmpty { entry["bad"] = marked.bad }
            return entry
        }
        let engines = (device.isEmpty ? [] : [plan.engine].compactMap { $0 }) + joined.engines
        return .success(Reading(replace: replace, engines: engines, pdfPages: count))
    }
}
