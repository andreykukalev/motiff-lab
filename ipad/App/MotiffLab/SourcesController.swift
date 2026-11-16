import PDFKit
import SwiftUI

/// What was just done in the Sources sheet, with Undo where it can be undone (sources sheet, 0.6).
struct SourcesToast: Identifiable, Equatable {
    enum Undo: Equatable {
        /// The project file as it was just before: written back, and the piece opened again from it.
        case restore(String)
        /// These notes made the working ones again, as «Сделать рабочими» is undone (0030, section 4).
        case makeWorking(String)
    }

    let id = UUID()
    var text: String
    var undo: Undo?
}

/// What passes from one screen of a piece to the next when the piece is opened again (version 0.6): the toast of what
/// was done, with its Undo, or a view of the switch to show, by the project file. Sources itself opens where it is asked
/// for, the PDF included (#582 item 4).
@MainActor
enum SourcesHandover {
    private static var toasts: [URL: SourcesToast] = [:]
    private static var views: [URL: String] = [:]

    static func give(_ toast: SourcesToast, to file: URL) { toasts[file.standardizedFileURL] = toast }

    static func toast(for file: URL?) -> SourcesToast? {
        guard let file else { return nil }
        return toasts.removeValue(forKey: file.standardizedFileURL)
    }

    static func show(_ view: String, on file: URL) { views[file.standardizedFileURL] = view }

    static func view(for file: URL?) -> String? {
        guard let file else { return nil }
        return views.removeValue(forKey: file.standardizedFileURL)
    }

    /// The switch of all views as the screen handing over had it (v0.6.4 spec section 13): the next screen of the same
    /// file draws it from its first frame, instead of PDF | Notes or no switch until it has read its own. Read as often as
    /// the screen is made, and dropped once that screen has read the sources itself.
    private static var layers: [URL: SourceLayers] = [:]

    static func give(_ shown: SourceLayers, to file: URL?) {
        guard let file, shown.working != nil else { return }
        layers[file.standardizedFileURL] = shown
    }

    static func layers(for file: URL?) -> SourceLayers? {
        file.flatMap { layers[$0.standardizedFileURL] }
    }

    static func drop(_ file: URL?) {
        if let file { layers[file.standardizedFileURL] = nil }
    }
}

/// The piece's sources on its screen (version 0.6; decision record 0030): music-core's
/// `sources.layers` for this iPad, with the recordings whose sound is kept here, and the Sources sheet's operations, each
/// saved to the project file at once. What changes the working notes («Сделать рабочими», an Undo) opens the piece
/// again in its screen's place (`reopen`). The player's own files are never changed: a recording is a copy kept in
/// Application Support (`OriginalSounds`), a linked original is only unlinked by music-core.
@MainActor
final class SourcesController: ObservableObject {
    @Published private(set) var layers = SourceLayers()
    @Published var toast: SourcesToast?
    /// Why the last operation was not done, in the player's words; nil when it was.
    @Published var status: String?
    /// The original whose pages are with the scan server now.
    @Published private(set) var reading: String?
    /// Notes just recognised, shown highlighted once.
    @Published var fresh: String?
    /// The recording heard as the «Исполнение» view; nil while the notes are shown.
    @Published var performance: String?

    private(set) var piece: Int32 = 0
    private(set) var projectId: String?
    private var file: URL?
    /// Keeps the screen's view and opens the piece's project file again in its place.
    var reopen: (() async -> Void)?
    private let text = UIText.current

    /// `layers` are the ones the screen before handed over (SourcesHandover), shown until the piece's own are read.
    init(layers handed: SourceLayers? = nil) {
        if let handed { layers = handed }
    }

    /// The ids of the originals whose sound is kept on this iPad, for music-core to count them here.
    var here: [String] { projectId.map { OriginalSounds.ids(project: $0) } ?? [] }

