import SwiftUI

/// What the «В черновик» sheet chooses for a fragment (v1.1, ADR 0034 section 3; spec «Экран (iPad)»): what is taken (all,
/// a hand, or the melody where music-core offers it), the draft it goes into (one of the piece's, or a new one) and how:
/// as a variant of a place (where it is the same music offered first, with how much), as a new place at the end, or on
/// the shelf. A new draft takes it as its first place. Since v1.2 (collage, the plan's answer 5) it can go into a draft of
/// another piece, at the end or on the shelf. Nothing is merged by itself: «Добавить» sends exactly this.
struct DraftChoice: Equatable, Sendable {
    enum Into: Equatable, Sendable {
        case new
        case draft(String)
        case elsewhere(Elsewhere)

        /// The other piece's draft, for one there.
        var elsewhere: Elsewhere? {
            if case .elsewhere(let other) = self { return other }
            return nil
        }
    }

    /// A draft of another piece (`OtherPieceDrafts`): its project file, its id and name, and the piece's title.
    struct Elsewhere: Equatable, Sendable {
        let file: URL
        let draft: String
        let name: String
        let piece: String
    }

    enum Drop: String, Sendable {
        case variant, place, shelf
    }

    /// One way a fragment goes into a draft: a variant of `place` (its label, and how much is the same music where it is),
    /// a new place at the end, or the shelf.
    struct Way: Equatable, Sendable {
        var drop: Drop
        var place: String?
        var label: String?
        var percent: Int?
    }

    /// "all", "right", "left" or "melody", among what the sheet offers.
    var part: String
    var into: Into
    var way: Way

    /// The ways `draft` offers: a variant of each of its places, first those where the fragment is the same music with how
    /// much (the place music-core suggests first of all), then the others in order, then a new place at the end, then the
    /// shelf; a new draft (nil) only its first place.
    static func ways(_ draft: DraftSheetAnswer.Draft?) -> [Way] {
        guard let draft else { return [Way(drop: .place)] }
        let same = draft.places.filter(\.sameMusic)
        let ordered = same.filter { $0.id == draft.suggested } + same.filter { $0.id != draft.suggested }
        let variants = ordered.map { Way(drop: .variant, place: $0.id, label: $0.label, percent: $0.percent) }
            + draft.places.filter { !$0.sameMusic }.map { Way(drop: .variant, place: $0.id, label: $0.label) }
        return variants + [Way(drop: .place), Way(drop: .shelf)]
    }

    /// The ways a fragment goes `into` that draft: as `ways(_:)` for one of this piece's, and into another piece's draft
    /// only a new place at the end or the shelf (the plan's answer 5).
    static func ways(_ into: Into, in sheet: DraftSheetAnswer) -> [Way] {
        into.elsewhere == nil ? ways(draft(into, in: sheet)) : [Way(drop: .place), Way(drop: .shelf)]
    }

    /// The way offered first among `ways`: a variant of the place where the fragment is the same music, else a new place
    /// at the end.
    static func firstWay(_ ways: [Way]) -> Way {
        ways.first { $0.percent != nil } ?? Way(drop: .place)
    }

    /// What the sheet offers first for `part`: `into` when given (the player's draft, another piece's, or a new one) and still
    /// there, else the draft where the fragment is the same music, else the piece's latest draft, else a new one; and that
    /// draft's `firstWay`.
    static func first(_ sheet: DraftSheetAnswer, part: String, into given: Into? = nil) -> DraftChoice {
        let chosen = given.flatMap { into -> Into? in
            guard case .draft(let id) = into else { return into }
            return sheet.drafts.contains { $0.id == id } ? into : nil
        }
        let into = chosen ?? (sheet.suggested?.draft ?? sheet.drafts.last?.id).map(Into.draft) ?? .new
        return DraftChoice(part: part, into: into, way: firstWay(ways(into, in: sheet)))
    }

    /// The sheet's draft for `into`; nil for a new one.
    static func draft(_ into: Into, in sheet: DraftSheetAnswer) -> DraftSheetAnswer.Draft? {
        guard case .draft(let id) = into else { return nil }
        return sheet.drafts.first { $0.id == id }
    }

