import Combine
import SwiftUI

// Assembling the player's own version from the piece's sources (decision record 0024; the mockups «Сборка своей версии»),
// as the web app's comparison does it, through music-core's `assembly` calls (contract 9). The score stays as it is
// while the piece has one source; once a version is added, a quiet card offers to compare. Comparing shows each source
// in its own bars on the same page, chosen by a chip («Ноты», each version, «Как было»), with a selection from bar to
// bar or note to note, a card at the selection, the carry sheet with its ghost, the journal with «Вернуть что было», and
// «Сделать нотами», the one rule that changes the piece. Everything runs on the device.

/// One source of the assembly, as `assembly` answers it.
struct AssemblySource: Identifiable, Equatable, Sendable {
    var id: String
    /// "notes", "version" or "before".
    var kind: String
    var name: String
    var reads: Bool
    var bars: Int
    /// A version's colour by the order it was added: plum, teal, amber, green; nil for the notes and «Как было».
    var tone: Int?
    /// A version's origin: "performance", "recording" or "score".
    var from: String?
    var summary: String?
    var corrections = 0
    var detail: String?

    init?(object: [String: Any], text: UIText) {
        guard let id = object["id"] as? String else { return nil }
        self.id = id
        kind = object["kind"] as? String ?? "version"
        name = object["name"] as? String ?? id
        reads = object["reads"] as? Bool ?? false
        bars = (object["bars"] as? NSNumber)?.intValue ?? 0
        tone = (object["tone"] as? NSNumber)?.intValue
        from = object["from"] as? String
        summary = object["summary"] as? String
        corrections = (object["corrections"] as? NSNumber)?.intValue ?? 0
        if let match = object["match"] as? [String: Any] {
            func count(_ key: String) -> String { String((match[key] as? NSNumber)?.intValue ?? 0) }
            detail = text.format("assembly.detailsLine", count("same"), count("partly"), count("differs"), count("notFound"), count("total"))
        }
    }

    /// The token of the source's colour on its chip, its dot and its card.
    var toneColor: Color {
        guard let tone else { return kind == "before" ? .ds("ink-3") : .ds("ink-2") }
        return [Color.ds("plum"), .ds("teal"), .ds("amber"), .ds("success")][tone % 4]
    }

    /// The colour of words in the tone: amber's own shade is too light for words on the paper.
    var toneText: Color {
        tone.map { $0 % 4 == 2 ? Color.ds("warning") : toneColor } ?? .ds("ink-2")
    }
}

/// What `assembly` answers: the sources and what the journal holds.
struct AssemblyInfo: Equatable, Sendable {
    var versions = 0
    var steps = 0
    var carries = 0
    var canUndo = false
    var canRedo = false
    var changed = false
    var applied = false
    var canUnapply = false
    var sources: [AssemblySource] = []

    init() {}

    init?(object: [String: Any], text: UIText) {
        guard let assembly = object["assembly"] as? [String: Any] else { return nil }
        func number(_ key: String) -> Int { (assembly[key] as? NSNumber)?.intValue ?? 0 }
        versions = number("versions")
        steps = number("steps")
        carries = number("carries")
        canUndo = assembly["canUndo"] as? Bool ?? false
        canRedo = assembly["canRedo"] as? Bool ?? false
        changed = assembly["changed"] as? Bool ?? false
        applied = assembly["applied"] as? Bool ?? false
        canUnapply = assembly["canUnapply"] as? Bool ?? false
        sources = (assembly["sources"] as? [[String: Any]] ?? []).compactMap { AssemblySource(object: $0, text: text) }
    }

    func source(_ id: String) -> AssemblySource? { sources.first { $0.id == id } }
}

/// A line of the carry sheet, a carry's card, the revert plan or the apply dialog: a choice (?), something around
/// changes (!), or it stays the same (✓).
struct AssemblyLine: Identifiable, Equatable, Sendable {
    var id: Int
    var name: String
    var mark: String
    var label: String
    var text: String