    /// The piece's sources as it opens; the sounds of recordings deleted meanwhile leave this iPad.
    func load(piece: Int32, file: URL?) async {
        self.piece = piece
        self.file = file
        if projectId == nil {
            let answer = await MusicCore.run { MusicCore.call(piece: piece, "sources") }
            projectId = facadeObject(answer)?["projectId"] as? String
        }
        // Only from an answer read: a piece whose sources could not be read keeps every sound.
        if await refresh(), let projectId {
            OriginalSounds.forget(project: projectId, keeping: Set(layers.originals.filter { !$0.isRemoved && $0.isRecording }.map(\.id)))
        }
    }

    /// Reads the sources again; false when music-core did not answer them.
    @discardableResult
    func refresh() async -> Bool {
        guard let read = SourceLayers(answer: await call("sources.layers")) else { return false }
        layers = read
        if let performance, !layers.views.contains(where: { $0.id == performance && $0.kind == "performance" && !$0.hidden }) {
            self.performance = nil
        }
        return true
    }

    // MARK: - The sheet's operations

    /// «Показывать в переключателе»: hides the view from the switch or shows it again; the view hidden while shown gives
    /// way to the working notes.
    func setHidden(_ id: String, _ hidden: Bool) async {
        await change("sources.view.hide", ["id": id, "hidden": hidden])
        if hidden, performance == id { performance = nil }
    }

    /// A PDF or photos added as an original without recognising it: kept in the project file on this iPad, never online.
    func addPages(_ data: Data, fileName: String, kind: String) async {
        var fields: [String: Any] = ["kind": kind, "fileName": fileName, "kept": "file", "data": data.base64EncodedString(), "now": Self.now()]
        if let pages = PDFDocument(data: data)?.pageCount, pages > 0 { fields["pages"] = pages }
        if await change("sources.original.add", fields) != nil {
            toast = SourcesToast(text: text.format("sources.sheet.toastAdded", fileName))
        }
    }

    /// Photos of pages from the iPad's document camera, as one PDF of images (v0.4 «Фото страниц»), added as an original.
    func addPhotos(_ images: [UIImage]) async {
        let pages = images.compactMap(PhotoImages.jpeg).map { PhotoPage(photo: $0) }
        guard !pages.isEmpty else { return }
        await addPages(PhotoImages.pdf(pages), fileName: text["sources.sheet.photosFile"] + ".pdf", kind: "images")
    }

