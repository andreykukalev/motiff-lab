import SwiftUI

/// A bar a scan or a transcription is unsure of, for one reason (facade `check`, 0018 section 9).
struct CheckFlagEntry: Equatable, Sendable {
    var bar: Int
    var reason: String
    /// The reason's short name in the piece's language: "Bar too long".
    var name: String
    /// What the reason means and what to look for, in the piece's language.
    var text: String
    var resolved: Bool
    /// The bar's span, or nil for a bar the piece does not reach.
    var span: Range<Double>?
}

/// The bars Check asks about, as music-core answers `check` and `check.resolve`: the flags in bar order, how many are open,
/// and where the doubts come from.
struct CheckShelf: Equatable, Sendable {
    /// A bar with its flags, as the drawer lists it.
    struct BarFlags: Identifiable, Equatable, Sendable {
        var bar: Int
        var flags: [CheckFlagEntry]
        var id: Int { bar }
        var span: Range<Double>? { flags.lazy.compactMap(\.span).first }
    }

    var flags: [CheckFlagEntry] = []
    var open = 0
    /// "scanned", "transcribed" or "imported".
    var origin = "imported"
    var engine: String?

    init() {}

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        flags = (object["flags"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let bar = number(entry["bar"]) else { return nil }
            let span = (entry["span"] as? [String: Any]).flatMap { span in
                number(span["onset"]).flatMap { onset in number(span["end"]).map { onset..<max($0, onset + 1) } }
            }
            return CheckFlagEntry(
                bar: Int(bar), reason: entry["reason"] as? String ?? "", name: entry["name"] as? String ?? "",
                text: entry["text"] as? String ?? "", resolved: entry["resolved"] as? Bool ?? false, span: span
            )
        }
        open = Int(number(object["open"]) ?? 0)
        origin = object["origin"] as? String ?? "imported"
        engine = object["engine"] as? String
    }

    var hasFlags: Bool { !flags.isEmpty }

    /// The bars still to check, each with its open flags, in bar order.
    var openBars: [BarFlags] { group(flags.filter { !$0.resolved }) }

    /// The bars checked, every flag on them, in bar order.
    var resolvedBars: [BarFlags] { group(flags.filter(\.resolved)) }

    private func group(_ flags: [CheckFlagEntry]) -> [BarFlags] {
        var bars: [BarFlags] = []
        for flag in flags {
            if bars.last?.bar == flag.bar { bars[bars.count - 1].flags.append(flag) } else { bars.append(BarFlags(bar: flag.bar, flags: [flag])) }
        }
        return bars
    }

    /// "Bar too long, grace notes": a bar's reasons, each once.
    static func reasonNames(_ flags: [CheckFlagEntry], _ text: UIText) -> String {
        var names: [String] = []
        for flag in flags where !names.contains(flag.name) { names.append(flag.name) }
        let locale = Locale(identifier: text.language)
        return names.enumerated().map { $0.offset == 0 ? $0.element : $0.element.lowercased(with: locale) }.joined(separator: ", ")
    }

    /// The flag's explanations, each reason once.
    static func explanations(_ flags: [CheckFlagEntry]) -> [String] {
        var seen: [String] = []
        for flag in flags where !seen.contains(flag.text) { seen.append(flag.text) }
        return seen
    }
}

/// The Check drawer (0018 section 9, the web app's `.check-sheet`): where the doubts come from, the bars still to check with
/// their reasons, Show and Looks right, then the bars checked, to check again. Over the right of the page in landscape and
/// the lower half in portrait, as Insights. Fixing the notes and the bars of a transcription stay in the web app for now.
struct CheckDrawer: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let close: () -> Void

    var body: some View {
        let check = score.check
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(text["check.title"]).overline()
                    Text(title(check))
                        .dsType(.heading)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Button(action: close) {
                    Icon(name: "close").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["check.close"])
            }
            .padding(.leading, CGFloat.ds("s-6"))
            .padding(.trailing, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("s-4"))
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    Text(howText(check))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                    if check.open == 0 && check.hasFlags {
                        Text(text["check.allDoneHint"])
                            .dsType(.caption)
                            .foregroundStyle(Color.ds("ink-2"))
                    }
                    ForEach(check.openBars) { bar in openEntry(bar) }
                    if !check.resolvedBars.isEmpty {
                        Text(text["check.resolvedHeading"]).overline().padding(.top, CGFloat.ds("s-2"))
                        ForEach(check.resolvedBars) { bar in resolvedEntry(bar) }
                    }
                }
                .padding(.leading, CGFloat.ds("s-6"))
                .padding(.trailing, CGFloat.ds("s-5"))
                .padding(.vertical, CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["check.title"])
    }

    private func title(_ check: CheckShelf) -> String {
        check.open > 0 ? text.count("check.toReview", check.openBars.count) : text["check.allDone"]
    }

    /// "Read from a scan by homr 0.7.0. Compare each flagged bar with the page: …"
    private func howText(_ check: CheckShelf) -> String {
        let source: String
        switch (check.origin, check.engine) {
        case ("scanned", let engine?): source = text.format("check.fromScan", engine) + " "
        case ("transcribed", let engine?): source = text.format("check.fromRecording", engine) + " "
        default: source = ""
        }
        return source + text[check.origin == "transcribed" ? "check.howRecordingShort" : "check.howScanShort"]
    }

    private func openEntry(_ bar: CheckShelf.BarFlags) -> some View {
        let selected = score.selection?.firstBar == bar.bar
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text("?")
                    .font(DesignTokens.font(weight: 700, size: 11))
                    .foregroundStyle(Color.ds("notation"))
                    .frame(width: 18, height: 18)
                    .background(Color.ds("warning-fill"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                    .accessibilityHidden(true)
                Text(text.format("check.bar", String(bar.bar)))
                    .font(DesignTokens.font(weight: 600, size: 15))
                    .foregroundStyle(Color.ds("ink"))
                Tag(text: CheckShelf.reasonNames(bar.flags, text), kind: .warn)
            }
            ForEach(CheckShelf.explanations(bar.flags), id: \.self) { explanation in
                Text(explanation)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["scanCheck.show"]) { score.showCheckBar(bar.bar) }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                    .disabled(bar.span == nil)
                Button(text["scanCheck.looksRight"]) { score.resolveCheck(bar.bar, true) }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(selected ? Color.ds("accent-tint") : Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(selected ? Color.ds("accent") : Color.ds("hairline")))
        .accessibilityElement(children: .contain)
    }

    private func resolvedEntry(_ bar: CheckShelf.BarFlags) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: "check", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("ink-2"))
                Text(text.format("check.bar", String(bar.bar)))
                    .font(DesignTokens.font(weight: 600, size: 15))
                    .foregroundStyle(Color.ds("ink"))
                Text(CheckShelf.reasonNames(bar.flags, text))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["scanCheck.show"]) { score.showCheckBar(bar.bar) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    .disabled(bar.span == nil)
                Button(text["scanCheck.again"]) { score.resolveCheck(bar.bar, false) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    .accessibilityHint(text["scanCheck.againHint"])
            }
        }
        .padding(.vertical, CGFloat.ds("s-1"))
    }
}
