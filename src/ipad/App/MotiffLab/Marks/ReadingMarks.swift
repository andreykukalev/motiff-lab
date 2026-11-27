import SwiftUI

/// A mark «что не так» on notes read from a PDF or photos (v1.6 c-14b3, the 1.6 plan's decision 15, «Как напечатано» step
/// 3), as `check` answers "marks": what the player says the page prints where the reading went wrong, kept with the notes
/// by music-core and used by the next reading of its pages. Its words ("text") are music-core's: «Руки перепутаны, такты
/// 3–4». It changes no note until the pages are read again.
struct ReadingMarkEntry: Identifiable, Equatable, Sendable {
    /// "meter", "key", "clef", "octave", "triplets", "hands" or "badlyRead".
    var kind: String
    var bar: Int
    /// The last bar; a meter or key mark's is its first, as it holds from there on.
    var last: Int
    /// A clef or octave mark's staff, from 1 (the upper); nil for the others.
    var staff: Int? = nil
    var value: String? = nil
    var text: String
    /// One mark of a kind at a bar and staff: music-core keeps one there.
    var id: String { "\(kind)-\(bar)-\(staff ?? 0)" }

    /// The entries of "marks", in bar order as music-core gives them; one without its kind or bars is left out.
    static func all(_ value: Any?) -> [ReadingMarkEntry] {
        (value as? [[String: Any]] ?? []).compactMap { entry in
            guard let kind = entry["kind"] as? String, let bar = (entry["bar"] as? NSNumber)?.intValue else { return nil }
            return ReadingMarkEntry(
                kind: kind, bar: bar, last: (entry["last"] as? NSNumber)?.intValue ?? bar, staff: (entry["staff"] as? NSNumber)?.intValue,
                value: entry["value"] as? String, text: entry["text"] as? String ?? ""
            )
        }
    }

    /// Whether any of its bars is one of `bars`.
    func lies(on bars: ClosedRange<Int>) -> Bool { bar <= bars.upperBound && last >= bars.lowerBound }

    /// `check.unmark`'s fields: the mark by its kind, its first bar and its staff.
    var unmarkFields: [String: Any] {
        var fields: [String: Any] = ["kind": kind, "bar": bar]
        if let staff { fields["staff"] = staff }
        return fields
    }
}

/// What «Что не так?» offers to mark (`check.mark`): a kind, the staff of a clef or an octave sign, and what the page prints.
/// Only the values music-core takes, each said in the interface's language.
struct ReadingMarkChoice: Hashable, Sendable {
    var kind: String
    var staff: Int? = nil
    var value: String? = nil

    /// The kinds in the order the menu offers them.
    static let kinds = ["meter", "key", "clef", "octave", "triplets", "hands", "badlyRead"]
    /// The time signatures offered: the common ones first, then the odd ones.
    static let meters = ["2/4", "3/4", "4/4", "5/4", "6/4", "2/2", "3/8", "6/8", "9/8", "12/8", "5/8", "7/8"]
    /// A key's sharps (above 0) or flats (below 0): none first, then the sharps, then the flats.
    static let fifths = [0, 1, 2, 3, 4, 5, 6, 7, -1, -2, -3, -4, -5, -6, -7]

    /// The marks of `kind` the menu offers: one for hands swapped and a bar read badly, which say nothing more; a clef or
    /// an octave sign on the upper or the lower staff (E-scan-14: always both, as a piano score has).
    static func choices(_ kind: String) -> [ReadingMarkChoice] {
        switch kind {
        case "meter": meters.map { ReadingMarkChoice(kind: kind, value: $0) }
        case "key": fifths.map { ReadingMarkChoice(kind: kind, value: String($0)) }
        case "clef": onStaves(kind, ["treble", "bass"])
        case "octave": onStaves(kind, ["8va", "8vb"])
        case "triplets": ["yes", "no"].map { ReadingMarkChoice(kind: kind, value: $0) }
        default: [ReadingMarkChoice(kind: kind)]
        }
    }

    private static func onStaves(_ kind: String, _ values: [String]) -> [ReadingMarkChoice] {
        [1, 2].flatMap { staff in values.map { ReadingMarkChoice(kind: kind, staff: staff, value: $0) } }
    }

    /// A meter or a key holds from its bar on, until the next of its kind, so its mark has one bar.
    var fromBarOn: Bool { kind == "meter" || kind == "key" }

    /// `check.mark`'s fields over the bars `first` to `last`: a meter or key at `first` alone (E-scan-13).
    func fields(first: Int, last: Int) -> [String: Any] {
        var fields: [String: Any] = ["kind": kind, "bar": first]
        if !fromBarOn, last > first { fields["last"] = last }
        if let staff { fields["staff"] = staff }
        if let value { fields["value"] = value }
        return fields
    }