    static func read(_ value: Any?) -> [AssemblyLine] {
        (value as? [[String: Any]] ?? []).enumerated().map { index, line in
            AssemblyLine(id: index, name: line["name"] as? String ?? "", mark: line["mark"] as? String ?? "same",
                         label: line["label"] as? String ?? "", text: line["text"] as? String ?? "")
        }
    }
}

/// A span {onset, end} in a facade answer, in ticks.
private func spanOf(_ value: Any?) -> Range<Double>? {
    guard let span = value as? [String: Any], let onset = (span["onset"] as? NSNumber)?.doubleValue,
          let end = (span["end"] as? NSNumber)?.doubleValue, end > onset else { return nil }
    return onset..<end
}

private func spanJson(_ span: Range<Double>) -> [String: Any] {
    ["onset": Int(span.lowerBound), "end": Int(span.upperBound)]
}

/// The card at the selection, as `assembly.pick` answers it.
struct AssemblyPickCard: Equatable, Sendable {
    var title: String
    var edges: String?
    var canCarry: Bool
    var canTranspose: Bool
    var similarSpan: Range<Double>?
    var similarText: String?
    /// On the notes, the carry that put the selected notes in, with its lines.
    var carryNumber: Int?
    var carrySource: String?
    var carryFrom: Range<Double>?
    var carryLines: [AssemblyLine] = []

    init(object: [String: Any]) {
        title = object["title"] as? String ?? ""
        edges = object["edges"] as? String
        canCarry = object["canCarry"] as? Bool ?? false
        canTranspose = object["canTranspose"] as? Bool ?? false
        if let similar = object["similar"] as? [String: Any] {
            similarSpan = spanOf(similar["span"])
            similarText = similar["text"] as? String
        }
        if let carry = object["carry"] as? [String: Any] {
            carryNumber = (carry["number"] as? NSNumber)?.intValue
            carrySource = carry["source"] as? String
            carryFrom = spanOf(carry["from"])
            carryLines = AssemblyLine.read(carry["lines"])
        }
    }
}

/// The carry sheet, as `assembly.sheet` answers it.
struct AssemblySheet: Equatable, Sendable {
    var title: String
    var route: String
    var replaceText: String?
    var insertText: String
    var keyText: String?
    var suggestedTranspose: Int
    var keepText: String?
    var heardText: String?
    var canReplace: Bool
    var insert: Bool
    var keep: Bool
    var transpose: Int
    var lines: [AssemblyLine]
    var ghost: Range<Double>?

    init?(object: [String: Any]) {
        guard let sheet = object["sheet"] as? [String: Any] else { return nil }
        title = sheet["title"] as? String ?? ""
        route = sheet["route"] as? String ?? ""
        replaceText = sheet["replaceText"] as? String
        insertText = sheet["insertText"] as? String ?? ""
        keyText = sheet["keyText"] as? String
        suggestedTranspose = (sheet["suggestedTranspose"] as? NSNumber)?.intValue ?? 0
        keepText = sheet["keepText"] as? String
        heardText = sheet["heardText"] as? String
        canReplace = sheet["canReplace"] as? Bool ?? false
        insert = sheet["insert"] as? Bool ?? true
        keep = sheet["keep"] as? Bool ?? false
        transpose = (sheet["transpose"] as? NSNumber)?.intValue ?? 0
        lines = AssemblyLine.read(sheet["lines"])
        ghost = spanOf(sheet["ghost"])
    }
}

/// The journal, as `assembly.journal` answers it.
struct AssemblyJournal: Equatable, Sendable {
    struct Row: Identifiable, Equatable, Sendable {
        var number: Int
        var source: String?
        var title: String
        var detail: String
        var applied: Bool
        var span: Range<Double>?
        var id: Int { number }
    }

    struct Revert: Equatable, Sendable {
        var number: Int
        var dependents: [Int]
        var lines: [AssemblyLine]
    }

    var rows: [Row] = []
    var revert: Revert?
    var applyPlaces = 0
    var applyBars = ""
    var applyLines: [AssemblyLine] = []

