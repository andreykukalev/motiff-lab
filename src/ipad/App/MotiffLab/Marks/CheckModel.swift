import SwiftUI

/// What Check needs of the score it checks (ADR 0031 section 3, S6, v1.6 i-8): the piece open in music-core, a bar selected
/// and brought into view, the flags drawn on the page (with the pages of a scan no engine read, which only the screen's
/// folder knows), what the page hides, the notes changed, and the screen's lines for a word of guidance and for what went
/// wrong. ScoreController, the composition root, is it.
@MainActor
protocol CheckScore: AnyObject {
    /// The piece open in music-core; nil while none is.
    var pieceId: Int32? { get }
    /// A passage selected, and brought into view.
    func showSpan(_ span: Range<Double>)
    /// The open bars drawn on the score as amber flags (`CheckModel.flags`).
    func drawChecks() async
    /// The page hides what it should now: Check's flags show while its drawer is open (`CheckModel.drawerOpen`).
    func applyLayers() async
    /// The notes changed: the player and the roll take them, and the score is engraved again and drawn with its marks.
    func notesDidChange() async
    /// A word of guidance over the pill for a moment, by the key of its text.
    func showNotice(_ key: String)
    /// What went wrong, in the screen's problem line; nil for nothing.
    func showProblem(_ message: String?)
}

/// Check of the notes read (ADR 0031 section 3, S6, v1.6 i-8): the bars a scan or a transcription is unsure of as
/// music-core answered the last Check call, whether the Check drawer is open, what this iPad kept of the working notes'
/// scan (its bars' variants and crops) and the variant chosen last, Fix notes on a bar (`corrections`), «Мои правки»
/// (`editsLayer`) and the roll's note editor. It owns this state, reads the selection (SelectionModel) for the note
/// editor's bar, and calls music-core itself (`check`, `check.resolve`, `corrections.*`); the page's part (the flags drawn,
/// the bar shown, what it hides) and what follows changed notes it asks of the score it is given (`CheckScore`). The views
/// that show Check, Fix notes, «Мои правки» and the note editor observe this model.
@MainActor
final class CheckModel: ObservableObject {
    /// Whether the Check drawer is open: its flags show then even with extra information off.
    var drawerOpen = false {
        didSet {
            guard oldValue != drawerOpen else { return }
            Task { await score?.applyLayers() }
        }
    }
    /// The bars a scan or a transcription is unsure of (0018 section 9), as music-core answered the last Check call.
    @Published private(set) var check = CheckShelf()
    /// With «Четыре движка» on (v1.3 i-2), what this iPad kept of the working notes' scan: its bars to check with their
    /// variants, none for notes read elsewhere or before; nil while scan jobs are off. Read with its crops (`scanCrops`, by
    /// bar) once for the working notes, off the main thread.
    @Published private(set) var scanReview: ScanReview?
    @Published private(set) var scanCrops: [Int: UIImage] = [:]
    /// The variant the player chose last, which one Undo takes back whole while nothing came after it.
    @Published private(set) var variantChoice = ScanVariantChoice()
    /// A variant on its way into the notes: the readings' buttons wait for music-core's answer, and a tap that reaches
    /// `chooseVariant` before they show it puts nothing in, so a double tap never leaves a reading in the bar that
    /// «Отменить выбор» does not take back (v1.6 i-8, E-scan-9).
    @Published private(set) var puttingVariant = false
    /// Fix notes (S1-I7): the bar whose notes the Check drawer lists to correct, as music-core answers `corrections`.
    @Published private(set) var corrections: NoteCorrections?
    /// «Мои правки» (version 0.5, on the iPad since v0.6.5): whether the corrections are laid over the notes, and those that
    /// no longer fit, as music-core answered last; nil for notes opened only to be shown.
    @Published private(set) var editsLayer: EditsLayerState?
    /// The note editor on the roll while it is open (testing item 15); nil when it is closed.
    @Published private(set) var noteEditor: NoteEditorState?
    /// The note edits were saved, «Мои правки» switched or a correction that no longer fits taken out: the screen writes
    /// the project file at once.
    var editsKept: (@MainActor () -> Void)?

