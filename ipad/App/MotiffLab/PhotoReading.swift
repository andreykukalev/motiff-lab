import PDFKit
import PhotosUI
import SwiftUI

/// Reading the notes from a PDF of photographed pages (v0.4, «Фото страниц»): one page a request to the scan server the
/// player set, so each page shows when it is done and can be sent again; the pages read are joined by music-core
/// (`scan.join`, `replace` for a page read again), and the server's page reports count in its doubtful bars. The notes stay
/// detected, and nothing about the pages' quality is kept in the project file.
@MainActor
final class PhotoReader: ObservableObject {
    @Published private(set) var readings = PhotoReadings(count: 0)
    @Published private(set) var warnings: [PageWarning] = []
    @Published private(set) var measuring = false
    @Published private(set) var engine = "unknown engine"
    @Published var problem: String?
    private var measuredPages = -1

    /// The warnings on photos too dark or too soft, measured once for these pages, before anything is sent.
    func prepare(_ document: PDFDocument) async {
        if readings.pages.count != document.pageCount { readings = PhotoReadings(count: document.pageCount) }
        guard measuredPages != document.pageCount, !measuring else { return }
        measuring = true
        defer { measuring = false }
        var found: [PageWarning] = []
        for index in 0..<document.pageCount {
            guard let page = document.page(at: index), let photo = PhotoImages.photo(of: page) else { continue }
            if let warning = PageWarning(page: index + 1, look: await PhotoImages.look(photo)) { found.append(warning) }
        }
        warnings = found
        measuredPages = document.pageCount
    }

    /// Sends each page not read yet, one after another, then joins what was read. Answers why nothing could be read, or nil.
    func readAll(_ document: PDFDocument, address: URL, account: Account, text: UIText) async -> String? {
        problem = nil
        for page in 1...max(1, document.pageCount) where !readings.pages.indices.contains(page - 1) || !readings.pages[page - 1].isDone {
            if let stop = await send(page, document, address: address, account: account, text: text) { return stop }
        }
        await join(again: nil)
        return readings.done == 0 ? text["photos.noneRead"] : nil
    }

    /// One page sent again, and joined in place of its earlier reading.
    func read(_ page: Int, _ document: PDFDocument, address: URL, account: Account, text: UIText) async {
        problem = nil
        if let stop = await send(page, document, address: address, account: account, text: text) {
            problem = stop
            return
        }
        await join(again: page)
    }

    /// The page photographed again, put in the PDF in place of the old photo (`document.replace`), to be read again.
    func retake(_ page: Int, _ document: PDFDocument, photo: Data, piece: Int32) async -> Bool {
        guard let pdf = PhotoImages.pdf(document, replacing: page - 1, with: photo),
              let body = try? JSONSerialization.data(withJSONObject: ["pdf": pdf.base64EncodedString()]) else { return false }
        let request = String(decoding: body, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "document.replace", request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            problem = facadeObject(answer)?["message"] as? String
            return false
        }
        readings.set(page, .waiting)
        if let index = warnings.firstIndex(where: { $0.page == page }) { warnings.remove(at: index) }
        if let look = await PhotoImages.look(photo), let warning = PageWarning(page: page, look: look) { warnings.append(warning) }
        return true
    }

    /// Sends one page; answers why to stop sending the others (no server, sign-in, Pro), else nil.
    private func send(_ page: Int, _ document: PDFDocument, address: URL, account: Account, text: UIText) async -> String? {
        guard let pdfPage = document.page(at: page - 1), let photo = PhotoImages.photo(of: pdfPage) else {
            readings.set(page, .failed(message: text["photos.pageUnreadable"], report: nil))
            return nil
        }
        readings.set(page, .sending)
        let answer = await ScanServer.recognisePage(fileName: "page-\(page).jpg", data: photo, address: address, account: account)
        switch answer.result {
        case .success(let read):
            engine = read.engine
            readings.set(page, .read(musicXml: String(decoding: read.musicXml, as: UTF8.self), report: answer.report))
            return nil
        case .failure(let failure):
            readings.set(page, .failed(message: failure.text(text), report: answer.report))
            if case .server = failure { return nil }
            return failure.text(text)
        }
    }