    init() {}

    init(object: [String: Any]) {
        rows = (object["rows"] as? [[String: Any]] ?? []).map { row in
            Row(number: (row["number"] as? NSNumber)?.intValue ?? 0, source: row["source"] as? String, title: row["title"] as? String ?? "",
                detail: row["detail"] as? String ?? "", applied: row["applied"] as? Bool ?? false, span: spanOf(row["span"]))
        }
        if let plan = object["revert"] as? [String: Any] {
            revert = Revert(number: (plan["number"] as? NSNumber)?.intValue ?? 0,
                            dependents: (plan["dependents"] as? [NSNumber] ?? []).map(\.intValue), lines: AssemblyLine.read(plan["lines"]))
        }
        if let apply = object["apply"] as? [String: Any] {
            applyPlaces = (apply["places"] as? NSNumber)?.intValue ?? 0
            applyBars = apply["bars"] as? String ?? ""
            applyLines = AssemblyLine.read(apply["lines"])
        }
    }
}

/// The comparison and the assembly of the piece open now: what is shown, the selection in each source, the carry being
/// made, the journal and the apply dialog. The score page shows the source this holds (ScoreController engraves it with
/// `assembly.notation`), and taps on the page come here while comparing.
@MainActor
final class AssemblyModel: ObservableObject {
    /// The chip of the player's notes as the journal builds them («Ноты»).
    static let notes = "notes"
    /// The chip of the notes before the assembly («Как было»).
    static let before = "before"

    /// The score page the sources are shown on.
    weak var score: ScoreController?
    private let text = UIText.current
    private var piece: Int32?

    @Published private(set) var info = AssemblyInfo()
    /// Whether the comparison is open: the chips, the card at the selection, the carry sheet.
    @Published private(set) var open = false
    /// The source the page shows while comparing.
    @Published private(set) var shown = AssemblyModel.notes
    /// The selection in each source, in that source's own time.
    @Published private(set) var picks: [String: Range<Double>] = [:]
    @Published private(set) var card: AssemblyPickCard?
    /// The carry being made in the sheet; nil while the sheet is shut.
    @Published private(set) var carry: Carry?
    @Published private(set) var sheet: AssemblySheet?
    @Published var journalOpen = false
    @Published private(set) var journal = AssemblyJournal()
    @Published private(set) var applyOpen = false
    /// What the last step did, with the step that takes it back.
    @Published private(set) var toast: Toast?
    /// The card after a version was added: it goes after ten seconds or on «Позже».
    @Published private(set) var offer: Offer?
    /// Bumped on every change of what the page should show, so the score engraves it again.
    @Published private(set) var revision = 0

    struct Carry: Equatable {
        var source: String
        var span: Range<Double>
        var target: Range<Double>?
        var insert: Bool?
        var transpose = 0
        var keep: Bool?
        /// The source shown before the sheet opened, shown again on «Отмена».
        var returnTo: String

        var json: [String: Any] {
            var carry: [String: Any] = ["source": source, "span": spanJson(span), "transpose": transpose]
            if let target { carry["target"] = spanJson(target) }
            if let insert { carry["insert"] = insert }
            if let keep { carry["keep"] = keep }
            return carry
        }
    }

    struct Toast: Equatable {
        enum Undo: Equatable { case step, unapply }
        var text: String
        var undo: Undo?
    }

    struct Offer: Equatable {
        var versionId: String
        var name: String
        var text: String
    }

    /// A source's bars and notes, for picking on its page.
    private struct SourceShape {
        var bars: [Range<Double>]
        var notes: [(id: String, span: Range<Double>)]
    }

    private var shapes: [String: SourceShape] = [:]
    /// The note a selection from note to note started at: the next note tapped ends it.
    private var pickFrom: (source: String, span: Range<Double>, id: String)?
    private var offerTimer: Task<Void, Never>?