    /// The score checked, set by the root once it is made.
    weak var score: CheckScore?
    private let selection: SelectionModel
    /// Whether scans go as jobs to the server's engines on this iPad (`ScanServer.jobs`, Import's).
    private let scanJobs: @MainActor () -> Bool
    private let text = UIText.current
    /// Where each bar starts and where the notes end, by the playback score, read while the notes have marks «что не так»:
    /// a mark names its bars only, and its flag and «Показать» need where they lie (`readBars`).
    private var barStarts: [Int: Double] = [:]
    private var notesEnd = 0.0

    /// `scanJobs`: whether this iPad sends scans as jobs, so it keeps their bars' variants and crops (`ScanServer.jobs`).
    init(selection: SelectionModel, scanJobs: @escaping @MainActor () -> Bool) {
        self.selection = selection
        self.scanJobs = scanJobs
    }

    /// A piece opened: «Мои правки» as its file has them.
    func open(edits: EditsLayerState?) {
        editsLayer = edits
    }

    /// Reads Check for the piece (`check`), the scan's variants and crops when the notes or the choice of jobs changed, and
    /// draws the flags.
    func load(in piece: Int32) async {
        let before = check.fingerprint
        check = CheckShelf(answer: await MusicCore.run { PieceCore(piece: piece).send(.check) }) ?? CheckShelf()
        variantChoice.notesRead(check.fingerprint)
        if check.fingerprint != before || scanJobs() != (scanReview != nil) { await loadScanReview() }
        await readBars(of: piece, marks: check.marks)
        await score?.drawChecks()
    }

    /// Where the bars start, from the playback score, while the notes have `marks`; nothing is read without one.
    private func readBars(of piece: Int32, marks: [ReadingMarkEntry]) async {
        guard !marks.isEmpty, let playback = await PieceCore(piece: piece).playback().value else { return }
        barStarts = Dictionary(playback.bars.map { ($0.number, $0.onset) }, uniquingKeysWith: { first, _ in first })
        notesEnd = playback.end
    }

    /// The span of the bars `first` to `last`, to the notes' end after the last bar; nil for a bar the notes lack.
    private func barSpan(_ first: Int, _ last: Int) -> Range<Double>? {
        guard let onset = barStarts[first] else { return nil }
        return onset..<max(barStarts[last + 1] ?? notesEnd, onset + 1)
    }

    /// What this iPad kept of the working notes' scan and its crops, read from the device off the main thread (v1.3 i-2).
    private func loadScanReview() async {
        guard scanJobs() else {
            (scanReview, scanCrops) = (nil, [:])
            return
        }
        let fingerprint = check.fingerprint
        let kept = await Task.detached(priority: .utility) { () -> (ScanReview?, [Int: Data]) in
            guard let fingerprint, let review = ScanReviewStore.load(fingerprint) else { return (nil, [:]) }
            var crops: [Int: Data] = [:]
            for bar in review.bars where bar.cropped { crops[bar.bar] = ScanReviewStore.crop(fingerprint, bar: bar.bar) }
            return (review, crops)
        }.value
        scanReview = kept.0 ?? ScanReview(fingerprint: fingerprint ?? "", bars: [])
        scanCrops = kept.1.compactMapValues { UIImage(data: $0) }
    }

    /// The open bars as amber flags on the score, each labelled with its reasons, as the page draws them, and each mark
    /// «что не так» at its first bar the same way, in music-core's words (v1.6 c-14b3, E-scan-12).
    func flags(_ text: UIText) -> [[String: Any]] {
        let open: [[String: Any]] = check.openBars.compactMap { bar in
            guard let span = bar.span else { return nil }
            return ["bar": bar.bar, "tick": span.lowerBound, "label": text.format("check.flagLabel", String(bar.bar), CheckShelf.reasonNames(bar.flags, text))]
        }
        return Self.adding(check.marks.compactMap { mark in barStarts[mark.bar].map { (bar: mark.bar, tick: $0, label: mark.text) } }, to: open)
    }

    /// `flags` with a flag at each bar of `marks`: its own, or its words added to the label of the flag that bar has
    /// already, so two never lie on one another (E-scan-1, E-scan-12). A tap opens Check, which lists both.
    nonisolated static func adding(_ marks: [(bar: Int, tick: Double, label: String)], to flags: [[String: Any]]) -> [[String: Any]] {
        var all = flags
        for mark in marks {
            if let index = all.firstIndex(where: { $0["bar"] as? Int == mark.bar }) {
                all[index]["label"] = "\(all[index]["label"] as? String ?? "") · \(mark.label)"
            } else {
                all.append(["bar": mark.bar, "tick": mark.tick, "label": mark.label])
            }
        }
        return all
    }