    /// The fields of `drafts.add` for the fragment `span` (in the ticks of the notes it is selected in) at `now`; for another
    /// piece's draft, its project file goes with them as "file" (`SourcesController.addToDraft`).
    func fields(span: Range<Double>, now: String) -> [String: Any] {
        var fields: [String: Any] = ["span": ["onset": Int(span.lowerBound), "end": Int(span.upperBound)], "part": part, "drop": way.drop.rawValue, "now": now]
        if case .draft(let id) = into { fields["draft"] = id }
        if let other = into.elsewhere { fields["draft"] = other.draft }
        if let place = way.place { fields["place"] = place }
        return fields
    }

    /// What a part is called on the sheet.
    static func word(part: String, _ text: UIText) -> String {
        switch part {
        case "right": text["drafts.partRight"]
        case "left": text["drafts.partLeft"]
        case "melody": text["drafts.partMelody"]
        default: text["drafts.partAll"]
        }
    }
}

/// The «В черновик» sheet asked for from the selection menu's «Черновик», or from Sources' «Новый черновик» with notes
/// selected (`into` a new draft): the notes shown on `piece` and the span selected there.
struct DraftSheetRequest: Identifiable {
    let id = UUID()
    let piece: Int32
    let span: Range<Double>
    var into: DraftChoice.Into?
}

