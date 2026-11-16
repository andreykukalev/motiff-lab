import SwiftUI
import WebKit

/// A piece's score (0018 section 2, design/kit/screens/s2_score_page.png): the score is the whole screen, A4 pages in one
/// column in portrait and two to a row in landscape, with the pencil marks on them (0019 section 2.2). Verovio's SVG is in
/// one web view that runs the web app's score modules (ScoreController, Web/score.js). Nothing is docked: the bar floats
/// over the top with the way back, the title, the pages in view and the actions, and the pill floats at the bottom with
/// play, the tempo and the metronome. Colours come from the design kit's tokens, Daylight or Night with
/// the iPad's appearance. In phrase focus (0018 section 4) the selection lifts into a card across the screen with the
/// page dimmed behind it, previous and next on the card's edges, and the bar becomes the focus bar. The piano roll opens
/// in a drawer from the bottom (section 4), Insights in a sheet over the right third in landscape or the lower half in
/// portrait (section 6), and the Contents, the player's bookmarks and the pages, in a drawer over the left of the page in
/// landscape or the lower half in portrait (section 5); one of the three at a time.
struct ScoreScreen: View {
    let piece: OpenPiece
    /// Opens the piece's other view in its place, for PDF | Notes (0023); nil where the library has none.
    var switchView: ((Landing) async -> Void)? = nil
    /// The tag of a view of the switch shown on its own over the piece (version 0.6): other Notes, from a copy never
    /// saved; nil for the piece itself.
    var viewing: String? = nil
    /// Opens the piece's project file again in this screen's place, at a place in the music (version 0.6: «Сделать
    /// рабочими» and the Sources toast's Undo); nil where the library has none.
    var reopen: ((Landing) async -> Void)? = nil
    private let text = UIText.current
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var account: Account
    @EnvironmentObject private var settings: DeviceSettings
    @StateObject var player = Player()
    @StateObject var score = ScoreController()
    @StateObject var roll = RollController()
    @State private var more = false
    /// Whether Check, Other versions or Settings was opened from More, so it shows "‹ More" (testing item 20).
    @State private var fromMore = false
    @StateObject var insights = InsightsController()
    /// The model-backed assistant (#582 item 17): its side panel and the piece's conversations.
    @StateObject var assistant = AssistantController()
    /// The piano roll's drawer (0018 section 4), from the bar or the selection menu.
    @State var rollOpen = false
    /// Whether the roll drawer was open when the phrase went into focus as a roll, so Back opens it again.
    @State private var rollBeforeFocus = false
    /// The Contents drawer and the loop as they were when Expand opened, given back with the workspace (#521 17).
    @State private var contentsBeforeFocus = false
    /// The roll drawer's share of the screen's height in landscape (true) and portrait, as the player left it with its handle
    /// (RollDrawerHeight, #511 section 7.1), and the share while the handle is dragged.
    @State private var drawerShares: [Bool: CGFloat] = [true: RollDrawerHeight.saved(landscape: true), false: RollDrawerHeight.saved(landscape: false)]
    @State private var drawerDrag: CGFloat?
    /// How tall the transport floating at the bottom is now: the selection's toast stands above it (v0.6.4, spec 7).
    @State private var transportHeight: CGFloat = 0
    /// The Insights sheet (0018 section 6), from the bar or the selection menu.
    @State private var insightsOpen = false
    @StateObject private var contents = ContentsController()
    /// The Contents drawer (0018 section 5), from the piece's title in the bar.
    @State private var contentsOpen = false
    /// The Check drawer (0018 section 9), from More or a flag on the score; where Insights goes, and one of the two.
    @State private var checkOpen = false
    /// Other versions (0022), from More or a version's label on the score: where Check goes, and one of them at a time.
    @State private var versionsOpen = false
    /// The Settings sheet (0018 section 10), from More: over the score, which follows each change at once.
    @State private var settingsOpen = false
    /// The Reference's circle of fifths, open over the score.
    @State private var referenceOpen = false
    /// Piece DNA (0018 section 11; testing item 22), from More: the map of the whole piece over the score; nil when closed.
    @State private var dna: DnaMap?
    /// Whether Piece DNA was opened from More, so it shows "‹ More".
    @State private var dnaFromMore = false
    /// The piece's originals on this iPad (version 0.3): where each one is, and the PDF linked where it lies in Files; known
    /// from the piece's opening, so PDF | Notes is whole from the first frame (#495 BUG-02).
    @StateObject private var originals: OriginalsController
    /// The resume point this visit brings up to date (#327), so the piece keeps one for it rather than one for each save.
    @State private var resumeId: String?
    /// The piece's sources by layers and the switch of all its views (version 0.6, 0030).
    @StateObject private var sources = SourcesController()
    /// A view of the switch shown over the score: another PDF or photos as they are, or other Notes; nil while none is.
    @State private var shownView: ShownView?
    /// The piece is being handed to its other view (PDF | Notes): a second press on the switch meanwhile does nothing
    /// (#582 item 2), as the first is already saving, closing and opening the piece.
    @State var handingOver = false
    /// The roll, Insights and the Contents as they were when Sources opened over them, given back as it closes (#582
    /// item 4: opening and closing Sources leaves the view as it was); nil while Sources is closed.
    @State private var beforeSources: SourcesShelved?

    /// The Insights sheet's width beside the score in landscape (ds.css `.sheet`).
    static let sheetWidth: CGFloat = 380
    /// The Contents drawer's width over the left of the page in landscape (ds.css `.sheet.left`).
    static let contentsWidth: CGFloat = 320

    var body: some View {
        GeometryReader { screen in
            let landscape = screen.size.width > screen.size.height
            let kept = drawerShare(landscape) * screen.size.height
            page(
                drawer: rollOpen ? (drawerDrag.map { $0 * screen.size.height } ?? kept) : 0,
                inset: rollOpen ? kept : 0,
                screen: screen.size,
                sheet: sheetPlace(landscape: landscape, height: screen.size.height),
                contentsPlace: contentsOpen ? (landscape ? .side : .bottom(screen.size.height / 2)) : .none
            )
        }
        // The keyboard never changes the screen's size (#582 item 16): before, a chat's keyboard shrank it, portrait read
        // as landscape and the sheets, the drawer, the pill and the page's room moved, and back again as it went. A chat
        // keeps itself clear of the keyboard (AskFlow, KeyboardRoom).
        .ignoresSafeArea(.keyboard)
        .environmentObject(assistant)
        // One thing at a time on the right: the assistant's panel or the Insights, Check and Other versions sheet.
        .onChange(of: assistant.panelOpen) { _, open in if open { insightsOpen = false; checkOpen = false; versionsOpen = false } }
        .onChange(of: insightsOpen || checkOpen || versionsOpen) { _, open in if open { assistant.close() } }
    }

    /// Where the Insights sheet is: none, beside the score on the right, or over the lower half.
    enum SheetPlace: Equatable {
        case none, side
        case bottom(CGFloat)

        var bottomHeight: CGFloat {
            if case .bottom(let height) = self { return height }
            return 0
        }
    }

    /// What loops while a variation is heard: what play takes for the selection, on to the variation's end.
    private var variationRange: Range<Double>? {
        guard let range = score.playRange?.range else { return nil }
        return range.lowerBound..<max(range.upperBound, insights.trial?.end ?? 0)
    }

