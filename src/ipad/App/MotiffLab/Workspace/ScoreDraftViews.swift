import SwiftUI

/// A draft's view and its приёмы on the score's screen (v1.1, ADR 0034 section 9 step 6; spec «Экран (iPad)»): the card of
/// the number tapped, the pill of the assembly under the bar, the toast of the step just taken, and the screen that shows
/// the draft over the piece and opens it again in its place after each step. A step goes to the piece the draft belongs to
/// (`owner`, its Sources: music-core's `drafts.*`, saved there at once); the notes it came from never change.

/// The card at a приём's number tapped (ds.css `.menu` surface, as a version marker's popover): its number and bar, its kind
/// as the engine read it, how sure that is, «▶ Как в нотах» and «▶ Как на записи», «Взять везде · N», «Только здесь»,
/// «Оставить», and one hand from its variant, each only where music-core takes it (`Spot.offer`), since v1.2 «Не тот
/// такт? Сопоставить…» (`DraftAnchorMatch`, an anchor), and since v1.4 «Перенести приём…» (`DraftCarryMenu`). A tap
/// elsewhere puts it away; once a step is taken the draft's view opens again at the occurrence (`reopen`).
struct DraftTechniqueCard: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var model: DraftTechniquesModel
    let player: Player
    @ObservedObject var owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    /// Why the last step was not taken: in the card's own words, or music-core's for a приём carried (v1.4).
    @State private var refusal: String?
    /// «Сопоставить…» tapped: the variant's bars show to pick the one that sounds here.
    @State private var matching = false
    /// A step sent from the card, until the draft's view has opened again (or it was refused): its steps are held.
    @State private var acting = false

    var body: some View {
        if let pick = model.picked, let at = pick.at {
            DraftCardAtMark(at: at, close: { model.closeCard() }) { card(pick.spot) }
                .onChange(of: pick.spot) { _, _ in
                    refusal = nil
                    matching = false
                }
        }
    }

    private func card(_ spot: DraftTechniquesModel.Spot) -> some View {
        let offer = spot.offer
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("drafts.techniqueAt", String(spot.technique.number), String(spot.occurrence.firstBar))).overline()
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(spot.technique.label).dsType(.label).foregroundStyle(Color.ds("ink"))
                // The kind is the engine's reading of the difference, never a fact (CLAUDE.md).
                Tag(text: text["drafts.inferred"], kind: .assumed)
            }
            Text(DraftTechniquesModel.status(spot, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-2")) {
                hear(spot, variant: false, "drafts.asInNotes")
                // «Как на записи» where the variant was heard from a recording, else its own notes «Как в варианте».
                hear(spot, variant: true, score.recordingHeard(spot) != nil ? "drafts.asOnRecording" : "drafts.asInVariant")
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text.format("drafts.takeEverywhere", String(offer.everywhere))) { act { await owner.take(spot, here: false) } }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                Button(text["drafts.takeHere"]) { act { await owner.take(spot, here: true) } }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                Button(text["drafts.leaveButton"]) { act { await owner.leave(spot) } }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            .disabled(!offer.steps)
            ForEach(["right", "left"], id: \.self) { part in
                Button { act { await owner.takeHand(part, at: spot) } } label: {
                    Label { Text(text[part == "left" ? "drafts.leftFromVariant" : "drafts.rightFromVariant"]) } icon: { Icon(name: "hands", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                .disabled(!offer.hands)
            }
            DraftCarryMenu(spot: spot, owner: owner, text: text) { act(told: true, $0) }
            if matching {
                DraftAnchorMatch(score: score, spot: spot, text: text) { tick in act { await owner.anchor(spot, variantTick: tick) } }
            } else {
                // An anchor holds a bar of the base: none where the base has no bars (an inserted bar).
                Button { matching = true } label: {
                    Label { Text(text["drafts.match"]) } icon: { Icon(name: "check", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                .disabled(spot.heard(variant: false).span.isEmpty)
            }
            if let refusal {
                Text(refusal).dsType(.caption).foregroundStyle(Color.ds("danger")).fixedSize(horizontal: false, vertical: true)
            }
        }
        // A step on its way (`SourcesController.stepping`), or the view opening again after it (`acting`): the card's
        // steps wait for it, as the place's card's do.
        .disabled(owner.stepping || acting)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(DraftTechniquesModel.title(spot, text))
    }

    private func hear(_ spot: DraftTechniquesModel.Spot, variant: Bool, _ key: String) -> some View {
        Button { score.hearTechnique(spot, variant: variant, pausing: player) } label: {
            Label { Text(text[key]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        .disabled(!score.canHear(spot, variant: variant))
    }

    /// A step on the draft: taken, the card goes and the draft's view opens again at the occurrence; refused, the card says
    /// it was not taken, in music-core's words when `told` (a приём carried, v1.4). Its steps are held meanwhile (`acting`).
    private func act(told: Bool = false, _ step: @escaping @MainActor () async -> Bool) {
        guard let spot = model.picked?.spot else { return }
        acting = true
        Task {
            defer { acting = false }
            let taken = await step()
            guard taken else {
                refusal = owner.refusal(taken, told: told, otherwise: text["drafts.refused"])
                return
            }
            model.closeCard()
            await reopen?(.tick(spot.span.lowerBound))
        }
    }
}

/// A card of a draft's view at its mark on the page (`at`, the mark's bottom left), on ds.css's `.menu` surface as a
/// version marker's popover, kept on the screen; a tap elsewhere puts it away (`close`). A phrase's mark's menu is one too
/// (ScorePhraseEdits.swift).
struct DraftCardAtMark<Content: View>: View {
    let at: CGPoint
    let close: @MainActor () -> Void
    @ViewBuilder let content: Content

    var body: some View {
        GeometryReader { area in
            ZStack(alignment: .topLeading) {
                Color.clear.contentShape(Rectangle()).onTapGesture { close() }
                content
                    .padding(CGFloat.ds("s-3"))
                    .frame(width: ScoreScreen.popoverWidth, alignment: .leading)
                    .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                    .dsShadow("shadow-menu")
                    .offset(x: min(max(at.x, CGFloat.ds("s-3")), area.size.width - ScoreScreen.popoverWidth - CGFloat.ds("s-3")), y: at.y + CGFloat.ds("s-1"))
            }
        }
    }
}

/// The pill under the bar of a draft's view (spec «Экран (iPad)», «при сборке»): with приёмы, ‹ › from one occurrence to
/// the next, the current one's name, «▶ Ноты» and «▶ Запись» for it; always «▶ Всё» (the whole draft) and «↻ Место» (its
/// place round and round) by the one playback model, «Карта» (the draft's map) and «Полка · N» (its shelf, on the map);
/// with приёмы «Скрыть номера», which leaves the score to practise on (the numbers, the card and the stepping put away)
/// with «Показать номера» alone. Where it does not fit, the items show their icons alone, their words for VoiceOver.
struct DraftAssemblyPill: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var model: DraftTechniquesModel
    @ObservedObject var player: Player
    let draft: DraftViewAnswer.Draft
    let text: UIText
    @Environment(\.draftMap) private var map

    var body: some View {
        ViewThatFits(in: .horizontal) {
            pill(compact: false)
            pill(compact: true)
        }
    }

    private func pill(compact: Bool) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            if model.numbersHidden {
                word("eye", text["drafts.showNumbers"]) { score.hideTechniqueNumbers(false) }
            } else {
                if !model.spots.isEmpty {
                    assembly(compact: compact)
                    ItemDivider()
                }
                Group {
                    word("play", text["drafts.playAll"], compact: compact) { score.playDraft(on: player) }
                    word("loop", text["drafts.loopPlace"], compact: compact) { Task { await score.loopPlace(on: player) } }
                }
                .disabled(!player.ready)
                if let map {
                    ItemDivider()
                    word("phrases", text["drafts.map"], compact: compact, on: map.wrappedValue == .places) { map.wrappedValue = map.wrappedValue == .places ? nil : .places }
                    word("folder", text.format("drafts.shelfCount", String(draft.shelf.count)), compact: compact, on: map.wrappedValue == .shelf) {
                        map.wrappedValue = map.wrappedValue == .shelf ? nil : .shelf
                    }
                }
                if !model.spots.isEmpty {
                    ItemDivider()
                    word("eyeoff", text["drafts.hideNumbers"], compact: compact) { score.hideTechniqueNumbers(true) }
                }
            }
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["drafts.pill"])
    }

    @ViewBuilder private func assembly(compact: Bool) -> some View {
        let current = model.picked?.spot
        stepItem("back", text["drafts.previous"]) { score.stepTechnique(-1) }
        Text(current.map { DraftTechniquesModel.title($0, text) } ?? text.count("drafts.techniqueCount", model.total))
            .dsType(.label)
            .foregroundStyle(Color.ds("plum"))
            .lineLimit(1)
        stepItem("fwd", text["drafts.next"]) { score.stepTechnique(1) }
        ItemDivider()
        ForEach([false, true], id: \.self) { variant in
            let key = variant ? (current.flatMap { score.recordingHeard($0) } != nil ? "drafts.pillRecording" : "drafts.pillVariant") : "drafts.pillNotes"
            word("play", text[key], compact: compact) {
                if let current { score.hearTechnique(current, variant: variant, pausing: player) }
            }
            .disabled(!(current.map { score.canHear($0, variant: variant) } ?? false))
        }
    }

    /// An item: its icon and words, or its icon alone where the pill does not fit (`compact`), its words for VoiceOver.
    private func word(_ icon: String, _ words: String, compact: Bool = false, on: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: CGFloat.ds("s-1")) {
                Icon(name: icon, size: CGFloat.ds("icon-s"))
                if !compact { Text(words).dsType(.label).lineLimit(1) }
            }
        }
        .buttonStyle(DSItemStyle(on: on))
        .accessibilityLabel(words)
        .accessibilityAddTraits(on ? .isSelected : [])
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
/// `ScoreScreen`'s), at the place asked, as music-core rebuilt it, its map and the assistant as they were; each number it
/// was open under is closed once the next shows or the screen goes.
struct ShownDraftScreen: View {
    @State private var piece: OpenPiece
    /// The draft's map, open or not, as the view opens again after each step.
    @State private var map: DraftMapOpen?
    /// The assistant of the draft's visit (v1.4): each screen the view opens again on is handed it, so a conversation in a
    /// place, and a question on its way, outlast the step that opened the view again; it goes with this screen.
    @StateObject private var assistant = AssistantController()
    let owner: SourcesController
    let viewing: String

    init(piece: OpenPiece, owner: SourcesController, viewing: String) {
        _piece = State(initialValue: piece)
        self.owner = owner
        self.viewing = viewing
    }

    var body: some View {
        ScoreScreen(piece: piece, viewing: viewing, reopen: reopen, assistant: assistant)
            .environment(\.draftMap, $map)
            .onDisappear { [id = piece.id] in ScoreScreenContent.ShownView.close(id) }
            .id(piece.id)
    }

    /// The draft opened again by `drafts.open`, at `landing`'s tick, else at its start; as it was when it cannot be.
    private func reopen(_ landing: Landing) async {
        guard let id = piece.draft?.id, let draft = owner.layers.draft(id), let next = await owner.openDraft(draft, at: landing.tick) else { return }
        piece = next
    }
}