    private func join(again: Int?) async {
        let read = readings.readPages
        guard let request = readings.joinRequest(again: again), let body = try? JSONSerialization.data(withJSONObject: request) else { return }
        let json = String(decoding: body, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: 0, "scan.join", json) }
        guard let object = facadeObject(answer), readings.joined(object, from: read) else {
            problem = facadeObject(answer)?["message"] as? String ?? "music-core did not join the pages."
            return
        }
    }
}

/// The summary of the pages last read, kept until the app closes, for Check on the notes (v0.4): the page reports are not
/// saved, so the pages' quality is shown only while the reading is fresh.
@MainActor
final class PhotoPageNotes: ObservableObject {
    static let shared = PhotoPageNotes()

    struct Kept: Equatable {
        var titles: Set<String>
        var lines: [PageSummaryLine]
        var bars: [Int: ClosedRange<Int>]
    }

    @Published private(set) var kept: Kept?

    func keep(titles: Set<String>, lines: [PageSummaryLine], bars: [Int: ClosedRange<Int>]) {
        kept = Kept(titles: titles, lines: lines.filter(\.unsure), bars: bars)
    }

    /// The pages in doubt of the piece with this title, if it is the one read last.
    func unsure(for title: String) -> Kept? {
        guard let kept, kept.titles.contains(title), !kept.lines.isEmpty else { return nil }
        return kept
    }
}

/// Check's part about the photographed pages just read: each page in doubt, and its bars to look at.
struct PhotoPagesCheck: View {
    @ObservedObject var score: ScoreController
    @ObservedObject private var notes = PhotoPageNotes.shared
    let text: UIText

    var body: some View {
        if let kept = notes.unsure(for: score.pieceTitle) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(text["photos.checkHeading"]).overline()
                Text(text["photos.checkHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                ForEach(kept.lines) { line in
                    HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                        Tag(text: text.format("photos.page", String(line.page)), kind: .warn)
                        Text(line.text).dsType(.caption).foregroundStyle(Color.ds("ink")).fixedSize(horizontal: false, vertical: true)
                        Spacer(minLength: 0)
                        if let bars = kept.bars[line.page] {
                            Button(text["scanCheck.show"]) { score.selectBars(bars.lowerBound, bars.upperBound, extend: false) }
                                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                        }
                    }
                }
            }
        }
    }
}

