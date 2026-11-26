import SwiftUI

/// An anchor's menu at its amber mark on a draft's view (v1.2, ADR 0034 section 2; the plan's answer 4): which bar it holds
/// and of which variant, and «Открепить» (`drafts.unanchor`), after which music-core aligns the variant without it. The
/// step is undone from its toast.
struct DraftAnchorContent: View {
    @ObservedObject var score: ScoreController
    let mark: DraftAnchorsModel.Mark
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    /// The last step was not taken: the menu says so in its own words, never music-core's.
    @State private var refused = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("drafts.anchorTitle", String(mark.anchor.bar))).overline()
            Text(text.format("drafts.anchorLine", mark.variant.label)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            Button {
                Task {
                    let taken = await owner.unanchor(mark, of: draft.id)
                    guard taken else {
                        refused = owner.refused(taken)
                        return
                    }
                    score.anchorsModel.close()
                    await reopen?(.tick(Double(mark.anchor.at)))
                }
            } label: {
                Label { Text(text["drafts.unpin"]) } icon: { Icon(name: "close", size: CGFloat.ds("icon-s")) }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            if refused {
                Text(text["drafts.refused"]).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
        }
        .onChange(of: mark) { _, _ in refused = false }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(DraftAnchorsModel.label(mark, text))
    }
}

/// «Не тот такт? Сопоставить…» on a приём's card (v1.2, the plan's answer 4): the bars of the occurrence's variant, the one
/// music-core matched with the occurrence marked; a tap on the bar that truly sounds here pins the occurrence's start in
/// the base to that bar's start (`pin`), and music-core aligns the variant again around it.
struct DraftAnchorMatch: View {
    @ObservedObject var score: ScoreController
    let spot: DraftTechniquesModel.Spot
    let text: UIText
    let pin: (Int) -> Void
    @State private var bars: [PlaybackAnswer.Bar]?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(text.format("drafts.matchLine", String(spot.occurrence.firstBar))).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
            if let bars {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: CGFloat.ds("s-1")) {
                        ForEach(bars, id: \.number) { bar in
                            Button(String(bar.number)) { pin(Int(bar.onset)) }
                                .buttonStyle(DSChipStyle(on: Self.matched(bar, in: bars, spot)))
                                .accessibilityLabel(text.format("drafts.matchBar", String(bar.number)))
                        }
                    }
                }
            } else {
                ProgressView()
            }
        }
        .task(id: spot) { bars = await score.variantBars(spot) }
    }

    /// The variant's bar music-core matched with the occurrence: the last that starts at or before its start in the variant.
    static func matched(_ bar: PlaybackAnswer.Bar, in bars: [PlaybackAnswer.Bar], _ spot: DraftTechniquesModel.Spot) -> Bool {
        bars.last { $0.onset <= Double(spot.occurrence.inVariant.onset) }?.number == bar.number
    }
}