    /// A recording added as an original without hearing it: its sound copied here (kept "device"), its description in the
    /// project file. The same recording as a deleted one comes back with its id, as music-core knows it by its bytes.
    func addRecording(_ picked: URL) async {
        guard let projectId else { return }
        let lent = picked.startAccessingSecurityScopedResource()
        let size = (try? picked.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
        // Its bytes only give music-core the fingerprint; a very large file is described without it.
        let data = size > 0 && size <= Self.fingerprintLimit ? try? Data(contentsOf: picked) : nil
        if lent { picked.stopAccessingSecurityScopedResource() }
        var fields: [String: Any] = ["kind": "audio", "fileName": picked.lastPathComponent, "kept": "device", "now": Self.now()]
        if let milliseconds = OriginalSounds.milliseconds(of: picked) { fields["durationMs"] = milliseconds }
        if let data { fields["data"] = data.base64EncodedString() }
        guard let object = await change("sources.original.add", fields), let id = object["id"] as? String else { return }
        guard OriginalSounds.keep(picked, project: projectId, original: id) else {
            status = text["sources.sheet.soundNotKept"]
            return
        }
        await refresh()
        toast = SourcesToast(text: text.format("sources.sheet.toastAdded", picked.lastPathComponent))
    }

    /// The largest recording whose bytes are read for its fingerprint.
    static let fingerprintLimit = 200 * 1024 * 1024

    /// «Удалить только оригинал» or «Удалить вместе с нотами» (0030, section 4): the toast undoes it by writing the file
    /// back as it was.
    func remove(_ original: SourceLayers.Original, withNotes: Bool) async {
        guard let before = await snapshot() else { return }
        guard await change("sources.original.remove", ["id": original.id, "withNotes": withNotes, "now": Self.now()]) != nil else { return }
        if performance == original.id { performance = nil }
        toast = SourcesToast(text: text[withNotes ? "sources.sheet.toastGoneWith" : "sources.sheet.toastGone"], undo: .restore(before))
    }

    /// «Удалить ноты…»: the Notes and their corrections; the original stays.
    func remove(_ notes: SourceLayers.Notes) async {
        guard let before = await snapshot() else { return }
        guard await change("sources.notes.remove", ["id": notes.id]) != nil else { return }
        toast = SourcesToast(text: text["sources.sheet.toastNotesGone"], undo: .restore(before))
    }

    /// «Распознать» or «Распознать ещё раз»: the original's pages go to the scan server chosen (the player's yes is the
    /// panel's Send), and the notes it reads become new Notes beside the ones there are, which keep their corrections.
    /// Answers why not, or nil once done.
    func recognise(_ original: SourceLayers.Original, address: URL, account: Account) async -> String? {
        reading = original.id
        defer { reading = nil }
        let kept = await call("sources.pdf", ["id": original.id])
        guard let object = facadeObject(kept), let base64 = object["pdf"] as? String, let data = Data(base64Encoded: base64) else {
            return facadeObject(kept)?["message"] as? String ?? text["pdf.readNone"]
        }
        let result: ScanResult
        switch await ScanServer.recognise(fileName: object["fileName"] as? String ?? original.fileName, data: data, address: address, account: account) {
        case .failure(let problem): return problem.text(text)
        case .success(let read): result = read
        }
        let pages = max(1, original.pages ?? result.pages)
        var fields: [String: Any] = ["original": original.id, "musicXml": result.musicXml.base64EncodedString(), "engine": result.engine, "pages": pages, "now": Self.now()]
        if let reports = result.reports { fields["reports"] = reports }
        var answer = await call("sources.notes.recognise", fields)
        if result.reports != nil, facadeObject(answer)?["error"] as? String == "badRequest" {
            // The reports did not match the pages read: the notes are kept without them.
            fields["reports"] = nil
            answer = await call("sources.notes.recognise", fields)
        }
        guard let read = SourceLayers(answer: answer), let id = facadeObject(answer)?["id"] as? String else {
            return facadeObject(answer)?["message"] as? String ?? text["pdf.readNone"]
        }
        layers = read
        fresh = id
        status = nil
        await save()
        toast = SourcesToast(text: text.count("sources.sheet.toastRead", pages))
        return nil
    }

    /// «Сделать рабочими» (0030, section 4 as amended): the badge moves to these notes, the working notes until now stay
    /// as other Notes, and the piece opens again on them; the toast on the next screen undoes it.
    func makeWorking(_ notes: SourceLayers.Notes) async {
        guard let file, let previous = layers.working else { return }
        let title = layers.title(of: notes, text)
        guard await swapWorking(to: notes.id) else { return }
        SourcesHandover.give(SourcesToast(text: text.format("sources.sheet.toastWorking", title), undo: .makeWorking(previous)), to: file)
        await reopen?()
    }

    /// The toast's Undo.
    func undo() async {
        guard let undo = toast?.undo else { return }
        toast = nil
        switch undo {
        case .restore(let before):
            guard let file else { return }
            do {
                try Data(before.utf8).write(to: file, options: .atomic)
            } catch {
                status = error.localizedDescription
                return
            }
            await reopen?()
        case .makeWorking(let id):
            guard await swapWorking(to: id) else { return }
            await reopen?()
        }
    }

    /// The bytes of another PDF or photos kept in the piece, opened in music-core as a PDF of its own for its view,
    /// shown as it is (nothing drawn on it is kept: music-core keeps page marks for the first PDF only).
    func openOriginal(_ original: SourceLayers.Original, title: String) async -> OpenPiece? {
        let kept = await call("sources.pdf", ["id": original.id])
        guard let object = facadeObject(kept), let base64 = object["pdf"] as? String, let data = Data(base64Encoded: base64) else {
            status = facadeObject(kept)?["message"] as? String ?? text["pdf.readNone"]
            return nil
        }
        let name = original.fileName.lowercased().hasSuffix(".pdf") ? original.fileName : original.fileName + ".pdf"
        let request = Self.json(["language": text.language])
        let answer = await MusicCore.run { MusicCore.open(fileName: name, data: data, request: request) }
        guard let opened = facadeObject(answer), let number = (opened["piece"] as? NSNumber)?.int32Value, number > 0, opened["document"] is [String: Any] else {
            status = facadeObject(answer)?["message"] as? String ?? text["originals.unreadable"]
            return nil
        }
        return OpenPiece(id: number, title: title, pages: [], timemap: "", isDocument: true)
    }

    /// Other Notes to look at: opened by music-core under a number of their own only to be shown (`sources.notes.open`,
    /// "readOnly"), so the piece itself stays as it is; the screen closes the number when it goes. Nil when they cannot
    /// be shown, with why in `status`.
    func openNotes(_ notes: SourceLayers.Notes, title: String, at tick: Double?) async -> OpenPiece? {
        let piece = self.piece
        let request = Self.json(["id": notes.id])
        let evenBars = DeviceSettings.shared.isOn("score.evenBars")
        let opened: Result<OpenPiece, EngraveError> = await MusicCore.run {
            let answer = facadeObject(MusicCore.call(piece: piece, "sources.notes.open", request))
            guard let number = (answer?["piece"] as? NSNumber)?.int32Value, number > 0 else {
                return .failure(EngraveError(message: answer?["message"] as? String ?? ""))
            }
            switch Engraver.engrave(piece: number, title: title, evenBars: evenBars) {
            case .success(let engraving):
                return .success(OpenPiece(id: number, title: title, pages: engraving.pages, timemap: engraving.timemap))
            case .failure(let error):
                _ = MusicCore.close(piece: number)
                return .failure(error)
            }
        }
        switch opened {
        case .success(var piece):
            piece.landing = tick.map { .tick($0) }
            return piece
        case .failure(let error):
            status = error.message.isEmpty ? text["pdf.readNone"] : error.message
            return nil
        }
    }

    // MARK: - Calls

    private func swapWorking(to id: String) async -> Bool {
        let piece = self.piece
        let request = Self.json(["id": id])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "sources.makeWorking", request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            status = facadeObject(answer)?["message"] as? String ?? text["sources.sheet.failed"]
            return false
        }
        await save()
        return true
    }

