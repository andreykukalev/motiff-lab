import XCTest

/// The journeys on the example piece's score (architecture blueprint 6.1, layer T4): each asserts what a player does and
/// sees, through the controls and the app's probe, and names the entries of the UX baseline (`docs/ux-baseline.md`) it
/// protects. Each starts and ends on the score with nothing over the page, no selection and nothing playing.
extension Walk {
    /// The example piece opened from the empty library and played: the sound loads and the playing moves on.
    func openExampleAndPlay() throws {
        try openExample(shots: true)
        for (label, what) in [("Библиотека", "Back"), ("Фразы", "Phrases"), ("Фортепианный ролл", "the piano roll"), ("Инсайты", "Insights"),
                              ("Навигация", "Navigate"), ("Выделение", "Select"), ("Перо", "the Pen"), ("Источники", "Sources")] {
            try need(button(label), "\(what) in the score's bar")
        }
        try need(more, "More in the score's bar")
        try need(button(beginning: "Играть с"), "the pill's Play")
        pause(1.5)
        shot("10-score")
        try play("on the piece")
        try stop()
    }

    /// PLAY-04: a selection, its loop, and a new selection while it plays. Цикл turns on as a selection appears (T9),
    /// follows a new selection while it plays (the playing goes to its start), and stays as it was when the selection
    /// changes with it off (T10).
    func selectLoopAndChange() throws {
        let first = try choosePhrase()
        guard first.on("cycle") else { throw miss("Цикл did not turn on as the selection appeared (PLAY-04)") }
        try need(button("Фокус"), "the selection menu")
        try need(button(beginning: "Цикл "), "the pill's Цикл with the selection's span")
        shot("13-selection-menu")
        try press(button("Ещё для выделения"), "the selection menu's More")
        try need(button("Спросить"), "Ask in the selection menu's More")
        shot("14-selection-menu-more")
        try press(button("Ещё для выделения"), "the selection menu's More again")

        try play("on the selection")
        let looping = try until("playing inside the selection") { $0.playing && $0.insideRange }
        try choosePhrase()
        try until("Цикл follows the new selection while it plays: the playing goes to it and loops it (PLAY-04)") {
            $0.playing && $0.on("cycle") && $0["range"] != looping["range"] && $0.insideRange
        }
        try press(button(beginning: "Цикл "), "Цикл")
        try until("Цикл off") { !$0.on("cycle") }
        try choosePhrase()
        try holds(for: 1.5, "Цикл turned on again by a new selection while it was off (PLAY-04)") { !$0.on("cycle") }
        try stop()
        try clearSelection()
    }

    /// SEL-03: in Select a drag selects where the finger goes, and a second drag begun over the selection makes a new one
    /// rather than leaving the old one as it was. The drags run along the second system, the same in both orientations.
    func selectByDragAndOver() throws {
        try press(button("Выделение"), "Select in the bar's modes")
        try until("Select chosen") { $0["mode"] == "select" }
        stroke(from: CGVector(dx: 0.12, dy: 0.40), to: CGVector(dx: 0.30, dy: 0.40))
        let first = try until("a selection made by a drag in Select") { $0.span("sel") != nil }
        guard let made = first.span("sel") else { throw miss("no selection") }
        stroke(from: CGVector(dx: 0.21, dy: 0.40), to: CGVector(dx: 0.40, dy: 0.40))
        try until("a new selection begun over the one there was (SEL-03)") { ($0.span("sel")?.lowerBound ?? 0) > made.lowerBound }
        try clearSelection()
        try press(button("Навигация"), "Navigate in the bar's modes")
        try until("Navigate chosen") { $0["mode"] == "navigate" }
    }

