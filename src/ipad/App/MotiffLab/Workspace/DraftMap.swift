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
/// order, each with a small roll of its notes and its variants, the base marked, «Сделать основой» and a variant moved to
/// another place, a new place or the shelf; «Взять всё уверенное» for each place; the diamond of each seam that shows
/// between two places, which opens the seam's card there; and the shelf, each variant on it put («Поставить…») into a
/// place or as a new place. Each step is music-core's (`drafts.base`, `drafts.place`, `drafts.takeSure`), undone from its
/// toast, and the view opens again in its place with the map open. Places are not reordered: music-core has no call for
/// it. In the sheet's place (`ScoreScreenContent.sheetPlace`): at the side in landscape, below the draft's pill, over the
/// lower half in portrait, so the pills and toasts make way.
struct DraftMap: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var seams: DraftSeamsModel
    @ObservedObject var techniques: DraftTechniquesModel
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    @Environment(\.draftMap) private var map
    /// The draft view's notes, for the places' rolls.
    @State private var notes: [PlaybackNote] = []
    @State private var asked: Asked?
    /// The last step was not taken: the map says so in its own words, never music-core's.
    @State private var refused = false

    /// A step the map asks about first, as it drops what was taken from the variant (a new base, a variant moved out).
    private struct Asked {
        let title: String
        let line: String
        let button: String
        let step: @MainActor () async -> Bool
    }

    var body: some View {
        if let map, let open = map.wrappedValue {
            panel(open, close: { map.wrappedValue = nil })
                .background(Color.ds("surface"))
                .task { notes = await score.draftNotes() ?? [] }
                .confirmationDialog(asked?.title ?? "", isPresented: Binding(get: { asked != nil }, set: { if !$0 { asked = nil } }), titleVisibility: .visible, presenting: asked) { asked in
                    Button(asked.button, role: .destructive) { take(asked.step) }
                    Button(text["common.cancel"], role: .cancel) {}
                } message: { asked in
                    Text(asked.line)
                }
        }
    }

    private func panel(_ open: DraftMapOpen, close: @escaping () -> Void) -> some View {
        ScrollViewReader { reader in
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    header(close: close).id(DraftMapOpen.places)
                    ForEach(Array(draft.places.enumerated()), id: \.element.id) { index, place in
                        if index > 0, let seam = seams.before(place.id) { seamRow(seam) }
                        placeCard(place)
                    }
                    shelf.id(DraftMapOpen.shelf)
                    if refused {
                        Text(text["drafts.refused"]).dsType(.caption).foregroundStyle(Color.ds("danger"))
                    }
                }
                .padding(CGFloat.ds("s-4"))
            }
            .onAppear { reader.scrollTo(open, anchor: .top) }
            .onChange(of: open) { _, now in withAnimation { reader.scrollTo(now, anchor: .top) } }
        }
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

    private func placeCard(_ place: DraftViewAnswer.Place) -> some View {
        let sure = techniques.places.first { $0.place == place.id }?.sure ?? 0
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            // A tap on the place brings it into view, selected, as a tap on its label on the page.
            Button { _ = score.pickPlace(startingAt: place.firstBar) } label: {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(place.label).dsType(.label).foregroundStyle(Color.ds("ink"))
                    DraftPlaceRoll(notes: notes, span: Double(place.onset)..<Double(place.end))
                }
            }
            .buttonStyle(.plain)
            ForEach(place.variants, id: \.id) { variant in
                variantRow(variant, of: place)
            }
            Button(text.format("drafts.takeSureCount", String(sure))) { take { await owner.takeSure(place, of: draft.id) } }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                .disabled(sure == 0)
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    /// A variant of a place: its words, the base marked; any other «Сделать основой» and moved elsewhere.
    private func variantRow(_ variant: DraftViewAnswer.Variant, of place: DraftViewAnswer.Place) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(variant.label).dsType(.body).foregroundStyle(Color.ds("ink"))
                if variant.base { Tag(text: text["drafts.baseTag"], kind: .plain) }
            }
            if !variant.base {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Button(text["drafts.makeBase"]) { makeBase(variant, of: place) }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    Menu {
                        ForEach(draft.places.filter { $0.id != place.id }, id: \.id) { other in
                            Button(text.format("drafts.toPlace", other.label)) { move(variant, into: other.id) }
                        }
                        Button(text["drafts.toNewPlace"]) { move(variant, into: nil) }
                        Button(text["drafts.moveToShelf"]) { move(variant, into: nil, shelf: true) }
                    } label: {
                        menuLabel("move", text["drafts.move"])
                    }
                }
            }
        }
    }

    /// «Сделать основой»: asked first, as every step of the place (приёмы, transposition, tempo) and the anchors of all its
    /// variants go with the old base; how many steps, where there are any.
    private func makeBase(_ variant: DraftViewAnswer.Variant, of place: DraftViewAnswer.Place) {
        let steps = place.steps > 0 ? text.count("drafts.baseSteps", place.steps) + " " : ""
        asked = Asked(title: text.format("drafts.baseTitle", variant.label), line: steps + text["drafts.baseLine"], button: text["drafts.makeBase"]) {
            await owner.makeBase(variant, of: place, in: draft.id)
        }
    }

    /// A variant moved out of its place, asked first when steps take from it, as they go with it.
    private func move(_ variant: DraftViewAnswer.Variant, into place: String?, shelf: Bool = false) {
        let step: @MainActor () async -> Bool = { await owner.put(variant.id, label: variant.label, into: place, shelf: shelf, of: draft.id) }
        guard variant.steps > 0 else { return take(step) }
        asked = Asked(title: text.format("drafts.moveTitle", variant.label), line: text["drafts.moveLine"], button: text["drafts.moveButton"], step: step)
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
                        menuLabel("plus", text["drafts.put"])
                    }
                }
            }
        }
    }

    private func menuLabel(_ icon: String, _ words: String) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Icon(name: icon, size: CGFloat.ds("icon-s"))
            Text(words).dsType(.label)
        }
        .foregroundStyle(Color.ds("ink-2"))
        .frame(minHeight: CGFloat.ds("h-touch"))
    }

    /// A step on the draft: taken, the view opens again where the player is, the map still open; refused, the map says so.
    private func take(_ step: @escaping @MainActor () async -> Bool) {
        Task {
            guard await step() else {
                refused = true
                return
            }
            refused = false
            await reopen?(.tick(await score.tickInView() ?? 0))
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
