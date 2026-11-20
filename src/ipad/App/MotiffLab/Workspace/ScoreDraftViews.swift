import SwiftUI

/// A draft's view and its приёмы on the score's screen (v1.1, ADR 0034 section 9 step 6; spec «Экран (iPad)»): the card of
/// the number tapped, the pill of the assembly under the bar, the toast of the step just taken, and the screen that shows
/// the draft over the piece and opens it again in its place after each step. A step goes to the piece the draft belongs to
/// (`owner`, its Sources: music-core's `drafts.*`, saved there at once); the notes it came from never change.

/// The card at a приём's number tapped (ds.css `.menu` surface, as a version marker's popover): its number and bar, its kind
/// as the engine read it, how sure that is, «▶ Как в нотах» and «▶ Как на записи», «Взять везде · N», «Только здесь»,
/// «Оставить», and one hand from its variant. A tap elsewhere puts it away; once a step is taken the draft's view opens
/// again at the occurrence (`reopen`).
struct DraftTechniqueCard: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var model: DraftTechniquesModel
    let owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    /// Why the last step was not taken, music-core's words; nil when it was.
    @State private var refused: String?

    var body: some View {
        if let pick = model.picked, let at = pick.at {
            GeometryReader { area in
                ZStack(alignment: .topLeading) {
                    Color.clear.contentShape(Rectangle()).onTapGesture { model.closeCard() }
                    card(pick.spot)
                        .offset(x: min(max(at.x, CGFloat.ds("s-3")), area.size.width - Self.width - CGFloat.ds("s-3")), y: at.y + CGFloat.ds("s-1"))
                }
            }
            .onChange(of: pick.spot) { _, _ in refused = nil }
        }
    }

    /// As wide as the selection menu's Bookmark card.
    private static let width: CGFloat = 340

    private func card(_ spot: DraftTechniquesModel.Spot) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("drafts.techniqueAt", String(spot.technique.number), String(spot.occurrence.firstBar))).overline()
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(spot.technique.label).dsType(.label).foregroundStyle(Color.ds("ink"))
                // The kind is the engine's reading of the difference, never a fact (CLAUDE.md).
                Tag(text: text["drafts.inferred"], kind: .assumed)
            }
            Text(DraftTechniquesModel.status(spot, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-2")) {
                hear(spot, variant: false, "drafts.asInNotes")
                hear(spot, variant: true, "drafts.asOnRecording")
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text.format("drafts.takeEverywhere", String(spot.technique.occurrences.count))) { act { await owner.take(spot, here: false) } }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                Button(text["drafts.takeHere"]) { act { await owner.take(spot, here: true) } }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                Button(text["drafts.leaveButton"]) { act { await owner.leave(spot) } }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            ForEach(["right", "left"], id: \.self) { part in
                Button { act { await owner.takeHand(part, at: spot) } } label: {
                    Label { Text(text[part == "left" ? "drafts.leftFromVariant" : "drafts.rightFromVariant"]) } icon: { Icon(name: "hands", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            if let refused {
                Text(refused).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(width: Self.width, alignment: .leading)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(DraftTechniquesModel.title(spot, text))
    }

    private func hear(_ spot: DraftTechniquesModel.Spot, variant: Bool, _ key: String) -> some View {
        Button { score.hearTechnique(spot, variant: variant) } label: {
            Label { Text(text[key]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        .disabled(!score.canHear(spot, variant: variant))
    }

    /// A step on the draft: taken, the card goes and the draft's view opens again at the occurrence; refused, why.
    private func act(_ step: @escaping () async -> Bool) {
        guard let spot = model.picked?.spot else { return }
        Task {
            guard await step() else {
                refused = owner.status
                return
            }
            model.closeCard()
            await reopen?(.tick(spot.span.lowerBound))
        }
    }
}

/// The pill of the assembly under the bar of a draft's view with приёмы (spec «Экран (iPad)», «при сборке»): ‹ › from one
/// occurrence to the next, the current one's name, «▶ Ноты» and «▶ Запись» for it, and «Скрыть номера», which leaves the
/// score to practise on (the numbers, the card and the stepping put away) with «Показать номера» alone.
struct DraftAssemblyPill: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var model: DraftTechniquesModel
    let text: UIText

    var body: some View {
        if !model.spots.isEmpty {
            HStack(spacing: CGFloat.ds("s-1")) {
                if model.numbersHidden {
                    word("eye", "drafts.showNumbers") { score.hideTechniqueNumbers(false) }
                } else {
                    assembly
                }
            }
            .padding(.horizontal, CGFloat.ds("s-2"))
            .frame(height: CGFloat.ds("h-touch"))
            .modifier(FloatingSurface())
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["drafts.pill"])
        }
    }

    @ViewBuilder private var assembly: some View {
        let current = model.picked?.spot
        step("back", "drafts.previous") { score.stepTechnique(-1) }
        Text(current.map { DraftTechniquesModel.title($0, text) } ?? text.count("drafts.techniqueCount", model.spots.count))
            .dsType(.label)
            .foregroundStyle(Color.ds("plum"))
            .lineLimit(1)
        step("fwd", "drafts.next") { score.stepTechnique(1) }
        Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
        ForEach([false, true], id: \.self) { variant in
            word("play", variant ? "drafts.pillRecording" : "drafts.pillNotes") {
                if let current { score.hearTechnique(current, variant: variant) }
            }
            .disabled(!(current.map { score.canHear($0, variant: variant) } ?? false))
        }
        Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
        word("eyeoff", "drafts.hideNumbers") { score.hideTechniqueNumbers(true) }
    }

    private func step(_ icon: String, _ key: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: icon).frame(width: 32, height: 32)
        }
        .buttonStyle(DSItemStyle())
        .accessibilityLabel(text[key])
        .help(text[key])
    }

    private func word(_ icon: String, _ key: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: CGFloat.ds("s-1")) {
                Icon(name: icon, size: CGFloat.ds("icon-s"))
                Text(text[key]).dsType(.label)
            }
        }
        .buttonStyle(DSItemStyle())
    }
}

/// The toast of the step just taken on a draft's view, with its Undo (`drafts.undo`), which opens the view again; it is the
/// owner's Sources toast, so it stays as the view opens again.
struct DraftStepToast: View {
    @ObservedObject var owner: SourcesController
    let score: ScoreController
    let text: UIText
    let reopen: ((Landing) async -> Void)?

    var body: some View {
        if let toast = owner.toast, case .draftStep = toast.undo {
            SourcesToastView(sources: owner, toast: toast, text: text) {
                // Where the player is in the draft now.
                await reopen?(.tick(await score.tickInView() ?? 0))
            }
            .transition(.opacity)
        }
    }
}

/// A draft's view on its screen over the piece (v1.1): after each step it opens again in its place (`reopen`, the
/// `ScoreScreen`'s), at the place asked, as music-core rebuilt it; each number it was open under is closed once the next
/// shows or the screen goes.
struct ShownDraftScreen: View {
    @State private var piece: OpenPiece
    let owner: SourcesController
    let viewing: String

    init(piece: OpenPiece, owner: SourcesController, viewing: String) {
        _piece = State(initialValue: piece)
        self.owner = owner
        self.viewing = viewing
    }

    var body: some View {
        ScoreScreen(piece: piece, viewing: viewing, reopen: reopen)
            .onDisappear { [id = piece.id] in ScoreScreenContent.ShownView.close(id) }
            .id(piece.id)
    }

    /// The draft opened again by `drafts.open`, at `landing`'s tick, else at its start; as it was when it cannot be.
    private func reopen(_ landing: Landing) async {
        guard let id = piece.draft?.id, let draft = owner.layers.draft(id), let next = await owner.openDraft(draft, at: landing.tick) else { return }
        piece = next
    }
}
