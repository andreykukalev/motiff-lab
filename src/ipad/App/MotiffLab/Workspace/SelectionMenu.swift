import SwiftUI

/// The menu over the selection (ds.css `.menu`, 0018 section 3): the actions a musician takes on a passage. It holds only
/// what applies and what the iPad has so far: Focus, Note, Bookmark and Draft (v1.1, none on a draft's view), then
/// Insights (none for a passage without notes), and More with Highlight; the piano roll is the pill's (Andrei's 0.8 test,
/// finding 7). Bookmark and Note turn the menu into their cards, as on the web. Its actions show their icons alone (v0.5.2,
/// #495 UX-11), each at a fingertip's square, with its word for VoiceOver.
struct SelectionMenu: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    @ObservedObject var penModel: PenModel
    @ObservedObject var marksModel: MarksModel
    @ObservedObject var player: Player
    @ObservedObject var contents: ContentsController
    let text: UIText
    @Binding var insightsOpen: Bool
    /// «Черновик» (v1.1, ADR 0034): the «В черновик» sheet for the passage; nil where the notes shown are a draft's.
    let toDraft: (() -> Void)?
    /// On a view only to be shown (Notes or a draft over the piece, v1.1): only what works there (`offered`).
    let viewOnly: Bool
    @State private var more = false
    @EnvironmentObject private var account: Account
    @EnvironmentObject private var settings: DeviceSettings
    @EnvironmentObject private var assistant: AssistantController

    var body: some View {
        Group {
            if marksModel.noteDraft != nil {
                NoteCard(marksModel: marksModel, text: text)
                    .frame(width: 320)
                    .padding(CGFloat.ds("s-3"))
                    .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else if let draft = contents.draft, draft.id == nil {
                BookmarkCard(contents: contents, text: text) {
                    let selection = selectionModel.selection
                    let title = score.bookmarkTitle
                    Task { await contents.save(over: selection, defaultTitle: title) }
                }
                .frame(width: 300)
                .padding(.vertical, CGFloat.ds("s-3"))
                .padding(.horizontal, CGFloat.ds("s-4"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else if assistant.here != nil, selectionModel.selection != nil {
                // The assistant at the selection where the menu was (#582 items 17.2 and 17.4).
                AssistantHereCard(assistant: assistant, score: score, selectionModel: selectionModel, text: text, ask: askHere) { offer in
                    Task { await score.practiseSuggestion(offer, on: player) }
                }
            } else {
                let card = more || selectionModel.phraseOffer != nil
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    actions
                    if more {
                        Rectangle().fill(Color.ds("hairline")).frame(height: 1)
                        Button {
                            // The assistant opens at the selection, where the menu was (#582 item 17.2).
                            AssistantHereCard.open(assistant, score: score, settings: settings)
                        } label: {
                            HStack(spacing: 6) {
                                Icon(name: "insight")
                                Text(text["ask.menu"])
                            }
                        }
                        .buttonStyle(DSItemStyle())
                        // A new boundary at the selection's first note (the phrases spec, decision 5), where none starts.
                        if startsPhrase {
                            Button(text["phrases.start"]) { score.startPhrase() }
                                .buttonStyle(DSItemStyle())
                        }
                    }
                    // The phrase the selection was changed by an edit: its new boundaries offered (ScorePhraseEdits.swift).
                    if let offer = selectionModel.phraseOffer {
                        Rectangle().fill(Color.ds("hairline")).frame(height: 1)
                        PhraseOfferCard(score: score, offer: offer, text: text)
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-2"))
                .padding(.vertical, CGFloat.ds("s-1"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: card ? CGFloat.ds("r-l") : 26))
                .overlay(RoundedRectangle(cornerRadius: card ? CGFloat.ds("r-l") : 26).strokeBorder(Color.ds("hairline")))
            }
        }
        .dsShadow("shadow-menu")
        .fixedSize()
        .background {
            GeometryReader { proxy in
                Color.clear
                    .onAppear { score.menuSize(proxy.size) }
                    .onChange(of: proxy.size) { _, size in score.menuSize(size) }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["menu.label"])
    }

    private func askHere(_ question: String) {
        guard let role = assistant.here?.role, let ask = AssistantHereCard.ask(score: score, text: text, role: role, relay: account.assistant) else { return }
        Task { await assistant.askHere(question, about: ask) }
    }

    /// Where the selection starts and ends: as bar.beat, "9.1 – 12.3", under the one playback position (v0.5.2); beat by
    /// beat until the sound has given the bars' beats. A selection that is a phrase exactly is named by it: «Фраза 2 · 4.3 –
    /// 8.3» (the phrases spec, «Polish of option A» 5).
    private var menuSpan: String? {
        var span = selectionModel.selection?.beatSpan(text)
        if let range = focusModel.playRange?.range, let beats = player.barBeats.span(range) {
            span = text.format("pill.span", beats.first, beats.last)
        }
        guard let span, let unit = selectionModel.phrases?.unit(selected: selectionModel.selection) else { return span }
        return text.format("selection.phrase", String(unit.number), span)
    }

    /// «Вся фраза · 4.3 – 8.3» for a selection inside a phrase (the phrases spec, decision 5, from option C); nil otherwise.
    private var wholePhrase: String? {
        guard !viewOnly, let unit = selectionModel.phrases?.unit(enclosing: selectionModel.selection) else { return nil }
        guard let beats = player.barBeats.span(unit.selectableOnset..<unit.selectableEnd) else { return text["phrases.whole"] }
        return text.format("phrases.wholeSpan", text.format("pill.span", beats.first, beats.last))
    }

    /// Whether More offers «Начать фразу здесь»: on the piece's working notes with the phrases' marks shown, where no phrase
    /// starts at the selection already.
    private var startsPhrase: Bool {
        guard !viewOnly, !marksModel.phraseMarksHidden, let selection = selectionModel.selection, let phrases = selectionModel.phrases else { return false }
        return !phrases.units.isEmpty && !phrases.units.contains { $0.onset == selection.onset }
    }

    /// The menu's items besides Expand, «Черновик», Hide and Clear: Note and Bookmark, Insights over notes, and More. On a
    /// view only to be shown, none of them, as music-core refuses them there (v1.1).
    enum Item { case note, bookmark, insights, more }

    static func offered(viewOnly: Bool, notes: Bool) -> [Item] {
        guard !viewOnly else { return [] }
        return notes ? [.note, .bookmark, .insights, .more] : [.note, .bookmark, .more]
    }

    private var actions: some View {
        let offered = Self.offered(viewOnly: viewOnly, notes: !(selectionModel.selection?.noteIds.isEmpty ?? true))
        return HStack(spacing: CGFloat.ds("s-1")) {
            // Where the selection starts and ends, by bar and beat.
            if let span = menuSpan {
                Text(span)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("accent"))
                    .padding(.horizontal, CGFloat.ds("s-2"))
                ItemDivider()
            }
            if let wholePhrase {
                Button(wholePhrase) { score.selectWholePhrase() }
                    .buttonStyle(DSChipStyle())
                ItemDivider()
            }
            if !penModel.penOn {
                // Expand into focus: two arrows to the corners, unlike Insights' magnifier (#511 section 9).
                iconItem("expand", text["focus.enter"], on: false) { focusModel.enter() }
                    .accessibilityHint(text["focus.enterHint"])
            }
            // Note before Bookmark, as the kit's menu; a sheet of paper, not the kit's speech bubble, which read as the
            // assistant (Andrei, 2026-11-18).
            if offered.contains(.note) {
                iconItem("memo", text["menu.note"], on: false) {
                    contents.cancel()
                    marksModel.startNote()
                }
            }
            if offered.contains(.bookmark) {
                iconItem("bookmark", text["menu.bookmark"], on: false) {
                    marksModel.noteDraft = nil
                    contents.start(title: score.bookmarkTitle)
                }
            }
            // The passage into a draft (v1.1): a fragment of these notes, which stay as they are.
            if let toDraft {
                iconItem("assemble", text["menu.draft"], on: false, action: toDraft)
            }
            ItemDivider()
            if offered.contains(.insights) {
                iconItem("insight", text["bar.insights"], on: insightsOpen) { insightsOpen.toggle() }
            }
            if offered.contains(.more) {
                iconItem("more", text["menu.more"], on: more) { more.toggle() }
            }
            // Hide (#533 11): the selection controls go, the selection staying and the playback controls as they are (#582
            // item 10); ✕ is the one that clears it.
            iconItem("practice", text["menu.hide"], on: false) { score.practise(.selection(shown: false)) }
            ClearSelectionButton(score: score, focusModel: focusModel, text: text)
        }
    }
}

/// Clear selection (#338): the one way to let go of a selection, as a tap elsewhere on the page or the roll keeps it.
struct ClearSelectionButton: View {
    @ObservedObject var score: ScoreController
    /// Practice view is off in Expand (`ScoreController.practicing`).
    @ObservedObject var focusModel: FocusModel
    let text: UIText

    /// At a fingertip's square, as the selection menu's other icons (#495 UX-11).
    var body: some View {
        Button {
            score.clearSelection()
        } label: {
            Icon(name: StylusRules.RingControl.clearSelection.icon)
        }
        .buttonStyle(DSIconItemStyle())
        // Escape removes the selection too (the modes, version 0.5); in practice view it leaves that view (#533 11).
        .keyboardShortcut(!score.practicing ? KeyboardShortcut(.escape, modifiers: []) : nil)
        .accessibilityLabel(text["menu.clearSelection"])
        .help(text["menu.clearSelection"])
    }
}
