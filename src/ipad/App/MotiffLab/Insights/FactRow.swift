import SwiftUI

// A fact's row in the Insights stream and what hangs from it (its Try and the way being tried), shown from
// InsightsController and calling its methods; the circle in a row is Circle.swift's `KeyCircleCard`.

/// A fact in the stream (ds.css `.fact`): the claim, which rings its places on the score; its certainty, how many places
/// and Evidence, which opens in place; while it is chosen, its places as chips, each ringing only itself.
struct FactRow: View {
    let fact: Fact
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        let chosen = insights.chosen?.fact == fact
        let evidence = insights.evidence.contains(fact.id)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            if fact.places.isEmpty {
                Text(fact.claim).dsType(.body).foregroundStyle(Color.ds("ink"))
            } else {
                Button {
                    insights.choose(fact)
                } label: {
                    Text(fact.claim)
                        .dsType(.body)
                        .foregroundStyle(Color.ds("ink"))
                        .multilineTextAlignment(.leading)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityHint(text["insights.claimHint"])
                .accessibilityAddTraits(chosen ? .isSelected : [])
            }
            if let doubt = fact.doubt {
                // The fact rests on recognised notes still to check (#352): said where, beside the uncertain tag.
                Text(doubt).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Tag(text: text["term.certainty.\(fact.certainty)"], kind: fact.certainty == "detected" ? .detected : .assumed)
                    .background(chosen ? Color.ds("surface") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                if !fact.places.isEmpty {
                    Text(text.count("understand.places", fact.places.count))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Button {
                    insights.toggleEvidence(fact)
                } label: {
                    Text(text["insights.evidence"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(evidence ? .isSelected : [])
                if InsightsController.opensCircle(fact) {
                    Button {
                        Task { await insights.toggleCircle(fact) }
                    } label: {
                        Text(text["insights.onCircle"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(insights.circleFor == fact.id ? .isSelected : [])
                }
            }
            if evidence {
                if fact.brief != nil {
                    Text(fact.text).dsType(.caption).foregroundStyle(Color.ds("ink"))
                }
                Text(fact.basis).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            if chosen, insights.chosen?.custom == nil, !fact.places.isEmpty {
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    ForEach(Array(fact.places.enumerated()), id: \.offset) { index, place in
                        let on = insights.chosen?.place == index
                        Button(place.label) {
                            insights.choosePlace(index, of: fact)
                        }
                        .buttonStyle(DSChipStyle(on: on))
                        .accessibilityHint(text["understand.placeHint"])
                        .accessibilityAddTraits(on ? .isSelected : [])
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["understand.placesLabel"])
            }
            if insights.circleFor == fact.id, let circle = insights.circle {
                KeyCircleCard(circle: circle, fact: fact, insights: insights, text: text)
            }
            if !fact.tries.isEmpty {
                TryRow(fact: fact, insights: insights, text: text)
            }
            if insights.trial?.factId == fact.id {
                TrialRow(insights: insights, text: text)
            }
        }
        .padding(CGFloat.ds("s-3"))
        .background(chosen ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(chosen ? Color.ds("accent") : .clear))
        .padding(.horizontal, -CGFloat.ds("s-3"))
        .accessibilityElement(children: .contain)
    }
}

/// A fact's Try (Andrei, 2026-11-04): one quiet button that opens the two or three ways music-core offers for the fact;
/// a way sounds at once. On the row that holds the variation, Keep follows the button.
struct TryRow: View {
    let fact: Fact
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        let holds = insights.trial?.factId == fact.id
        let open = insights.tryOpen.contains(fact.id)
        FlowLayout(spacing: CGFloat.ds("s-2")) {
            Button {
                insights.toggleTry(fact)
            } label: {
                Text(text["insights.try"])
                    .font(DesignTokens.font(weight: 500, size: 13))
                    .foregroundStyle(Color.ds("accent"))
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .frame(minHeight: CGFloat.ds("h-compact"))
                    .background(open || holds ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(holds)
            .accessibilityAddTraits(open ? .isSelected : [])
            if open && !holds {
                ForEach(fact.tries, id: \.id) { way in
                    Button {
                        Task { await insights.tryWay(way, of: fact) }
                    } label: {
                        HStack(spacing: 6) {
                            if insights.starting == way.id { ProgressView().controlSize(.mini) }
                            Text(way.label)
                        }
                    }
                    .buttonStyle(DSChipStyle())
                    .disabled(insights.starting != nil)
                    .accessibilityIdentifier("ml-screens-try-way")
                }
            }
            if holds, let trial = insights.trial, !trial.kept {
                Button {
                    Task { await insights.keep() }
                } label: {
                    Text(text["insights.keep"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                }
                .buttonStyle(.plain)
                .frame(minHeight: CGFloat.ds("h-compact"))
                .accessibilityLabel(text["explore.keep"])
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["insights.try"])
    }
}

/// The variation being tried, in short: its way, what changed in music-core's words, whether it is kept, and Leave it.
struct TrialRow: View {
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        if let trial = insights.trial {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Tag(text: trial.way.label, kind: .saved)
                    if trial.kept {
                        Text(text["explore.keptWithProject"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    }
                }
                if !trial.explanation.isEmpty {
                    (Text(text["explore.whatChanged"]).bold() + Text(" " + trial.explanation))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Button {
                    Task { await insights.leave() }
                } label: {
                    Text(text["explore.leave"]).dsType(.label).foregroundStyle(Color.ds("accent-hover"))
                }
                .buttonStyle(.plain)
                .accessibilityHint(text["explore.leaveHint"])
            }
            .padding(CGFloat.ds("s-3"))
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .composedFrame()
        }
    }
}
