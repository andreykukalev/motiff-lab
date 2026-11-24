import Foundation
import PDFKit

/// «Повторить» for the pages of a PDF piece no engine read, once the rest opened (v1.3 i-1b, Andrei 2026-11-24 «Вставить в те
/// же ноты»): only those pages are read again, as a new scan job, and each read is put into the same notes at its place by
/// music-core (`scan.insert`), the player's corrections carried over, the bars after it numbered on. The bars to check are
/// kept on this iPad for the new notes (`ScanCrops.keepReview`).
@MainActor
enum ScanPageAgain {
    /// Where a PDF page goes among the notes' pages: after the pages the notes have before it (those read on this iPad or by
    /// the server, but not the ones still unread nor a blank one), from 1.
    static func place(of page: Int, read: Set<Int>) -> Int {
        read.filter { $0 < page }.count + 1
    }

    /// The project file `file` with `unread` (PDF pages, from 1) read again by the job and put in: the file, the pages still
    /// unread, and the job (deleted by the caller once the file is written), or why not. A page no engine read again, or one
    /// music-core does not put in (the player's work would move), stays unread.
    static func read(
        _ unread: [Int], file: String, pdf data: Data, to target: ScanTarget, job: RecognitionJob, text: UIText
    ) async -> Result<(file: String, unread: [Int], job: ScanJobRecord), ScanProblem> {
        let pages = max(1, PDFDocument(data: data)?.pageCount ?? 1)
        if job.kept == nil {
            guard case .success(let server) = await ScanServer.health(target.address, session: target.session), server.takesJobs else {
                return .failure(.server(text["pdf.readNone"]))
            }
        }
        let plan = await ScanJobPlan.of(data, pages: pages)
        let sent: (pages: [[String: Any]], job: ScanJobRecord)
        switch await ScanServer.readJob(fileName: "score.pdf", data: data, pages: unread, to: target, job: job.kept, keep: job.keep, hear: job.hear, wanted: job.wanted, ready: job.ready) {
        case .failure(let problem): return .failure(problem)
        case .success(let done): sent = done
        }
        let joined = ScanJobs.joined(device: [:], server: sent.pages)
        var read = Set(plan.device.keys).union(plan.server).subtracting(unread)
        var current = file
        var left = Set(joined.unread)
        for (index, (page, number)) in zip(joined.pages, joined.numbers).enumerated() {
            var fields: [String: Any] = ["file": current, "page": place(of: number, read: read), "reading": page, "pages": pages, "language": text.language]
            if let report = joined.reports?[index] { fields["reports"] = [report] }
            let json = facadeRequest(fields)
            let answer = await MusicCore.run { MusicCore.call(piece: 0, "scan.insert", json) }
            guard let object = facadeObject(answer), object["error"] == nil, let with = object["file"] as? String else {
                left.insert(number)
                continue
            }
            ScanCrops.keepReview(object, pages: [number], pdf: data)
            current = with
            read.insert(number)
        }
        return .success((current, left.sorted(), sent.job))
    }
}