/// The «В черновик» sheet: «Что взять», «Куда», «Как» and «Добавить» (spec «Экран (iPad)»), for a new draft its card
/// «Новый черновик» and «Создать» (v1.4, F4), each choice a row that
/// shows itself chosen with the accent's tint and a check, as the switch's menu does. What it offers is music-core's
/// (`drafts.sheet`, asked again when another part is chosen, as the same music depends on it); `sources` is the piece
/// whose drafts they are, which keeps the answer and the project file. Under «Куда», after this piece's, the drafts of the
/// other pieces in the folder, grouped by each piece's title (v1.2, collage, `OtherPieceDrafts`), read once the sheet shows.
/// Since v1.5 (d-11, DRAFT-11) the card «Новый черновик» first searches the piece's recordings for the passage and, where
/// it is found, also offers «С ассистентом · Pro» (`assist`), in the words the screen hands it (`assistLabel`, the
/// draft's map's own).
struct DraftSheet<AssistLabel: View>: View {
    let request: DraftSheetRequest
    @ObservedObject var sources: SourcesController
    let text: UIText
    /// Once added, where it went: the sheet goes, and the screen says so.
    let added: (DraftChoice) -> Void
    /// Once a new draft is made with the stretch found on a recording (v1.5 d-11), its id: the sheet goes, and the screen
    /// opens the draft's view with its map and the assistant on ①. Nil where the screen cannot open a draft's view (one
    /// shown over the piece, which has no switch): no «С ассистентом · Pro» there (E-drafts-2).
    let assist: ((String) -> Void)?
    let assistLabel: AssistLabel
    let cancel: () -> Void
    @State private var sheet: DraftSheetAnswer?
    @State private var others: [OtherPieceDrafts] = []
    @State private var choice: DraftChoice?
    @State private var problem: String?
    @State private var adding = false
    /// What the card «Новый черновик» found on the recordings for the passage (v1.5 d-11).
    @State private var recording: DraftRecordingOffer = .ready

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                Text(text["drafts.sheetTitle"]).dsType(.heading).foregroundStyle(Color.ds("ink")).accessibilityAddTraits(.isHeader)
                if let sheet, let choice {
                    Text(text.count("drafts.notes", sheet.notes)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    choices(sheet, choice)
                } else if problem == nil {
                    ProgressView().frame(maxWidth: .infinity)
                }
                if let problem { SourcesCaption(words: problem, colour: "danger") }
                buttons
            }
            .padding(CGFloat.ds("s-6"))
        }
        .background(Color.ds("surface"))
        .presentationDetents([.medium, .large])
        .task {
            await load(part: nil)
            others = await OtherPieceDrafts.list(except: sources.file, project: sources.projectId, language: text.language)
        }
        // Searched again for another part, the search before dropped; none for a draft that is there.
        .task(id: newDraftPart) { await search(newDraftPart) }
    }

    /// The part of the passage a new draft would take, which its card searches on the recordings; nil while the sheet is
    /// for a draft that is there, or not read yet.
    private var newDraftPart: String? {
        choice?.into == .new ? choice?.part : nil
    }

    @ViewBuilder private func choices(_ sheet: DraftSheetAnswer, _ choice: DraftChoice) -> some View {
        section("drafts.what")
        ForEach(sheet.parts, id: \.self) { part in
            row(DraftChoice.word(part: part, text), part == "melody" ? text["drafts.melodyFound"] : nil, on: part == choice.part) {
                Task { await load(part: part) }
            }
        }
        section("drafts.where")
        ForEach(sheet.drafts, id: \.id) { draft in
            row(draft.name, nil, on: choice.into == .draft(draft.id)) { self.choice = DraftChoice.first(sheet, part: choice.part, into: .draft(draft.id)) }
        }
        row(text["drafts.new"], sheet.newName, on: choice.into == .new) { self.choice = DraftChoice.first(sheet, part: choice.part, into: .new) }
        ForEach(others) { piece in
            Text(piece.title).dsType(.caption).foregroundStyle(Color.ds("ink-2")).padding(.top, CGFloat.ds("s-2")).accessibilityAddTraits(.isHeader)
            ForEach(piece.drafts) { draft in
                let into = DraftChoice.Into.elsewhere(DraftChoice.Elsewhere(file: piece.file, draft: draft.id, name: draft.name, piece: piece.title))
                row(draft.name, SourcesDraftRow.caption(draft, text), on: choice.into == into) { self.choice = DraftChoice.first(sheet, part: choice.part, into: into) }
            }
        }
        if choice.into == .new {
            newDraftCard(sheet, choice)
        } else {
            ways(sheet, choice)
        }
    }

    @ViewBuilder private func ways(_ sheet: DraftSheetAnswer, _ choice: DraftChoice) -> some View {
        section("drafts.how")
        ForEach(Array(DraftChoice.ways(choice.into, in: sheet).enumerated()), id: \.offset) { _, way in
            row(title(way), caption(way), on: way == choice.way) { self.choice?.way = way }
        }
    }

    /// The card «Новый черновик» (v1.4, F4; the drafts spec «Ноты + запись»), in place of «Как»: the name it takes and its
    /// base (the notes selected, the part chosen); «Создать» (`buttons`) makes the draft with the fragment as its first
    /// place, the one way a new draft has (`DraftChoice.ways(nil)`). Since v1.5 (d-11) also where the passage is on the
    /// recordings, how much and how many приёмы, or that it searches (`DraftRecordingOffer.newDraftLine`).
    private func newDraftCard(_ sheet: DraftSheetAnswer, _ choice: DraftChoice) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["drafts.new"]).overline()
            Text(sheet.newName).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
            Text(text.format("drafts.newBase", DraftChoice.word(part: choice.part, text))).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            if let line = recording.newDraftLine(text) {
                Text(line).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    /// In one row where they fit; else (Split View, the Russian words) one under another at the trailing edge (v1.5 d-11).
    private var buttons: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: CGFloat.ds("s-2")) {
                cancelButton
                Spacer(minLength: 0)
                actions
            }
            VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
                actions
                cancelButton
            }
            .frame(maxWidth: .infinity, alignment: .trailing)
        }
        .padding(.top, CGFloat.ds("s-2"))
    }

    private var cancelButton: some View {
        Button(text["common.cancel"], action: cancel).buttonStyle(DSButtonStyle(kind: .quiet))
    }

    /// «С ассистентом · Pro», only once the passage is found on a recording (v1.5 d-11): Pro, said as on the draft's map,
    /// the assistant telling in the draft what the account allows; and «Создать» or «Добавить».
    @ViewBuilder private var actions: some View {
        if choice?.into == .new, assist != nil, let found = recording.adds {
            Button { Task { await create(with: found) } } label: { assistLabel }
                .buttonStyle(DSButtonStyle(kind: .secondary))
                .disabled(adding)
                .accessibilityHint(text["drafts.newWithAssistantHint"])
        }
        Button(text[choice?.into == .new ? "drafts.create" : "drafts.addButton"]) { Task { await add() } }
            .buttonStyle(DSButtonStyle(kind: .primary))
            .disabled(choice == nil || adding)
    }

    private func title(_ way: DraftChoice.Way) -> String {
        switch way.drop {
        case .variant: text["drafts.asVariant"]
        case .place: text["drafts.asPlace"]
        case .shelf: text["drafts.toShelf"]
        }
    }

    /// The place a variant goes to, with how much is the same music there when it is.
    private func caption(_ way: DraftChoice.Way) -> String? {
        guard let label = way.label else { return nil }
        return way.percent.map { text.format("drafts.sameMusic", label, String($0)) } ?? label
    }

    private func section(_ key: String) -> some View {
        Text(text[key])
            .dsType(.overline)
            .textCase(.uppercase)
            .foregroundStyle(Color.ds("ink-2"))
            .padding(.top, CGFloat.ds("s-2"))
            .accessibilityAddTraits(.isHeader)
    }

    /// A choice: its words and a line under them, tinted with a check when it is the one chosen.
    private func row(_ words: String, _ line: String?, on: Bool, pick: @escaping () -> Void) -> some View {
        Button(action: pick) {
            HStack(spacing: CGFloat.ds("s-3")) {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(words).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                    if let line { Text(line).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true) }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                if on { Icon(name: "check", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("accent")) }
            }
            .padding(.horizontal, CGFloat.ds("s-3"))
            .padding(.vertical, CGFloat.ds("s-2"))
            .frame(minHeight: CGFloat.ds("h-touch"))
            .background(on ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    /// The sheet as music-core answers it for `part` (its default, all, at first), the draft chosen kept.
    private func load(part: String?) async {
        var fields: [String: Any] = ["span": ["onset": Int(request.span.lowerBound), "end": Int(request.span.upperBound)]]
        if let part { fields["part"] = part }
        let answer = await PieceCore(piece: request.piece).draftSheet(facadeRequest(fields))
        guard let read = answer.value else {
            problem = answer.message ?? text["sources.sheet.failed"]
            return
        }
        problem = nil
        sheet = read
        choice = DraftChoice.first(read, part: read.part, into: choice?.into ?? request.into)
    }

    /// «Добавить» or «Создать»; a second tap while one is on its way does nothing (v1.5 d-11).
    private func add() async {
        guard let choice, !adding else { return }
        adding = true
        defer { adding = false }
        if await sources.addToDraft(choice, span: request.span, from: request.piece) {
            added(choice)
        } else {
            problem = sources.status
        }
    }

    /// «С ассистентом · Pro» (v1.5 d-11): the new draft made with the stretch `found` as a variant of ①, then the screen's
    /// `assist` with its id; refused, why, under the card.
    private func create(with found: DraftRecordingAnswer.Found) async {
        guard let choice, let assist, !adding else { return }
        adding = true
        defer { adding = false }
        if let draft = await sources.createWithRecording(choice, found: found, span: request.span, from: request.piece) {
            assist(draft)
        } else {
            problem = sources.status
        }
    }

    /// The card's search for `part` of the passage (nil: none, the card not shown); a search the sheet left (closed, or
    /// another part chosen) says nothing once it ends.
    private func search(_ part: String?) async {
        guard let part else {
            recording = .ready
            return
        }
        recording = sources.hasHeardNotes ? .searching : .noRecording
        guard recording == .searching else { return }
        let found = await sources.findNewOnRecording(span: request.span, part: part, from: request.piece)
        // E-drafts-3: the sheet closed or another part chosen meanwhile: this search's end says nothing.
        guard !Task.isCancelled else { return }
        recording = found
    }
}

/// A fragment just added to another piece's draft `draft`, each add with its own id, so its toast stays its while.
struct DraftAddedElsewhere: Identifiable, Equatable {
    let id = UUID()
    let draft: DraftChoice.Elsewhere
}

/// The toast of a fragment just added to another piece's draft (v1.2, collage, the plan's answer 5): «Добавлено в черновик
/// «…» · Открыть», where «Открыть» (`open`, the screen's way; none where it has none) opens that piece at that draft. It
/// goes by itself, as a Sources toast with an action does.
struct DraftElsewhereToast: View {
    @ObservedObject var owner: SourcesController
    let text: UIText
    let open: ((DraftChoice.Elsewhere) async -> Void)?

    var body: some View {
        if let added = owner.addedElsewhere {
            let opening: (() -> Void)? = open.map { open in { owner.addedElsewhere = nil; Task { await open(added.draft) } } }
            UndoToast(words: text.format("drafts.addedElsewhere", added.draft.name), text: text, undo: opening, close: { owner.addedElsewhere = nil }, actionKey: "drafts.openElsewhere")
                .task(id: added.id) {
                    try? await Task.sleep(for: SourcesToastView.lasting)
                    if owner.addedElsewhere == added { owner.addedElsewhere = nil }
                }
                .transition(.opacity)
        }
    }
}
