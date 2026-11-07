import SwiftUI

// The comparison's views over the score (decision record 0024; the mockups «Сборка своей версии»): the quiet card after
// a version is added, the chips of the sources at the bottom, the card at the selection, the carry sheet, the journal
// and the apply dialog. The tones are the design kit's: the versions plum, teal, amber and green in the order they were
// added, the notes graphite.

/// A source's dot in its tone; «Как было» is a ring.
private struct ToneDot: View {
    let source: AssemblySource

    var body: some View {
        Group {
            if source.kind == "before" {
                Circle().strokeBorder(source.toneColor, lineWidth: 1.5)
            } else {
                Circle().fill(source.toneColor)
            }
        }
        .frame(width: 8, height: 8)
    }
}

/// The panels' common look: a surface with the kit's radius, hairline and shadow.
private struct AssemblyPanel: ViewModifier {
    var width: CGFloat = 380

    func body(content: Content) -> some View {
        content
            .padding(CGFloat.ds("s-4"))
            .frame(maxWidth: width, alignment: .leading)
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            .dsShadow("shadow-sheet")
    }
}

private extension View {
    func assemblyPanel(width: CGFloat = 380) -> some View { modifier(AssemblyPanel(width: width)) }
}

/// A line of the sheet, a carry's card, the revert plan or the apply dialog: ? a choice, ! changes around, ✓ the same.
private struct AssemblyLineRow: View {
    let line: AssemblyLine
    let text: UIText

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
            Text(symbol)
                .dsType(.label)
                .foregroundStyle(tint)
                .frame(width: 14)
                .accessibilityLabel(text["assembly.mark.\(line.mark)"])
            VStack(alignment: .leading, spacing: 2) {
                if !line.label.isEmpty {
                    Text(line.label).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                Text(line.text).dsType(.label).foregroundStyle(Color.ds("ink")).fixedSize(horizontal: false, vertical: true)
            }
        }
        .accessibilityElement(children: .combine)
    }

    private var symbol: String {
        switch line.mark {
        case "choice": "?"
        case "changes": "!"
        default: "✓"
        }
    }

    private var tint: Color {
        switch line.mark {
        case "choice": .ds("accent")
        case "changes": .ds("warning")
        default: .ds("success")
        }
    }
}

/// After a version is added: one quiet card for ten seconds, with where it matches the notes. Nothing changes in the notes.
struct AssemblyOfferCard: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText
    let offer: AssemblyModel.Offer

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text.format("assembly.offerTitle", offer.name)).dsType(.heading).foregroundStyle(Color.ds("ink"))
            Text(offer.text).dsType(.label).foregroundStyle(Color.ds("ink"))
            Text(text["assembly.offerSafe"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["assembly.compare"]) { assembly.openComparison(offer.versionId) }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                Button { assembly.hearOffered() } label: {
                    Label { Text(text["assembly.hearVersion"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                Button(text["assembly.later"]) { assembly.later() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        .assemblyPanel()
        .accessibilityElement(children: .contain)
    }
}

/// The bottom of the page while comparing: a chip for each source, ▶ on each, Undo and Redo, the journal, «Сделать нотами…» and Done.
struct AssemblyChipsBar: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: CGFloat.ds("s-1")) {
                    ForEach(assembly.info.sources.filter(\.reads)) { source in
                        chip(source)
                    }
                }
            }
            Divider().frame(height: 24)
            Button { assembly.undo() } label: { Icon(name: "undo", size: CGFloat.ds("icon-s")).frame(width: 32, height: 32) }
                .buttonStyle(BarActionStyle())
                .disabled(!assembly.info.canUndo)
                .accessibilityLabel(text["assembly.undo"])
            Button { assembly.redo() } label: { Icon(name: "undo", size: CGFloat.ds("icon-s")).scaleEffect(x: -1).frame(width: 32, height: 32) }
                .buttonStyle(BarActionStyle())
                .disabled(!assembly.info.canRedo)
                .accessibilityLabel(text["assembly.redo"])
            Button(text["assembly.journal"]) { assembly.toggleJournal() }
                .buttonStyle(DSButtonStyle(kind: assembly.journalOpen ? .secondary : .quiet, compact: true))
            Button(text["assembly.applyOpen"]) { assembly.openApply() }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                .disabled(!assembly.info.changed)
            Button(text["assembly.done"]) { assembly.close() }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
        }
        .padding(.horizontal, CGFloat.ds("s-3"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .frame(minHeight: 52)
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["assembly.pill"])
    }

    private func chip(_ source: AssemblySource) -> some View {
        let shown = assembly.shown == source.id
        return HStack(spacing: 6) {
            Button { assembly.choose(source.id) } label: {
                HStack(spacing: 6) {
                    ToneDot(source: source)
                    Text(source.name).dsType(.label).lineLimit(1)
                }
            }
            .accessibilityAddTraits(shown ? .isSelected : [])
            Button { assembly.hear(source.id) } label: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                .accessibilityLabel(text.format("assembly.hearSource", source.name))
        }
        .buttonStyle(.plain)
        .foregroundStyle(shown ? Color.ds("ink") : Color.ds("ink-2"))
        .padding(.horizontal, CGFloat.ds("s-3"))
        .frame(height: 36)
        .background(shown ? Color.ds("surface-2") : Color.clear, in: Capsule())
        .overlay(Capsule().strokeBorder(shown ? source.toneColor : Color.ds("hairline"), lineWidth: shown ? 1.5 : 1))
        .disabled(assembly.carry != nil)
    }
}