    // MARK: - Check

    /// Show: the bar is selected, so it can be looped and compared with the page or the recording, and comes into view.
    func showCheckBar(_ bar: Int) {
        // A bar of a flag, of notes whose length is uncertain (#352), or of a mark «что не так» (v1.6 c-14b3).
        guard let span = check.flags.first(where: { $0.bar == bar && $0.span != nil })?.span
            ?? check.lengths.first(where: { $0.bar == bar })?.span
            ?? check.marks.first(where: { $0.bar == bar }).flatMap({ barSpan($0.bar, $0.last) }) else { return }
        score?.showSpan(span)
    }

    /// Looks right, or Check again: every flag on the bar resolved or open again, kept with the piece.
    func resolveCheck(_ bar: Int, _ resolved: Bool) {
        changeCheck(.checkResolve, ["bar": bar, "resolved": resolved])
    }

    /// Looks right for a whole piece of the pieces layer (version 0.5, Check by piece): every flagged bar from `first` to
    /// `last` resolved, in one change music-core makes, kept with the piece.
    func resolveCheckPiece(first: Int, last: Int) {
        changeCheck(.checkResolve, ["bars": ["first": first, "last": last], "resolved": true])
    }

    // MARK: - Marks «что не так» (v1.6 c-14b3, the 1.6 plan's decision 15)

    /// «Что не так?»: what the page prints over the bars `first` to `last` (a meter or key at `first`), kept with the working
    /// notes by music-core (`check.mark`) for their next reading, and flagged on the score; `told` says so over the pill,
    /// for a mark made from the selection menu (E-scan-15).
    func mark(_ choice: ReadingMarkChoice, first: Int, last: Int, told: Bool = false) {
        changeCheck(.checkMark, choice.fields(first: first, last: last)) { [weak self] in
            if told { self?.score?.showNotice("whatsWrong.markedNotice") }
        }
    }

    /// «Убрать» on a mark: music-core takes it away (`check.unmark`), and its flag goes.
    func unmark(_ mark: ReadingMarkEntry) {
        changeCheck(.checkUnmark, mark.unmarkFields)
    }

    /// «Показать» on a mark: its bars are selected and come into view.
    func showMark(_ mark: ReadingMarkEntry) {
        guard let span = barSpan(mark.bar, mark.last) else { return }
        score?.showSpan(span)
    }

    /// A call that changes what Check asks about and answers as `check` does (`check.resolve`, `check.mark`,
    /// `check.unmark`): Check as it answers, the flags drawn again, then `then`; music-core's words when it refuses.
    private func changeCheck(_ call: PieceCore.Call, _ fields: [String: Any], then: (@MainActor () -> Void)? = nil) {
        guard let piece = score?.pieceId else { return }
        let request = facadeRequest(fields)
        Task {
            let answer = await MusicCore.run { PieceCore(piece: piece).send(call, request) }
            if let shelf = CheckShelf(answer: answer) {
                // The bars first, so a mark shows with its flag.
                await readBars(of: piece, marks: shelf.marks)
                check = shelf
                await score?.drawChecks()
                then?()
            } else {
                score?.showProblem(FacadeAnswer.message(answer))
            }
        }
    }

    // MARK: - Fix notes

    /// Fix notes on a bar: it is selected and comes into view, and its notes are listed to correct; again, the list closes.
    func fixCheckBar(_ bar: Int) {
        guard corrections?.bar != bar else {
            corrections = nil
            return
        }
        guard editsOn else {
            score?.showNotice("padEdits.offNotice")
            return
        }
        showCheckBar(bar)
        correct(.bar, ["bar": bar])
    }

    /// A note of the bar a step higher or lower (semitones), earlier or later, shorter or longer (sixteenths), softer or
    /// louder: a correction in the piece's edit log, and the score engraved again.
    func correctNote(_ id: String, pitch: Int = 0, onset: Int = 0, length: Int = 0, velocity: Int = 0) {
        correct(.change, ["id": id, "pitch": pitch, "onset": onset, "length": length, "velocity": velocity], notes: true)
    }

    /// Takes the note out of the piece.
    func removeNote(_ id: String) { correct(.remove, ["id": id], notes: true) }

