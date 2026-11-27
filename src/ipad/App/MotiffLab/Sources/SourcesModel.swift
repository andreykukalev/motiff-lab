import Foundation

/// What the versions need back from the score they are drawn on (ADR 0031 section 3, v1.6 i-10), as `PenScore` and
/// `CheckScore`: the page, the piece, whether extra information of a kind shows, a draft's labels drawn with the markers,
/// and what follows a version drawn, added, heard or picked. ScoreController, the composition root, is it.
@MainActor
protocol SourcesScore: AnyObject {
    /// The score's page, the one way to its functions (`PageCommand`).
    var scorePage: ScorePage { get }
    /// The piece open in music-core; nil while none is.
    var pieceId: Int32? { get }
    /// Whether the pages are drawn, so marks can go on them.
    var shown: Bool { get }
    /// The labels of a draft's places and the numbers of their приёмы, drawn with the versions' markers.
    var placeMarkers: [[String: Any]] { get }
    /// Whether extra information of this kind shows on the score (`MarksModel.extraShown`).
    func extraShown(_ kind: String) -> Bool
    /// A passage selected, and brought into view.
    func showSpan(_ span: Range<Double>)
    /// The versions were drawn: the version in focus and the engraving (even bars) follow.
    func versionsDrawn() async
    /// A version was added to the piece: Check reads its flags again.
    func loadCheck(in piece: Int32) async
    /// A version's notes heard over a span, once (a marker's «Как в версии»).
    func hear(version id: String, over span: Range<Double>)
    /// The way to the versions from a marker, by its bar: the screen opens Sources.
    func openSources(atBar bar: Int)
}

/// The piece's other versions on the score (ADR 0031 section 3, S4): the versions and the one compared bar by bar (0022),
/// what the last thing done with a version came to, a version's marker tapped on the score, and the score as last
/// engraved (even bars, the bars the app wrote). It owns this state, reads a version and the list through music-core,
/// and since v1.6 i-10 draws the versions on the page and acts on them (show, compare, add, rename, a marker's way to
/// Sources and its «Как в версии») through the score it is given (`SourcesScore`), as CheckModel does. Since S7c it
/// holds the piece's sources too (`info`). Engraving stays ScoreController's, the composition root, which hands in what
/// it gives (`keepEngraved`); the views observe this model. ADR 0031, the notes of 2026-11-18 (S4, S7c) and v1.6 i-10;
/// the merge draft left in v1.1 (ADR 0034, section 8).
@MainActor
final class SourcesModel: ObservableObject {
    /// The score as last engraved: whether its bars are even (Settings · Notes, testing item 35), and how many bars the app
    /// wrote itself where the notes' source record did not fit («Как напечатано», v1.4), nil for notes with no record.
    struct Engraved: Equatable {
        var even: Bool
        var appWrittenBars: Int?

        /// The view's note in the bar (concept «Как напечатано», section 2): «По записи источника, 4 такта записаны
        /// приложением»; nil when the app wrote no bar or the notes have no record.
        func note(_ text: UIText) -> String? {
            guard let bars = appWrittenBars, bars > 0 else { return nil }
            return text.count("sources.appWrittenBars", bars)
        }
    }

    /// A version's marker tapped on the score, and where it is on the screen, in the web view's points: the popover goes
    /// under it.
    struct MarkerPick: Equatable {
        var bar: Int
        var left: Double
        var bottom: Double
    }

    /// The other versions of the piece and the one shown, compared bar by bar (0022).
    @Published private(set) var versions = VersionShelf()
    /// What the last thing done with a version came to: "Bar 12 is now as in Cover", or why it could not be done.
    @Published private(set) var status: String?
    /// A version's marker tapped on the score: the popover under it says how the bar differs (testing item 33).
    @Published private(set) var markerPick: MarkerPick?
    /// The piece's sources (testing item 16): the base, the line on what the player's version holds, the PDF kept inside.
    @Published private(set) var info = SourcesInfo()
    @Published private(set) var engraved: Engraved
    /// The score the versions are drawn on, set by the root once it is made.
    weak var score: SourcesScore?
    private let text = UIText.current

    /// `evenBars`: whether the score is engraved with even bars now (the device's setting).
    init(evenBars: Bool) {
        engraved = Engraved(even: evenBars)
    }

    /// Another piece shown: none of the last one's versions, words, marker or sources; engraved as it opened.
    func open(evenBars: Bool, appWrittenBars: Int? = nil) {
        versions = VersionShelf()
        status = nil
        markerPick = nil
        engraved = Engraved(even: evenBars, appWrittenBars: appWrittenBars)
        info = SourcesInfo()
    }

    /// The piece's sources as music-core answered `sources`; an answer it cannot read leaves them as they were.
    func read(sources answer: String) {
        if let read = SourcesInfo(answer: answer) { info = read }
    }

    /// What the last thing done with a version came to, or nothing.
    func say(_ status: String?) {
        self.status = status
    }

    /// A version's marker tapped, or the popover closed.
    func pickMarker(_ pick: MarkerPick?) {
        markerPick = pick
    }

    /// The score as it was just engraved.
    func keepEngraved(_ engraved: Engraved) {
        self.engraved = engraved
    }

    /// The versions kept with the piece, as `performances` or a removal answers them.
    func read(list answer: String) {
        versions.read(list: answer)
    }

    /// A version compared, in an answer's text: it is the one shown now. False for a refusal.
    func read(performance answer: String) -> Bool {
        versions.read(performance: answer)
    }

    /// A version compared, as `PieceCore` answers it: it is the one shown now. False for a refusal.
    func read(performance answer: PerformanceAnswer?) -> Bool {
        versions.read(performance: answer)
    }