    /// Piece DNA from More: the map music-core gives for the piece as it is now, the selection as its first idea.
    private func openDna(fromMore: Bool) {
        let id = piece.id
        // «Идеи по смыслу» (#582 item 15): the ideas by what they are in the music.
        let request = #"{"ideas":"musical"}"#
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: id, "dna", request) }
            guard let map = DnaMap(answer: answer) else { return }
            dnaFromMore = fromMore
            dna = map
        }
    }

    /// ‹ More: the place opened from More goes, and More opens again once it has (after a sheet's dismissal, so the popover can show).
    private func backToMore(_ putAway: () -> Void) {
        putAway()
        fromMore = false
        Task {
            try? await Task.sleep(for: .milliseconds(350))
            more = true
        }
    }

    /// What the sheet over the right holds: Sources, Check, or Insights.
    @ViewBuilder private func sheetContent() -> some View {
        if versionsOpen && sourcesOn {
            sourcesSheet
        } else if checkOpen {
            CheckDrawer(score: score, text: text, close: { checkOpen = false }, back: fromMore ? (label: text["bar.more"], action: { backToMore { checkOpen = false } }) : nil)
        } else {
            InsightsSheet(insights: insights, score: score, player: player, range: variationRange, text: text) { insightsOpen = false }
        }
    }

    /// The Contents drawer where it goes: over the left of the page in landscape, from the bottom in portrait.
    @ViewBuilder private func contentsDrawer(_ place: SheetPlace) -> some View {
        switch place {
        case .none:
            EmptyView()
        case .side:
            ContentsDrawer(contents: contents, score: score, text: text) { contentsOpen = false }
                .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                .dsShadow("shadow-sheet")
                .frame(width: Self.contentsWidth)
                .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
                .padding([.leading, .bottom], CGFloat.ds("s-3"))
                .transition(.move(edge: .leading).combined(with: .opacity))
        case .bottom(let height):
            VStack {
                Spacer(minLength: 0)
                ContentsDrawer(contents: contents, score: score, text: text) { contentsOpen = false }
                    .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                    .dsShadow("shadow-sheet")
                    .frame(height: height)
                    .padding(.horizontal, CGFloat.ds("s-3"))
            }
            .transition(.move(edge: .bottom))
            .ignoresSafeArea(edges: .bottom)
        }
    }

    /// The drawer's Expand: only with a selection (#521 19.1), without one the dimmed button says why. Expand inspects the
    /// passage, so the Pen, which would write nothing there, is put away first.
    private func expandFromDrawer() {
        guard score.selection != nil else {
            score.showNotice("roll.expandNeedsSelection")
            return
        }
        if score.penOn { score.penOn = false }
        rollBeforeFocus = true
        rollOpen = false
        score.expandRoll()
    }

    /// Over the bottom of the page: the chip back to the playing, the pill, and in practice view its own controls (#533 11).
    @ViewBuilder private var transport: some View {
        // Scrolled away from the playing, a chip leads back to it (testing item 4).
        if let away = score.playheadAway, player.state != .stopped, !score.focused, score.practiceChrome.transport, let bar = player.barNumber(at: away) {
            Button {
                score.returnToPlayhead()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "play", size: CGFloat.ds("icon-s"))
                    Text(text.format("transport.backToPlay", String(bar))).dsType(.label)
                }
                .foregroundStyle(Color.ds("accent"))
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(height: 32)
                .modifier(FloatingSurface())
            }
            .buttonStyle(.plain)
            .transition(.opacity)
        }
        // Practice view (#533 11): the pill gives way to Play/Pause and Leave, shown a few seconds after a tap.
        // The pill always shows (the playback model, v0.5.2, #479 item 22): it is where the music plays.
        if score.practiceChrome.transport {
            Pill(player: player, score: score, text: text)
                .transition(.opacity)
        }
        if score.practiceChrome.controls {
            PracticeControls(player: player, score: score, text: text)
                .transition(.opacity)
        }
    }

    /// Whether the selection's menu is open: a selection, outside Expand, the roll's drawer closed, and the selection
    /// controls shown (#582 item 10: `ScoreController.selectionControlsShown`, whatever the playback controls do).
    private var selectionMenuOpen: Bool {
        score.selection != nil && !score.focused && !rollOpen && score.selectionControlsShown
    }

    /// The score and everything that floats over it.
    private func layers(drawer: CGFloat, screen: CGSize, sheet: SheetPlace, contentsPlace: SheetPlace, covered: CGFloat) -> some View {
        let gap = CGFloat.ds("s-3")
        let pillBottom = covered > 0 ? covered + gap : CGFloat.ds("s-4")
        // The full assistant's panel sits beside the score, which keeps the rest of the width (#582 item 17.1).
        let side = assistant.panelOpen ? Self.assistantWidth(landscape: screen.width > screen.height) + gap : 0
        let pillTrailing: CGFloat = sheet == .side ? Self.sheetWidth + gap : side
        let pillLeading: CGFloat = contentsPlace == .side ? Self.contentsWidth + gap : 0
        return ScoreWebView(score: score, piece: piece, dark: colorScheme == .dark)
            // While a field has the keyboard, a press on the score only puts it away (TypingFocus, v0.6.4).
            .padding(.trailing, side).overlay { TypingShield() }
            .overlay(alignment: .topLeading) {
                // The menu over the selection, where the page places it (the web app's menuPlace). While the roll's drawer is
                // open it gives way to the drawer's head and the pill, so Loop is not shown twice (#338). Hidden with the selection
                // controls (#582 item 10), whatever the playback controls do.
                if selectionMenuOpen, let place = score.menuPlace {
                    SelectionMenu(score: score, player: player, contents: contents, text: text, rollOpen: $rollOpen, insightsOpen: $insightsOpen)
                        .modifier(MenuDrag(score: score))
                        .offset(x: place.x, y: place.y)
                }
            }
            // Put away, the menu forgets where the player dragged it (#582 item 7).
            .onChange(of: selectionMenuOpen) { _, open in score.menuShown(open) }
            .overlay(alignment: .topLeading) {
                // A version's marker tapped (testing item 33): the popover under it; a tap elsewhere puts it away.
                if let pick = score.markerPick, !score.focused {
                    GeometryReader { area in
                        ZStack(alignment: .topLeading) {
                            Color.clear.contentShape(Rectangle()).onTapGesture { score.closeMarker() }
                            MarkerPopover(score: score, text: text, pick: pick)
                                .offset(x: min(max(CGFloat(pick.left), CGFloat.ds("s-3")), area.size.width - 312), y: CGFloat(pick.bottom) + CGFloat.ds("s-1"))
                        }
                    }
                }
            }
            .overlay(alignment: .bottom) {
                // What was just done in the Sources sheet, with Undo where it can be undone (version 0.6); beside the sheet.
                if let toast = sources.toast, sourcesOn {
                    SourcesToastView(sources: sources, toast: toast, text: text)
                        .padding(.bottom, pillBottom + 56)
                        .padding(.trailing, pillTrailing)
                        .transition(.opacity)
                } else if let toast = score.mergeToast {
                    MergeToastView(score: score, text: text, toast: toast)
                        .padding(.bottom, pillBottom + 56)
                        .transition(.opacity)
                } else if score.practiceChrome.hints {
                    // «Выделение снято · Вернуть» and the hint to hold (#511 11), with ⌘Z and ⇧⌘Z; not in practice view (#533 11).
                    // Above the pill and whatever floats with it, as tall as that is now (v0.6.4, spec 7: never under Loop or the metronome).
                    SelectionToastView(score: score, text: text).padding(ToastPlace.insets(pillBottom: pillBottom, transport: transportHeight, gap: gap, leading: pillLeading, trailing: pillTrailing))
                }
            }
            .fullScreenCover(isPresented: $score.comparing) {
                CompareVersionsScreen(score: score, text: text)
            }
            .overlay {
                // The phrase as a roll over the card (testing item 12); the drawer's roll moves here, so it shows once the drawer is gone.
                if rollPlace == .focus {
                    FocusRollCard(roll: roll, score: score, text: text)
                }
            }
            .ignoresSafeArea(edges: .bottom)
            .background(Color.ds("desk").ignoresSafeArea())
            #if DEBUG
            .overlay(alignment: .bottomLeading) {
                // The screenshot walk chooses a phrase here by its number (ScreensHook).
                if ScreensHook.active {
                    Button { score.selectPhrase(ScreensHook.nextPhrase()) } label: {
                        Color.clear.frame(width: 8, height: 8).contentShape(Rectangle())
                    }
                    .accessibilityLabel("ml-screens-phrase")
                }
            }
            #endif
            .overlay(alignment: .top) {
                VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
                    if score.focused {
                        FocusBar(score: score, player: player, variationRange: variationRange, text: text)
                    } else {
                        ScoreBar(title: piece.title, pages: score.pages, text: text, player: player, score: score, more: $more, fromMore: $fromMore, rollOpen: $rollOpen, insightsOpen: $insightsOpen, contentsOpen: $contentsOpen, checkOpen: $checkOpen, versionsOpen: $versionsOpen, settingsOpen: $settingsOpen, pdf: toPdf, openDna: { openDna(fromMore: true) }, openReference: { referenceOpen = true },
                                 sourceLayers: switchLayers, currentView: currentView, pickView: switchPick, viewing: viewing,
                                 width: screen.width - 2 * CGFloat.ds("s-4")) { dismiss() }
                    }
                    if sourcesOn && !score.focused { recordingCapsule }
                    if !score.focused {
                        // The chosen mode's options under the bar (#511 4.2, option B): the Pen's palette, Select's row.
                        ModeOptions(score: score, text: text)
                    }
                    if score.merge != nil && !score.focused && !score.comparing {
                        MergeShowSwitch(score: score, text: text)
                            .transition(.opacity)
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-4"))
                .padding(.top, CGFloat.ds("s-3"))
                .animation(.easeOut(duration: 0.16), value: score.penOn)
                .animation(.easeOut(duration: 0.16), value: score.focused)
            }
            .overlay(alignment: .bottom) {
                if rollOpen {
                    RollDrawer(roll: roll, score: score, text: text, span: rollSpan, expand: expandFromDrawer, resize: { dy, ended in
                        resizeDrawer(by: dy, ended: ended, screen: screen)
                    }, step: { direction in
                        stepDrawer(direction, screen: screen)
                    }) { rollOpen = false }
                        .frame(height: drawer)
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .transition(.move(edge: .bottom))
                        .ignoresSafeArea(edges: .bottom)
                }
            }
            .overlay(alignment: .bottomTrailing) {
                switch sheet {
                case .none:
                    EmptyView()
                case .side:
                    sheetContent()
                        .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                        .dsShadow("shadow-sheet")
                        .frame(width: Self.sheetWidth)
                        .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
                        .padding([.trailing, .bottom], CGFloat.ds("s-3"))
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                case .bottom(let height):
                    sheetContent()
                        .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                        .dsShadow("shadow-sheet")
                        .frame(height: height)
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .transition(.move(edge: .bottom))
                        .ignoresSafeArea(edges: .bottom)
                }
            }
            .overlay(alignment: .topLeading) { contentsDrawer(contentsPlace) }
            .overlay(alignment: .bottom) {
                // The pill rides above whatever comes up from the bottom, and beside a sheet or a drawer at the side. It
                // shows with a selection or while the music plays, or only while it plays (the setting
                // "guidance.transportPill", 0018 section 10).
                // Measured, so the selection's toast stands above it (v0.6.4, spec 7); above a hidden keyboard's bar (iPadOS's
                // «RU» and Undo), never above the keyboard itself (v0.6.4).
                VStack(spacing: CGFloat.ds("s-2")) { transport }.onGeometryChange(for: CGFloat.self) { $0.size.height } action: { transportHeight = $0 }.keyboardLift(barOnly: true)
                .padding(.bottom, pillBottom)
                .padding(.trailing, pillTrailing).padding(.leading, pillLeading)
            }
            .animation(.easeOut(duration: 0.2), value: rollOpen)
            .animation(.easeOut(duration: 0.2), value: insightsOpen)
            .animation(.easeOut(duration: 0.2), value: contentsOpen)
            .ignoresSafeArea(.keyboard)
    }

    /// What the screen hands its controllers when it appears, and the project file written when it goes.
    private func wired(_ content: some View) -> some View {
        content
            .toolbar(.hidden, for: .navigationBar)
            .task { await wire() }
            // A new interface language saves the piece and opens it again where it is; not a view shown over the piece.
            .handsOver(piece: viewing == nil ? piece.id : nil) { await handOverForLanguage() }
            .onDisappear {
                // The Sources sheet's way back held this screen, and with it every controller and web view of it (v0.6.4
                // spec section 11: each visit with the sources on stayed in memory).
                sources.reopen = nil
                let now = resumeNow()
                let view = notesViewNow()
                // Handed to its other view, the piece was saved and closed already (#511 section 10); the metronome goes on.
                let handed = ClosedPieces.take(piece.id)
                if !handed { Metronome.shared.stop() }
                player.shutdown()
                guard !handed else { return }
                // A variation only tried is not kept with the piece: it is left before the project file is written.
                let file = piece.file
                let id = piece.id
                let tried = insights
                Task {
                    await keepNotesView(view)
                    await keepResume(now)
                    await tried.leave()
                    if let file { try? await ProjectFiles.save(piece: id, to: file) }
                }
            }
            .overlay {
                // The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8), over everything else.
                if let call = score.pencilPalette {
                    PencilPalette(holder: score, call: call, text: text, modes: score.ringModes)
                }
            }
            .animation(.easeOut(duration: 0.16), value: score.pencilPalette)
            .onChange(of: scenePhase) { _, phase in
                // The project file is written whenever the app leaves the screen, so nothing waits for a Save button.
                if phase == .background { save() }
                // The iPad's setting «Only draw with Apple Pencil» may have changed meanwhile.
                if phase == .active {
                    score.applyInput()
                    roll.applyInput()
                }
            }
    }

    private func wire() async {
        let heard = player
        let shown = score
        // A tap on the page plays nothing (#338): the music starts only with Play or Loop. With the sheet open, a tap that
        // selects nothing puts it away; the selection stays as it is (0018 section 6).
        let sheetOpen = $insightsOpen
        score.blankTapped = { [weak shown] in
            if sheetOpen.wrappedValue { sheetOpen.wrappedValue = false }
            // In practice view the tap shows Play/Pause and Leave for a few seconds too (#533 11).
            shown?.practise(.tap)
        }
        // The modes (version 0.5): a tap on a beat moves the black playhead there (the playback model, v0.5.2, T12), playing
        // on from there when it plays; in focus it stays inside the loop. Notes are edited only in the roll (#479 item 23).
        score.placeTapped = { [weak heard, weak shown] beat in
            guard let heard, let shown else { return }
            heard.go(to: beat.lowerBound, play: false)
            shown.practise(.tap)
        }
        // A flag tapped on the score opens Check on its bar.
        let checking = $checkOpen
        score.checkPicked = { _ in checking.wrappedValue = true }
        // Open in the draft, from a marker's popover or focus, opens Sources on the version's page; a merge applied or
        // undone changes what plays. The draft's views are heard on their own, or the notes and the version in turn.
        let comparing = $versionsOpen
        score.versionPicked = { _ in comparing.wrappedValue = true }
        score.hearNotes = { [weak heard] notes, range in heard?.hearAlone(notes, over: range) }
        score.hearInTurn = { [weak heard] notes, range in
            guard let heard else { return }
            heard.setVariation(notes, version: true)
            heard.hear(.alternate, over: range)
            if heard.state != .playing { heard.play(from: range.lowerBound) }
        }
        let id = piece.id
        let rolled = roll
        let understood = insights
        score.notesChanged = { [weak heard, weak rolled, weak understood] in
            rolled?.notesChanged()
            Task { await heard?.reload(piece: id) }
            // A fixed note may be one a fact rested on while it was still to check (#352).
            Task { await understood?.reread() }
        }
        insights.placeChosen = { [weak heard] tick in heard?.seek(to: tick) }
        insights.scaleReady = { [weak heard] notes, range in heard?.hearAlone(notes, over: range) }
        insights.scaleStop = { [weak heard] in heard?.stopAlone() }
        // A way tried sounds at once, looping the selection to the variation's end, and the card in focus shows
        // it as the ossia; left, the original sounds again (0018 section 6).
        let tried = insights
        insights.variationReady = { [weak heard, weak shown, weak tried] notes, end in
            heard?.setVariation(notes)
            shown?.ossia = tried?.trial?.way.label
            if let range = shown?.playRange?.range {
                heard?.hear(.variation, over: range.lowerBound..<max(range.upperBound, end))
            }
        }
        insights.variationLeft = { [weak heard, weak shown] in
            heard?.setVariation(nil)
            shown?.ossia = nil
            // In focus, another version takes the variation's place again.
            if let version = shown?.focusVersion, version.sounds { heard?.setVariation(version.notes, version: true) }
        }
        wireRollSelection()
        wireRollPlayback()
        roll.noteEdited = { [weak shown] edit in shown?.editNote(edit) }
        // Picked notes dragged in Select (#601): the first step opens the note editor's draft, and the notes let go are its
        // edits, kept until Cancel or Save in the roll's head.
        roll.notesMoveBegan = { [weak shown] in if shown?.noteEditor == nil { shown?.beginNoteEdit() } }
        roll.notesMoved = { [weak shown] edits in edits.forEach { shown?.editNote($0) } }
        // The note edits saved are written to the project file at once.
        let file = piece.file
        score.noteEditSaved = {
            guard let file else { return }
            Task { try? await ProjectFiles.save(piece: id, to: file) }
        }
        // The bookmarks are drawn as brackets on the score, and again whenever they change.
        contents.changed = { [weak shown] shelf in shown?.showBookmarks(shelf) }
        await contents.load(piece: piece.id)
        assistant.load(piece: piece.own?.id ?? piece.file?.lastPathComponent ?? piece.title, score: score)
        await player.load(piece: piece.id, key: piece.own?.id); ProbeCounts.shared.mark("sound")
        applyModel()
        await resume()
        await returnToNotesView()
    }

    /// The drawers and sheets: what covers the page, and one of them at a time.
    private func watching(_ content: some View, covered: CGFloat, contentsSide: Bool) -> some View {
        content
            .onChange(of: player.hearingAlone) { _, alone in
                insights.scaleHeard = alone
            }
            .onChange(of: covered) { _, height in
                // Room under the last page for whatever covers the bottom of the score.
                score.insetBottom(height)
            }
            .onChange(of: contentsSide) { _, side in
                // The selection menu keeps to the right of the drawer over the left of the page.
                score.clearLeft(side ? Self.contentsWidth + CGFloat.ds("s-3") : 0)
            }
            .onChange(of: contentsOpen) { _, open in
                if open {
                    rollOpen = false
                    insightsOpen = false
                } else if contents.draft?.id != nil {
                    // A bookmark being changed in the drawer goes with it; one being made over the selection stays.
                    contents.cancel()
                }
            }
            .onChange(of: score.focused) { _, focused in
                expandChanged(focused)
            }
            // Practice view (#533 11): the page and the roll hide the blue playhead, the roll its loop strip too.
            .onChange(of: score.practicing, initial: true) { _, on in score.sendPractice(); roll.practice(on) }
            // The selection controls (#582 item 10): the page's handles go with the menu.
            .onChange(of: score.selectionControlsShown) { _, shown in score.sendSelectionControls(shown) }
            .onChange(of: score.focused && score.focusRoll) { _, shown in
                expandRollShown(shown)
            }
            .onChange(of: rollOpen) { _, open in
                if open {
                    contentsOpen = false
                    insightsOpen = false
                    roll.show(piece, dark: colorScheme == .dark)
                    roll.showSelection(score.selection, loop: player.loop)
                } else {
                    // Edits are kept only by Save: the drawer put away with the editor open leaves them, as Cancel does.
                    // Expand takes the editor into the roll in focus, with its draft (#479 item 23).
                    if !rollBeforeFocus { score.cancelNoteEdit() }
                }
            }
            .onChange(of: score.check) { _, _ in
                // A bar checked, or to check again: the facts on its notes say so, or no longer do (#352).
                Task { await insights.reread() }
            }
            .onChange(of: insightsOpen) { _, open in
                if open {
                    rollOpen = false
                    contentsOpen = false
                    Task { await insights.load(piece: piece.id, selection: score.selection) }
                } else {
                    // The rings go with the sheet.
                    insights.clearChoice()
                }
            }
    }

    /// Check and Other versions take the place of the other drawers and sheets, and go when one of them opens. The Settings sheet comes
    /// over the score, and the score and the roll follow each change at once.
    private func checking(_ content: some View) -> some View {
        content
            .fullScreenCover(isPresented: Binding(get: { dna != nil }, set: { if !$0 { dna = nil } })) {
                if let map = dna {
                    DnaScreen(
                        map: map, title: piece.title, player: player, text: text,
                        back: dnaFromMore ? (label: text["bar.more"], action: { backToMore { dna = nil } }) : nil,
                        close: { dna = nil },
                        showInScore: { range, openInsights in
                            dna = nil
                            Task {
                                await score.select(range.lowerBound, range.upperBound)
                                if openInsights { insightsOpen = true }
                            }
                        },
                        addBookmarks: {
                            let added = await contents.addForm()
                            return text.count("bookmark.structureAdded", added)
                        },
                        ask: {
                            // The map gives way to the score with the assistant beside it, on the whole piece.
                            dna = nil
                            assistant.open(wholePiece: true, selection: score.selection)
                        }
                    )
                }
            }
            // The PDF linked where it lies in Files (version 0.3): read from there, shown, never written into the piece.
            .fullScreenCover(item: $originals.linkedPdf, onDismiss: { originals.closeLinkedPdf() }) { pdf in
                PdfScreen(piece: pdf, linked: true).environmentObject(account)
            }
            .task(id: piece.id) {
                let shown = score
                originals.changed = { [weak shown] in shown?.refreshSources() }
                await originals.load(piece)
            }
            .overlay(alignment: .center) {
                // Opening the linked PDF from the bar: iOS may first download it from iCloud Drive or Google Drive.
                if originals.loading != nil && !versionsOpen {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        ProgressView()
                        Text(text["originals.loading"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                    }
                    .padding(CGFloat.ds("s-4"))
                    .modifier(FloatingSurface())
                }
            }
            .sheet(isPresented: $referenceOpen) {
                ReferenceSheet(text: text, settings: settings, piece: piece.id)
            }
            .sheet(isPresented: $settingsOpen) {
                SettingsSheet(settings: settings, account: account, text: text, pieceOverrides: score.pieceOverrides, clearPiece: { score.clearPieceOverrides() },
                              back: fromMore ? (label: text["bar.more"], action: { backToMore { settingsOpen = false } }) : nil)
            }
            .onChange(of: settings.values) { _, _ in
                score.settingsChanged()
                roll.settingsChanged()
            }
            .onChange(of: checkOpen || versionsOpen || settingsOpen) { _, open in
                // The way back to More belongs to the place opened from it, and goes with it.
                if !open { fromMore = false }
            }
            .onChange(of: checkOpen) { _, open in
                score.checkDrawerOpen = open
                if open {
                    insightsOpen = false
                    rollOpen = false
                    contentsOpen = false
                    versionsOpen = false
                }
            }
            .onChange(of: versionsOpen) { _, open in versionsOpened(open) }
            .onChange(of: insightsOpen || rollOpen || contentsOpen || score.focused) { _, other in
                if other {
                    checkOpen = false
                    versionsOpen = false
                }
            }
    }

    /// What follows the selection, the player and the fact chosen.
    private func following(_ content: some View) -> some View {
        content
            .onChange(of: score.focusVersion) { _, version in
                // In focus with no way tried, the other version is there to hear in the variation's place (0022, section 5).
                guard insights.trial == nil else { return }
                if let version, version.sounds {
                    player.setVariation(version.notes, version: true)
                } else if player.secondIsVersion {
                    player.setVariation(nil)
                }
            }
            .onChange(of: insights.chosen) { _, chosen in
                let places = chosen?.places ?? []
                score.highlight(places, scroll: player.state != .playing)
                roll.highlight(places)
            }
            .onChange(of: colorScheme) { _, scheme in
                if rollShown { roll.show(piece, dark: scheme == .dark) }
            }
            .onChange(of: rollPlace) { _, place in
                // The one holder of the roll's web view follows the place shown, so Expand and Back move it once (#495 BUG-01).
                roll.place = place
            }
            .onChange(of: player.tick) { _, tick in
                score.playhead(tick, playing: player.state == .playing)
                if rollShown { showRollPosition() }
            }
            .onChange(of: player.state) { old, state in
                // A new Play follows the music again, even after the player scrolled away by hand.
                score.playhead(player.tick, playing: state == .playing, follow: old != .playing && state == .playing)
                if rollShown { showRollPosition() }
            }
            .onChange(of: score.selection) { _, selection in
                roll.showSelection(selection, loop: player.loop)
                // A bookmark being made belongs to the selection it was started on, and so does a note.
                if let draft = contents.draft, draft.id == nil { contents.cancel() }
                if let note = score.noteDraft, note.onset != selection?.onset || note.end != selection?.end { score.noteDraft = nil }
                // The stream follows the selection while it is open; a variation tried belongs to the passage it was
                // tried on, so it is left either way.
                if insightsOpen {
                    Task { await insights.load(piece: piece.id, selection: selection) }
                } else if insights.trial != nil {
                    Task { await insights.leave() }
                }
            }
            .onChange(of: player.loop) { _, loop in
                roll.showSelection(score.selection, loop: loop)
                // Цикл turned on or off: the blue playhead waits at the selection's start or stands at its end (v0.5.2).
                syncPlayheads()
            }
            .onChange(of: score.noteEditor) { _, editor in
                // The note editor on the roll (testing item 15): a press on its notes edits them while it is open.
                roll.showEditor(editor)
            }
            .onChange(of: score.playRange) { old, new in
                playRangeChanged(from: old, to: new)
                syncPlayheads()
            }
    }

    /// PDF in the bar's PDF | Notes: for a piece with a PDF kept inside it, or notes read from a PDF piece the library holds;
    /// else the PDF linked where it lies in Files, over the score (version 0.3).
    private var toPdf: (() -> Void)? {
        if switchView != nil, piece.other != nil || score.sources.document != nil {
            // The switch of all views goes with the piece to its PDF (SourcesHandover).
            return { ProbeCounts.shared.begin(); if sourcesOn { SourcesHandover.give(sources.layers, to: piece.other?.file ?? piece.file) }; Task { await showPdf() } }
        }
        guard originals.canOpenLinkedPdf else { return nil }
        return {
            if player.state == .playing { player.toggle() }
            Task {
                await originals.openLinkedPdf(title: piece.title)
                // Changed, gone or not downloaded: Sources says why, under the PDF.
                if originals.linkedPdf == nil { versionsOpen = true }
            }
        }
    }
}

extension ScoreScreen {
    /// The piece's screen, its originals known from the piece's opening (#495 BUG-02).
    init(piece: OpenPiece, switchView: ((Landing) async -> Void)? = nil, viewing: String? = nil, reopen: ((Landing) async -> Void)? = nil) {
        self.piece = piece
        self.switchView = switchView
        self.viewing = viewing
        self.reopen = reopen
        _originals = StateObject(wrappedValue: OriginalsController(opened: piece.own))
        // The score opens in Navigate (the modes, permanent since v0.6.5).
        _score = StateObject(wrappedValue: ScoreController())
        // The switch of all views as the screen before had it, until this one has read its own (SourcesHandover).
        _sources = StateObject(wrappedValue: SourcesController(layers: SourcesHandover.layers(for: piece.file)))
    }
}

/// The switch of all the piece's views and the Sources sheet (version 0.6; decision record
/// 0030). The working notes are this screen; the first PDF is the PDF view of PDF | Notes; another PDF or photos, and other
/// Notes, open on a screen of their own over this one, read-only, from a copy never saved; a version opens its page in
/// the sheet, as Other versions did; a recording's sound is heard from a capsule under the bar.
extension ScoreScreen {
    /// What was open under Sources as it opened (#582 item 4).
    struct SourcesShelved: Equatable {
        let roll: Bool
        let insights: Bool
        let contents: Bool
    }

    /// Where the sheet over the score goes: Insights, Check and Other versions beside the score in landscape and over the
    /// lower half in portrait; Sources always as a panel over the lower half (#582 item 4), so it is plainly apart from
    /// the document and its view.
    func sheetPlace(landscape: Bool, height: CGFloat) -> SheetPlace {
        if versionsOpen && sourcesOn { return .bottom(height / 2) }
        return insightsOpen || checkOpen || versionsOpen ? (landscape ? .side : .bottom(height / 2)) : .none
    }

    /// Other versions or Sources opened or closed: opening puts the other places away, keeping what was open under Sources;
    /// closing Sources by its ✕ or the bar's button, not by another place opening, gives the view back as it was (#582 item 4).
    func versionsOpened(_ open: Bool) {
        if open {
            if sourcesOn { beforeSources = SourcesShelved(roll: rollOpen, insights: insightsOpen, contents: contentsOpen) }
            insightsOpen = false
            rollOpen = false
            contentsOpen = false
            checkOpen = false
        } else if let before = beforeSources {
            beforeSources = nil
            if !(insightsOpen || rollOpen || contentsOpen || checkOpen || score.focused) {
                rollOpen = before.roll
                insightsOpen = before.insights
                contentsOpen = before.contents
            }
        }
    }

    /// A view of the switch on a screen of its own over the score, its piece closed in music-core as it goes.
    struct ShownView: Identifiable {
        let piece: OpenPiece
        /// Shown as a PDF (another original) rather than as notes.
        let pdf: Bool
        var id: Int32 { piece.id }
    }

    /// Whether the switch of all views and Sources are there: never on a view shown over the piece (0030).
    var sourcesOn: Bool { viewing == nil }

    /// The Sources sheet in Other versions' place.
    var sourcesSheet: some View {
        SourcesSheet(
            sources: sources, score: score, originals: originals, text: text, current: currentView,
            close: { versionsOpen = false },
            back: fromMore ? (label: text["bar.more"], action: { backToMore { versionsOpen = false } }) : nil,
            open: pickView,
            versionPage: SourcesSheet.versionPage(score),
            versionStatus: score.versionStatus
        )
    }

    /// «Исполнение»: the recording's sound kept on this iPad, heard from under the bar.
    @ViewBuilder var recordingCapsule: some View {
        if let id = sources.performance, let projectId = sources.projectId, let url = OriginalSounds.file(project: projectId, original: id) {
            RecordingViewCapsule(url: url, title: sources.layers.original(id)?.shown ?? text["sources.rolePlaying"], text: text) {
                sources.performance = nil
            }
            .id(id)
            .transition(.opacity)
        }
    }

    /// The views shown over the score, the sources read as the screen opens, and a variation only tried left before the
    /// Sources sheet writes the project file.
    func sourcing(_ content: some View) -> some View {
        content
            .fullScreenCover(item: $shownView) { view in shownViewScreen(view) }
            .task(id: sourcesOn) { await loadSources() }
            .onChange(of: versionsOpen) { _, open in
                if open && sourcesOn { Task { await insights.leave() } }
            }
    }

    /// The view shown now: the recording heard, else the working notes.
    var currentView: String? { sources.performance ?? sources.layers.working }

    /// The views for the bar's switch, and what picking one does; nil while the switch is off.
    var switchLayers: SourceLayers? { sourcesOn ? sources.layers : nil }
    var switchPick: ((SourceLayers.ViewEntry) -> Void)? {
        guard sourcesOn else { return nil }
        return { view in pickView(view) }
    }

    /// The sources as the screen opens then what the screen before handed over: the toast
    /// of what was done, or a view picked on the PDF's switch.
    func loadSources() async {
        guard sourcesOn else { return }
        sources.reopen = { await reopenHere() }
        await sources.load(piece: piece.id, file: piece.file)
        SourcesHandover.drop(piece.file)
        if let toast = SourcesHandover.toast(for: piece.file) { sources.toast = toast }
        if let id = SourcesHandover.view(for: piece.file), let view = sources.layers.views.first(where: { $0.id == id }) {
            pickView(view)
        }
    }

    /// A view picked in the switch, its menu or the sheet's «Открыть».
    func pickView(_ view: SourceLayers.ViewEntry) {
        switch view.kind {
        case "performance":
            if player.state == .playing { player.toggle() }
            sources.performance = view.id
        case "original":
            sources.performance = nil
            if view.id == PdfScreen.documentView, let toPdf {
                toPdf()
            } else {
                Task { await showOriginal(view.id) }
            }
        default:
            sources.performance = nil
            guard view.id != sources.layers.working else { return }
            if sources.layers.notes(view.id)?.role == "version", score.versions.entries.contains(where: { $0.id == view.id }) {
                versionsOpen = true
                score.openVersionPage(view.id)
            } else {
                Task { await showNotes(view.id) }
            }
        }
    }

    /// Another PDF or photos kept in the piece, shown as they are; why not, in the sheet.
    private func showOriginal(_ id: String) async {
        guard let original = sources.layers.original(id) else { return }
        if player.state == .playing { player.toggle() }
        guard let opened = await sources.openOriginal(original, title: original.shown) else {
            versionsOpen = true
            return
        }
        shownView = ShownView(piece: opened, pdf: true)
    }

    /// Other Notes shown on their own, at the place in the music in view here; why not, in the sheet.
    private func showNotes(_ id: String) async {
        guard let notes = sources.layers.notes(id) else { return }
        if player.state == .playing { player.toggle() }
        let at = await score.tickInView()
        guard let opened = await sources.openNotes(notes, title: sources.layers.title(of: notes, text), at: at) else {
            versionsOpen = true
            return
        }
        shownView = ShownView(piece: opened, pdf: false)
    }

    /// The screen of a view shown over the piece; its piece is closed in music-core once the screen has gone.
    func shownViewScreen(_ view: ShownView) -> some View {
        Group {
            if view.pdf {
                PdfScreen(piece: view.piece, linked: true, readOnlyNote: text["sources.sheet.viewOnly"])
            } else {
                ScoreScreen(piece: view.piece, viewing: text["sources.sheet.viewOnly"])
            }
        }
        .environmentObject(account)
        .environmentObject(settings)
        .onDisappear {
            let id = view.piece.id
            Task {
                // After the screen's own leaving, which still reads the piece.
                try? await Task.sleep(for: .seconds(2))
                _ = await MusicCore.run { MusicCore.close(piece: id) }
            }
        }
    }

    /// The piece opened again in this screen's place from its project file, as «Сделать рабочими» or the toast's Undo has
    /// just left it: the "Notes" view is kept for it and nothing more is written to the file.
    private func reopenHere() async {
        guard let reopen else { return }
        let at = await score.tickInView() ?? 0
        let view = notesViewNow()
        player.shutdown()
        await keepNotesView(view)
        await reopen(.tick(at))
    }
}

/// The roll on the score's screen: its drawer's height (#511 section 7.1) and where the roll is shown.
extension ScoreScreen {
    /// `drawer` is the roll drawer's height as shown, following its handle while dragged; `inset`, the height it was left at,
    /// is the room the page keeps under its last page, so the score is not laid out again on every move of the handle.
    private func page(drawer: CGFloat, inset: CGFloat, screen: CGSize, sheet: SheetPlace, contentsPlace: SheetPlace) -> some View {
        // The chain is cut into parts so the compiler type-checks each on its own.
        let covered = max(drawer, sheet.bottomHeight, contentsPlace.bottomHeight)
        let laid = layers(drawer: drawer, screen: screen, sheet: sheet, contentsPlace: contentsPlace, covered: covered)
            .overlay(alignment: .bottomTrailing) {
                // The full assistant beside the score, which layers narrowed for it (#582 item 17.1).
                if assistant.panelOpen {
                    AssistantSide(assistant: assistant, text: text, selection: score.selection, label: score.askLabel, landscape: screen.width > screen.height, drawer: drawer, ask: askAssistant)
                }
            }
        let room = max(inset, sheet.bottomHeight, contentsPlace.bottomHeight)
        return following(sourcing(checking(watching(wired(laid), covered: room, contentsSide: contentsPlace == .side))))
    }

    /// A question to the full assistant about what it looks at now: the selection, or the whole piece.
    private func askAssistant(_ question: String) {
        let whole = assistant.scope.isWholePiece(score.selection)
        let ask = AssistantAsk(
            piece: piece.id, selection: score.selection, wholePiece: whole, scope: whole ? text["ask.wholePiece"] : score.askLabel ?? "",
            language: text.language, role: AssistantSide.role(settings), relay: account.assistant
        )
        Task { await assistant.ask(question, about: ask) }
    }

    /// The drawer's share of the screen's height: as the player left it in this orientation, 40% in landscape and 30% in
    /// portrait until then.
    private func drawerShare(_ landscape: Bool) -> CGFloat {
        drawerShares[landscape] ?? RollDrawerHeight.standard(landscape: landscape)
    }

    /// The drawer's handle dragged by `dy` points (#511 section 7.1): the drawer follows it, and when let go keeps that
    /// height on the device, or goes away when dragged nearly off. The roll keeps its zoom, view and selection, and the
    /// music plays on.
    private func resizeDrawer(by dy: CGFloat, ended: Bool, screen: CGSize) {
        let landscape = screen.width > screen.height
        let share = RollDrawerHeight.share(from: drawerShare(landscape), dragged: dy, height: screen.height)
        guard ended else {
            drawerDrag = share ?? RollDrawerHeight.minShare
            return
        }
        withAnimation(.ds("t-fast")) {
            drawerDrag = nil
            if let share {
                drawerShares[landscape] = share
                RollDrawerHeight.keep(share, landscape: landscape)
            } else {
                rollOpen = false
            }
        }
    }

    /// A step of the drawer's height from VoiceOver's adjust (the web's arrow keys on the handle).
    private func stepDrawer(_ direction: CGFloat, screen: CGSize) {
        let landscape = screen.width > screen.height
        let share = RollDrawerHeight.clamped(drawerShare(landscape) + direction * RollDrawerHeight.step)
        withAnimation(.ds("t-fast")) { drawerShares[landscape] = share }
        RollDrawerHeight.keep(share, landscape: landscape)
    }

    /// Where the roll is on screen: in its drawer, or over the card in focus; the one place its web view is in (#495 BUG-01).
    private var rollPlace: RollPlace? { RollPlace.shown(drawerOpen: rollOpen, focused: score.focused, focusRoll: score.focusRoll) }

    /// Whether the roll is on screen.
    private var rollShown: Bool { rollPlace != nil }

    /// The selection's span as bar.beat for the roll's heading, under the one playback position (v0.5.2); nil for its bars.
    private var rollSpan: String? {
        guard let range = score.playRange?.range, let span = player.barBeats.span(range) else { return nil }
        return text.format("pill.span", span.first, span.last)
    }
}

/// At the instrument, on the score: resume where the player stopped (#327), hands separately (#328) and selecting by
/// beats (#349).
extension ScoreScreen {
    /// The playback model as the page and the roll draw it (v0.5.2, #495 UX-10): the selection the blue playhead keeps to and
    /// Цикл, and the roll's lanes. In Expand the page draws the black playhead on the card inside the selection, its passage
    /// (v0.6.4, spec 8); the roll there keeps its own playhead, without lanes.
    private func syncPlayheads() {
        // The hand the selection is narrowed to (v0.6.2): its band covers that staff, and the blue playhead with it (#608).
        let selection: Any = score.playRange.map { PlayheadsShown.selection($0.range, hand: $0.selection.hand) as Any } ?? NSNull()
        score.showPlayheads(["selection": selection, "loop": player.cycle])
        roll.lanes(score.holdsSelection)
        if rollShown { showRollPosition() }
    }

    /// One selection for the score, the roll, the pill and the loop strip (#479 item 23): what the roll picks is the score's
    /// selection, by beats where the score selects by beats, and the roll shows it back with the score. Each change comes with
    /// its gesture, for the selection's history (#511 11); the roll's hold and its hint are the score's too.
    private func wireRollSelection() {
        roll.byBeats = { [weak score] in !(score?.beatLines.isEmpty ?? true) }
        roll.picked = { [weak score] pick, gesture in
            switch pick {
            case let .bars(first, last, extend): score?.selectBars(first, last, extend: extend, gesture: gesture)
            case let .beats(span): score?.selectBeats(span.lowerBound, span.upperBound, gesture: gesture)
            }
        }
        roll.cleared = { [weak score] gesture in score?.clearSelection(gesture: gesture) }
        // Notes a lasso or a tap picked in the roll's Select (#601): the one selection, lit on the score too.
        roll.notesPicked = { [weak score] pick in score?.selectNotes(pick.ids, add: pick.add, gesture: pick.gesture, replaces: pick.replaces) }
        // A span drawn there, notes or none (v0.6.4), and the score's tuplets for the roll's grid: one selection for both.
        roll.sharePlaces(with: score)
        roll.gestureCancelled = { [weak score] gesture in score?.revertGesture(gesture) }
        roll.holdHinted = { [weak score] in score?.receivedGesture(["holdHint": true]) }
        // The Pen's stylus changes nothing on the roll (#511 4.1, #521 10), and the Pen takes the roll out of Edit notes:
        // one tool at a time.
        score.penChanged = { [weak roll, weak score] on in
            guard let roll else { return }
            roll.penOn = on
            if let score, RollInput.rollNavigates(penOn: on, mode: roll.mode) { roll.choose(.navigate, score: score) }
        }
        score.rollEditing = { [weak roll] in roll?.shownMode == .edit }
        // The bar's tool decides on the roll too (#543 2–4): only Select changes the selection there.
        score.toolChanged = { [weak roll] mode, penOn in roll?.barTool = RollInput.barTool(mode: mode, penOn: penOn) }
        roll.barTool = RollInput.barTool(mode: score.mode, penOn: score.penOn)
    }

    /// Space and the playhead on the roll. The playhead moved there (v0.2) stays inside the loop, and a tap in a selection that
    /// does not loop yet loops it; under the one playback position (v0.5.2) it goes anywhere, and so do taps on the loop strip
    /// and the whole piece.
    private func wireRollPlayback() {
        let heard = player
        let shown = score
        roll.spacePressed = { [weak heard, weak shown] in
            guard let heard, let shown else { return }
            if shown.focused, let passage = shown.playRange?.range {
                heard.playPassage(passage)
            } else {
                heard.apply(.press)
            }
        }
        // The playhead moved on the roll (v0.2): it stays inside the loop, and a tap in a selection that does not loop yet loops
        // it. Under the playback model (v0.5.2) it goes anywhere, as a tap on a beat does (T12): the loop strip and the whole piece
        // move it too, and Цикл turns off when it leaves the selection (the loop strip only moves it inside, T13).
        roll.playheadMoved = { [weak heard, weak shown] tick, play in
            guard let heard, let shown else { return }
            if shown.focused, let passage = shown.playRange?.range {
                // In Expand within the passage, playing it round from there (#533 2).
                heard.move(to: tick, play: play, within: passage)
            } else {
                heard.go(to: tick, play: play)
            }
        }
        // A playhead dragged by its handle on the score or the roll (#495 UX-08, T14–T16): the player runs the model. The pages
        // send it only under the playback model, and a gesture that ends after the model went is still let go. On Expand's
        // roll it is held inside the passage, which goes on looping (#533 2).
        // On Expand's card the black playhead is held inside the passage too (v0.6.4, spec 8), as on its roll.
        score.playheadGestured = { [weak heard, weak shown] gesture in
            guard let heard else { return }
            if let passage = shown?.expandPassage {
                heard.handle(gesture, within: passage)
            } else {
                heard.handle(gesture)
            }
        }
        roll.playheadGestured = { [weak heard, weak shown] gesture in
            guard let heard else { return }
            if shown?.focused == true, let passage = shown?.playRange?.range {
                heard.handle(gesture, within: passage)
            } else {
                heard.handle(gesture)
            }
        }
    }

    /// Where the player is, for the roll: with the lanes, also the place as bar.beat and whether the selection loops.
    private func showRollPosition() {
        let on = score.holdsSelection
        roll.position(player.tick, playing: player.state == .playing, label: on ? player.placeLabel : "", looping: on && player.loop != nil)
    }

    /// What plays for the selection changed. A loop follows it, as on the web; a new selection stops the old loop. Under the
    /// playback model (v0.5.2) the player takes the selection, and Цикл decides (T9–T11).
    private func playRangeChanged(from old: ScoreController.PlayRange?, to new: ScoreController.PlayRange?) {
        // Expand loops its passage (#533 2): what plays for it plays on round it.
        if score.focused {
            if let new, player.state != .stopped { player.keepWithin(new.range) }
            return
        }
        player.apply(.select(new?.range))
    }

    /// Once the sound has loaded: the piece's beat lines for the one selection on the sixteenth grid (v0.6.4), and the
    /// playback model as the page and the roll draw it.
    private func applyModel() {
        score.setBeatLines(player.beatLines)
        syncPlayheads()
        if score.holdsSelection { player.hold(selection: score.playRange?.range) }
    }

    /// Back where the player stopped (#327): the project's latest resume point, the web app's too, with its passage, focus,
    /// tempo, loop and hands. Nothing starts playing.
    private func resume() async {
        // From the PDF in PDF | Notes the notes open at the page the player came from instead.
        guard piece.landing == nil else { return }
        let id = piece.id
        let answer = await MusicCore.run { MusicCore.call(piece: id, "resume") }
        guard let place = ResumePlace(answer: answer) else { return }
        resumeId = place.id
        player.tempoPercent = place.tempoPercent
        if player.handsHeld.canSeparate { player.hands = place.hands }
        await score.arrive(at: place)
        if place.loop, let range = score.playRange?.range ?? place.passage {
            player.setLoop(range, play: false)
        }
    }

    /// Where the player is now, taken before the player stops.
    func resumeNow() -> ResumePlace.Now? {
        ResumePlace.Now(
            passage: score.selection.map { $0.onset..<$0.end },
            tick: player.tick,
            focused: score.focused,
            tempoPercent: player.tempoPercent,
            hands: player.hands,
            loop: player.loop != nil
        )
    }

    /// Keeps where the player is as the project's resume point (`resume.save`), before the project file is written.
    func keepResume(_ now: ResumePlace.Now?) async {
        guard let now else { return }
        let id = piece.id
        var at = now.tick
        if now.passage == nil, let inView = await score.tickInView() { at = inView }
        let request = ResumePlace.saveRequest(
            id: resumeId, passage: now.passage, at: at, focused: now.focused, tempoPercent: now.tempoPercent, hands: now.hands, loop: now.loop, now: Date()
        )
        let answer = await MusicCore.run { MusicCore.call(piece: id, "resume.save", request) }
        if let saved = facadeObject(answer)?["saved"] as? [String: Any], let point = ResumePlace(point: saved) {
            resumeId = point.id
        }
    }
}

/// The bar over the score (ds.css `.bar`): back to the library, the title, which opens the Contents the way a reader opens
/// a book at its contents, and the pages in view, then Phrases, Pen, the piano roll, Insights, Play and More, in the web
/// app's order.
struct ScoreBar: View {
    let title: String
    let pages: ScoreController.PagesInView
    let text: UIText
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @Binding var more: Bool
    /// Whether the place open now was opened from More, so it shows "‹ More" (testing item 20).
    @Binding var fromMore: Bool
    @Binding var rollOpen: Bool
    @Binding var insightsOpen: Bool
    @Binding var contentsOpen: Bool
    @Binding var checkOpen: Bool
    @Binding var versionsOpen: Bool
    @Binding var settingsOpen: Bool
    /// PDF | Notes (0023): goes to the PDF the notes were read from; nil for notes with none.
    let pdf: (() -> Void)?
    /// Piece DNA from More.
    let openDna: () -> Void
    /// The Reference's circle of fifths; nil where there is none.
    var openReference: (() -> Void)?
    /// The piece's views for the switch of all views (version 0.6, 0030), in PDF | Notes' place with
    /// Sources after it.
    var sourceLayers: SourceLayers? = nil
    var currentView: String? = nil
    var pickView: ((SourceLayers.ViewEntry) -> Void)? = nil
    /// The read-only tag of a view shown over the piece; nil for the piece itself.
    var viewing: String? = nil
    /// The bar's width, the screen's less its margins (TopBar).
    var width: CGFloat = 0
    let back: () -> Void
    @State private var phrasesOpen = false
    /// The list of extra information under the ⓘ button's chevron.
    @State private var extraOpen = false
    @EnvironmentObject private var settings: DeviceSettings
    @EnvironmentObject private var assistant: AssistantController

    /// The bar is laid out as the PDF's (TopBar, #479 item 13, #582 item 2): Back, the Contents, the title and the switch
    /// in the same places in both views, then the pages in view, and each view's own actions at the right.
    var body: some View {
        TopBar(width: width, pages: pageText) {
            BarLeading(title: title, contentsOpen: $contentsOpen, text: text, back: back)
            if let sourceLayers, let pickView {
                SourcesViewSwitch(layers: sourceLayers, current: currentView, text: text, pick: pickView) { versionsOpen = true }
                SourcesButton(open: versionsOpen, text: text) { versionsOpen.toggle() }
            } else if let pdf {
                PdfNotesSwitch(onPdf: false, text: text) { pdf() }
            }
            if let viewing { Tag(text: viewing, kind: .assumed) }
        } actions: {
            actions(compact: false)
        } compact: {
            actions(compact: true)
        }
    }

    /// In groups without captions, as the approved mockup v0.5.2 (#479 item 21): what the score shows (Phrases, the extra
    /// information, the piano roll), then how the score is used (the modes), then the Circle and
    /// Insights, then Play and More. Compact where the bar is short (#582 item 2): the chosen mode by its icon alone.
    @ViewBuilder private func actions(compact: Bool) -> some View {
        viewButtons
        PaletteDivider()
        // The modes (version 0.5): one group for the score and the roll, the Pen in it.
        ModeGroup(score: score, text: text, compact: compact)
        PaletteDivider()
        if let openReference {
            // The Reference: the Circle, the circle with a sharp, opens the circle of fifths.
            Button(action: openReference) {
                Icon(name: "keysig", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["reference.open"])
            .accessibilityHint(text["reference.openHint"])
        }
        Button {
            insightsOpen.toggle()
        } label: {
            Icon(name: "insight", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: insightsOpen))
        .accessibilityLabel(text["bar.insights"])
        .accessibilityAddTraits(insightsOpen ? .isSelected : [])
        PracticeButton(score: score, text: text)
        PaletteDivider()
        playAndMore
    }

    /// What the score shows: Phrases and its menu, the extra information and its list, and the piano roll.
    @ViewBuilder private var viewButtons: some View {
        Button {
            phrasesOpen.toggle()
        } label: {
            HStack(spacing: 0) {
                Icon(name: "phrases", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
                Icon(name: "chevd", size: CGFloat.ds("icon-s")).padding(.trailing, CGFloat.ds("s-1"))
            }
        }
        .buttonStyle(BarActionStyle(on: phrasesOpen))
        .accessibilityLabel(text["bar.phrases"])
        .accessibilityValue(score.phrasesHidden ? text["phrases.hide"] : "")
        .popover(isPresented: $phrasesOpen) { PhrasesPanel(score: score, text: text) }
        // Extra information (testing item 33): one press shows or hides what the app adds over the notes; the chevron
        // lists each kind and the player's own marks.
        HStack(spacing: 0) {
            let on = !score.hiddenLayers.contains("extra")
            Button {
                score.toggleExtra()
            } label: {
                Icon(name: "info", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: on))
            .accessibilityLabel(text["extra.toggle"])
            .accessibilityHint(text["extra.hint"])
            .accessibilityAddTraits(on ? .isSelected : [])
            Button {
                extraOpen.toggle()
            } label: {
                Icon(name: "chevd", size: CGFloat.ds("icon-s")).frame(width: 20, height: 40)
            }
            .buttonStyle(BarActionStyle(on: extraOpen))
            .accessibilityLabel(text["extra.list"])
            .popover(isPresented: $extraOpen) {
                LayersList(score: score, text: text)
                    .padding(CGFloat.ds("s-4"))
                    .frame(width: 300, alignment: .leading)
                    .presentationCompactAdaptation(.popover)
            }
        }
        Button {
            rollOpen.toggle()
        } label: {
            Icon(name: "roll", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: rollOpen))
        .accessibilityLabel(text["bar.roll"])
        .accessibilityAddTraits(rollOpen ? .isSelected : [])
    }

    /// More: the pill, always shown, plays (the playback model, v0.5.2, #479 item 22).
    @ViewBuilder private var playAndMore: some View {
        Button {
            more.toggle()
        } label: {
            Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: more))
        .accessibilityLabel(text["bar.more"])
        .popover(isPresented: $more) {
            MorePanel(score: score, text: text, problem: player.problem ?? score.problem, openCheck: {
                more = false
                checkOpen = true
                fromMore = true
            }, openVersions: viewing == nil ? openSources : nil, openSettings: {
                more = false
                settingsOpen = true
                fromMore = true
            }, openDna: {
                more = false
                openDna()
            }, askAboutPiece: {
                more = false
                // The assistant's panel opens on the whole piece (#582 item 17.1).
                assistant.open(wholePiece: true, selection: score.selection)
            })
        }
    }

    /// Sources from More.
    private func openSources() {
        more = false
        versionsOpen = true
        fromMore = true
    }

    /// "p. 2 of 4", or "pp. 1–2 of 4" with two pages to a row; nothing before the score is laid out.
    private var pageText: String? {
        guard pages.count > 0 else { return nil }
        return pages.first == pages.last
            ? text.format("bar.page", String(pages.first), String(pages.count))
            : text.format("bar.pages", String(pages.first), String(pages.last), String(pages.count))
    }
}

/// The pill (ds.css `.pill`): play, the tempo and the metronome, floating at the bottom of the score.
struct Pill: View {
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @ObservedObject private var metronome = Metronome.shared
    let text: UIText
    @EnvironmentObject private var settings: DeviceSettings

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            // The playback model (v0.5.2, #495 UX-10): one Play, cobalt or ink by the place, and Цикл with the span; in
            // Expand the same Play over its passage, which always loops (v0.6.4, spec 8).
            PositionTransport(player: player, passage: score.expandPassage, text: text)
            // The tempo observes only itself (#511 section 17), so it opens and changes while playing.
            TempoControl(tempo: player.tempo, text: text) { [weak score] perMinute in
                Task { await score?.setPieceTempo(perMinute) }
            }
            .equatable()
            handsControls
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            // The metronome clicks at once, with or without Play; held, a slide sets its tempo (#521 section 15).
            Button {
                metronome.toggle()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "metronome")
                    Text(text["transport.metronome"])
                }
            }
            .buttonStyle(DSItemStyle(on: metronome.running))
            .accessibilityAddTraits(metronome.running ? .isSelected : [])
            .modifier(MetronomeHold(metronome: metronome, text: text) { metronome.toggle() })
        }
        .disabled(!player.ready)
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["transport.label"])
    }

    /// Hands separately at the instrument (#328): both, right or left, two touches away, and the
    /// phrase before or after without lifting it into focus. The hands are the ones music-core gives the notes, and the
    /// menu says when they were worked out rather than read from the file.
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
        if score.selection != nil && !score.focused {
            step("back", text["focus.previous"], enabled: score.canStepBack) { score.step(-1) }
            step("fwd", text["focus.next"], enabled: score.canStepOn) { score.step(1) }
        }
    }

    private func step(_ icon: String, _ label: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: icon).frame(width: 32, height: 32)
        }
        .buttonStyle(DSItemStyle())
        .disabled(!enabled)
        .accessibilityLabel(label)
    }
}