/// The source shown, when nothing is selected in it: what it is, how it matches the notes, and «Принять совпавшее».
struct AssemblySourceCard: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText
    let source: AssemblySource

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: 6) {
                ToneDot(source: source)
                Text(source.name).dsType(.bodyStrong).foregroundStyle(source.toneText)
                Text(text.count("assembly.bars", source.bars)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            switch source.kind {
            case "notes":
                Text(text[assembly.info.steps > 0 ? "assembly.notesCardWork" : "assembly.notesCard"]).dsType(.label).foregroundStyle(Color.ds("ink"))
            case "before":
                Text(text["assembly.beforeCard"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                if assembly.info.steps > 0 {
                    Button(text["assembly.restartFrom"]) { assembly.restart() }
                        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                }
            default:
                if let summary = source.summary { Text(summary).dsType(.label).foregroundStyle(Color.ds("ink")) }
                if let detail = source.detail { Text(detail).dsType(.caption).foregroundStyle(Color.ds("ink-2")) }
                if source.corrections > 0 {
                    Button(text.format("assembly.accept", String(source.corrections))) { assembly.acceptMatched(source.id) }
                        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                        .accessibilityHint(text["assembly.acceptHint"])
                }
            }
            Text(text["assembly.sourcesSafe"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
        .assemblyPanel()
        .accessibilityElement(children: .contain)
    }
}

/// The card at the selection: what it holds, where a version's selection is like the notes, and what can be done with it.
struct AssemblyPickView: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText
    let card: AssemblyPickCard

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(card.title).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
            if let edges = card.edges {
                Text(edges).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            if let similar = card.similarText {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Text(similar).dsType(.label).foregroundStyle(Color.ds("ink"))
                    if card.similarSpan != nil {
                        Button(text["assembly.show"]) { assembly.showSimilar() }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    }
                }
            }
            if let number = card.carryNumber, let source = card.carrySource {
                let name = assembly.source(source)?.name ?? ""
                Text(text.format("assembly.fromSource", name, text.format("assembly.carryNumber", String(number))))
                    .overline()
                ForEach(card.carryLines) { AssemblyLineRow(line: $0, text: text) }
                HStack(spacing: CGFloat.ds("s-2")) {
                    Button(text.format("assembly.showIn", name)) { assembly.showCarrySource() }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        .disabled(assembly.source(source) == nil)
                    Button(text["assembly.revertWhat"]) {
                        assembly.journalOpen = true
                        assembly.planRevert(number)
                    }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            }
            HStack(spacing: CGFloat.ds("s-2")) {
                Button { assembly.hear() } label: {
                    Label { Text(text["assembly.hear"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                if card.canTranspose {
                    Button(text["assembly.semitoneDown"]) { assembly.transpose(-1) }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        .accessibilityHint(text["assembly.transposeHint"])
                    Button(text["assembly.semitoneUp"]) { assembly.transpose(1) }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        .accessibilityHint(text["assembly.transposeHint"])
                }
                if card.canCarry {
                    Button(text["assembly.toNotes"]) { assembly.openCarry() }
                        .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                }
            }
        }
        .assemblyPanel()
        .accessibilityElement(children: .contain)
    }
}

/// «В ноты…»: what the carry will do, in the same lines every time, with the choices it offers, before it is made.
struct AssemblySheetView: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText
    let sheet: AssemblySheet

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text.format("assembly.sheetOverline", String(assembly.info.steps + 1))).overline()
            VStack(alignment: .leading, spacing: 2) {
                Text(sheet.title).dsType(.heading).foregroundStyle(Color.ds("ink"))
                Text(sheet.route).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                Text("\(text["assembly.ghost"]) · \(text["assembly.ghostShown"])").dsType(.caption).foregroundStyle(Color.ds("plum"))
            }
            Text(text["assembly.sheetLines"]).overline()
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    ForEach(sheet.lines) { line in
                        AssemblyLineRow(line: line, text: text)
                        choices(line)
                    }
                }
            }
            .frame(maxHeight: 360)
            HStack(spacing: CGFloat.ds("s-2")) {
                Button { assembly.hear() } label: {
                    Label { Text(text["assembly.hearGhost"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                Spacer()
                Button(text["assembly.cancel"]) { assembly.cancelCarry() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button(text["assembly.carry"]) { assembly.makeCarry() }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
        }
        .assemblyPanel(width: 420)
        .accessibilityElement(children: .contain)
    }

    /// The choices under a line that offers them: how to get the notes from a recording, where, and the key.
    @ViewBuilder private func choices(_ line: AssemblyLine) -> some View {
        switch line.name {
        case "how":
            if let keep = sheet.keepText, let heard = sheet.heardText {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    choice(keep, on: sheet.keep) { assembly.chooseKeep(true) }
                    choice(heard, on: !sheet.keep) { assembly.chooseKeep(false) }
                }
                .padding(.leading, 22)
            }
        case "where":
            HStack(spacing: CGFloat.ds("s-1")) {
                if let replace = sheet.replaceText, sheet.canReplace {
                    choice(replace, on: !sheet.insert) { assembly.chooseInsert(false) }
                }
                choice(sheet.insertText, on: sheet.insert) { assembly.chooseInsert(true) }
            }
            .padding(.leading, 22)
        case "key":
            if let key = sheet.keyText, sheet.suggestedTranspose != 0 {
                HStack(spacing: CGFloat.ds("s-1")) {
                    choice(text["assembly.asIs"], on: sheet.transpose == 0) { assembly.chooseTranspose(0) }
                    choice(key, on: sheet.transpose == sheet.suggestedTranspose) { assembly.chooseTranspose(sheet.suggestedTranspose) }
                }
                .padding(.leading, 22)
            }
        default:
            EmptyView()
        }
    }

    private func choice(_ label: String, on: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(label).dsType(.label).multilineTextAlignment(.leading).fixedSize(horizontal: false, vertical: true)
        }
        .buttonStyle(DSButtonStyle(kind: on ? .secondary : .quiet, compact: true))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(on ? Color.ds("accent") : Color.clear))
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// The journal: every change to the notes, in order; any of them returned on its own, with what leans on it shown first.
struct AssemblyJournalView: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText

    var body: some View {
        let journal = assembly.journal
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            HStack {
                Text(text.count("assembly.journalOverline", journal.rows.count)).overline()
                Spacer()
                Button { assembly.journalOpen = false } label: { Icon(name: "close", size: CGFloat.ds("icon-s")).frame(width: 28, height: 28) }
                    .buttonStyle(BarActionStyle())
                    .accessibilityLabel(text["bar.close"])
            }
            if let plan = journal.revert {
                Text(text.format("assembly.revertTitle", String(plan.number))).dsType(.heading).foregroundStyle(Color.ds("ink"))
                Text(text["assembly.revertHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                ForEach(plan.lines) { AssemblyLineRow(line: $0, text: text) }
                HStack(spacing: CGFloat.ds("s-2")) {
                    Button(text["assembly.cancel"]) { assembly.planRevert(nil) }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    Button(plan.dependents.isEmpty ? text.format("assembly.revertOne", String(plan.number)) : text.format("assembly.revertWith", String(plan.number), String(plan.dependents.count))) {
                        assembly.revert(plan.number)
                    }
                    .buttonStyle(DSButtonStyle(kind: .danger, compact: true))
                }
            } else if journal.rows.isEmpty {
                Text(text["assembly.journalEmpty"]).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            } else {
                Text(text["assembly.journalHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                ScrollView {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                        ForEach(Array(journal.rows.reversed())) { row in rowView(row) }
                    }
                }
                .frame(maxHeight: 360)
                Button(text["assembly.restart"]) { assembly.restart() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        .assemblyPanel(width: 420)
        .accessibilityElement(children: .contain)
    }

    private func rowView(_ row: AssemblyJournal.Row) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 6) {
                if let id = row.source, let source = assembly.source(id) { ToneDot(source: source) }
                Text("№\(row.number) · \(row.title)").dsType(.label).foregroundStyle(row.applied ? Color.ds("ink") : Color.ds("ink-3"))
            }
            Text(row.detail).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-2")) {
                if row.span != nil {
                    Button(text["assembly.show"]) { assembly.showStep(row) }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
                Button(text["assembly.revertWhat"]) { assembly.planRevert(row.number) }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        .padding(.vertical, CGFloat.ds("s-1"))
        .accessibilityElement(children: .contain)
    }
}

/// «Сделать нотами…»: the dialog says what will change before anything does.
struct AssemblyApplyView: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText

    var body: some View {
        let journal = assembly.journal
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text.format("assembly.applyTitle", journal.applyBars)).dsType(.heading).foregroundStyle(Color.ds("ink"))
            Text(text[assembly.info.applied ? "assembly.applyAgain" : "assembly.applyFirst"]).dsType(.label).foregroundStyle(Color.ds("ink"))
            ForEach(journal.applyLines) { AssemblyLineRow(line: $0, text: text) }
            Text(text["assembly.applyMarks"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            HStack(spacing: CGFloat.ds("s-2")) {
                Button { assembly.hear(AssemblyModel.notes) } label: {
                    Label { Text(text["assembly.hearNotes"]) } icon: { Icon(name: "play", size: CGFloat.ds("icon-s")) }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                Spacer()
                Button(text["assembly.cancel"]) { assembly.closeApply() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                Button(text["assembly.apply"]) { assembly.apply() }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
            }
        }
        .assemblyPanel(width: 420)
        .accessibilityElement(children: .contain)
    }
}

/// What the last step of the assembly did, with the step that undoes it.
struct AssemblyToastView: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText
    let toast: AssemblyModel.Toast

    var body: some View {
        HStack(spacing: CGFloat.ds("s-3")) {
            Text(toast.text).dsType(.label).foregroundStyle(Color.ds("ink"))
            if toast.undo != nil {
                Button(text["merge.toastUndo"]) { assembly.undoToast() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            Button { assembly.closeToast() } label: { Icon(name: "close", size: CGFloat.ds("icon-s")).frame(width: 28, height: 28) }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["bar.close"])
        }
        .padding(.leading, CGFloat.ds("s-4"))
        .padding(.trailing, CGFloat.ds("s-2"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
    }
}

/// Everything the comparison puts over the score: the chips at the bottom, the card or a sheet above them, and the toast.
struct AssemblyOverlay: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText
    let bottom: CGFloat

    var body: some View {
        VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
            Spacer(minLength: 0)
            if let toast = assembly.toast {
                AssemblyToastView(assembly: assembly, text: text, toast: toast)
                    .frame(maxWidth: .infinity)
                    .transition(.opacity)
            }
            if assembly.open {
                Group {
                    if let sheet = assembly.sheet, assembly.carry != nil {
                        AssemblySheetView(assembly: assembly, text: text, sheet: sheet)
                    } else if assembly.applyOpen {
                        AssemblyApplyView(assembly: assembly, text: text)
                    } else if assembly.journalOpen {
                        AssemblyJournalView(assembly: assembly, text: text)
                    } else if let card = assembly.card {
                        AssemblyPickView(assembly: assembly, text: text, card: card)
                    } else if let source = assembly.source(assembly.shown) {
                        AssemblySourceCard(assembly: assembly, text: text, source: source)
                    }
                }
                .transition(.opacity)
                AssemblyChipsBar(assembly: assembly, text: text)
                    .frame(maxWidth: .infinity)
            } else if let offer = assembly.offer {
                AssemblyOfferCard(assembly: assembly, text: text, offer: offer)
                    .transition(.opacity)
            }
        }
        .padding(.horizontal, CGFloat.ds("s-4"))
        .padding(.bottom, bottom)
        .animation(.easeOut(duration: 0.16), value: assembly.open)
        .animation(.easeOut(duration: 0.16), value: assembly.toast)
    }
}

/// The source shown, by the bar while comparing: «Сравнение · Кавер», or «Ноты · призрак» while the sheet is open.
struct AssemblyShownLabel: View {
    @ObservedObject var assembly: AssemblyModel
    let text: UIText

    var body: some View {
        HStack(spacing: 6) {
            if let source = assembly.source(assembly.shown) { ToneDot(source: source) }
            Text("\(text["assembly.title"]) · \(assembly.shownLabel)").dsType(.label).foregroundStyle(Color.ds("ink"))
        }
        .padding(.horizontal, CGFloat.ds("s-3"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .background(Color.ds("surface-2"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .accessibilityElement(children: .combine)
    }
}
