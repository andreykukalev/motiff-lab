import SwiftUI

// The controls that change a draft's structure (v1.4, ADR 0034 section 8; drafts spec «Ноты + запись», «Коллаж»): a place
// moved earlier or later on the draft's map (F1), «Найти на записи» (F2) and the place's length in its card, and
// «Перенести приём» in a приём's card. Each reads music-core's answer to know what it may offer, and each step is
// `SourcesController`'s (SourcesDrafts.swift), handed to the card's own `act`, which says why music-core refused it in its
// words and opens the draft's view again once it is taken; its toast undoes it (`drafts.undo`).

/// «Раньше» and «Позже» on a place's card of the draft's map (F1): the place one place earlier or later in the draft, none
/// before the first nor after the last; a bridge place is refused in music-core's words.
struct DraftPlaceOrder: View {
    let place: DraftViewAnswer.Place
    let index: Int
    let count: Int
    let text: UIText
    let move: (Int) -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            button("back", "drafts.earlier", "drafts.earlierSpoken", to: index - 1)
            button("fwd", "drafts.later", "drafts.laterSpoken", to: index + 1)
        }
    }

    private func button(_ icon: String, _ key: String, _ spoken: String, to target: Int) -> some View {
        Button { move(target) } label: {
            Label { Text(text[key]) } icon: { Icon(name: icon, size: CGFloat.ds("icon-s")) }
        }
        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        .disabled(!(0..<count).contains(target))
        .accessibilityLabel(text.format(spoken, place.label))
    }
}

/// «Найти на записи» in a place's card (F2): music-core searches the piece's recordings heard as notes for the place's
/// music as the card shows; the entry puts the stretch it found in as a variant, and is off where it found none or only
/// one the place has already, its line saying which.
struct DraftRecordingRow: View {
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let act: (@escaping @MainActor () async -> Bool) -> Void
    @State private var offer = DraftRecordingOffer.searching

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Button {
                guard let found = offer.adds else { return }
                act { await owner.addFromRecording(found, to: place.id, of: draft.id) }
            } label: {
                Label { Text(text["drafts.onRecording"]) } icon: { Icon(name: "fromRec", size: CGFloat.ds("icon-s")) }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            .disabled(offer.adds == nil)
            Text(offer.line(text)).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
        }
        // Searched again whenever the place changes (a variant put in, its length), the entry off meanwhile.
        .task(id: place) {
            offer = .searching
            offer = await owner.recordingOffer(for: place.id, of: draft.id)
        }
    }
}

/// A place's length in its card (v1.4): the bars of its source its base is cut from and «Изменить длину…», which shows the
/// first and the last bar stepped within the bars its Notes have, «Применить» once they differ, and «Отмена»; where it
/// cannot change, music-core's reason.
struct DraftLengthRow: View {
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let act: (@escaping @MainActor () async -> Bool) -> Void
    @State private var choice: DraftLengthChoice?
    @State private var reason: String?
    /// «Изменить длину…» tapped: the steppers show.
    @State private var changing = false

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            if let choice {
                Text(text.format("drafts.lengthLine", String(choice.now.first), String(choice.now.last))).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                if changing {
                    PageStepper(label: text["drafts.lengthFrom"], spoken: text["drafts.lengthFromSpoken"], value: choice.first) { self.choice?.step(first: $0) }
                    PageStepper(label: text["drafts.lengthTo"], spoken: text["drafts.lengthToSpoken"], value: choice.last) { self.choice?.step(last: $0) }
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Button(text["drafts.lengthApply"]) {
                            guard let bars = choice.asked else { return }
                            act { await owner.resize(place.id, to: bars, of: draft.id) }
                        }
                        .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                        .disabled(choice.asked == nil)
                        Button(text["common.cancel"]) { cancel() }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    }
                } else {
                    Button(text["drafts.lengthChange"]) { changing = true }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            } else if let reason {
                Text(reason).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
        }
        // Read again whenever the place changes, so the bars shown are the place's as it is.
        .task(id: place) {
            let length = await owner.length(of: place.id, in: draft.id)
            choice = DraftLengthChoice(length)
            reason = length?.reason
            changing = false
        }
    }

    /// «Отмена»: the steppers go, the bars as the place has them.
    private func cancel() {
        changing = false
        choice = choice.flatMap { DraftLengthChoice(now: $0.now, most: $0.most) }
    }
}

/// «Перенести приём…» on a приём's card (v1.4): the draft's other places music-core lists for it, each with how many of
/// its bars the приём fits, one it fits nowhere off; the menu off where it fits no other place, its line saying why.
struct DraftCarryMenu: View {
    let spot: DraftTechniquesModel.Spot
    let owner: SourcesController
    let text: UIText
    let act: (@escaping @MainActor () async -> Bool) -> Void
    @State private var places: [DraftCarryPlacesAnswer.Place] = []
    @State private var reason: String?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Menu {
                ForEach(places, id: \.place) { target in
                    Button(target.bars.isEmpty ? text.format("drafts.carryNowhere", target.label) : target.label + " · " + text.count("drafts.carryBars", target.bars.count)) {
                        act { await owner.carry(spot, to: target) }
                    }
                    .disabled(target.bars.isEmpty)
                }
            } label: {
                DraftMenuLabel(icon: "move", words: text["drafts.carryTechnique"])
            }
            .disabled(!places.contains { !$0.bars.isEmpty })
            if let reason {
                Text(reason).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
        }
        // Read again for each occurrence the card shows, as it is now.
        .task(id: spot) {
            let answer = await owner.carryPlaces(spot)
            places = answer.value?.places ?? []
            reason = Self.reason(answer.value, refused: answer.message, text)
        }
    }

    /// Why the приём goes nowhere, or nil where it fits a place: music-core's words where it does not carry it (`refused`,
    /// bars put in), else that the draft has no other place, or that it fits none of them.
    static func reason(_ places: DraftCarryPlacesAnswer?, refused: String?, _ text: UIText) -> String? {
        guard let places else { return refused ?? text["drafts.refused"] }
        if places.places.isEmpty { return text["drafts.carryNoPlace"] }
        return places.places.contains { !$0.bars.isEmpty } ? nil : text["drafts.carryNoFit"]
    }
}