    func attach(_ piece: Int32) {
        guard self.piece != piece else { return }
        self.piece = piece
        open = false
        shown = Self.notes
        picks = [:]
        card = nil
        carry = nil
        sheet = nil
        journalOpen = false
        applyOpen = false
        toast = nil
        offer = nil
        shapes = [:]
        pickFrom = nil
        Task { await refresh() }
    }

    /// What the page should be engraved with while comparing: a key that changes whenever the engraving would.
    var engravingKey: String {
        "assembly:\(shown):\(revision)"
    }

    /// The request `assembly.notation` takes for the page as it should be.
    func notationRequest(title: String) -> [String: Any] {
        var request: [String: Any] = ["source": shown, "title": title]
        if let carry, shown == Self.notes { request["carry"] = carry.json }
        return request
    }

    func source(_ id: String) -> AssemblySource? { info.source(id) }

    /// The name of the source shown, by the title while comparing: «Ноты · призрак» while the sheet is open.
    var shownLabel: String {
        carry != nil && shown == Self.notes ? text["assembly.notesGhost"] : source(shown)?.name ?? ""
    }

    /// «+1 версия · 3 переноса»: the only trace of the assembly in everyday work.
    var versionsLabel: String? {
        guard info.versions > 0 else { return nil }
        let label = text.count("assembly.plusVersions", info.versions)
        return info.carries > 0 ? "\(label) · \(text.count("assembly.carries", info.carries))" : label
    }

    // MARK: - Opening and closing

    /// After a version is added: one quiet card for ten seconds, with where it matches the notes.
    func offerComparison(_ versionId: String) {
        Task {
            await refresh()
            guard let version = source(versionId) else { return }
            offer = Offer(versionId: versionId, name: version.name, text: version.summary ?? text["assembly.unreadable"])
            offerTimer?.cancel()
            offerTimer = Task { [weak self] in
                try? await Task.sleep(for: .seconds(10))
                guard !Task.isCancelled, self?.offer?.versionId == versionId else { return }
                self?.offer = nil
            }
        }
    }

    func later() {
        offerTimer?.cancel()
        offer = nil
    }

    /// Compare: the chips, the card and the sheet come; the page shows `show`, or the notes.
    func openComparison(_ show: String? = nil) {
        offerTimer?.cancel()
        offer = nil
        Task {
            await refresh()
            open = true
            score?.assemblyOpened()
            await self.show(show ?? shown)
        }
    }

    /// «Послушать версию» on the card after adding: compared, with the version shown and playing.
    func hearOffered() {
        guard let offer else { return }
        let id = offer.versionId
        openComparison(id)
        Task {
            try? await Task.sleep(for: .milliseconds(300))
            hear(id)
        }
    }

    /// Done: the comparison folds away and the piece shows and plays as it was.
    func close() {
        guard open else { return }
        open = false
        carry = nil
        sheet = nil
        card = nil
        journalOpen = false
        applyOpen = false
        pickFrom = nil
        score?.assemblyClosed()
    }

    // MARK: - Showing a source and selecting in it

    /// A chip's name: the page shows that source in its own bars, with its own selection.
    func show(_ id: String) async {
        guard let piece, source(id)?.reads ?? false else { return }
        shown = id
        pickFrom = nil
        await loadShape(piece, id)
        revision += 1
        await score?.assemblyChanged()
        await pickChanged()
    }

    func choose(_ id: String) {
        guard carry == nil else { return }
        Task { await show(id) }
    }

    private func loadShape(_ piece: Int32, _ id: String) async {
        let request = Self.json(["source": id])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly.source", request) }
        guard let object = facadeObject(answer), object["error"] == nil else { return }
        // A bar and a note carry their onset and end as a span does.
        let bars = (object["bars"] as? [[String: Any]] ?? []).compactMap { spanOf($0) }
        let notes = (object["notes"] as? [[String: Any]] ?? []).compactMap { note -> (id: String, span: Range<Double>)? in
            guard let noteId = note["id"] as? String, let span = spanOf(note) else { return nil }
            return (noteId, span)
        }
        shapes[id] = SourceShape(bars: bars, notes: notes)
    }

