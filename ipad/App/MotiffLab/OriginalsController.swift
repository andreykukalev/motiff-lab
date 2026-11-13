import PDFKit
import SwiftUI

/// The piece's originals on this iPad (version 0.3, local-first, as the web's `PlaybackLab.Originals`): where each one's
/// bytes are, «Привязать оригинал» from Files with the same file only, kept as a link to the file where it lies or as a
/// copy in the piece, and the iPad's own mark that it holds the original, set and taken back only here (`sources.held`)
/// and sent online with the piece by the sync. A link is checked when Sources opens and when the PDF opens: while the
/// file's size and date are the same it is taken as the same file; when either changed it is read again (downloaded first
/// when it is only in the cloud) and its fingerprint checked, and a different file says «Оригинал изменился», with the
/// notes as they were.
@MainActor
final class OriginalsController: ObservableObject {
    @Published private(set) var originals: [OriginalEntry] = []
    /// This iPad's links, by original.
    @Published private(set) var links: [String: OriginalLink] = [:]
    /// What came of the last thing done with an original, by its id: linked, another file, no connection…
    @Published private(set) var status: [String: String] = [:]
    /// The original being read from Files now, downloaded first when it is only in the cloud.
    @Published private(set) var loading: String?
    /// A picked file that is the original, waiting for the player's choice: a link or a copy.
    @Published var choice: Picked?
    /// The linked PDF open over the score: a PDF piece in music-core for that view only, never written anywhere.
    @Published var linkedPdf: OpenPiece?

    struct Picked: Identifiable {
        let original: OriginalEntry
        let data: Data
        /// Nil when iOS gave no bookmark for the file: then only a copy is offered.
        let bookmark: Data?
        let bytes: Int64?
        let modified: Date?
        var id: String { original.id }
    }

    private var piece: OpenPiece?
    private(set) var projectId: String?
    /// The music-core piece of the linked PDF shown, closed when its view goes.
    private var linkedPiece: Int32?
    var folder = OriginalLinks.folder
    /// The piece changed here: the score's Sources read it again.
    var changed: (@MainActor () -> Void)?
    private let text = UIText.current

    /// The originals and this iPad's links to them as music-core said them when the piece opened (`OpenPiece.own`), so
    /// PDF | Notes offers a linked PDF from the score's first frame rather than once `load` has asked again (#495 BUG-02);
    /// `load` reads and checks them as before.
    init(opened summary: PieceSummary? = nil, folder: URL = OriginalLinks.folder) {
        self.folder = folder
        guard let summary else { return }
        originals = summary.originals
        projectId = summary.id
        links = Self.linksHere(to: originals, project: projectId, in: folder)
    }

    /// The piece's PDF original, when it has one.
    var pdf: OriginalEntry? { originals.first(where: \.isDocument) }

    func badge(_ original: OriginalEntry) -> OriginalBadge { .of(original, link: links[original.id]) }

    /// The PDF can be opened from its link: linked here and not found changed.
    var canOpenLinkedPdf: Bool { pdf.map { badge($0) == .linked } ?? false }

    /// The piece's originals and this iPad's links to them, as the score opens: each link checked by the file's size and
    /// date only, nothing downloaded.
    func load(_ piece: OpenPiece) async {
        self.piece = piece
        await checkLinks(verifying: false)
    }

    /// As Sources opens: a link whose file's size or date changed is read again and its fingerprint checked.
    func verify() async {
        await checkLinks(verifying: true)
    }

    private func checkLinks(verifying: Bool) async {
        await refresh()
        for original in originals where !original.inFile {
            guard let link = links[original.id] else { continue }
            _ = await linkedBytes(original, link, reading: false, verifying: verifying)
        }
    }

