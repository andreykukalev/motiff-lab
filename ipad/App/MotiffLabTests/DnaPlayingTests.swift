import XCTest
@testable import MotiffLab

/// Piece DNA's Hear on a card (#582 item 14): the card's own button starts its passage and reads Stop while it sounds;
/// it reads Hear again when the passage ends, when it is stopped, and when another passage or the map starts. The map's
/// button reads Stop only for what it, a mark or a section started.
final class DnaPlayingTests: XCTestCase {
    private let first = 0.0..<1920.0
    private let second = 3840.0..<5760.0

    func testTheCardThatStartedItsPassageReadsStopWhileItSounds() {
        XCTAssertTrue(DnaPlaying.card(first, heard: first, loop: first, state: .playing))
        XCTAssertFalse(DnaPlaying.card(second, heard: first, loop: first, state: .playing))
    }

    func testTheCardReadsHearAgainWhenItsPassageEndsOrIsStopped() {
        // Played once, the engine stops at the passage's end, the loop still set.
        XCTAssertFalse(DnaPlaying.card(first, heard: first, loop: first, state: .stopped))
        // Stop clears the card's passage and the loop.
        XCTAssertFalse(DnaPlaying.card(first, heard: nil, loop: nil, state: .stopped))
    }

    func testAnotherPassageOrTheMapStartingTurnsTheFirstCardBack() {
        // Another card: the card's passage and the loop are the other one's.
        XCTAssertFalse(DnaPlaying.card(first, heard: second, loop: second, state: .playing))
        // A mark or a section loops its passage and clears the card's.
        XCTAssertFalse(DnaPlaying.card(first, heard: nil, loop: second, state: .playing))
        // The map plays the piece with no loop.
        XCTAssertFalse(DnaPlaying.card(first, heard: nil, loop: nil, state: .playing))
    }

    func testTheMapsButtonHasNoStopForACardsPassage() {
        XCTAssertFalse(DnaPlaying.map(playing: true, card: first, state: .playing))
        XCTAssertTrue(DnaPlaying.map(playing: true, card: nil, state: .playing))
        XCTAssertFalse(DnaPlaying.map(playing: true, card: nil, state: .stopped))
        XCTAssertFalse(DnaPlaying.map(playing: false, card: nil, state: .playing))
    }
}
