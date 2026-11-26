import Foundation

/// The piece's other versions on the score (ADR 0031 section 3, S4): the versions and the one compared bar by bar (0022),
/// what the last thing done with a version came to, a version's marker tapped on the score, and the score as last
/// engraved (even bars, the bars the app wrote). It owns this state and the rules of its own that need nothing else:
/// reading a version and the list through music-core, and the words on a version just added. Since S7c it holds the
/// piece's sources too (`info`).
/// Drawing the versions on the page and engraving are ScoreController's, the composition root, which hands in what they
/// give (`read`, `say`, `pickMarker`, `keepEngraved`); the views observe this model. ADR 0031, the notes of 2026-11-18
/// (S4, S7c); the merge draft left in v1.1 (ADR 0034, section 8).
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
        read(list: await MusicCore.run { MusicCore.call(piece: piece, "performances") })
    }

    /// The version compared bar by bar with these notes (`performance`), or why it could not be read.
    func readVersion(_ id: String, in piece: Int32) async {
        let request = facadeRequest(["id": id])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "performance", request) }
        if !read(performance: answer) { status = FacadeAnswer.message(answer) }
    }

    /// «Переименовать…» on a version's row in Sources: music-core keeps its new name (`performance.rename`) and the list is
    /// read again, so the version shown stays the one shown; why not, in `status`. A blank name or the same one sends nothing.
    func rename(_ id: String, to name: String, in piece: Int32) async {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != versions.entries.first(where: { $0.id == id })?.name else { return }
        let request = facadeRequest(["id": id, "name": trimmed])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "performance.rename", request) }
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
}