    /// The originals again, as music-core has them now.
    func refresh() async {
        guard let piece else { return }
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "sources") }
        read(answer)
    }

    /// A file picked in Files to link the original again: read (downloaded first when needed) and checked by its
    /// fingerprint. The same file waits for the player's choice, a link or a copy; another one is refused, as on the web.
    func picked(_ url: URL, for original: OriginalEntry) async {
        status[original.id] = nil
        let lent = url.startAccessingSecurityScopedResource()
        defer { if lent { url.stopAccessingSecurityScopedResource() } }
        let bookmark = try? LinkedFile.bookmark(of: url)
        loading = original.id
        let result = await LinkedFile.read(url)
        loading = nil
        switch result {
        case .failure(let problem):
            status[original.id] = problem.text(text)
        case .success(let data):
            guard let sha256 = original.sha256, CloudSync.hash(data) == sha256 else {
                status[original.id] = text["originals.otherFile"]
                return
            }
            let attributes = LinkedFile.attributes(of: url)
            choice = Picked(original: original, data: data, bookmark: bookmark, bytes: attributes.bytes, modified: attributes.modified)
        }
    }

    /// Keep it where it is: the link stays on this iPad, the project file gets only the iPad's mark.
    func keepLink(_ picked: Picked) async {
        guard let bookmark = picked.bookmark, let sha256 = picked.original.sha256 else { return }
        let link = OriginalLink(
            bookmark: bookmark, fileName: picked.original.fileName, sha256: sha256,
            bytes: picked.bytes, modified: picked.modified, linkedAt: Date()
        )
        keep(link, for: picked.original)
        status[picked.original.id] = text["originals.linkedHere"]
        await mark(picked.original, held: true)
    }

    /// Copy to iPad: the PDF is kept in the piece again (`sources.relink`), the same original with no copy of the piece, and
    /// the link, if there was one, is no longer needed.
    func keepCopy(_ picked: Picked) async {
        guard let piece else { return }
        var fields: [String: Any] = ["pdf": picked.data.base64EncodedString(), "now": ISO8601DateFormatter().string(from: Date())]
        if let pages = PDFDocument(data: picked.data)?.pageCount, pages > 0 { fields["pages"] = pages }
        let request = Self.json(fields)
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "sources.relink", request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            status[picked.id] = facadeObject(answer)?["message"] as? String ?? text["originals.unreadable"]
            return
        }
        guard object["linked"] as? Bool == true else {
            status[picked.id] = text["originals.otherFile"]
            return
        }
        read(answer)
        forget(picked.original)
        status[picked.id] = text["originals.linked"]
        await save()
    }

    /// Copy to iPad from the link: the linked file's bytes, read and checked, kept in the piece.
    func copyLinked(_ original: OriginalEntry) async {
        guard let link = links[original.id], let data = await linkedBytes(original, link, reading: true) else { return }
        await keepCopy(Picked(original: original, data: data, bookmark: nil, bytes: nil, modified: nil))
    }

    /// Opens the linked PDF over the score: read from Files, checked, and opened in music-core as a PDF for this view only;
    /// the piece's file never gets its bytes.
    func openLinkedPdf(title: String) async {
        guard let original = pdf, let link = links[original.id], let data = await linkedBytes(original, link, reading: true) else { return }
        var fields: [String: Any] = ["language": text.language]
        // The PDF view keeps the page it was left at under the project's id, as the copy's view does.
        if let projectId { fields["id"] = projectId }
        let request = Self.json(fields)
        let name = original.fileName.lowercased().hasSuffix(".pdf") ? original.fileName : original.fileName + ".pdf"
        let answer = await MusicCore.run { MusicCore.open(fileName: name, data: data, request: request) }
        guard let object = facadeObject(answer), let number = (object["piece"] as? NSNumber)?.int32Value, number > 0,
              object["document"] is [String: Any] else {
            status[original.id] = facadeObject(answer)?["message"] as? String ?? text["originals.unreadable"]
            return
        }
        closeLinkedPdf()
        linkedPiece = number
        linkedPdf = OpenPiece(id: number, title: title, pages: [], timemap: "", isDocument: true)
    }

    /// The linked PDF's view went: its piece in music-core goes too.
    func closeLinkedPdf() {
        guard let number = linkedPiece else { return }
        linkedPiece = nil
        Task { _ = await MusicCore.run { MusicCore.close(piece: number) } }
    }

    // MARK: - Links

    /// The link's file: resolved (a stale bookmark made again) and taken as the same while its size and date are; else
    /// read, downloaded first when needed, and its fingerprint checked, unless only the size and date are to be looked at
    /// (not `verifying`). `reading` reads the bytes even when the size and date are the same, for the PDF's view or a
    /// copy. The iPad's mark follows what is found. Nil when the bytes are not the original's or were not read, with the
    /// reason in `status` when one was looked for.
    private func linkedBytes(_ original: OriginalEntry, _ kept: OriginalLink, reading: Bool, verifying: Bool = true) async -> Data? {
        if kept.folderId != nil {
            guard reading || verifying else { return nil }
            return await folderBytes(original, kept)
        }
        guard let target = LinkedFile.resolve(kept.bookmark) else {
            await lost(original)
            return nil
        }
        var link = kept
        if let fresh = target.fresh {
            link.bookmark = fresh
            keep(link, for: original)
        }
        let attributes = LinkedFile.attributes(of: target.url)
        let check = link.check(bytes: attributes.bytes, modified: attributes.modified)
        if check == .changed || (check == .same && !reading) {
            await mark(original, held: check == .same)
            return nil
        }
        if check == .unknown && !reading && !verifying { return nil }
        return await verified(original, link, target.url, trusted: check == .same)
    }

    /// A PDF linked in a folder the player imported (version 0.5): read through the folder's bookmark and its path there,
    /// then by the file's own bookmark, and checked by its fingerprint. A file not found keeps its link: «Проверить папку»
    /// finds it again.
    private func folderBytes(_ original: OriginalEntry, _ link: OriginalLink) async -> Data? {
        loading = original.id
        let read = await AwayPdf.bytes(link)
        loading = nil
        switch read {
        case .failure(let reason):
            status[original.id] = reason.text(text)
            return nil
        case .success(let data):
            let checked = link.verified(sha256: CloudSync.hash(data), bytes: Int64(data.count), modified: nil)
            keep(checked, for: original)
            await mark(original, held: !checked.changed)
            return checked.changed ? nil : data
        }
    }

    /// Reads the linked file and, unless its size and date say it is the same (`trusted`), checks its fingerprint.
    private func verified(_ original: OriginalEntry, _ link: OriginalLink, _ url: URL, trusted: Bool) async -> Data? {
        loading = original.id
        let read = await LinkedFile.read(url)
        loading = nil
        switch read {
        case .failure(let problem):
            status[original.id] = problem.text(text)
            if problem == .missing { await lost(original) }
            return nil
        case .success(let data):
            if trusted { return data }
            let attributes = LinkedFile.attributes(of: url)
            let checked = link.verified(sha256: CloudSync.hash(data), bytes: attributes.bytes, modified: attributes.modified)
            keep(checked, for: original)
            await mark(original, held: !checked.changed)
            return checked.changed ? nil : data
        }
    }

    /// The linked file is gone: the link goes, and so does the iPad's mark.
    private func lost(_ original: OriginalEntry) async {
        forget(original)
        status[original.id] = text["originals.missing"]
        await mark(original, held: false)
    }

    private func keep(_ link: OriginalLink, for original: OriginalEntry) {
        guard let projectId else { return }
        OriginalLinks.save(link, project: projectId, original: original.id, in: folder)
        links[original.id] = link
    }

    private func forget(_ original: OriginalEntry) {
        if let projectId { OriginalLinks.remove(project: projectId, original: original.id, in: folder) }
        links[original.id] = nil
    }

    // MARK: - The piece

    /// The iPad's own mark on an original, set or taken back (`sources.held`), and the piece saved when it changed. A PDF
    /// kept in the piece is held by the iPad whatever the link says.
    private func mark(_ original: OriginalEntry, held: Bool) async {
        guard let piece, let now = originals.first(where: { $0.id == original.id }), !now.inFile,
              now.held.contains("ipad") != held else { return }
        let request = Self.json(["id": original.id, "held": held, "now": ISO8601DateFormatter().string(from: Date())])
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "sources.held", request) }
        if read(answer) { await save() }
    }

    /// The originals and the project's id in a `sources` answer, and this iPad's links to them; false for an error.
    @discardableResult
    private func read(_ answer: String) -> Bool {
        guard let object = facadeObject(answer), object["error"] == nil else { return false }
        originals = OriginalEntry.list(object["originals"])
        projectId = object["projectId"] as? String ?? projectId
        links = Self.linksHere(to: originals, project: projectId, in: folder)
        return true
    }

    /// This iPad's links to the originals, by original.
    private static func linksHere(to originals: [OriginalEntry], project: String?, in folder: URL) -> [String: OriginalLink] {
        guard let project else { return [:] }
        var found: [String: OriginalLink] = [:]
        for original in originals {
            found[original.id] = OriginalLinks.load(project: project, original: original.id, in: folder)
        }
        return found
    }

    /// The piece written to its project file, which the sync then sends online with the iPad's mark.
    private func save() async {
        guard let piece, let file = piece.file else { return }
        try? await ProjectFiles.save(piece: piece.id, to: file)
        changed?()
    }

    private static func json(_ object: [String: Any]) -> String {
        (try? JSONSerialization.data(withJSONObject: object)).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
    }
}