    /// ROLL-01, PLAY-01, PLAY-03: the roll's drawer keeps its zoom when the selection changes; Expand from the drawer shows
    /// the passage as a roll and Collapse gives the drawer back; Expand from the notes has its own playhead, which a tap
    /// on the card moves inside the passage; in Expand Play plays the passage round and round.
    func rollAndExpand() throws {
        try choosePhrase()
        try press(button("Фортепианный ролл"), "the piano roll in the bar")
        let open = try until("the roll drawer open and drawn", timeout: 15) { $0["roll"] == "drawer" && $0["zoom"] != "none" }
        try need(button(beginning: "Потяните"), "the roll drawer's handle")
        pause(1)
        shot("15-piano-roll")
        try choosePhrase()
        try holds(for: 2, "the roll's zoom changed with the selection (ROLL-01)") { $0["zoom"] == open["zoom"] }

        try expandFromDrawer()
        try until("Expand from the drawer shows the passage as a roll", timeout: 8) { $0.on("focus") && $0["as"] == "roll" && $0["roll"] == "focus" }
        try playRoundThePassage()
        try press(button("Свернуть"), "Collapse in Expand's bar")
        try until("Collapse gives the roll drawer back", timeout: 8) { !$0.on("focus") && $0["roll"] == "drawer" }
        try press(button("Фортепианный ролл"), "the piano roll in the bar again")
        try until("the roll drawer put away") { $0["roll"] == "none" }

        try press(button("Фокус"), "Expand in the selection menu")
        try until("Expand from the notes", timeout: 8) { $0.on("focus") && $0["as"] == "notes" }
        try need(button("К странице"), "Back to the page in Expand's bar")
        pause(1.5)
        shot("21-focus")
        let still = try until("stopped in Expand") { !$0.playing }
        app.windows.firstMatch.coordinate(withNormalizedOffset: CGVector(dx: 0.62, dy: 0.5)).tap()
        try until("a tap on Expand's card moves its playhead, inside the passage (PLAY-01)") { $0.tick != still.tick && $0.insideRange }
        try playRoundThePassage()
        try press(button("К странице"), "Back to the page")
        try until("back on the page") { !$0.on("focus") }
        try clearSelection()
    }

    /// Expand in the roll drawer's head, or in its More where the head is narrow.
    private func expandFromDrawer() throws {
        let expand = button("Развернуть")
        if expand.exists && expand.isHittable {
            try press(expand, "Expand in the roll drawer")
        } else {
            try press(button(id: "roll.more"), "the roll drawer's More")
            try press(button("Развернуть"), "Expand in the roll drawer's More")
        }
    }

    /// In Expand the pill's Play plays the passage (PLAY-01: Expand has the transport) and comes round to its start again
    /// (PLAY-03: the passage loops), never playing outside it.
    private func playRoundThePassage() throws {
        let first = try play("in Expand")
        guard first.insideRange else { throw WalkMiss(what: "Expand played outside its passage (PLAY-01)", probe: first.line) }
        var furthest = first.tick
        try until("Expand's passage played round to its start again (PLAY-03)", timeout: 25) { probe in
            guard probe.playing, probe.insideRange else { return false }
            furthest = max(furthest, probe.tick)
            return probe.tick < furthest - 120
        }
        try stop()
    }

    /// Insights on a phrase, and a way tried from a fact: it sounds as a variation until it is left.
    func insightsAndTry() throws {
        try choosePhrase()
        try press(button("Инсайты"), "Insights")
        try need(button(beginning: "Спросить помощника"), "the Insights sheet")
        let tryIt = button("Попробовать")
        try need(tryIt, "Try on a fact of the phrase", timeout: 20)
        pause(1)
        shot("16-insights")
        try press(tryIt, "Try")
        let way = button(id: "insights.tryWay")
        try need(way, "a way to try")
        shot("18-insights-try")
        try press(way, "a way to try")
        try until("the way tried sounds as a variation", timeout: 15) { $0.on("variation") }
        try need(button("Отказаться"), "Leave on the variation tried")
        pause(1)
        shot("19-insights-variation")
        try press(button("Отказаться"), "Leave")
        try until("the variation left", timeout: 8) { !$0.on("variation") }
        if probe()?.playing == true { try stop() }
        try press(button("Инсайты"), "Insights again")
        try gone(button(beginning: "Спросить помощника"), "the Insights sheet")
        try clearSelection()
    }