    /// The menu item's words: "5/4", "2 sharps", "Bass clef, lower staff", "Triplets here", "Hands swapped".
    func label(_ text: UIText) -> String {
        let staffName = text[staff == 2 ? "whatsWrong.staff.lower" : "whatsWrong.staff.upper"]
        switch kind {
        case "meter": return value ?? ""
        case "key":
            let number = Int(value ?? "") ?? 0
            if number == 0 { return text["whatsWrong.value.key.none"] }
            return number > 0 ? text.count("whatsWrong.value.key.sharps", number) : text.count("whatsWrong.value.key.flats", -number)
        case "clef": return text.format("whatsWrong.onStaff", text["whatsWrong.value.clef.\(value ?? "")"], staffName)
        case "octave": return text.format("whatsWrong.onStaff", text["whatsWrong.value.octave.\(value ?? "")"], staffName)
        case "triplets": return text["whatsWrong.value.triplets.\(value ?? "")"]
        default: return text["whatsWrong.kind.\(kind)"]
        }
    }
}

/// «Что не так?» (v1.6 c-14b3) on the bars `bars`: an ordinary native menu of what can be marked, a kind's values in its own
/// list, and the marks already on those bars to take away. A mark is kept at once (`CheckModel.mark`); from the selection
/// menu (`told`) a word over the pill says so, as nothing else on the menu shows it (E-scan-15).
struct WhatsWrongMenu: View {
    let checkModel: CheckModel
    let bars: ClosedRange<Int>
    let text: UIText
    var told = false

    var body: some View {
        let marked = checkModel.check.marks.filter { $0.lies(on: bars) }
        Menu {
            Section(bars.count == 1 ? text.format("check.bar", String(bars.lowerBound)) : text.format("whatsWrong.headingBars", String(bars.lowerBound), String(bars.upperBound))) {
                ForEach(ReadingMarkChoice.kinds, id: \.self) { kind in
                    let choices = ReadingMarkChoice.choices(kind)
                    if choices.count == 1 {
                        Button(text["whatsWrong.kind.\(kind)"]) { mark(choices[0]) }
                    } else {
                        Menu(text["whatsWrong.kind.\(kind)"]) {
                            ForEach(choices, id: \.self) { choice in
                                Button(choice.label(text)) { mark(choice) }
                            }
                        }
                    }
                }
            }
            if !marked.isEmpty {
                Section(text["whatsWrong.removeHeading"]) {
                    ForEach(marked) { entry in
                        Button(role: .destructive) { checkModel.unmark(entry) } label: { Text(entry.text) }
                    }
                }
            }
        } label: {
            Text(text["whatsWrong.menu"])
        }
        .menuStyle(.button)
        .accessibilityHint(text["whatsWrong.hint"])
    }

    private func mark(_ choice: ReadingMarkChoice) {
        checkModel.mark(choice, first: bars.lowerBound, last: bars.upperBound, told: told)
    }
}

/// The amber «?» of a bar Check asks about, as the page draws its flag (ds.css `.check-flag`): beside the bar in the
/// drawer, and beside each mark «что не так», which the page flags the same way.
struct CheckFlagSign: View {
    var body: some View {
        Text("?")
            .font(DesignTokens.font(weight: 700, size: 11))
            .foregroundStyle(Color.ds("notation"))
            .frame(width: 18, height: 18)
            .background(Color.ds("warning-fill"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
            .accessibilityHidden(true)
    }
}

/// The marks «что не так» in Check (v1.6 c-14b3): each in music-core's words with «Показать» and «Убрать», then what reads
/// the marked pages again (`reading`, «Распознать заново с отметками», built by Workspace). On notes only shown
/// (`marking` false) the marks are listed without what changes them.
struct ReadingMarksSection<Reading: View>: View {
    @ObservedObject var checkModel: CheckModel
    let marking: Bool
    let text: UIText
    let reading: Reading

    var body: some View {
        let marks = checkModel.check.marks
        if !marks.isEmpty {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(text["whatsWrong.title"]).overline().padding(.top, CGFloat.ds("s-2"))
                Text(text["whatsWrong.listHint"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
                ForEach(marks) { mark in row(mark) }
                if marking { reading }
            }
            .accessibilityElement(children: .contain)
        }
    }

    private func row(_ mark: ReadingMarkEntry) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
            CheckFlagSign()
            Text(mark.text)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            Button(text["scanCheck.show"]) { checkModel.showMark(mark) }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            if marking {
                Button(text["notes.remove"]) { checkModel.unmark(mark) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    .accessibilityHint(text["whatsWrong.removeHint"])
            }
        }
    }
}