/// The menu over the selection (ds.css `.menu`, 0018 section 3): the actions a musician takes on a passage. It holds only
/// what applies and what the iPad has so far: Loop, Focus, Bookmark and Note, then the piano roll and Insights (none for a
/// passage without notes), and More with Highlight. Bookmark and Note turn the menu into their cards, as on the web. Its
/// actions show their icons alone (v0.5.2, #495 UX-11), each at a fingertip's square, with its word for VoiceOver.
struct SelectionMenu: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var player: Player
    @ObservedObject var contents: ContentsController
    let text: UIText
    @Binding var rollOpen: Bool
    @Binding var insightsOpen: Bool
    @State private var more = false
    @EnvironmentObject private var account: Account
    @EnvironmentObject private var settings: DeviceSettings
    @EnvironmentObject private var assistant: AssistantController

    var body: some View {
        Group {
            if score.noteDraft != nil {
                NoteCard(score: score, text: text)
                    .frame(width: 320)
                    .padding(CGFloat.ds("s-3"))
                    .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else if let draft = contents.draft, draft.id == nil {
                BookmarkCard(contents: contents, text: text) {
                    let selection = score.selection
                    let title = score.bookmarkTitle
                    Task { await contents.save(over: selection, defaultTitle: title) }
                }
                .frame(width: 300)
                .padding(.vertical, CGFloat.ds("s-3"))
                .padding(.horizontal, CGFloat.ds("s-4"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else if assistant.here != nil, score.selection != nil {
                // The assistant at the selection where the menu was (#582 items 17.2 and 17.4).
                AssistantHereCard(assistant: assistant, score: score, text: text, ask: askHere)
            } else {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    actions
                    if more {
                        Rectangle().fill(Color.ds("hairline")).frame(height: 1)
                        HighlightRow(score: score, text: text)
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
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-2"))
                .padding(.vertical, CGFloat.ds("s-1"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: more ? CGFloat.ds("r-l") : 26))
                .overlay(RoundedRectangle(cornerRadius: more ? CGFloat.ds("r-l") : 26).strokeBorder(Color.ds("hairline")))
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
    /// beat until the sound has given the bars' beats.
    private var menuSpan: String? {
        if let range = score.playRange?.range, let span = player.barBeats.span(range) {
            return text.format("pill.span", span.first, span.last)
        }
        return score.selection?.beatSpan(text)
    }

    private var actions: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            // Where the selection starts and ends, by bar and beat.
            if let span = menuSpan {
                Text(span)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("accent"))
                    .padding(.horizontal, CGFloat.ds("s-2"))
                Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            }
            if !score.penOn {
                // Expand into focus: two arrows to the corners, unlike Insights' magnifier (#511 section 9).
                item("expand", text["focus.enter"], on: false) { score.enterFocus() }
                    .accessibilityHint(text["focus.enterHint"])
            }
            item("bookmark", text["menu.bookmark"], on: false) {
                score.noteDraft = nil
                contents.start(title: score.bookmarkTitle)
            }
            item("note", text["menu.note"], on: false) {
                contents.cancel()
                score.startNote()
            }
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            item("roll", text["bar.roll"], on: rollOpen) { rollOpen.toggle() }
            if !(score.selection?.noteIds.isEmpty ?? true) {
                item("insight", text["bar.insights"], on: insightsOpen) { insightsOpen.toggle() }
            }
            item("more", text["menu.more"], on: more || score.selectedHighlight != nil) { more.toggle() }
            // Hide (#533 11): the selection controls go, the selection staying and the playback controls as they are (#582
            // item 10); ✕ is the one that clears it.
            item("practice", text["menu.hide"], on: false) { score.practise(.selection(shown: false)) }
            ClearSelectionButton(score: score, text: text, iconOnly: true)
        }
    }

    /// An action of the menu as its icon alone (#495 UX-11): the word for VoiceOver and the pointer's tip.
    private func item(_ icon: String, _ label: String, on: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: icon)
        }
        .buttonStyle(DSIconItemStyle(on: on))
        .accessibilityLabel(label)
        .accessibilityAddTraits(on ? .isSelected : [])
        .help(label)
    }
}

/// Clear selection (#338): the one way to let go of a selection, as a tap elsewhere on the page or the roll keeps it.
struct ClearSelectionButton: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    /// At a fingertip's square, as the selection menu's other icons (#495 UX-11); the roll's head keeps its size.
    var iconOnly = false

    var body: some View {
        let button = Button {
            score.clearSelection()
        } label: {
            Icon(name: StylusRules.RingControl.clearSelection.icon).frame(width: iconOnly ? nil : 32, height: iconOnly ? nil : 32)
        }
        Group {
            if iconOnly {
                button.buttonStyle(DSIconItemStyle())
            } else {
                button.buttonStyle(DSItemStyle())
            }
        }
        // Escape removes the selection too (the modes, version 0.5); in practice view it leaves that view (#533 11).
        .keyboardShortcut(!score.practicing ? KeyboardShortcut(.escape, modifiers: []) : nil)
        .accessibilityLabel(text["menu.clearSelection"])
        .help(text["menu.clearSelection"])
    }
}

/// The focus bar (0018 section 4, the web app's focus bar): Back to the page with its word, where the phrase is, and Play.
/// Escape on a keyboard goes back too. While a variation is tried, Original / Variation / Alternate choose what sounds;
/// with another version over the phrase and no variation, Notes / Version / Alternate, and Take into the score or
/// Restore these notes for the phrase's bars (0022, section 5). Reading / As played comes with transcriptions on the iPad.
/// The phrase as a roll in focus (testing item 12): a card over the page's card, as wide and as tall as the stage between the bar
/// and the pill, with the roll inside and the zoom on each axis floating over the notes. Expand inspects the passage (#521
/// 19.3): no mode switch and no note editor here, no arrows to another phrase; a draft of note edits waits in the drawer.
struct FocusRollCard: View {
    @ObservedObject var roll: RollController
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        RollSurface(roll: roll, place: .focus, text: text)
            .padding(CGFloat.ds("s-3"))
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("accent"), lineWidth: 2))
            .dsShadow("shadow-sheet")
            // The stage's insets, as Web/score.css `#focus .stage` has them.
            .padding(.horizontal, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-5"))
            .padding(.bottom, 52 + CGFloat.ds("s-4") * 2)
    }
}