    /// One of the sheet's operations, answered as the sources; saved to the project file at once. Nil, with why in
    /// `status`, when music-core refused it.
    @discardableResult
    private func change(_ name: String, _ fields: [String: Any]) async -> [String: Any]? {
        let answer = await call(name, fields)
        guard let object = facadeObject(answer), object["error"] == nil, let read = SourceLayers(answer: answer) else {
            status = facadeObject(answer)?["message"] as? String ?? text["sources.sheet.failed"]
            return nil
        }
        status = nil
        layers = read
        await save()
        return object
    }

    private func call(_ name: String, _ fields: [String: Any] = [:]) async -> String {
        var request = fields
        request["device"] = "ipad"
        request["here"] = here
        let body = Self.json(request)
        let piece = self.piece
        return await MusicCore.run { MusicCore.call(piece: piece, name, body) }
    }

    private func save() async {
        if let file { try? await ProjectFiles.save(piece: piece, to: file) }
    }

    /// The project file as it is now, for an Undo or a copy to look at.
    private func snapshot() async -> String? {
        let piece = self.piece
        let request = Self.json(["now": Self.now()])
        let saved = await MusicCore.run { MusicCore.call(piece: piece, "project.save", request) }
        guard let object = facadeObject(saved), object["error"] == nil else {
            status = facadeObject(saved)?["message"] as? String ?? text["sources.sheet.failed"]
            return nil
        }
        return saved
    }

    static func now() -> String {
        ISO8601DateFormatter().string(from: Date())
    }

    nonisolated static func json(_ object: [String: Any]) -> String {
        (try? JSONSerialization.data(withJSONObject: object)).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
    }
}