    /// A bar tapped, or bars chosen by a drag: whole bars of the source shown.
    func pickBars(_ start: Double, _ end: Double) {
        guard carry == nil else { return }
        pickFrom = nil
        let bars = shapes[shown]?.bars ?? []
        let first = bars.last { $0.lowerBound <= start }?.lowerBound ?? start
        let last = bars.first { $0.upperBound >= end }?.upperBound ?? end
        setPick(first..<max(last, first + 1))
    }

    /// A note tapped: the selection starts at it; the next note tapped ends it there, from note to note.
    func pickNote(_ tick: Double) {
        guard carry == nil, let notes = shapes[shown]?.notes else { return }
        guard let note = notes.filter({ $0.span.contains(tick) }).min(by: { abs($0.span.lowerBound - tick) < abs($1.span.lowerBound - tick) })
                ?? notes.min(by: { abs($0.span.lowerBound - tick) < abs($1.span.lowerBound - tick) }) else { return }
        if let from = pickFrom, from.source == shown, from.id != note.id {
            setPick(min(from.span.lowerBound, note.span.lowerBound)..<max(from.span.upperBound, note.span.upperBound))
            pickFrom = nil
            return
        }
        pickFrom = (shown, note.span, note.id)
        setPick(note.span)
    }

    func clearPick() {
        guard carry == nil else { return }
        pickFrom = nil
        setPick(nil)
    }

    private func setPick(_ span: Range<Double>?) {
        picks[shown] = span
        Task { await pickChanged() }
    }

    /// The selection of the source shown: the band on the page and the card under it.
    private func pickChanged() async {
        let pick = carry == nil ? picks[shown] : sheet?.ghost
        await score?.showAssemblyPick(pick)
        guard carry == nil, let piece, let span = picks[shown] else {
            card = nil
            return
        }
        let request = Self.json(["source": shown, "span": spanJson(span), "naming": DeviceSettings.shared["notes.naming"]])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly.pick", request) }
        guard let object = facadeObject(answer), object["error"] == nil, picks[shown] == span else { return }
        card = AssemblyPickCard(object: object)
    }

    /// «Показать»: the notes are shown with the place the version's selection is like.
    func showSimilar() {
        guard let span = card?.similarSpan else { return }
        picks[Self.notes] = span
        Task { await show(Self.notes) }
    }

    /// «Показать в Кавере»: the version is shown with the fragment the carry took.
    func showCarrySource() {
        guard let source = card?.carrySource, let from = card?.carryFrom else { return }
        picks[source] = from
        journalOpen = false
        Task { await show(source) }
    }

    // MARK: - Hearing

    /// A chip's ▶ or «Слушать»: the source is heard over its selection, or from its start; pressed again, it stops.
    func hear(_ id: String? = nil) {
        guard let piece else { return }
        let source = id ?? shown
        Task {
            if source != shown { await show(source) }
            var request: [String: Any] = ["source": source]
            if let carry, source == Self.notes { request["carry"] = carry.json }
            let body = Self.json(request)
            let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly.playback", body) }
            guard let score = PlaybackScore.read(answer) else { return }
            let end = score.notes.map { $0.onset + $0.duration }.max() ?? 0
            let range = (carry != nil && source == Self.notes ? sheet?.ghost : nil) ?? picks[source] ?? 0..<max(end, 1)
            self.score?.hearNotes?(score.notes, range)
        }
    }

    // MARK: - The carry

    /// «В ноты…»: the sheet opens on what the carry will do, and the page shows the notes as they will be. It replaces the
    /// notes selected in the notes, or else the place the fragment is like; or it goes in at the start of that place.
    func openCarry() {
        guard let span = picks[shown], source(shown)?.kind == "version", card?.canCarry == true else { return }
        carry = Carry(source: shown, span: span, target: picks[Self.notes], returnTo: shown)
        journalOpen = false
        applyOpen = false
        Task {
            await loadSheet()
            await show(Self.notes)
        }
    }

