import SwiftUI

/// A seam's card on a draft's view (v1.1, ADR 0034 sections 5 and 9 step 7; spec «Коллаж», «Экран (iPad)»): the two places
/// either side, each as its source states its key, meter and tempo, what differs, «▶ Послушать стык», what music-core
/// offers for the place after it («② в соль мажор», «Темп как в ①») and «Оставить». Shown at its diamond on the page
/// (`DraftViewCards`) or on the draft's map under the diamond between two places (`DraftMap`). No «Связка» (version 1.2).
struct DraftSeamContent: View {
    @ObservedObject var score: ScoreController
    let seam: DraftSeamsAnswer.Seam
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    /// The last step was not taken: the card says so in its own words, never music-core's.
    @State private var refused = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(DraftSeamsModel.title(seam, text)).overline()
            ForEach(seam.sides, id: \.place) { side in
                VStack(alignment: .leading, spacing: 0) {
                    Text(draft.places.first { $0.id == side.place }?.label ?? "").dsType(.label).foregroundStyle(Color.ds("ink"))
                    Text(Self.facts(side, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
            }
            Text(text.format("drafts.seamDiffers", DraftSeamsModel.changes(seam, text))).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            Button { score.hearSeam(seam) } label: {
                Label { Text(text["drafts.seamListen"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            ForEach(seam.offers, id: \.label) { offer in
                Button { act { await owner.take(offer, of: draft.id) } } label: {
                    Label { Text(offer.label) } icon: { Icon(name: offer.kind == "tempo" ? "tempo" : "keysig", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["drafts.leaveButton"]) { act { await owner.leave(seam, of: draft.id) } }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    .disabled(seam.left)
                if seam.left {
                    Text(text["drafts.seamLeft"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
            }
            if refused {
                Text(text["drafts.refused"]).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
        }
        .onChange(of: seam) { _, _ in refused = false }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(DraftSeamsModel.title(seam, text))
    }

    /// A side's key, meter and tempo as its source states them: «ля минор · 3/4 · ♩ = 72»; a key it does not state, said so.
    static func facts(_ side: DraftSeamsAnswer.Side, _ text: UIText) -> String {
        let tempo = TempoReading.note(beat: side.tempo.beat) + " " + (side.tempo.mark ?? "?")
        return [side.key?.name ?? text["drafts.seamNoKey"], side.meter.label, tempo].joined(separator: " · ")
    }

    /// A step on the draft: taken, the card goes and the draft's view opens again at the seam; refused, the card says it
    /// was not taken.
    private func act(_ step: @escaping () async -> Bool) {
        Task {
            guard await step() else {
                refused = true
                return
            }
            score.seamsModel.close()
            await reopen?(.tick(Double(seam.at)))
        }
    }
}

/// The cards over a draft's view: a приём's at its number, a seam's at its diamond, one at a time (picking one closes the
/// other); a tap elsewhere puts the seam's away.
struct DraftViewCards: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var seams: DraftSeamsModel
    let player: Player
    let owner: SourcesController
    let draft: DraftViewAnswer.Draft
    let text: UIText
    let reopen: ((Landing) async -> Void)?

    var body: some View {
        if let pick = seams.picked, let at = pick.at {
            DraftCardAtMark(at: at, close: { seams.close() }) {
                DraftSeamContent(score: score, seam: pick.seam, draft: draft, owner: owner, text: text, reopen: reopen)
            }
        } else {
            DraftTechniqueCard(score: score, model: score.draftModel, player: player, owner: owner, text: text, reopen: reopen)
        }
    }
}