    /// Puts the note back as it was imported.
    func revertNote(_ id: String) { correct(.revert, ["id": id], notes: true) }

    /// Takes back the last correction.
    func undoCorrection() { correct(.undo, [:], notes: true) }

    /// Another engine's reading of a scan's bar to check chosen (v1.3 i-2, 0035 section 6): music-core puts it in that bar's
    /// staves to check only, as the player's corrections (`corrections.variant`); the bar's notes are listed to correct, the
    /// score engraved again and saved. A tap while one is being put in does nothing.
    func chooseVariant(_ variant: ScanVariant, bar: Int) {
        guard !puttingVariant, let piece = score?.pieceId else { return }
        puttingVariant = true
        let body = facadeRequest(variant.fields(bar: bar))
        Task {
            let answer = await PieceCore(piece: piece).variant(body)
            puttingVariant = false
            guard let read = answer.value else {
                score?.showProblem(FacadeAnswer.problem(answer.text))
                return
            }
            variantChoice.chose(bar: bar, engines: variant.engines, notes: check.fingerprint)
            corrections = NoteCorrections(read)
            readEdits(read)
            await score?.notesDidChange()
        }
    }

    /// The variant chosen last taken back whole (`corrections.undoDraft`), while nothing came after it.
    func undoVariant() {
        guard let bar = variantChoice.bar else { return }
        correct(.undoDraft, ["bar": bar], notes: true)
    }

    /// Accepts a draft of the assistant (#582 item 17.3) by its request (`AssistantDraftOffer.acceptRequest`): music-core
    /// makes it again from its call over the notes as they are now and adds its edits as corrections
    /// (`corrections.acceptDraft`), each undone like any other, and the score is engraved again. Nil when it is accepted,
    /// else why not, in music-core's words (empty when it gave none).
    func acceptAssistantDraft(_ request: String?) async -> String? {
        guard editsOn else { return text["edits.off"] }
        guard let piece = score?.pieceId, let request else { return "" }
        variantChoice.edited()
        let answer = await PieceCore(piece: piece).corrections(.acceptDraft, request)
        guard let read = answer.value else { return FacadeAnswer.problem(answer.text) }
        readEdits(read)
        await score?.notesDidChange()
        score?.showNotice("padAssistant.accepted")
        return nil
    }

    /// Undo on the assistant's accepted draft, from its first bar: the whole draft goes at once (`corrections.undoDraft`), as
    /// on the web. Nil when it is undone, else why not, in music-core's words.
    func undoAssistantDraft(firstBar: Int?) async -> String? {
        guard editsOn else { return text["edits.off"] }
        guard let piece = score?.pieceId else { return "" }
        variantChoice.edited()
        let answer = await PieceCore(piece: piece).corrections(.undoDraft, facadeRequest(EditsLayerState.correctionsRequest([:], bar: firstBar)))
        guard let read = answer.value else { return FacadeAnswer.problem(answer.text) }
        readEdits(read)
        await score?.notesDidChange()
        score?.showNotice("padAssistant.undone")
        return nil
    }

    /// A correction call; one that changes the notes (`notes`) ends what a variant's Undo may take back.
    private func correct(_ call: PieceCore.Correction, _ fields: [String: Any], notes: Bool = false) {
        guard let piece = score?.pieceId, let bar = fields["bar"] as? Int ?? corrections?.bar else { return }
        if notes { variantChoice.edited() }
        var request = fields
        request["bar"] = bar
        let body = facadeRequest(request)
        Task {
            let answer = await PieceCore(piece: piece).corrections(call, body)
            guard let read = answer.value else {
                score?.showProblem(FacadeAnswer.problem(answer.text))
                return
            }
            corrections = NoteCorrections(read)
            readEdits(read)
            if notes { await score?.notesDidChange() }
        }
    }

    // MARK: - My corrections (version 0.5, «Мои правки: вкл / выкл»)

    /// Whether notes can be corrected now: not while «Мои правки» is off, when music-core refuses every correction.
    var editsOn: Bool { editsLayer?.on ?? true }

    /// «Мои правки» switched: music-core lays the corrections over the notes one by one, or leaves them out, and keeps that
    /// in the piece's file (`corrections.layer`); the score is engraved again from the notes as they are now. Off, the
    /// bar listed to correct in Check closes, as nothing can be corrected then.
    func switchEdits(on: Bool) {
        let bar = on ? corrections?.bar : nil
        if !on { corrections = nil }
        editsCall(.layer, EditsLayerState.layerRequest(on: on, bar: bar))
    }