    func chooseInsert(_ insert: Bool) { changeCarry { $0.insert = insert } }

    func chooseKeep(_ keep: Bool) { changeCarry { $0.keep = keep } }

    func chooseTranspose(_ semitones: Int) { changeCarry { $0.transpose = semitones } }

    private func changeCarry(_ change: (inout Carry) -> Void) {
        guard var draft = carry else { return }
        change(&draft)
        carry = draft
        Task {
            await loadSheet()
            revision += 1
            await score?.assemblyChanged()
            await pickChanged()
        }
    }

    private func loadSheet() async {
        guard let piece, let carry else { return }
        let request = Self.json(["carry": carry.json])
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly.sheet", request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            say(facadeObject(answer)?["message"] as? String)
            return
        }
        sheet = AssemblySheet(object: object)
    }

    func cancelCarry() {
        guard let draft = carry else { return }
        carry = nil
        sheet = nil
        Task { await show(draft.returnTo) }
    }

    /// «Перенести»: the carry joins the journal, the notes show where it went, and the toast can take it back.
    func makeCarry() {
        guard let piece, let draft = carry else { return }
        Task {
            let request = Self.json(["carry": draft.json, "now": Self.now])
            guard let object = await change(piece, "assembly.carry", request) else { return }
            carry = nil
            sheet = nil
            let carried = object["carried"] as? [String: Any]
            if let placed = spanOf(carried?["span"]) { picks[Self.notes] = placed }
            picks[draft.source] = nil
            toast = Toast(text: text.format("assembly.carried", String((carried?["number"] as? NSNumber)?.intValue ?? 0), carried?["title"] as? String ?? ""), undo: .step)
            await show(Self.notes)
        }
    }

    /// «На полтона ниже / выше» on the notes: the selected notes transposed, as a step of the journal.
    func transpose(_ semitones: Int) {
        guard let piece, shown == Self.notes, let span = picks[Self.notes] else { return }
        Task {
            let request = Self.json(["span": spanJson(span), "semitones": semitones, "now": Self.now])
            guard await change(piece, "assembly.transpose", request) != nil else { return }
            toast = Toast(text: text["assembly.transposed"], undo: .step)
            await afterChange()
        }
    }

    /// «Принять совпавшее»: every bar of a version that matches the notes but is not identical, carried in one change.
    func acceptMatched(_ id: String) {
        guard let piece else { return }
        Task {
            let request = Self.json(["id": id, "now": Self.now])
            guard let object = await change(piece, "assembly.accept", request) else { return }
            let accepted = (object["accepted"] as? NSNumber)?.intValue ?? 0
            toast = accepted > 0 ? Toast(text: text.count("assembly.accepted", accepted), undo: .step) : Toast(text: text["assembly.acceptNone"], undo: nil)
            await afterChange()
        }
    }

    func undo() { step("assembly.undo") }

    func redo() { step("assembly.redo") }

    private func step(_ call: String) {
        guard let piece else { return }
        Task {
            guard await change(piece, call, "{}") != nil else { return }
            toast = nil
            await afterChange()
        }
    }

    // MARK: - The journal

    func toggleJournal() {
        journalOpen.toggle()
        carry = nil
        sheet = nil
        applyOpen = false
        if journalOpen { Task { await loadJournal(revert: nil) } }
    }

    /// «Вернуть что было» on a step: the journal shows what goes with it and what stays, before anything is done.
    func planRevert(_ number: Int?) {
        Task { await loadJournal(revert: number) }
    }

    private func loadJournal(revert: Int?) async {
        guard let piece else { return }
        var request: [String: Any] = ["naming": DeviceSettings.shared["notes.naming"]]
        if let revert { request["revert"] = revert }
        let body = Self.json(request)
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly.journal", body) }
        guard let object = facadeObject(answer), object["error"] == nil else { return }
        journal = AssemblyJournal(object: object)
    }

    /// «Вернуть»: the step and every step that leans on it leave the journal, as one change Undo brings back.
    func revert(_ number: Int) {
        guard let piece else { return }
        Task {
            guard let object = await change(piece, "assembly.revert", Self.json(["number": number])) else { return }
            let dependents = ((object["reverted"] as? [String: Any])?["dependents"] as? [Any])?.count ?? 0
            picks[Self.notes] = nil
            toast = Toast(text: text.format("assembly.reverted", String(number), String(dependents)), undo: .step)
            await loadJournal(revert: nil)
            await afterChange()
        }
    }

    /// «Начать заново»: the notes back to the notes before the assembly, as one change Undo brings back.
    func restart() {
        guard let piece else { return }
        Task {
            guard await change(piece, "assembly.restart", "{}") != nil else { return }
            picks[Self.notes] = nil
            toast = Toast(text: text["assembly.restarted"], undo: .step)
            await loadJournal(revert: nil)
            await afterChange()
        }
    }

    /// «Показать» on a journal row: the notes, with the step's music selected.
    func showStep(_ row: AssemblyJournal.Row) {
        guard let span = row.span else { return }
        picks[Self.notes] = span
        journalOpen = false
        Task { await show(Self.notes) }
    }

    // MARK: - «Сделать нотами»

    func openApply() {
        applyOpen = true
        journalOpen = false
        carry = nil
        sheet = nil
        Task { await loadJournal(revert: nil) }
    }

    func closeApply() {
        applyOpen = false
    }

    /// «Сделать нотами», the one rule that changes the piece (0024, section 7): music-core opens the piece again with the
    /// notes the journal builds; the notes before stay as «Ноты до сборки», and the toast can take it back.
    func apply() {
        guard let piece else { return }
        Task {
            let body = Self.json(["now": Self.now])
            let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly.apply", body) }
            guard let object = facadeObject(answer), object["error"] == nil else {
                say(facadeObject(answer)?["message"] as? String)
                return
            }
            let places = (object["places"] as? NSNumber)?.intValue ?? 0
            applyOpen = false
            picks = [:]
            close()
            await refresh()
            await score?.assemblyApplied()
            toast = Toast(text: text.count("assembly.applied", places), undo: .unapply)
        }
    }

    func undoToast() {
        guard let undo = toast?.undo, let piece else { return }
        toast = nil
        switch undo {
        case .step:
            step("assembly.undo")
        case .unapply:
            Task {
                let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly.unapply") }
                guard facadeObject(answer)?["error"] == nil else { return }
                await refresh()
                await score?.assemblyApplied()
            }
        }
    }

    func closeToast() {
        toast = nil
    }

    // MARK: - Calls

    func refresh() async {
        guard let piece else { return }
        let answer = await MusicCore.run { MusicCore.call(piece: piece, "assembly") }
        if let object = facadeObject(answer), let read = AssemblyInfo(object: object, text: text) { info = read }
    }

    /// One call that changes the journal; the assembly as it answers, nil with why for an error.
    private func change(_ piece: Int32, _ call: String, _ request: String) async -> [String: Any]? {
        let answer = await MusicCore.run { MusicCore.call(piece: piece, call, request) }
        guard let object = facadeObject(answer), object["error"] == nil else {
            say(facadeObject(answer)?["message"] as? String)
            return nil
        }
        if let read = AssemblyInfo(object: object, text: text) { info = read }
        await score?.assemblySaved()
        return object
    }

    /// After the journal changed: the notes or «Как было» are shown again as they now are.
    private func afterChange() async {
        if !open { return }
        if shown == Self.notes || shown == Self.before || source(shown) == nil {
            await show(source(shown) == nil ? Self.notes : shown)
        } else {
            await pickChanged()
        }
    }

    private func say(_ message: String?) {
        guard let message else { return }
        toast = Toast(text: message, undo: nil)
    }

    private static var now: String { ISO8601DateFormatter().string(from: Date()) }

    private static func json(_ object: [String: Any]) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: object) else { return "{}" }
        return String(decoding: data, as: UTF8.self)
    }
}