    /// INK-01: with the Pen a stroke writes and does not scroll the page (one finger writes in the simulator, as the Pencil
    /// does on an iPad used with one); the eraser drawn over it takes it away.
    func penAndEraser() throws {
        try press(button("Перо"), "the Pen in the bar's modes")
        let before = try until("the Pen on") { $0.on("pen") }
        try press(button("Карандаш"), "the pencil in the pen's palette")
        let from = CGVector(dx: 0.30, dy: 0.42)
        let to = CGVector(dx: 0.36, dy: 0.62)
        stroke(from: from, to: to)
        let written = try until("a stroke written with the Pen") { ($0.int("ink") ?? 0) > (before.int("ink") ?? 0) }
        guard written["scroll"] == before["scroll"] else {
            throw WalkMiss(what: "the Pen's stroke scrolled the page from \(before["scroll"] ?? "?") (INK-01)", probe: written.line)
        }
        shot("28-pen-palette")
        try press(button("Ластик"), "the eraser in the pen's palette")
        stroke(from: from, to: to)
        try until("the stroke erased") { ($0.int("ink") ?? 0) < (written.int("ink") ?? 0) }
        try press(button("Убрать карандаш"), "Put the pen away")
        try until("the Pen off") { !$0.on("pen") }
    }

    /// The assistant's panel opened from More on the whole piece, and closed.
    func assistantPanel() throws {
        try press(more, "More")
        try press(button("Спросить о пьесе"), "Ask about the piece in More")
        try until("the assistant's panel open") { $0.on("assistant") }
        try need(button("Новый разговор"), "the assistant's panel")
        pause(1)
        shot("35-assistant")
        try press(button("Закрыть"), "Close on the assistant's panel")
        try until("the assistant's panel closed") { !$0.on("assistant") }
    }

    /// The other places of the score for the mockups: Phrases, a bookmark, the Contents, More and Settings, Sources; then
    /// back to the library with the example's card.
    func otherPlaces() throws {
        try press(button("Фразы"), "Phrases")
        try need(app.otherElements["PopoverDismissRegion"], "the Phrases popover")
        shot("12-phrases-levels")
        dismissPopover()

        try choosePhrase()
        try press(button("Закладка"), "Bookmark in the selection menu")
        try need(button("Готово"), "the new bookmark's card")
        pause(1)
        shot("22-bookmark-card")
        try press(button("Готово"), "Done on the bookmark's card")
        pause(1)
        shot("24-marks-on-page")
        try clearSelection()

        try press(title, "the title, which opens the Contents")
        try need(button("Страницы"), "the Contents drawer")
        pause(1)
        shot("25-contents")
        // The chips scroll sideways: where Notes is off the drawer's width, its picture is not taken.
        let notes = button("Заметки")
        if notes.exists && notes.isHittable {
            notes.tap()
            shot("26-contents-notes")
        }
        try press(button("Страницы"), "Pages in the Contents")
        pause(2)
        shot("27-contents-pages")
        try press(title, "the title again")

        try press(more, "More")
        try need(button("Все настройки"), "All settings in More")
        shot("30-more")
        try press(button("Все настройки"), "All settings")
        try need(button("Закрыть настройки"), "the Settings sheet")
        pause(1)
        shot("32-settings")
        try press(button("Оформление"), "Appearance in Settings")
        shot("33-settings-appearance")
        try press(button("Закрыть настройки"), "Close settings")

        try press(button("Источники"), "Sources in the bar")
        try need(button("Закрыть другие версии"), "the Sources sheet")
        pause(1)
        shot("50-sources")
        try press(button("Закрыть другие версии"), "Close Sources")

        try press(button("Библиотека"), "Back to the library")
        try need(button("Добавить пьесу"), "the library with the example's card")
        pause(2)
        shot("04-library")
    }
}
