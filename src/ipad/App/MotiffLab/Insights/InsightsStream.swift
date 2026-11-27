import SwiftUI

// The Insights stream's list, shown from InsightsController: which facts, lenses, folds and ideas it shows are the
// controller's rules; the sheet around it (its head, what sounds, the assistant's foot) is Workspace/InsightsSheet.swift.

/// The stream itself: «Границы фраз изменились» while phrase anchors match no phrase (#318), the ideas kept on the
/// selection's phrases, filter chips, Main (the harmonic motion, the texture, the strongest of the rest), then Details,
/// the other facts folded by lens with their count (testing item 14).
struct InsightsStream: View {
    @ObservedObject var insights: InsightsController
    let text: UIText
    let hasSelection: Bool
    /// A selection with no notes in it (v0.6.4, a passage of time): the stream says so instead of asking for one.
    var noNotes = false
    /// The piece's phrases with their anchors, which place the ideas and count the anchors that match no phrase.
    var phrases: PiecePhrases?
    /// «Привязать заново»; nil where the phrases cannot be changed (a view only shown, E-insights-5), which has no line.
    var reattach: (() -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            if let reattach, let phrases, phrases.unmatched > 0 {
                PhrasesChangedRow(text: text, reattach: reattach)
            }
            let ideas = insights.ideasShown(on: phrases)
            if !ideas.isEmpty {
                group(text["insights.ideas"]) {
                    FlowLayout(spacing: CGFloat.ds("s-2")) {
                        ForEach(ideas) { shown in
                            ideaTag(shown)
                        }
                    }
                }
            }
            if let understanding = insights.understanding {
                let lenses = understanding.lensesShown
                if lenses.count > 1 {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: CGFloat.ds("s-2")) {
                            chip(text["insights.all"], on: insights.lens == nil) { insights.filter(nil) }
                            ForEach(lenses, id: \.self) { lens in
                                chip(lensLabel(lens), on: insights.lens == lens) { insights.filter(lens) }
                            }
                        }
                    }
                    .padding(.bottom, CGFloat.ds("s-3"))
                    .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel(text["insights.filter"])
                }
                if let problem = insights.problem {
                    // E-insights-1: a Try, Keep or On the circle that failed says why, as a stream not read does.
                    Text(problem).dsType(.label).foregroundStyle(Color.ds("danger"))
                }
                if understanding.facts.isEmpty {
                    caption(text["understand.nothingYet"])
                }
                if insights.trialFilteredOut {
                    // The fact the variation came from is filtered out: the trial stays in reach at the top.
                    group(text["insights.trying"]) {
                        TrialRow(insights: insights, text: text)
                    }
                }
                let main = insights.mainShown
                if !main.isEmpty {
                    group(text["insights.main"]) {
                        ForEach(main) { fact in
                            FactRow(fact: fact, insights: insights, text: text)
                        }
                    }
                }
                let details = insights.detailsShown
                if !details.isEmpty {
                    VStack(alignment: .leading, spacing: 0) {
                        Text(text["insights.details"])
                            .overline()
                            .padding(.bottom, CGFloat.ds("s-2"))
                            .accessibilityAddTraits(.isHeader)
                        ForEach(details, id: \.self) { lens in
                            let facts = understanding.facts(of: lens)
                            let open = insights.opens(lens)
                            Button {
                                insights.fold(lens)
                            } label: {
                                HStack {
                                    Text(text.format("insights.lensCount", lensLabel(lens), text.count("understand.facts", facts.count)))
                                        .dsType(.body)
                                        .foregroundStyle(Color.ds("ink"))
                                    Spacer()
                                    Icon(name: "chevd", size: CGFloat.ds("icon-s"))
                                        .foregroundStyle(Color.ds("ink-3"))
                                        .rotationEffect(.degrees(open ? 180 : 0))
                                }
                                .frame(minHeight: CGFloat.ds("h-touch"))
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .disabled(insights.lens == lens)
                            .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
                            .accessibilityAddTraits(.isHeader)
                            .accessibilityValue(open ? text["insights.lensOpen"] : text["insights.lensFolded"])
                            if open {
                                VStack(alignment: .leading, spacing: 2) {
                                    ForEach(facts) { fact in
                                        FactRow(fact: fact, insights: insights, text: text)
                                    }
                                }
                                .padding(.top, CGFloat.ds("s-2"))
                            }
                        }
                    }
                }
            } else if insights.loading {
                ProgressView().frame(maxWidth: .infinity)
            } else if let problem = insights.problem {
                Text(problem).dsType(.label).foregroundStyle(Color.ds("danger"))
            } else if !hasSelection {
                caption(text[noNotes ? "insights.noNotes" : "insights.selectFirst"])
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["insights.stream"])
    }

    private func group<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
                .overline()
                .padding(.bottom, CGFloat.ds("s-2"))
                .accessibilityAddTraits(.isHeader)
            content()
        }
    }

    /// A kept idea, in plum (what the app composed): its name and where it is, the phrase of its anchor or its bars.
    private func ideaTag(_ shown: KeptIdea.Shown) -> some View {
        let place = shown.phrase.map { text.format("insights.ideaPhrase", String($0.number)) } ?? barsText(shown.idea.firstBar, shown.idea.lastBar, text)
        return HStack(spacing: CGFloat.ds("s-1")) {
            Tag(text: shown.idea.name, kind: .saved)
            Text(place).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
        .accessibilityElement(children: .combine)
    }

    private func chip(_ label: String, on: Bool, action: @escaping () -> Void) -> some View {
        Button(label, action: action)
            .buttonStyle(DSChipStyle(on: on))
            .accessibilityAddTraits(on ? .isSelected : [])
    }

    private func caption(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
    }

    private func lensLabel(_ lens: String) -> String {
        text["term.lens.\(lens)"]
    }
}

/// «Границы фраз изменились» (#318): some ideas or resume points are attached to phrases the notes or their analysis no
/// longer have, and the one group action «Привязать заново» attaches them to the phrases there now.
private struct PhrasesChangedRow: View {
    let text: UIText
    let reattach: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["insights.phrasesChanged"]).dsType(.label).foregroundStyle(Color.ds("ink"))
            Button(text["insights.reattach"], action: reattach)
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .accessibilityElement(children: .contain)
    }
}
