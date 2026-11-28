import SwiftUI

/// The pill, since v1.7 e-1 the bottom shelf of «Пюпитр» (spec score-reading §3.2): at the left Play with the place, with a
/// selection Цикл with its span and the phrase's ‹ ›; in the middle the tempo, the hands and the metronome; at the right
/// the piano roll and the assistant, where the hand looks for them (Andrei's 0.8 test, findings 7 and 8). Its controls show
/// their icons alone, their words for VoiceOver and the pointer's tip (v0.10, finding 22); what shows a value keeps it: the
/// place, Цикл's span, the tempo and the hands, which say the mode. Where it has no room, the tempo, the metronome and the
/// roll go into «⋯» (Andrei's answer A), then the phrase's steps. Every control takes a fingertip (`h-touch`, the shelf's
/// `itemReach`). It shows
/// what it is handed and calls the actions it is handed; the shelf it lies on is the screen's
/// (`ScoreScreenContent.bottomShelf`).
struct Pill: View {
    @ObservedObject var player: Player
    @ObservedObject var selectionModel: SelectionModel
    @ObservedObject var focusModel: FocusModel
    /// A draft's приёмы (v1.1): while their pill steps through them, the phrase's steps are not offered here.
    @ObservedObject var draftModel: DraftTechniquesModel
    @ObservedObject private var metronome = Metronome.shared
    let text: UIText
    /// Expand's passage while it is open, which Play plays round and round (`ScoreController.expandPassage`); nil out of it.
    let passage: Range<Double>?
    /// The phrase before (-1) or after (1), from the pill's ‹ › (`ScoreController.step`).
    let step: (Int) -> Void
    /// The piece tempo the player typed, kept with the piece (`ScoreController.setPieceTempo`).
    let setPieceTempo: (Int?) -> Void
    /// The roll's drawer, which its button opens and puts away.
    @Binding var rollOpen: Bool
    /// Whether the assistant's panel is open: its button is lit.
    let assistantOpen: Bool
    /// The assistant's button: its panel with the conversation it had, or closed (ScoreWorkspace.assistantPressed).
    let assistantPressed: () -> Void

    /// The tempo's popover opened from «⋯» while the pill has no room for the tempo (finding 22).
    @State private var tempoFromMore = false

    /// How much of the pill has room on it (finding 22): all of it; all but the tempo, the metronome and the roll, which go
    /// into «⋯»; and with less still, the phrase's steps go there too.
    private enum Room { case full, compact, tight }