/// Notes | Piano roll in the focus bar (testing item 12), drawn as the hearing switch is.
struct FocusShowAs: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: 0) {
            option(false, "focus.asNotes")
            option(true, "focus.asRoll")
        }
        .padding(2)
        .frame(height: CGFloat.ds("h-compact"))
        .background(Color.ds("surface"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["focus.showAs"])
    }

    private func option(_ roll: Bool, _ key: String) -> some View {
        let on = score.focusRoll == roll
        return Button {
            score.focusRoll = roll
        } label: {
            SteadyWord(text: text[key], on: on)
                .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                .modifier(SegmentFill(on: on))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

struct FocusBar: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var player: Player
    let variationRange: Range<Double>?
    let text: UIText
    @EnvironmentObject private var assistant: AssistantController

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            // The way back by the way in (#521 17): «К странице» from the score, «Свернуть» from the roll; Escape too.
            let entry = score.expandEntry
            Button {
                score.leaveFocus()
            } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: entry.backIcon, size: CGFloat.ds("icon-l"))
                    Text(text[entry.backKey]).dsType(.label)
                }
                .padding(.leading, CGFloat.ds("s-1"))
                .padding(.trailing, CGFloat.ds("s-3"))
                .frame(height: CGFloat.ds("h-control"))
            }
            .buttonStyle(BarActionStyle())
            .keyboardShortcut(.cancelAction)
            .help(text[entry.backHintKey])
            .accessibilityHint(text[entry.backHintKey])
            Text(score.focusPosition)
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
                .monospacedDigit()
                .lineLimit(1)
                .truncationMode(.tail)
            FocusShowAs(score: score, text: text)
            Spacer(minLength: CGFloat.ds("s-2"))
            if player.hasVariation, let variationRange {
                HearingSwitch(player: player, range: variationRange, text: text, version: player.secondIsVersion ? score.focusVersion?.name : nil)
            }
            if score.focusVersion != nil, !player.hasVariation || player.secondIsVersion {
                // The way into the merge draft for this phrase (testing item 32); bars are no longer taken here.
                Button(text["merge.openDraft"]) { score.openDraftFromFocus() }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            Button {
                // The assistant's panel opens on the phrase in focus (#582 item 17.1).
                assistant.open(wholePiece: false, selection: score.selection)
            } label: {
                Text(text["ask.menu"]).dsType(.label).padding(.horizontal, CGFloat.ds("s-2")).frame(height: 40)
            }
            .buttonStyle(BarActionStyle(on: assistant.panelOpen))
            // No Play here (#511 section 8.1): the pill's blue Play is the one, and the pill always shows in focus.
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }
}

