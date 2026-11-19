import XCTest

@testable import MotiffLab

/// Dictation into a chat's field (v0.6.4, DictationFlow in Dictation.swift): on the device only, in the interface's
/// language, live into the field after what was typed, stopped by a press or a pause.
final class DictationTests: XCTestCase {
    func testAPressStartsListensAndWritesAfterWhatWasTyped() {
        var flow = DictationFlow()
        XCTAssertEqual(flow.apply(.press, draft: "Bar 12:"), .start)
        XCTAssertEqual(flow.state, .starting)
        XCTAssertFalse(flow.pressable, "a second press while it starts does nothing")
        XCTAssertNil(flow.apply(.press, draft: "Bar 12:"))
        flow.apply(.started)
        XCTAssertTrue(flow.listening)
        flow.apply(.heard("why"))
        XCTAssertEqual(flow.text, "Bar 12: why")
        flow.apply(.heard("why is it loud"))
        XCTAssertEqual(flow.text, "Bar 12: why is it loud", "the words heard so far replace the ones before, whole")
    }

    func testTheWordsJoinWhatWasTypedWithOneSpace() {
        XCTAssertEqual(DictationFlow.join("", "hello"), "hello")
        XCTAssertEqual(DictationFlow.join("Bar 3", ""), "Bar 3")
        XCTAssertEqual(DictationFlow.join("Bar 3", "left hand"), "Bar 3 left hand")
        XCTAssertEqual(DictationFlow.join("Bar 3 ", "left hand"), "Bar 3 left hand")
    }

    func testAPressWhileListeningStopsAndKeepsTheText() {
        var flow = listening("Remove the stray note")
        XCTAssertEqual(flow.apply(.press), .stop)
        XCTAssertEqual(flow.state, .ready)
        XCTAssertEqual(flow.text, "Remove the stray note")
        flow.apply(.heard("late words"))
        XCTAssertEqual(flow.text, "Remove the stray note", "nothing is heard once stopped")
    }

    func testAPauseStopsLongerBeforeTheFirstWordsThanAfterThem() {
        var quiet = DictationFlow()
        quiet.apply(.press)
        quiet.apply(.started)
        XCTAssertNil(quiet.apply(.silence(DictationFlow.pauseAfterWords)), "before any words the player gets longer to start")
        XCTAssertEqual(quiet.apply(.silence(DictationFlow.pauseBeforeWords)), .stop)

        var talking = listening("Make it legato")
        XCTAssertNil(talking.apply(.silence(DictationFlow.pauseAfterWords - 0.5)))
        XCTAssertEqual(talking.apply(.silence(DictationFlow.pauseAfterWords)), .stop)
        XCTAssertEqual(talking.state, .ready)
        XCTAssertEqual(talking.text, "Make it legato")
    }

    func testWithoutOnDeviceRecognitionTheMicrophoneSaysWhyOnAPressAndNeverListens() {
        var flow = DictationFlow(problem: .notOnDevice)
        XCTAssertEqual(flow.state, .unavailable(.notOnDevice))
        XCTAssertFalse(flow.told, "known before, it waits for a press to say so")
        XCTAssertEqual(flow.apply(.press), .start, "the controller checks again and refuses")
        XCTAssertEqual(flow.apply(.refused(.notOnDevice)), .stop)
        XCTAssertEqual(flow.state, .unavailable(.notOnDevice))
        XCTAssertTrue(flow.told)
        XCTAssertFalse(flow.listening)
    }

    func testRefusedPermissionsAndABreakOffAreSaid() {
        var denied = DictationFlow()
        denied.apply(.press)
        denied.apply(.refused(.denied))
        XCTAssertEqual(denied.state, .unavailable(.denied))
        XCTAssertTrue(denied.pressable, "allowed in Settings since, a press asks again")

        var broke = DictationFlow()
        broke.apply(.press)
        broke.apply(.started)
        XCTAssertEqual(broke.apply(.ended(failed: true)), .stop)
        XCTAssertEqual(broke.state, .unavailable(.failed))

        var finished = listening("All of it")
        finished.apply(.ended(failed: true))
        XCTAssertEqual(finished.state, .ready, "words heard before the break-off stay, and it is no failure")
    }

    func testTypingSendingOrTheChatGoingHaltsIt() {
        var flow = listening("Half said")
        XCTAssertEqual(flow.apply(.halt), .stop)
        XCTAssertEqual(flow.state, .ready)
        XCTAssertNil(flow.apply(.halt), "halted twice, nothing more")
        var starting = DictationFlow()
        starting.apply(.press)
        XCTAssertEqual(starting.apply(.halt), .stop)
        XCTAssertNil(starting.apply(.refused(.denied)), "a refusal after the halt changes nothing")
        XCTAssertEqual(starting.state, .ready)
    }

    func testItHearsTheInterfacesLanguage() {
        XCTAssertEqual(DictationFlow.locale(language: "ru").identifier, "ru-RU")
        XCTAssertEqual(DictationFlow.locale(language: "en").identifier, "en-US")
    }

    private func listening(_ words: String) -> DictationFlow {
        var flow = DictationFlow()
        flow.apply(.press)
        flow.apply(.started)
        flow.apply(.heard(words))
        return flow
    }
}
