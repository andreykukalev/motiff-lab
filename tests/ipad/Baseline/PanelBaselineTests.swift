import XCTest

@testable import MotiffLab

/// The score screen's panels as Andrei approved them (docs/ux-baseline.md, VIEW-05 and VIEW-06): PanelStateTests' cases
/// that the baseline names, kept in its protected folder so that they change only with his label. What every action does
/// to the panels, render by render, is PanelStateTests' table (Golden/panel-states.txt).
extension PanelStateTests {
    /// VIEW-05 (Andrei's answer 9, 2026-11-17): More → «Спросить о пьесе» while Sources is open over Insights. The
    /// assistant's panel opens and stays, and Sources closes: Insights, which Sources kept under it, does not come back,
    /// so no sheet opens on the right to put the panel away again. What else Sources kept (the roll's drawer) comes back.
    func testVIEW05TheAssistantFromMoreStaysAndSourcesCloses() {
        let insights = PanelState().asked(.open(.insights)).following(.insights(true))
        let sources = insights.asked(.open(.versions)).following(.versions(true, focused: false)).following(.insights(false))
        XCTAssertEqual(sources.beforeSources, PanelState.Shelved(insights: true), "Sources is open over Insights")
        // More's «Спросить о пьесе»: More goes and the assistant's panel opens, which puts the sheet away.
        let asked = sources.asked(.close(.more)).asked(.assistantOpened)
        XCTAssertFalse(asked[.versions], "Sources closes")
        let closed = asked.following(.versions(false, focused: false))
        XCTAssertFalse(closed[.insights], "Insights does not come back under the assistant's panel")
        XCTAssertFalse(closed.sheetOpen, "nothing on the right puts the assistant's panel away")
        XCTAssertNil(closed.beforeSources)

        let roll = PanelState().asked(.open(.roll)).following(.roll(true))
            .asked(.open(.versions)).following(.versions(true, focused: false)).following(.roll(false))
        let back = roll.asked(.assistantOpened).following(.versions(false, focused: false))
        XCTAssertTrue(back[.roll], "the roll's drawer is not on the right: it comes back as before")
    }

    /// VIEW-06 (Andrei's answer 10, 2026-11-17): «‹ Ещё» shows only on the place opened from More. Check from More, then
    /// a version's marker opens Sources: Sources has none; Sources from More, then a flag on the score opens Check: Check
    /// has none. Two places both opened from More both keep it, as before, and «‹ Ещё» takes the way back of all.
    func testVIEW06TheWayBackToMoreOnlyOnThePlaceMoreOpened() {
        let check = PanelState().asked(.openFromMore(.check)).following(.check(true))
        XCTAssertTrue(check.fromMore.contains(.check))
        let marker = check.asked(.open(.versions)).following(.versions(true, focused: false)).following(.check(false))
        XCTAssertTrue(marker[.versions] && !marker[.check])
        XCTAssertFalse(marker.fromMore.contains(.versions), "Sources opened by a version's marker shows no «‹ Ещё»")

        let sources = PanelState().asked(.openFromMore(.versions)).following(.versions(true, focused: false))
        let flag = sources.asked(.open(.check)).following(.check(true)).following(.versions(false, focused: false))
        XCTAssertTrue(flag[.check] && !flag[.versions])
        XCTAssertFalse(flag.fromMore.contains(.check), "Check opened by a flag shows no «‹ Ещё»")

        let both = check.asked(.openFromMore(.settings))
        XCTAssertTrue(both.fromMore.contains(.check) && both.fromMore.contains(.settings), "each was opened from More")
        let again = both.asked(.close(.check)).following(.check(false)).asked(.open(.check))
        XCTAssertFalse(again.fromMore.contains(.check), "closed, then opened by a flag: no «‹ Ещё»")
        XCTAssertTrue(again.fromMore.contains(.settings))
        let backToMore = both.asked(.backToMore(.settings))
        XCTAssertFalse(backToMore.fromMore.contains(.check) || backToMore.fromMore.contains(.settings), "as before")
    }
}
