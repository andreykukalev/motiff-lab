import Foundation

/// The phrases' marks on the score (the phrases spec, v1.2, decision 1 and «Polish of option A»): at each phrase's start a
/// breath mark and its number, at a section's start its boxed letter, which the page draws (`phraseMarks`,
/// `Scripts/score/ipad-page/page-phrases.ts`) with what a screen reader says of each. A tap on a number selects its whole
/// phrase, a tap on a letter its section (at most 32 bars, as the Contents' chips select it), each a new selection in place
/// of the one there was, with «Вернуть»; the marks themselves never change the selection. Their editing is not here. They
/// stand only on the piece's working notes, where they are edited: none on a draft's view or other Notes shown over the
/// piece (`OpenPiece.shownOnly`; the lead's decision, v1.2 i-2 review 1).
extension ScoreController {
    /// The marks for the page: each phrase's number, start and whether it is the player's own, each section's letter at
    /// its start, with their labels («Фраза 2, с такта 4, доля 3, начинается после половинной каденции»); none on a view
    /// only shown.
    var phraseMarks: [String: Any] {
        let phrases = piece?.shownOnly == false ? selectionModel.phrases : nil
        let numbers = (phrases?.units ?? []).map { unit -> [String: Any] in
            let label = text.format("phrases.markLabel", String(unit.number), String(unit.startBar), String(unit.startBeat), unit.why)
            return ["number": unit.number, "tick": unit.onset, "own": unit.own, "label": label]
        }
        let letters = (phrases?.sections ?? []).map { section -> [String: Any] in
            let label = text.format("phrases.sectionLabel", section.label, barsText(section.firstBar, section.lastBar, text))
            return ["id": section.id, "tick": section.onset, "letter": section.label, "label": label]
        }
        return ["phrases": numbers, "sections": letters]
    }

    /// A phrase's number tapped: the whole phrase, its selectable span, selected in place of the selection there was.
    func pickPhrase(_ number: Int) {
        guard let unit = selectionModel.phrases?.unit(number) else { return }
        Task { await select(unit.selectableOnset, unit.selectableEnd, replaces: true) }
    }

    /// A section's letter tapped: the section, as far as a selection holds it, selected in place of the one there was.
    func pickSection(_ id: String) {
        guard let section = selectionModel.phrases?.sections.first(where: { $0.id == id }) else { return }
        Task { await select(section.selectableOnset, section.selectableEnd, replaces: true) }
    }
}
