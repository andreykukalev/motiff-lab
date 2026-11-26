import Foundation
import PDFKit

/// One page of a PDF as music-core's vector reader leaves it (`scan.vector`, decision record 0029).
struct VectorPage: Sendable {
    /// The page number, from 1.
    let number: Int
    /// "read" (read on the device), "scan" (for the scan server) or "blank" (no music).
    let kind: String
    /// The page's MusicXML, for a page read on the device.
    let musicXml: String?
    /// Its page report, as the scan server's `X-Page-Report` header writes one page.
    let report: String?
}

extension ScanServer {
    /// The choice «Авто» (decision record 0029): each page is read on the device when a notation program drew it, else sent
    /// to the server. It is never named to the server.
    static let auto = "auto"

    /// The PDF's pages as the vector reader reads them on the device, with the reader's name ("vector 1"); nil when it
    /// cannot open the file.
    static func vectorPages(_ data: Data) async -> (engine: String, pages: [VectorPage])? {
        let json = facadeRequest(["pdf": data.base64EncodedString()])
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(.scanVector, json) }
        guard let object = facadeObject(answer), object["error"] == nil, let engine = object["engine"] as? String,
              let pages = object["pages"] as? [[String: Any]] else { return nil }
        return (engine, pages.compactMap { page in
            guard let number = page["page"] as? Int, let kind = page["kind"] as? String else { return nil }
            let report = (page["report"] as? [String: Any]).flatMap { try? JSONSerialization.data(withJSONObject: [$0]) }
            return VectorPage(number: number, kind: kind, musicXml: page["musicxml"] as? String, report: report.map { String(decoding: $0, as: UTF8.self) })
        })
    }

    /// «Авто», as the web app's `ScoreScan`: the pages read on the device are kept as read; the others go to the server one
    /// a request, with its own engine; a page with no music is left out. The pages are joined in their order, and the engines
    /// that read them are named together ("vector 1 + homr 0.7.1").
    static func recogniseAuto(fileName: String, document: PDFDocument, vector: (engine: String, pages: [VectorPage]), address: URL, account: Account, report: ScanProgressListener?) async -> Result<ScanResult, ScanProblem> {
        let text = UIText.current
        var read: [Int: (score: String, report: String?)] = [:]
        var engines: [String] = []
        for page in vector.pages {
            if let musicXml = page.musicXml { read[page.number] = (musicXml, page.report) }
        }
        if !read.isEmpty { engines.append(vector.engine) }
        let toServer = vector.pages.filter { $0.kind == "scan" }
        if !toServer.isEmpty {
            let name = (fileName as NSString).deletingPathExtension
            var progress = ScanProgress(pages: toServer.count)
            for page in toServer {
                report?(progress)
                guard let part = document.pageCount == 1 ? document.dataRepresentation() : ScanParts.part(of: document, page.number...page.number) else {
                    return .failure(.server(text["pdf.unreadable"]))
                }
                let start = Date()
                let sent = await recognisePage(fileName: "\(name) \(page.number).pdf", data: part, address: address, account: account)
                switch sent.result {
                case .failure(let problem):
                    return .failure(problem.onPage(page.number, text))
                case .success(let scanned):
                    progress = progress.read(after: ScanProgress.since(start))
                    read[page.number] = (String(decoding: scanned.musicXml, as: UTF8.self), scanned.report)
                    if !engines.contains(scanned.engine) { engines.append(scanned.engine) }
                }
            }
            report?(progress)
        }
        guard !read.isEmpty else { return .failure(.server(text["pdf.readNone"])) }
        let ordered = read.keys.sorted().compactMap { read[$0] }
        let json = facadeRequest(["pages": ordered.map(\.score)])
        let answer = await MusicCore.run { PieceCore.sendWithoutPiece(.scanJoin, json) }
        guard let object = facadeObject(answer), object["error"] == nil, let score = object["score"] as? String else {
            return .failure(.server(text["import.scanPagesNotJoined"]))
        }
        let reports = ordered.compactMap(\.report)
        return .success(ScanResult(musicXml: Data(score.utf8), engine: engines.joined(separator: " + "), pages: document.pageCount, report: nil,
                                   partReports: reports.count == ordered.count ? reports : nil))
    }
}
