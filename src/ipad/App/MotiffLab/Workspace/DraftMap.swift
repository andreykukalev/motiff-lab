import SwiftUI

/// Which part of a draft's map is open: its places from the pill's «Карта», its shelf from «Полка · N».
enum DraftMapOpen: Hashable {
    case places, shelf
}

private struct DraftMapKey: EnvironmentKey {
    static var defaultValue: Binding<DraftMapOpen?>? { nil }
}

extension EnvironmentValues {
    /// Whether the draft's map is open and at which part, kept by the draft's screen (`ShownDraftScreen`), so it stays open
    /// as the view opens again after each step; nil on any screen that is no draft's.
    var draftMap: Binding<DraftMapOpen?>? {
        get { self[DraftMapKey.self] }
        set { self[DraftMapKey.self] = newValue }
    }
}

/// A draft's map over its view (v1.1, ADR 0034 sections 2 and 9 step 7; spec «Коллаж», «Экран (iPad)»): the places in
/// order, each with a small roll of its notes and its card (`DraftPlaceContent`, as at its label: the variants, the base
/// marked, «Сделать основой» and a variant moved to another place, a new place or the shelf; «Взять всё уверенное») and,
/// since v1.2, «С ассистентом · Pro» (`DraftAssistantSection`) for each place with a variant besides its base; a bridge
/// place's candidates (v1.2, `DraftBridgeContent`), with the assistant to choose among them (v1.4); the diamond of each seam that shows between two places, which opens the seam's card there; and
/// the shelf, each variant on it put («Поставить…») into a place or as a new place. Each step is music-core's
/// (`drafts.base`, `drafts.place`, `drafts.takeSure`, `drafts.leaveBridge`), undone from its toast, and the view opens
/// again in its place with the map open. Since v1.4 (F1, spec «Карта черновика: порядок мест») each place moves before
/// or after the nearest other place that is not a bridge (`DraftPlaceOrder`, `drafts.movePlace`; a bridge place has no
/// such buttons, a refusal said in music-core's words). In the sheet's place (`ScoreScreenContent.sheetPlace`): at the side in landscape,
/// below the draft's pill, over the lower half in portrait, so the pills and toasts make way.
struct DraftMap: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var seams: DraftSeamsModel
    @ObservedObject var techniques: DraftTechniquesModel
    let draft: DraftViewAnswer.Draft
    @ObservedObject var owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    @Environment(\.draftMap) private var map
    @EnvironmentObject private var assistant: AssistantController
    /// The draft view's notes, for the places' rolls.
    @State private var notes: [PlaybackNote] = []
    /// Why the last step was not taken: on the shelf in the map's own words, a place moved in music-core's (v1.4), said
    /// under that place's card.
    @State private var refusal: Refusal?

    private struct Refusal {
        /// The place moved; nil for a step on the shelf.
        let place: String?
        let words: String
    }

    var body: some View {
        if let map, let open = map.wrappedValue {
            panel(open, close: { map.wrappedValue = nil })
                .background(Color.ds("surface"))
                .task { notes = await score.draftNotes() ?? [] }
        }
    }

    private func panel(_ open: DraftMapOpen, close: @escaping () -> Void) -> some View {
        ScrollViewReader { reader in
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    header(close: close).id(DraftMapOpen.places)
                    ForEach(Array(draft.places.enumerated()), id: \.element.id) { index, place in
                        if index > 0, let seam = seams.before(place.id) { seamRow(seam) }
                        placeCard(place, at: index)
                    }
                    shelf.id(DraftMapOpen.shelf)
                    if let refusal, refusal.place == nil {
                        Text(refusal.words).dsType(.caption).foregroundStyle(Color.ds("danger"))
                    }
                }
                .padding(CGFloat.ds("s-4"))
            }
            .onAppear { reader.scrollTo(open, anchor: .top) }
            .onChange(of: open) { _, now in withAnimation { reader.scrollTo(now, anchor: .top) } }
        }
        // The assistant's conversation goes with a place the draft no longer offers it in, as it was (v1.4).
        .onAppear { closeAssistantIfGone() }
        .onChange(of: draft) { _, _ in closeAssistantIfGone() }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["drafts.mapTitle"])
    }

    private func header(close: @escaping () -> Void) -> some View {
        HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                Text(text["drafts.mapTitle"]).overline()
                Text(draft.name).dsType(.heading).foregroundStyle(Color.ds("ink"))
            }
            Spacer(minLength: 0)
            Button(action: close) { Icon(name: "close", size: CGFloat.ds("icon-m")) }
                .buttonStyle(DSIconItemStyle())
                .accessibilityLabel(text["drafts.mapClose"])
        }
    }

    // MARK: - Places

    private func placeCard(_ place: DraftViewAnswer.Place, at index: Int) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            // A tap on the place brings it into view, selected, as a tap on its label on the page.
            Button { _ = score.pickPlace(startingAt: place.firstBar) } label: {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(place.label).dsType(.label).foregroundStyle(Color.ds("ink"))
                    DraftPlaceRoll(notes: notes, span: Double(place.onset)..<Double(place.end))
                }
            }
            .buttonStyle(.plain)
            // Its order in the draft (v1.4, F1), the steps waiting while one is on its way; none on a bridge.
            if DraftPlaceOrder.offered(place) {
                DraftPlaceOrder(place: place, index: index, places: draft.places, text: text) { target in
                    take(place: place.id) { await owner.movePlace(place, to: target, of: draft.id) }
                }
                .disabled(owner.stepping)
            }
            if let refusal, refusal.place == place.id {
                Text(refusal.words).dsType(.caption).foregroundStyle(Color.ds("danger")).fixedSize(horizontal: false, vertical: true)
            }
            if place.bridge {
                // A bridge's candidates (v1.2): heard, chosen and the place removed, as on its card on the page.
                DraftBridgeContent(score: score, place: place, draft: draft, owner: owner, text: text, reopen: reopen)
            } else {
                // Its variants and steps, as in its ▾ menu at its label on the page (v1.4).
                DraftPlaceContent(score: score, techniques: techniques, place: place, draft: draft, owner: owner, text: text, reopen: reopen)
            }
            // The assistant (v1.2 step 8): it assembles a place from a variant or explains them, and in a bridge place
            // chooses a candidate (v1.4, F5); none in a place of the base alone.
            if DraftAssistantSection.offered(place) {
                DraftAssistantSection(assistant: assistant, score: score, place: place, draft: draft, owner: owner, text: text, reopen: reopen)
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    /// The assistant in a draft's place closed when the draft as it is now no longer offers it there (`offered`).
    private func closeAssistantIfGone() {
        assistant.closeInDraft(unlessOffered: draft.places.filter(DraftAssistantSection.offered).map { DraftAssistantSection.named($0, of: draft) })
    }

    // MARK: - Seams

    /// The diamond of the seam between two places, with what differs; tapped, the seam's card opens under it.
    private func seamRow(_ seam: DraftSeamsAnswer.Seam) -> some View {
        let open = seams.picked.map { $0.at == nil && $0.seam == seam } ?? false
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Button { open ? seams.close() : score.pickSeam(DraftSeamsModel.key(seam), at: nil) } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    SeamDiamond(left: seam.left)
                    Text(DraftSeamsModel.title(seam, text) + " · " + DraftSeamsModel.changes(seam, text))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                .frame(minHeight: CGFloat.ds("h-touch"))
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(DraftSeamsModel.label(seam, text))
            .accessibilityAddTraits(open ? .isSelected : [])
            if open {
                DraftSeamContent(score: score, seam: seam, draft: draft, owner: owner, text: text, reopen: reopen)
                    .padding(CGFloat.ds("s-3"))
                    .background(Color.ds("paper"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
            }
        }
        .padding(.leading, CGFloat.ds("s-4"))
    }

    // MARK: - The shelf

    private var shelf: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("drafts.shelfCount", String(draft.shelf.count))).overline()
            if draft.shelf.isEmpty {
                Text(text["drafts.shelfEmpty"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            ForEach(draft.shelf, id: \.id) { shelved in
                HStack(spacing: CGFloat.ds("s-2")) {
                    Text(shelved.label).dsType(.body).foregroundStyle(Color.ds("ink"))
                    Spacer(minLength: 0)
                    Menu {
                        ForEach(draft.places, id: \.id) { place in
                            Button(text.format("drafts.toPlace", place.label)) { take { await owner.put(shelved.id, label: shelved.label, into: place.id, of: draft.id) } }
                        }
                        Button(text["drafts.toNewPlace"]) { take { await owner.put(shelved.id, label: shelved.label, into: nil, of: draft.id) } }
                    } label: {
                        DraftMenuLabel(icon: "plus", words: text["drafts.put"])
                    }
                    .disabled(owner.stepping)
                }
            }
        }
    }

    /// A step on the draft: taken, the view opens again where the player is, the map still open; refused, the map says so,
    /// under the place moved in music-core's words (`place`, v1.4), else at the shelf in its own. No other step goes until
    /// the view has opened again (`reopening`).
    private func take(place: String? = nil, _ step: @escaping @MainActor () async -> Bool) {
        Task {
            let taken = await step()
            guard taken else {
                refusal = owner.refusal(taken, told: place != nil, otherwise: text["drafts.refused"]).map { Refusal(place: place, words: $0) }
                return
            }
            refusal = nil
            await owner.reopening { await reopen?(.tick(await score.tickInView() ?? 0)) }
        }
    }
}

/// A seam's diamond on the map, as on the page (score.css `.perf-marker.seam`): the ink's square on its corner, hollow once
/// the seam is kept as it is.
struct SeamDiamond: View {
    let left: Bool

    var body: some View {
        Rectangle()
            .fill(Color.ds(left ? "paper" : "ink-2"))
            .overlay(Rectangle().strokeBorder(Color.ds("ink-2"), lineWidth: left ? 1.5 : 0))
            .frame(width: CGFloat.ds("s-3"), height: CGFloat.ds("s-3"))
            .rotationEffect(.degrees(45))
            .accessibilityHidden(true)
    }
}

/// A place's notes as a small roll (its thumbnail on the map): each note a bar from its onset for its length, higher
/// pitches higher, within the place's ticks.
struct DraftPlaceRoll: View {
    let notes: [PlaybackNote]
    let span: Range<Double>

    var body: some View {
        Canvas { context, size in
            let inside = notes.filter { span.contains($0.onset) }
            guard let low = inside.map(\.pitch).min(), let high = inside.map(\.pitch).max(), !span.isEmpty else { return }
            let row = size.height / CGFloat(high - low + 1)
            let scale = size.width / CGFloat(span.upperBound - span.lowerBound)
            for note in inside {
                let box = CGRect(x: CGFloat(note.onset - span.lowerBound) * scale, y: CGFloat(high - note.pitch) * row, width: max(1, CGFloat(note.duration) * scale), height: max(1, row))
                context.fill(Path(box), with: .color(Color.ds("ink-2")))
            }
        }
        .frame(height: CGFloat.ds("h-touch"))
        .background(Color.ds("paper"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .accessibilityHidden(true)
    }
}
