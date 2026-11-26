import SwiftUI

/// A place's card (v1.1 on the draft's map; v1.4, F3, also the ▾ menu at the place's label on the page, the drafts spec
/// «Экран (iPad)»: «① Ноты 9–16 + Запись · 2 варианта ▾»): its variants, the base marked «основа», any other made the base
/// («Сделать основой», asked first, as the place's steps go) or moved to another place, a new place at the end or the shelf
/// («Перенести…», asked first when steps take from it), and «Взять всё уверенное · N». Each step is music-core's
/// (`drafts.base`, `drafts.place`, `drafts.takeSure`), undone from its toast; once taken, the card at the label goes
/// (`taken`) and the draft's view opens again where the player is, the map as it was.
struct DraftPlaceContent: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var techniques: DraftTechniquesModel
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let reopen: ((Landing) async -> Void)?
    /// What else a step taken does here: the card at the label goes; nothing on the map.
    var taken: @MainActor () -> Void = {}
    @State private var asked: Asked?
    /// The last step was not taken: the card says so in its own words, never music-core's.
    @State private var refused = false
    /// A step is on its way: the card's steps wait, so a second tap does not send it again.
    @State private var taking = false

    /// A step the card asks about first, as it drops what was taken from the variant (a new base, a variant moved out).
    private struct Asked {
        let title: String
        let line: String
        let button: String
        let step: @MainActor () async -> Bool
    }

    var body: some View {
        let sure = techniques.places.first { $0.place == place.id }?.sure ?? 0
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            ForEach(place.variants, id: \.id) { variant in
                variantRow(variant)
            }
            Button(text.format("drafts.takeSureCount", String(sure))) { take { await owner.takeSure(place, of: draft.id) } }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                .disabled(sure == 0)
            if refused {
                Text(text["drafts.refused"]).dsType(.caption).foregroundStyle(Color.ds("danger"))
            }
        }
        .disabled(taking)
        .onChange(of: place) { _, _ in refused = false }
        .confirmationDialog(asked?.title ?? "", isPresented: Binding(get: { asked != nil }, set: { if !$0 { asked = nil } }), titleVisibility: .visible, presenting: asked) { asked in
            Button(asked.button, role: .destructive) { take(asked.step) }
            Button(text["common.cancel"], role: .cancel) {}
        } message: { asked in
            Text(asked.line)
        }
    }

    /// A variant of the place: its words, the base marked; any other «Сделать основой» and moved elsewhere.
    private func variantRow(_ variant: DraftViewAnswer.Variant) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(variant.label).dsType(.body).foregroundStyle(Color.ds("ink")).fixedSize(horizontal: false, vertical: true)
                if variant.base { Tag(text: text["drafts.baseTag"], kind: .plain) }
            }
            if !variant.base {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Button(text["drafts.makeBase"]) { makeBase(variant) }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    Menu {
                        ForEach(draft.places.filter { $0.id != place.id }, id: \.id) { other in
                            Button(text.format("drafts.toPlace", other.label)) { move(variant, into: other.id) }
                        }
                        Button(text["drafts.toNewPlace"]) { move(variant, into: nil) }
                        Button(text["drafts.moveToShelf"]) { move(variant, into: nil, shelf: true) }
                    } label: {
                        DraftMenuLabel(icon: "move", words: text["drafts.move"])
                    }
                }
            }
        }
    }

    /// «Сделать основой»: asked first, as every step of the place (приёмы, transposition, tempo) and the anchors of all its
    /// variants go with the old base; how many steps, where there are any.
    private func makeBase(_ variant: DraftViewAnswer.Variant) {
        let steps = place.steps > 0 ? text.count("drafts.baseSteps", place.steps) + " " : ""
        asked = Asked(title: text.format("drafts.baseTitle", variant.label), line: steps + text["drafts.baseLine"], button: text["drafts.makeBase"]) {
            await owner.makeBase(variant, of: place, in: draft.id)
        }
    }

    /// A variant moved out of its place, asked first when steps take from it, as they go with it.
    private func move(_ variant: DraftViewAnswer.Variant, into other: String?, shelf: Bool = false) {
        let step: @MainActor () async -> Bool = { await owner.put(variant.id, label: variant.label, into: other, shelf: shelf, of: draft.id) }
        guard variant.steps > 0 else { return take(step) }
        asked = Asked(title: text.format("drafts.moveTitle", variant.label), line: text["drafts.moveLine"], button: text["drafts.moveButton"], step: step)
    }

    /// A step on the draft: taken, the view opens again where the player is; refused, the card says so.
    private func take(_ step: @escaping @MainActor () async -> Bool) {
        guard !taking else { return }
        taking = true
        Task {
            defer { taking = false }
            guard await step() else {
                refused = true
                return
            }
            refused = false
            taken()
            await reopen?(.tick(await score.tickInView() ?? 0))
        }
    }
}

/// A menu's label on a draft's cards and map: its icon and words in the second ink, a touch target high.
struct DraftMenuLabel: View {
    let icon: String
    let words: String

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Icon(name: icon, size: CGFloat.ds("icon-s"))
            Text(words).dsType(.label)
        }
        .foregroundStyle(Color.ds("ink-2"))
        .frame(minHeight: CGFloat.ds("h-touch"))
    }
}