    /// The versions kept with the piece (`performances`), as music-core answers them.
    func readList(of piece: Int32) async {
        read(list: await MusicCore.run { PieceCore(piece: piece).send(.performances) })
    }

    /// The version compared bar by bar with these notes (`performance`), or why it could not be read.
    func readVersion(_ id: String, in piece: Int32) async {
        let request = facadeRequest(["id": id])
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.performance, request) }
        if !read(performance: answer) { status = FacadeAnswer.message(answer) }
    }

    /// «Переименовать…» on a version's row in Sources: music-core keeps its new name (`performance.rename`) and the list is
    /// read again, so the version shown stays the one shown; why not, in `status`. A blank name or the same one sends nothing.
    func rename(_ id: String, to name: String, in piece: Int32) async {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != versions.entries.first(where: { $0.id == id })?.name else { return }
        let request = facadeRequest(["id": id, "name": trimmed])
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.performanceRename, request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            status = FacadeAnswer.message(answer)
            return
        }
        status = nil
        await readList(of: piece)
    }

    /// The words on a version just added: how many of the printed notes it lines up with, or that it lines up with none.
    func addedStatus(in text: UIText) -> String {
        versions.matched == 0
            ? text["performance.noMatch"]
            : text.format("performance.added", String(versions.matched), String(versions.printed), String(versions.bars.filter(\.differs).count))
    }

    // MARK: - On the score (v1.6 i-10, from ScoreController)

    /// Shows another version: its bars compared with these notes, and markers above the bars it has otherwise.
    func showVersion(_ id: String) {
        guard let piece = score?.pieceId else { return }
        status = nil
        Task { await readAndDraw(id, in: piece) }
    }

    /// A version picked in the switch: it is the one compared on the score.
    func compareVersion(_ id: String) {
        if versions.shownId != id { showVersion(id) }
    }

    /// A MIDI or MusicXML file of the piece added as another version: music-core lines it up with these notes, keeps it
    /// with the piece and raises the Check flags it gives a scanned piece (`performance.add`, 0022); the list, the
    /// markers and Check follow.
    func addVersion(fileName: String, data: Data) async {
        guard let piece = score?.pieceId else { return }
        let name = (fileName as NSString).deletingPathExtension
        let request = facadeRequest(["name": name.isEmpty ? text["performance.defaultName"] : name, "file": data.base64EncodedString(), "fileName": fileName])
        let answer = await MusicCore.run { PieceCore(piece: piece).send(.performanceAdd, request) }
        guard read(performance: answer) else {
            status = FacadeAnswer.message(answer)
            return
        }
        status = addedStatus(in: text)
        await readList(of: piece)
        await drawVersions()
        await score?.loadCheck(in: piece)
    }

    /// «Переименовать…» on a version's row in Sources: the markers' labels and the popover take the new name.
    func renameVersion(_ id: String, to name: String) async {
        guard let piece = score?.pieceId else { return }
        await rename(id, to: name, in: piece)
        await drawVersions()
    }

    /// The piece's sources read again (testing item 16).
    func refreshSources() {
        Task { await readSources() }
    }

    private func readSources() async {
        guard let piece = score?.pieceId else { return }
        read(sources: await MusicCore.run { PieceCore(piece: piece).send(.sources) })
    }

    /// Show on a bar of the version: the bar is selected and comes into view.
    func showVersionBar(_ bar: Int) {
        guard let span = versions.bars.first(where: { $0.bar == bar })?.span else { return }
        score?.showSpan(span)
    }

    /// The versions kept with the piece, and the one shown compared bar by bar, as the piece's pages are drawn.
    func loadVersions(of piece: Int32) async {
        await readList(of: piece)
        if let id = versions.shownId ?? versions.entries.first?.id {
            await readVersion(id, in: piece)
        }
        await drawVersions()
    }

    private func readAndDraw(_ id: String, in piece: Int32) async {
        await readVersion(id, in: piece)
        await drawVersions()
    }

    /// With extra information on, the bars the version has otherwise as plum labels above them and the printed notes it
    /// does not have tinted, and a thin plum line under the bars taken from it; in a draft's view, always, the labels of
    /// its places and the numbers of their приёмы (`SourcesScore.placeMarkers`). The score follows (`versionsDrawn`).
    func drawVersions() async {
        await readSources()
        guard let score, score.shown else { return }
        let name = versions.shown?.name ?? ""
        let differing = score.extraShown("performance") ? versions.differing : []
        let markers: [[String: Any]] = differing.compactMap { bar in
            guard let span = bar.span else { return nil }
            return ["bar": bar.bar, "tick": span.lowerBound, "text": bar.marker, "label": text.format("performance.markerLabel", String(bar.bar), name, bar.marker)]
        } + score.placeMarkers
        let data: [String: Any] = [
            "markers": markers, "unheard": differing.flatMap(\.notHeard), "artefacts": [String](),
            "taken": score.extraShown("taken") ? versions.taken.compactMap { $0.span?.lowerBound } : [],
        ]
        await score.scorePage.send(.performance, data)
        await score.versionsDrawn()
    }

    /// ▶ As in the version, from a marker's popover: the bar as the version plays it.
    func hearMarkerVersion() {
        guard let pick = markerPick, let id = versions.shownId,
              let span = versions.bars.first(where: { $0.bar == pick.bar })?.span else { return }
        score?.hear(version: id, over: span)
    }

    /// «Источники» on a marker's popover: the popover goes and the screen opens Sources (`SourcesScore.openSources`).
    func openSourcesFromMarker() {
        guard let pick = markerPick else { return }
        markerPick = nil
        score?.openSources(atBar: pick.bar)
    }
}
