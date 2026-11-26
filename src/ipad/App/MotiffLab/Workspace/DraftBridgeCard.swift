import SwiftUI

/// A bridge place's card (v1.2, ADR 0034 section 5; the plan's answer 2): the place music-core put between two places whose
/// keys differ, its candidates as its variants («Доминанта», «Через общий аккорд», «Ход баса», in plum: what the app
/// composed), each with the chords it sounds, heard alone («▶»), chosen («Выбрать», `drafts.base`: the base is the one
/// chosen); «Оставить» removes the place (`drafts.leaveBridge`). Shown at the place's label on the page (`DraftViewCards`)
/// and in the place's card on the draft's map (`DraftMap`). Each step is undone from its toast.
struct DraftBridgeContent: View {
    @ObservedObject var score: ScoreController
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    /// The last step was not taken: the card says so in its own words, never music-core's.
    @State private var refused = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("drafts.bridgeTitle", String(place.firstBar))).overline()
            Text(text["drafts.bridgeLine"]).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            ForEach(place.variants, id: \.id) { candidate in
                candidateRow(candidate)
            }
            Button(text["drafts.leaveButton"]) { act { await owner.leaveBridge(place, of: draft.id) } }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            if refused {
                Text(text["drafts.refused"]).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
        }
        .onChange(of: place) { _, _ in refused = false }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("drafts.bridgeTitle", String(place.firstBar)))
    }

    /// A candidate: its kind and the chords it sounds, the one chosen marked; «▶» and, for any other, «Выбрать».
    private func candidateRow(_ candidate: DraftViewAnswer.Variant) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(candidate.label).dsType(.label).foregroundStyle(Color.ds("plum"))
                if candidate.base { Tag(text: text["drafts.bridgeChosen"], kind: .saved) }
            }
            if let explanation = candidate.explanation {
                Text(explanation).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button { score.hearVariant(candidate.id, of: draft.id, over: 0..<Double(place.end - place.onset)) } label: {
                    Label { Text(text["drafts.bridgeHear"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                if !candidate.base {
                    Button(text["drafts.bridgeChoose"]) { act { await owner.makeBase(candidate, of: place, in: draft.id) } }
                        .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                }
            }
        }
    }

    /// A step on the draft: taken, the card goes and the draft's view opens again where the player is (as the map's steps,
    /// so a step on the map does not scroll); refused, the card says so.
    private func act(_ step: @escaping () async -> Bool) {
        Task {
            let taken = await step()
            guard taken else {
                refused = owner.refused(taken)
                return
            }
            score.seamsModel.close()
            await reopen?(.tick(await score.tickInView() ?? 0))
        }
    }
}