    /// «Убрать» on a note whose corrections no longer fit: they leave the piece's edit log (`corrections.drop`).
    func dropUnfitEdits(_ noteIds: [String]) {
        editsCall(.drop, EditsLayerState.dropRequest(noteIds, bar: corrections?.bar))
    }

    private func editsCall(_ call: PieceCore.Correction, _ fields: [String: Any]) {
        guard let piece = score?.pieceId else { return }
        variantChoice.edited()
        let body = facadeRequest(fields)
        Task {
            let answer = await PieceCore(piece: piece).corrections(call, body)
            guard let read = answer.value else {
                score?.showProblem(FacadeAnswer.problem(answer.text))
                return
            }
            editsLayer = EditsLayerState(read.edits)
            if corrections != nil, read.bar == corrections?.bar { corrections = NoteCorrections(read) }
            await score?.notesDidChange()
            editsKept?()
        }
    }

    /// Every corrections answer says how many corrections there are and, with a layer, which no longer fit.
    private func readEdits(_ answer: CorrectionsAnswer) {
        editsLayer = EditsLayerState(answer.edits)
    }

    // MARK: - Note editor on the roll

    /// Edit notes in the roll's drawer (testing item 15): the editor opens on a draft in music-core, so every edit sounds
    /// and shows at once but the project file keeps the notes as they were until Save; Cancel puts them back.
    func beginNoteEdit() {
        guard editsOn else {
            score?.showNotice("padEdits.offNotice")
            return
        }
        noteEditor = NoteEditorState()
        noteEditCall(.begin, [:])
    }

    /// Move, Add or Remove: what a press on the roll's notes does.
    func chooseNoteEditTool(_ tool: String) {
        noteEditor?.tool = tool
    }

    /// A note moved, added or taken out on the roll.
    func editNote(_ edit: RollNoteEdit) {
        guard noteEditor != nil else { return }
        switch edit {
        case let .change(id, onset, pitch, length):
            noteEditor?.picked = id
            noteEditCall(.set, ["id": id, "onset": onset, "pitch": pitch, "length": length], notes: true)
        case let .add(onset, pitch, length):
            noteEditCall(.add, ["onset": onset, "pitch": pitch, "length": length], notes: true)
        case let .remove(id):
            noteEditor?.picked = nil
            noteEditCall(.remove, ["id": id], notes: true)
        }
    }

    /// Takes back the last edit, the reset too.
    func undoNoteEdit() { noteEditCall(.undo, [:], notes: true) }

    /// Every correction taken out: the notes as imported, until Save.
    func resetNotes() { noteEditCall(.reset, [:], notes: true) }

    /// Closes the editor with the notes as they were when it opened.
    func cancelNoteEdit() {
        guard noteEditor != nil else { return }
        let changed = noteEditor?.changed ?? false
        noteEditor = nil
        noteEditCall(.discard, [:], notes: changed)
    }

    /// Keeps the edits with the piece and writes the project file.
    func saveNoteEdit() {
        guard noteEditor != nil else { return }
        noteEditor = nil
        noteEditCall(.save, [:]) { [weak self] in self?.editsKept?() }
    }

    private func noteEditCall(_ call: PieceCore.Correction, _ fields: [String: Any], notes: Bool = false, then: (@MainActor () -> Void)? = nil) {
        guard let piece = score?.pieceId else { return }
        if notes { variantChoice.edited() }
        // The corrections answer with a bar's notes; the editor needs only what the answer says about the draft.
        let body = facadeRequest(EditsLayerState.correctionsRequest(fields, bar: selection.selection?.firstBar))
        Task {
            let answer = await PieceCore(piece: piece).corrections(call, body)
            guard let read = answer.value else {
                score?.showProblem(FacadeAnswer.problem(answer.text))
                return
            }
            if noteEditor != nil {
                noteEditor?.canUndo = read.canUndo
                noteEditor?.changed = read.changed
                noteEditor?.canReset = read.edits.count > 0
                if let added = read.added { noteEditor?.picked = added }
            }
            if corrections != nil, read.bar == corrections?.bar { corrections = NoteCorrections(read) }
            readEdits(read)
            if notes { await score?.notesDidChange() }
            then?()
        }
    }
}
