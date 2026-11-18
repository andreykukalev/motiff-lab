import Foundation

/// The piece's other versions on the score (ADR 0031 section 3, S4): the versions and the one compared bar by bar (0022),
/// what the last thing done with a version came to, the version whose page Sources shows (testing item 32), and the score
/// as last engraved (the draft's view, even bars, and the notes it colours). It owns this state and the rules of its own
/// that need nothing else: reading a version and the list through music-core, and the words on a version just added.
/// The piece's `sources`, the version's notes to hear, drawing the versions on the page, engraving and merging are
/// ScoreController's, the composition root, which hands in what they give (`read`, `say`, `showPage`, `keepEngraved`);
/// its observers see each change of this model as a change of the controller. Until which step: ADR 0031, the note of
/// 2026-11-18 (S4).
@MainActor
final class SourcesModel: ObservableObject {
    /// The score as last engraved: the draft's view ("version" or "result", nil for the player's notes), whether its bars
    /// are even (Settings · Notes, testing item 35), and on a draft's view the ids of the notes from the version (plum) and
    /// of the player's own (amber).
    struct Engraved: Equatable {
        var view: String?
        var even: Bool
        var added: [String] = []
        var own: [String] = []
    }

    /// The other versions of the piece and the one shown, compared bar by bar (0022).
    @Published private(set) var versions = VersionShelf()
    /// What the last thing done with a version came to: "Bar 12 is now as in Cover", or why it could not be done.
    @Published private(set) var status: String?
    /// The version whose page Sources shows (testing item 32): its overview, the merge draft and what was merged; nil for
    /// the list of sources.
    @Published private(set) var page: String?
    private(set) var engraved: Engraved

    /// `evenBars`: whether the score is engraved with even bars now (the device's setting).
    init(evenBars: Bool) {
        engraved = Engraved(view: nil, even: evenBars)
    }

    /// Another piece shown: none of the last one's versions, words, page or view.
    func open(evenBars: Bool) {
        versions = VersionShelf()
        status = nil
        page = nil
        engraved = Engraved(view: nil, even: evenBars)
    }

    /// What the last thing done with a version came to, or nothing.
    func say(_ status: String?) {
        self.status = status
    }

    /// The version whose page Sources shows, or nil for the list.
    func showPage(_ id: String?) {
        page = id
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

    /// The words on a version just added: how many of the printed notes it lines up with, or that it lines up with none.
    func addedStatus(in text: UIText) -> String {
        versions.matched == 0
            ? text["performance.noMatch"]
            : text.format("performance.added", String(versions.matched), String(versions.printed), String(versions.bars.filter(\.differs).count))
    }
}