/// Phrases, from the bar (0018 section 3; the approved mockup v0.5.2, #479 item 21): the level focus steps by, Short,
/// Phrases or Sentences, each with how many units the piece has at it, or «Не показывать», then «Вернуть найденные» for
/// a level the player changed. It never lists the phrases, and since #479 item 14 the score does not draw them.
struct PhrasesPanel: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    private static let names = ["short": "phrases.levelShort", "phrase": "phrases.levelPhrases", "sentence": "phrases.levelSentences"]

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(text["phrases.levelTitle"]).overline().padding(.bottom, CGFloat.ds("s-1"))
            ForEach(PhraseLevelsShown.ids, id: \.self) { level in
                let count = score.phrases?.counts[level]
                row(text[Self.names[level] ?? ""], count: count.map(String.init), on: !score.phrasesHidden && (score.phrases?.level ?? "phrase") == level) {
                    score.showLevel(level)
                }
            }
            PaletteRule()
            row(text["phrases.hide"], count: nil, on: score.phrasesHidden) {
                score.phrasesHidden = true
            }
            .accessibilityHint(text["phrases.hideHint"])
            PaletteRule()
            Button(text["phrases.resetLevel"]) { score.resetLevel() }
                .buttonStyle(DSItemStyle())
                .disabled(score.phrasesHidden || !(score.phrases.map { $0.edited.contains($0.level) } ?? false))
                .accessibilityHint(text["phrases.resetHint"])
        }
        .padding(CGFloat.ds("s-3"))
        .frame(width: 300, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    /// A choice of the menu: a round mark, filled while it is chosen, its word and, for a level, its count.
    private func row(_ title: String, count: String?, on: Bool, choose: @escaping () -> Void) -> some View {
        Button(action: choose) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Circle()
                    .strokeBorder(on ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: on ? 5 : 1.5)
                    .frame(width: 16, height: 16)
                Text(title).lineLimit(1)
                Spacer(minLength: CGFloat.ds("s-2"))
                if let count {
                    Text(count).dsType(.caption).foregroundStyle(Color.ds("ink-3"))
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(DSItemStyle(on: on))
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A thin rule across a menu, between its groups.
private struct PaletteRule: View {
    var body: some View {
        Rectangle().fill(Color.ds("hairline")).frame(height: 1).padding(.vertical, CGFloat.ds("s-1"))
    }
}

/// More, from the bar: Check for a piece with flags, Other versions, the layers of marks, what the app owes the piano and the engraving (CC BY 3.0 asks for the piano's
/// credit wherever it plays), and what went wrong, if anything did. The layout, the PDF and the other tools come with later
/// steps.
struct MorePanel: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let problem: String?
    let openCheck: () -> Void
    /// Sources; nil on a view shown over the piece, which has none.
    let openVersions: (() -> Void)?
    let openSettings: () -> Void
    let openDna: () -> Void
    let askAboutPiece: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let problem {
                Text(problem)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("danger"))
            }
            if score.check.hasFlags {
                Button(action: openCheck) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "check")
                        Text(score.check.open > 0 ? text.count("check.toReview", score.check.openBars.count) : text["check.title"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
                Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            }
            // The piece's sources (0030): its originals and the notes read from them.
            if let openVersions {
                Button(action: openVersions) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "mic")
                        Text(text["sources.title"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            // Piece DNA, the map of the whole piece, and the assistant on the whole piece (testing items 22, 23).
            Button(action: openDna) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "dial")
                    Text(text["dna.title"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Button(action: askAboutPiece) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "insight")
                    Text(text["ask.aboutPiece"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            LayersList(score: score, text: text)
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Button(action: openSettings) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "dial")
                    Text(text["settings.all"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Text(credit)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .tint(Color.ds("accent"))
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 340, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    private var credit: AttributedString {
        let markdown = "\(text["credit.piano"]) [Salamander Grand Piano V3](https://archive.org/details/SalamanderGrandPianoV3) \(text["credit.by"]) Alexander Holm, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), \(text["credit.shortened"]) \(text["credit.engraving"]) [Verovio](https://www.verovio.org) 6.3.0, [LGPL 3.0](https://www.gnu.org/licenses/lgpl-3.0.html), [\(text["credit.source"])](https://github.com/rism-digital/verovio/tree/version-6.3.0)."
        return (try? AttributedString(markdown: markdown)) ?? AttributedString(markdown)
    }
}

struct ScoreWebView: UIViewRepresentable {
    @ObservedObject var score: ScoreController
    let piece: OpenPiece
    let dark: Bool

    /// A web view made at launch, so the first score does not wait for WebKit's process to start (0019).
    @MainActor private static var warm: WKWebView?

    @MainActor static func prewarm() {
        guard warm == nil else { return }
        let view = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        view.loadHTMLString("<!doctype html><title></title>", baseURL: nil)
        warm = view
    }

    func makeUIView(context: Context) -> WKWebView {
        score.webView
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        score.show(piece, dark: dark)
        // The stylus menu opens where the Pencil is over any part of the screen, the roll's too (#521 7).
        score.watchStylus(in: view.window)
    }
}

/// Expand as a focused practice view (#521 17, 19): what goes and comes back with it.
extension ScoreScreen {
    /// Expand covers the page (#521 17, 19): the drawers go, and come back with the way back as they were, and so does the
    /// loop. Opened while the music plays, it plays on within the passage. Out of it the playback model takes the selection
    /// and the loop back (v0.5.2).
    private func expandChanged(_ focused: Bool) {
        if focused {
            contentsBeforeFocus = contentsOpen
            contentsOpen = false
        } else if player.loop != nil {
            // Expand's passage looped: the loop it left goes, so the one before comes back as it was.
            player.setLoop(nil)
        }
        syncPlayheads()
        if focused {
            // Expand plays on round its passage; the model out of it is left as it is (T18).
            if player.state != .stopped, let passage = score.playRange?.range { player.keepWithin(passage) }
        } else {
            player.hold(selection: score.playRange?.range)
        }
        guard !focused else { return }
        // Collapse opens the roll drawer again, as Expand found it; Back to page the Contents.
        if rollBeforeFocus {
            rollBeforeFocus = false
            rollOpen = true
        }
        if contentsBeforeFocus {
            contentsBeforeFocus = false
            contentsOpen = true
        }
    }

    /// The roll in focus is the drawer's roll laid over the card, at the player's zoom (#511 section 5), bound to the passage
    /// Expand was opened on (#521 19): only it, laid across the card. Back, or Notes chosen, lets the passage go: its zoom and
    /// view are as before Expand (#521 17).
    private func expandRollShown(_ shown: Bool) {
        if shown {
            if rollOpen {
                rollBeforeFocus = true
                rollOpen = false
            }
            roll.show(piece, dark: colorScheme == .dark)
            roll.showSelection(score.selection, loop: player.loop)
            roll.bind(score.selection.map { $0.onset..<max($0.end, $0.onset + 1) })
            return
        }
        roll.bind(nil)
        if !rollOpen && !rollBeforeFocus {
            // The roll in focus put away, not back to the drawer: its unsaved edits go, as the drawer's do.
            score.cancelNoteEdit()
        }
    }
}