/// Read the notes, page by page: the send panel (what leaves the iPad, and to where), each page's progress with Send again,
/// the warnings before sending, the summary after (bars in doubt, a page to photograph again), and Open the notes.
struct PhotoReadingPanel: View {
    @ObservedObject var reader: PhotoReader
    /// The PDF piece's pages, loaded again once a page is photographed again.
    @ObservedObject var pdf: PdfController
    let titles: Set<String>
    let address: URL?
    let text: UIText
    /// The send panel, with what to do on Send.
    let send: (@escaping () async -> String?) -> ReadNotesPanel
    /// Puts the joined notes into the piece and opens them: answers why not, or nil.
    let open: (Data, String, Int) async -> String?
    @EnvironmentObject private var account: Account
    @State private var retaking: Int?
    @State private var cameraOpen = false
    @State private var photosOpen = false
    @State private var picked: [PhotosPickerItem] = []
    @State private var opening = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                send { await readAll() }
                if reader.measuring {
                    Text(text["photos.measuring"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                if reader.readings.done == 0 {
                    ForEach(reader.warnings, id: \.page) { warning in
                        Text(warning.text(text)).dsType(.caption).foregroundStyle(Color.ds("warning"))
                    }
                }
                pages
                if let problem = reader.problem {
                    Text(problem).dsType(.caption).foregroundStyle(Color.ds("danger")).fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.bottom, CGFloat.ds("s-6"))
        }
        .background(Color.ds("surface"))
        .task { if let document = pdf.document { await reader.prepare(document) } }
        .fullScreenCover(isPresented: $cameraOpen) {
            DocumentCamera { images in
                cameraOpen = false
                if let photo = images.first.flatMap(PhotoImages.jpeg) { Task { await retake(photo) } }
            }
            .ignoresSafeArea()
        }
        .photosPicker(isPresented: $photosOpen, selection: $picked, maxSelectionCount: 1, matching: .images)
        .onChange(of: picked) { _, items in
            guard let item = items.first else { return }
            picked = []
            Task {
                if let data = try? await item.loadTransferable(type: Data.self), let image = UIImage(data: data), let photo = PhotoImages.jpeg(image) {
                    await retake(photo)
                }
            }
        }
    }

    @ViewBuilder private var pages: some View {
        let readings = reader.readings
        if readings.pages.contains(where: { $0 != .waiting }) {
            Text(readings.progress(text)).overline().padding(.horizontal, CGFloat.ds("s-4")).accessibilityAddTraits(.updatesFrequently)
            ForEach(readings.summary(text)) { line in row(line, readings.pages[line.page - 1]) }
                .padding(.horizontal, CGFloat.ds("s-4"))
            if readings.joined != nil && !readings.isBusy {
                Button {
                    Task { await openNotes() }
                } label: {
                    Text(opening ? text["photos.opening"] : text["photos.openNotes"])
                }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                .disabled(opening)
                .padding(.horizontal, CGFloat.ds("s-4"))
                Text(text["photos.reportNotKept"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, CGFloat.ds("s-4"))
            }
        }
    }

    private func row(_ line: PageSummaryLine, _ reading: PageReading) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                if reading == .sending { ProgressView().controlSize(.small) }
                Text(reading == .sending ? text.format("photos.pageSending", String(line.page)) : line.text)
                    .dsType(.caption)
                    .foregroundStyle(line.unsure ? Color.ds("warning") : Color.ds("ink"))
                    .fixedSize(horizontal: false, vertical: true)
            }
            if reading != .sending && reading != .waiting {
                HStack(spacing: CGFloat.ds("s-2")) {
                    if line.retake {
                        Menu {
                            if DocumentCamera.isAvailable { Button(text["photos.camera"]) { retakeWith(line.page, camera: true) } }
                            Button(text["photos.fromPhotos"]) { retakeWith(line.page, camera: false) }
                        } label: {
                            Text(text["photos.retakePage"])
                        }
                        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                    }
                    Button(text["photos.sendAgain"]) { Task { await readAgain(line.page) } }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        .disabled(reader.readings.isBusy || address == nil)
                }
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
    }

    private func readAll() async -> String? {
        guard let address, let document = pdf.document else { return text["pdf.readNone"] }
        return await reader.readAll(document, address: address, account: account, text: text)
    }

    private func readAgain(_ page: Int) async {
        guard let address, let document = pdf.document else { return }
        await reader.read(page, document, address: address, account: account, text: text)
    }

    private func retakeWith(_ page: Int, camera: Bool) {
        retaking = page
        if camera { cameraOpen = true } else { photosOpen = true }
    }

    /// The page photographed again: into the PDF, which loads again, and read again at once.
    private func retake(_ photo: Data) async {
        guard let page = retaking else { return }
        retaking = nil
        guard let document = pdf.document, await reader.retake(page, document, photo: photo, piece: pdf.piece) else { return }
        await pdf.load(piece: pdf.piece, text: text, page: page)
        await readAgain(page)
    }

    private func openNotes() async {
        let readings = reader.readings
        guard let score = readings.joined else { return }
        opening = true
        defer { opening = false }
        PhotoPageNotes.shared.keep(titles: titles, lines: readings.summary(text), bars: readings.barRanges)
        if let problem = await open(Data(score.utf8), reader.engine, max(1, readings.joinedFrom.count)) {
            reader.problem = problem
        }
    }
}
