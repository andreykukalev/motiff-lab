import XCTest

@testable import MotiffLab

/// The pill's tempo (version 0.5.4, #511 section 17): 1 % steps from 25 % to 150 %, the beats heard beside them, where the
/// piece's tempo comes from, as music-core's playback score says it, and dotted quarters in 6/8.
@MainActor
final class TempoTests: XCTestCase {
    private func reading(_ json: String) -> TempoReading? {
        TempoReading.read(facadeObject(json) ?? [:])
    }

    func testTheTempoChangesIn1PercentStepsWithin25To150AndTellsTheEngineOnlyWhenItChanged() {
        let tempo = TempoSetting()
        var sent: [Int] = []
        tempo.changed = { sent.append($0) }

        tempo.percent = 77
        tempo.percent += 1
        tempo.percent = 400
        tempo.percent = 150
        tempo.percent = 3

        XCTAssertEqual(tempo.percent, 25)
        XCTAssertEqual(sent, [77, 78, 150, 25])
        XCTAssertEqual(TempoReading.clamped(151), 150)
        XCTAssertEqual(TempoReading.steps, [50, 60, 70, 80, 90, 100, 110])
    }

    func testThePlaybackScoresTempoIsReadWithItsSourceAndTheBeatsHeard() throws {
        let assumed = try XCTUnwrap(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 500000, "source": "assumed"}],
             "tempo": {"perMinute": 120, "source": "assumed", "beat": "quarter", "beatsPerMinute": 120,
                       "piece": {"perMinute": 120, "source": "assumed", "beatsPerMinute": 120}, "player": null}}
            """))
        XCTAssertEqual(assumed.source, "assumed")
        XCTAssertNil(assumed.player)
        XCTAssertEqual(assumed.heard(at: 77), 92)
        // Not recognised: the pill never shows the default as the piece's tempo.
        XCTAssertEqual(assumed.heardText(at: 90), "♩ ?")

        let set = try XCTUnwrap(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 652174, "source": "player"}],
             "tempo": {"perMinute": 92, "source": "player", "beat": "quarter", "beatsPerMinute": 92,
                       "piece": {"perMinute": 120, "source": "notated", "beatsPerMinute": 120}, "player": 92}}
            """))
        XCTAssertEqual(set.player, 92)
        XCTAssertEqual(set.playerBeats, 92)
        XCTAssertEqual(set.heard(at: 100), 92)
        XCTAssertEqual(set.heard(at: 90), 83)
        XCTAssertEqual(set.heardText(at: 90), "♩ 83")
        XCTAssertEqual(set.pieceSource, "notated")
        // ♩ = 76 typed over 92 is 83 %, which plays 76.
        XCTAssertEqual(set.percent(for: 76), 83)
        XCTAssertEqual(set.heard(at: set.percent(for: 76)), 76)

        // A music-core before 0.5.4 answers no tempo: the pill shows the percentage alone.
        XCTAssertNil(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 500000}]}
            """))
    }

    func testIn6Over8TheTempoIsReadAndTypedInDottedQuarters() throws {
        let compound = try XCTUnwrap(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 666667, "source": "notated"}],
             "tempo": {"perMinute": 90, "source": "notated", "beat": "dottedQuarter", "beatsPerMinute": 60,
                       "piece": {"perMinute": 90, "source": "notated", "beatsPerMinute": 60}, "player": null}}
            """))
        XCTAssertEqual(compound.note, "♩.")
        XCTAssertEqual(compound.heardText(at: 100), "♩. 60")
        XCTAssertEqual(compound.heard(at: 50), 30)
        // The printed "♩. = 61" is kept as 92 quarter notes a minute.
        XCTAssertEqual(compound.quarterNotes(61), 92)
        XCTAssertTrue(compound.canSet(200))
        XCTAssertFalse(compound.canSet(201))
        XCTAssertEqual(compound.sourceLine(UIText(language: "ru")), "♩. = 60")
    }

    func testTheSourceIsSaidSoThePrintedTempoAnEstimateAndThePlayersAreNeverMixedUp() {
        let text = UIText(language: "ru")
        func said(_ source: String, piece: String = "assumed") -> (String, String) {
            let reading = TempoReading(perMinute: 120, source: source, piecePerMinute: 100, pieceSource: piece, player: nil)
            return (reading.sourceTag(text).0, reading.sourceLine(text))
        }
        XCTAssertEqual(said("assumed").0, "не распознан")
        XCTAssertEqual(said("assumed").1, "принято ♩ = 120")
        XCTAssertEqual(said("notated").0, "из нот")
        XCTAssertEqual(said("notated").1, "♩ = 120")
        XCTAssertEqual(said("inferred").0, "по записи")
        XCTAssertEqual(said("inferred").1, "♩ ≈ 120")
        XCTAssertEqual(said("player").0, "задан вами")
        XCTAssertEqual(said("player", piece: "notated").1, "♩ = 120, в файле ♩ = 100")
        XCTAssertTrue(TempoReading.canSet(92))
        XCTAssertFalse(TempoReading.canSet(301))
    }
}
