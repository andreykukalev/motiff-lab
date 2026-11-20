import SwiftUI

/// What the «В черновик» sheet chooses for a fragment (v1.1, ADR 0034 section 3; spec «Экран (iPad)»): what is taken (all,
/// a hand, or the melody where music-core offers it), the draft it goes into (one of the piece's, or a new one) and how:
/// as a variant of a place (where it is the same music offered first, with how much), as a new place at the end, or on
/// the shelf. A new draft takes it as its first place. Nothing is merged by itself: «Добавить» sends exactly this.
struct DraftChoice: Equatable, Sendable {
    enum Into: Equatable, Sendable {
        case new
        case draft(String)
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

    /// The way offered first among `ways`: a variant of the place where the fragment is the same music, else a new place
    /// at the end.
    static func firstWay(_ ways: [Way]) -> Way {
        ways.first { $0.percent != nil } ?? Way(drop: .place)
    }

    /// What the sheet offers first for `part`: `into` when given (the player's draft, or a new one) and still there, else the
    /// draft where the fragment is the same music, else the piece's latest draft, else a new one; and that draft's
    /// `firstWay`.
    static func first(_ sheet: DraftSheetAnswer, part: String, into given: Into? = nil) -> DraftChoice {
        let chosen = given.flatMap { into -> Into? in
            guard case .draft(let id) = into else { return into }
            return sheet.drafts.contains { $0.id == id } ? into : nil
        }
        let into = chosen ?? (sheet.suggested?.draft ?? sheet.drafts.last?.id).map(Into.draft) ?? .new
        return DraftChoice(part: part, into: into, way: firstWay(ways(draft(into, in: sheet))))
    }

    /// The sheet's draft for `into`; nil for a new one.
    static func draft(_ into: Into, in sheet: DraftSheetAnswer) -> DraftSheetAnswer.Draft? {
        guard case .draft(let id) = into else { return nil }
        return sheet.drafts.first { $0.id == id }
    }

    /// The fields of `drafts.add` for the fragment `span` (in the ticks of the notes it is selected in) at `now`.
    func fields(span: Range<Double>, now: String) -> [String: Any] {
        var fields: [String: Any] = ["span": ["onset": Int(span.lowerBound), "end": Int(span.upperBound)], "part": part, "drop": way.drop.rawValue, "now": now]
        if case .draft(let id) = into { fields["draft"] = id }
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

/// The «В черновик» sheet: «Что взять», «Куда», «Как» and «Добавить» (spec «Экран (iPad)»), each choice a row that
/// shows itself chosen with the accent's tint and a check, as the switch's menu does. What it offers is music-core's
/// (`drafts.sheet`, asked again when another part is chosen, as the same music depends on it); `sources` is the piece
/// whose drafts they are, which keeps the answer and the project file.
struct DraftSheet: View {
    let request: DraftSheetRequest
    @ObservedObject var sources: SourcesController
    let text: UIText
    /// Once added: the sheet goes, and the screen says so.
    let added: () -> Void
    let cancel: () -> Void
    @State private var sheet: DraftSheetAnswer?
    @State private var choice: DraftChoice?
    @State private var problem: String?
    @State private var adding = false

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
        .task { await load(part: nil) }
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
        section("drafts.how")
        ForEach(Array(DraftChoice.ways(DraftChoice.draft(choice.into, in: sheet)).enumerated()), id: \.offset) { _, way in
            row(title(way, newDraft: choice.into == .new), caption(way), on: way == choice.way) { self.choice?.way = way }
        }
    }

    private var buttons: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(text["common.cancel"], action: cancel).buttonStyle(DSButtonStyle(kind: .quiet))
            Spacer(minLength: 0)
            Button(text["drafts.addButton"]) { Task { await add() } }
                .buttonStyle(DSButtonStyle(kind: .primary))
                .disabled(choice == nil || adding)
        }
        .padding(.top, CGFloat.ds("s-2"))
    }

    private func title(_ way: DraftChoice.Way, newDraft: Bool) -> String {
        switch way.drop {
        case .variant: text["drafts.asVariant"]
        case .place: text[newDraft ? "drafts.asFirst" : "drafts.asPlace"]
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

    private func add() async {
        guard let choice else { return }
        adding = true
        defer { adding = false }
        if await sources.addToDraft(choice, span: request.span, from: request.piece) {
            added()
        } else {
            problem = sources.status
        }
    }
}