    var body: some View {
        // As built while it fits; with no room (a narrow Split View, say), the tempo, the metronome and the roll go into
        // «⋯» and play, the place, Цикл and the hands stay (finding 22, Andrei's answer A of 2026-11-19 09:55 UTC), then the
        // phrase's steps too. SwiftUI measures: the first that fits the width the shelf gives, else the last.
        ViewThatFits(in: .horizontal) {
            items(.full)
            items(.compact)
            items(.tight)
        }
        .frame(height: CGFloat.ds("h-touch"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["transport.label"])
    }

    /// The three groups across the shelf: the left and the right at the shelf's ends, the middle centred between them
    /// (where one end is wider than its half, the middle gives way to it and never covers it).
    private func items(_ room: Room) -> some View {
        HStack(spacing: CGFloat.ds("s-4")) {
            start(room).fixedSize().frame(maxWidth: .infinity, alignment: .leading)
            middle(room).fixedSize()
            end(room).fixedSize().frame(maxWidth: .infinity, alignment: .trailing)
        }
    }

    /// Play with the place and Цикл, which wait for the player, and the phrase's steps while there is room for them.
    private func start(_ room: Room) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            // The playback model (v0.5.2, #495 UX-10): one Play, cobalt or ink by the place, and Цикл with the span; in
            // Expand the same Play over its passage, which always loops (v0.6.4, spec 8).
            PositionTransport(player: player, passage: passage, text: text)
                .disabled(!player.ready)
            if room != .tight {
                stepButtons.disabled(!player.ready)
            }
        }
    }

    /// The tempo, the hands and the metronome; without room the hands alone, the rest in «⋯».
    private func middle(_ room: Room) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            if room == .full {
                // The tempo observes only itself (#511 section 17), so it opens and changes while playing.
                TempoControl(tempo: player.tempo, text: text, setPiece: setPieceTempo)
                    .equatable()
            }
            handsControls
            if room == .full {
                ItemDivider()
                // The metronome clicks at once, with or without Play; held, a slide sets its tempo (#521 section 15). Its icon
                // alone, as the roll's and the assistant's (finding 22).
                iconItem("metronome", text["transport.metronome"], on: metronome.running) { metronome.toggle() }
                    .modifier(MetronomeHold(metronome: metronome, text: text) { metronome.toggle() })
            }
        }
        .disabled(!player.ready)
    }

    /// The roll and the assistant, with «⋯» before them where the shelf has no room.
    private func end(_ room: Room) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            if room == .full {
                // The roll in its drawer (finding 7); in Expand the bar's Notes | Piano roll shows the passage as a roll.
                if !focusModel.focused {
                    iconItem("roll", text["bar.roll"], on: rollOpen) { rollOpen.toggle() }
                }
            } else {
                more(steps: room == .tight)
            }
            // The assistant (finding 8, Andrei's card of 2026-11-18 21:49 UTC): its panel with the last conversation, or
            // closed; an Ask elsewhere opens it empty.
            iconItem("note", text["pill.assistant"], on: assistantOpen, action: assistantPressed)
                .accessibilityHint(text["pill.assistantHint"])
        }
    }

    /// «⋯» with what the shelf had no room for, by their words, in the shelf's order: with `steps`, the phrase before and
    /// after; the tempo, whose item opens its popover here; the metronome on or off; the roll's drawer, not in Expand. All
    /// but the roll wait for the player.
    private func more(steps: Bool) -> some View {
        Menu {
            if steps && stepsShown {
                Button {
                    step(-1)
                } label: {
                    Label(text["focus.previous"], image: "ic-back")
                }
                .disabled(!player.ready || !focusModel.canStepBack)
                Button {
                    step(1)
                } label: {
                    Label(text["focus.next"], image: "ic-fwd")
                }
                .disabled(!player.ready || !focusModel.canStepOn)
            }
            TempoMenuItem(tempo: player.tempo, text: text) { tempoFromMore = true }
                .disabled(!player.ready)
            Toggle(isOn: Binding(get: { metronome.running }, set: { _ in metronome.toggle() })) {
                Label(text["transport.metronome"], image: "ic-metronome")
            }
            .disabled(!player.ready)
            if !focusModel.focused {
                Toggle(isOn: $rollOpen) {
                    Label(text["bar.roll"], image: "ic-roll")
                }
            }
        } label: {
            Icon(name: "more")
        }
        .menuStyle(.button)
        .buttonStyle(DSIconItemStyle(on: tempoFromMore))
        .accessibilityLabel(text["bar.more"])
        // «Ещё» as the bar's More is: the walk tells them apart by this.
        .accessibilityIdentifier("ml-pill-more")
        .help(text["bar.more"])
        .tempoPopover(isPresented: $tempoFromMore, tempo: player.tempo, text: text, setPiece: setPieceTempo)
        // With room again «⋯» goes, and its popover with it, not to come back when the room goes again.
        .onDisappear { tempoFromMore = false }
    }

    /// Hands separately at the instrument (#328): both, right or left, two touches away. The hands are the ones music-core
    /// gives the notes, and the menu says when they were worked out rather than read from the file.
    @ViewBuilder private var handsControls: some View {
        if player.handsHeld.canSeparate {
            let label = text.format("pill.handsLabel", text[player.hands.labelKey])
            Menu {
                Picker(label, selection: $player.hands) {
                    ForEach(PlayerHands.allCases, id: \.self) { Text(text[$0.labelKey]).tag($0) }
                }
                if player.handsHeld.inferred {
                    Text(text["import.handsInferredWhy"])
                }
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "hands")
                    Text(text[player.hands.labelKey])
                }
            }
            .menuStyle(.button)
            .buttonStyle(DSItemStyle(on: player.hands != .both))
            .accessibilityLabel(label)
            .accessibilityHint(player.handsHeld.inferred ? text["import.handsInferredWhy"] : "")
        }
    }

    /// The phrase before or after without lifting it into focus (#328), with a selection out of Expand; not while a draft's
    /// приёмы are stepped through (`DraftTechniquesModel.stepping`), so one pair of ‹ › is on the screen.
    private var stepsShown: Bool {
        selectionModel.selection != nil && !focusModel.focused && !draftModel.stepping
    }

    @ViewBuilder private var stepButtons: some View {
        if stepsShown {
            stepItem("back", text["focus.previous"], enabled: focusModel.canStepBack) { step(-1) }
            stepItem("fwd", text["focus.next"], enabled: focusModel.canStepOn) { step(1) }
        }
    }
}
