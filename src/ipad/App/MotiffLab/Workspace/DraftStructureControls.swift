import SwiftUI

// The controls that change a draft's structure (v1.4, ADR 0034 section 8; drafts spec «Ноты + запись», «Коллаж»): a place
// moved earlier or later on the draft's map (F1), «Найти на записи» (F2) and the place's length in its card, and
// «Перенести приём» in a приём's card. Each reads music-core's answer to know what it may offer, and each step is
// `SourcesController`'s (SourcesDrafts.swift), handed to the card's own `act`, which holds the card's steps until the
// draft's view has opened again, and says why music-core refused it in its words; its toast undoes it (`drafts.undo`).

/// «Раньше» and «Позже» on a place's card of the draft's map (F1): the place moved before or after the nearest other place
/// that is not a bridge (`target`), none before the first nor after the last; a bridge place is refused in music-core's
/// words.
struct DraftPlaceOrder: View {
    let place: DraftViewAnswer.Place
    let index: Int
    let places: [DraftViewAnswer.Place]
    let text: UIText
    let move: (Int) -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            button("back", "drafts.earlier", "drafts.earlierSpoken", to: Self.target(index, by: -1, in: places))
            button("fwd", "drafts.later", "drafts.laterSpoken", to: Self.target(index, by: 1, in: places))
        }
    }

    /// The index «Раньше» (`step` -1) or «Позже» (+1) moves the place at `index` of `places` to: that of the nearest place
    /// that way that is not a bridge, as a bridge between goes with its seam (on [A, bridge, B], A «Позже» is 2 and B
    /// «Раньше» 0, both [B, A]); nil where there is none.
    static func target(_ index: Int, by step: Int, in places: [DraftViewAnswer.Place]) -> Int? {
        var at = index + step
        while places.indices.contains(at) {
            if !places[at].bridge { return at }
            at += step
        }
        return nil
    }

    private func button(_ icon: String, _ key: String, _ spoken: String, to target: Int?) -> some View {
        Button { if let target { move(target) } } label: {
            Label { Text(text[key]) } icon: { Icon(name: icon, size: CGFloat.ds("icon-s")) }
        }
        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        .disabled(target == nil)
        .accessibilityLabel(text.format(spoken, place.label))
    }
}

/// «Найти на записи» in a place's card (F2; spec «Ноты + запись»): tapped, music-core searches the piece's recordings heard
/// as notes for the place's music and the stretch it finds most is put in as a variant; where it finds none, or only one
/// the place has already, nothing is, and the line says which. Off on a piece with no recording heard, nothing searched.
struct DraftRecordingRow: View {
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    @ObservedObject var owner: SourcesController
    let text: UIText
    /// The place «Найти на записи» was tapped for, while music-core searches: the card's, which holds its other steps
    /// meanwhile.
    @Binding var asked: DraftViewAnswer.Place?
    let act: (@escaping @MainActor () async -> Bool) -> Void
    @State private var offer = DraftRecordingOffer.ready

    var body: some View {
        let shown: DraftRecordingOffer = !owner.hasHeardNotes ? .noRecording : asked == nil ? offer : .searching
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Button { asked = place } label: {
                Label { Text(text["drafts.onRecording"]) } icon: { Icon(name: "fromRec", size: CGFloat.ds("icon-s")) }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            .disabled(!shown.searchable)
            if let line = shown.line(text) {
                Text(line).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
        }
        // Tapped: the search, then the stretch found most put in through the card's `act`, the card's steps held until
        // the step is answered; a place changed meanwhile drops the answer.
        .task(id: asked) {
            guard let asked else { return }
            let found = await owner.findOnRecording(asked.id, of: draft.id)
            guard !Task.isCancelled else { return }
            offer = found.adds == nil ? found : .ready
            guard let best = found.adds else {
                self.asked = nil
                return
            }
            act {
                defer { self.asked = nil }
                return await owner.addFromRecording(best, to: asked.id, of: draft.id)
            }
        }
        .onChange(of: place) { _, _ in
            asked = nil
            offer = .ready
        }
    }
}

/// A place's length in its card (v1.4): «Изменить длину…», which reads music-core's bounds (`drafts.length`, only then, as
/// it opens the Notes the place was cut from) and shows the bars of its source its base is cut from, the first and the
/// last bar stepped within the bars its Notes have, «Применить» once they differ, and «Отмена»; where it cannot change,
/// music-core's reason.
struct DraftLengthRow: View {
    let place: DraftViewAnswer.Place
    let draft: DraftViewAnswer.Draft
    let owner: SourcesController
    let text: UIText
    let act: (@escaping @MainActor () async -> Bool) -> Void
    @State private var choice: DraftLengthChoice?
    @State private var reason: String?
    /// The place «Изменить длину…» was tapped for, while its bounds are read.
    @State private var asked: DraftViewAnswer.Place?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            if let choice {
                Text(text.format("drafts.lengthLine", String(choice.now.first), String(choice.now.last))).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                PageStepper(label: text["drafts.lengthFrom"], spoken: text["drafts.lengthFromSpoken"], value: choice.first) { self.choice?.step(first: $0) }
                PageStepper(label: text["drafts.lengthTo"], spoken: text["drafts.lengthToSpoken"], value: choice.last) { self.choice?.step(last: $0) }
                HStack(spacing: CGFloat.ds("s-2")) {
                    Button(text["drafts.lengthApply"]) {
                        guard let bars = choice.asked else { return }
                        act { await owner.resize(place.id, to: bars, of: draft.id) }
                    }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(choice.asked == nil)
                    Button(text["common.cancel"]) { self.choice = nil }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            } else {
                Button(text["drafts.lengthChange"]) { asked = place }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    .disabled(asked != nil)
                if let reason {
                    Text(reason).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        // Tapped: the bounds read, the steppers shown at the place's own bars, else why not; a place changed meanwhile
        // drops the answer.
        .task(id: asked) {
            guard let asked else { return }
            let length = await owner.length(of: asked.id, in: draft.id)
            guard !Task.isCancelled else { return }
            let read = DraftLengthChoice(length)
            choice = read
            reason = read == nil ? length?.reason ?? text["drafts.refused"] : nil
            self.asked = nil
        }
        // The place changed (a step taken): the steppers go, read again at the next «Изменить длину…».
        .onChange(of: place) { _, _ in
            asked = nil
            choice = nil
            reason = nil
        }
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
            guard !Task.isCancelled else { return }
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
