import Foundation

/// «Повторить» for the pages of a PDF piece no engine read, once the rest opened (v1.3 i-1b, Andrei 2026-11-24 «Вставить в те
/// же ноты»): only those pages are read again, as a new scan job, and each read is put into the same notes at its place by
/// music-core (`scan.insert`), the player's corrections carried over, the bars after it numbered on. The bars to check are
/// kept on this iPad for the new notes (`ScanCrops.keepReview`).
@MainActor
enum ScanPageAgain {
    /// What came of the pages read again.
    struct Outcome {
        /// The project file with the pages put in, read from disk once the job was done.
        var file: String
        /// The pages still not in the notes, from 1.
        var unread: [Int]
        /// The PDF pages the notes have now, in order.
        var read: [Int]
        /// The job, deleted by the caller once the file is written.
        var job: ScanJobRecord
        /// Why a page read was not put in, in the player's words (music-core's); nil when every page read went in.
        var why: String?
    }

    /// Where a PDF page goes among the notes' pages: after the pages the notes have before it, from 1.
    static func place(of page: Int, read: [Int]) -> Int {
        read.filter { $0 < page }.count + 1
    }

    /// `unread` (PDF pages, from 1, of a PDF of `pages` pages) read again by the job and put into the notes of the project
    /// file at `url`, which has the PDF pages `read`. The file is read once the job is done, so what the player saved while
    /// it ran stays. A page no engine read again, or one music-core does not put in, stays unread.
    static func read(
        _ unread: [Int], read: [Int], pages: Int, file url: URL, pdf data: Data, to target: ScanTarget, job: RecognitionJob, text: UIText
    ) async -> Result<Outcome, ScanProblem> {
        if job.kept == nil {
            switch await ScanServer.takesJobs(target, pages: unread, hear: job.hear) {
            case .failure(let problem): return .failure(problem)
            case .success(false): return .failure(.server(text["pdf.readNone"]))
            case .success(true): break
            }
        }
        let sent: (pages: [[String: Any]], job: ScanJobRecord)
        switch await ScanServer.readJob(fileName: "score.pdf", data: data, pages: unread, to: target, job: job.kept, keep: job.keep, hear: job.hear, wanted: job.wanted, ready: job.ready) {
        case .failure(let problem): return .failure(problem)
        case .success(let done): sent = done
        }
        guard let file = try? String(contentsOf: url, encoding: .utf8) else { return .failure(.server(text["recognition.failMissing"])) }
        let joined = ScanJobs.joined(device: [:], server: sent.pages)
        var outcome = Outcome(file: file, unread: joined.unread, read: read, job: sent.job)
        for (index, (page, number)) in zip(joined.pages, joined.numbers).enumerated() {
            var fields: [String: Any] = ["file": outcome.file, "page": place(of: number, read: outcome.read), "reading": page, "pages": pages, "language": text.language]
            if let report = joined.reports?[index] { fields["reports"] = [report] }
            let json = facadeRequest(fields)
            let answer = facadeObject(await MusicCore.run { PieceCore.sendWithoutPiece(.scanInsert, json) })
            guard let answer, answer["error"] == nil, let with = answer["file"] as? String else {
                outcome.unread.append(number)
                outcome.why = answer?["message"] as? String ?? text["pdf.readNone"]
                continue
            }
            ScanCrops.keepReview(answer, pages: [number], pdf: data)
            outcome.file = with
            outcome.read = (outcome.read + [number]).sorted()
        }
        outcome.unread.sort()
        return .success(outcome)
    }
}
