import XCTest

@testable import MotiffLab

/// The pill's tempo (version 0.5.4, #511 section 17; version 0.5.5, #521 section 12): the piece tempo, one source of truth,
/// apart from the playback pace (1 % steps from 25 % to 150 %), which changes only the speed heard; dotted quarters in 6/8.
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

    func testThePieceTempoIsOneSourceOfTruthAndThePaceOnlyChangesTheSpeedHeard() throws {
        let text = UIText(language: "en")
        let set = try XCTUnwrap(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 500000, "source": "player"}],
             "tempo": {"perMinute": 120, "source": "player", "beat": "quarter", "beatsPerMinute": 120,
                       "piece": {"perMinute": 72, "source": "notated", "beatsPerMinute": 72}, "player": 120}}
            """))
        // Spec example (#521 12.2): 120 at 80 % plays at 96; the piece tempo and the score's mark stay 120.
        XCTAssertEqual(set.pieceText, "♩ = 120")
        XCTAssertEqual(set.heard(at: 80), 96)
        XCTAssertEqual(set.heardText(at: 80), "♩ 96")
        XCTAssertEqual(set.pillText(at: 80, text), "♩ = 120 · 80%")
        XCTAssertEqual(set.pillText(at: 100, text), "♩ = 120")
        // The player's tempo can be cleared back to the notes' own.
        XCTAssertEqual(set.ownBeats, 72)
        XCTAssertEqual(set.playerBeats, 120)

        let notated = try XCTUnwrap(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 833333, "source": "notated"}],
             "tempo": {"perMinute": 72, "source": "notated", "beat": "quarter", "beatsPerMinute": 72,
                       "piece": {"perMinute": 72, "source": "notated", "beatsPerMinute": 72}, "player": null}}
            """))
        XCTAssertNil(notated.ownBeats)
        XCTAssertEqual(notated.pieceCaption(text), text["tempoControl.pieceCaption"])

        // A music-core before 0.5.4 answers no tempo: the pill shows the percentage alone.
        XCTAssertNil(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 500000}]}
            """))
    }

    func testAPieceWithoutATempoPlaysAtTheDefault80MarkedAndSaidToBeAssumed() throws {
        let text = UIText(language: "ru")
        let byDefault = try XCTUnwrap(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 750000, "source": "default"}],
             "tempo": {"perMinute": 80, "source": "default", "beat": "quarter", "beatsPerMinute": 80,
                       "piece": {"perMinute": 80, "source": "default", "beatsPerMinute": 80}, "player": null}}
            """))
        XCTAssertTrue(byDefault.known)
        XCTAssertEqual(byDefault.pieceText, "♩ = 80")
        XCTAssertEqual(byDefault.heardText(at: 50), "♩ 40")
        XCTAssertEqual(byDefault.pieceCaption(text), "Не распознан: принято ♩ = 80. Коснитесь, чтобы ввести напечатанный темп")

        // A recording's own clock, before its bars, is no tempo of the piece.
        let clock = TempoReading(perMinute: 120, source: "assumed", piecePerMinute: 120, pieceSource: "assumed", player: nil)
        XCTAssertFalse(clock.known)
        XCTAssertEqual(clock.pieceText, "♩ ?")
        XCTAssertNil(clock.heardText(at: 90))
        let inferred = TempoReading(perMinute: 88, source: "inferred", piecePerMinute: 88, pieceSource: "inferred", player: nil)
        XCTAssertEqual(inferred.pieceText, "♩ ≈ 88")
        XCTAssertEqual(inferred.heardText(at: 50), "♩ ≈ 44")
        XCTAssertTrue(TempoReading.canSet(92))
        XCTAssertFalse(TempoReading.canSet(301))
    }

    func testIn6Over8TheTempoIsReadAndTypedInDottedQuarters() throws {
        let compound = try XCTUnwrap(reading("""
            {"tempos": [{"at": 0, "microsecondsPerQuarter": 666667, "source": "notated"}],
             "tempo": {"perMinute": 90, "source": "notated", "beat": "dottedQuarter", "beatsPerMinute": 60,
                       "piece": {"perMinute": 90, "source": "notated", "beatsPerMinute": 60}, "player": null}}
            """))
        XCTAssertEqual(compound.note, "♩.")
        XCTAssertEqual(compound.pieceText, "♩. = 60")
        XCTAssertEqual(compound.heardText(at: 100), "♩. 60")
        XCTAssertEqual(compound.heard(at: 50), 30)
        // The printed "♩. = 61" is kept as 92 quarter notes a minute.
        XCTAssertEqual(compound.quarterNotes(61), 92)
        XCTAssertTrue(compound.canSet(200))
        XCTAssertFalse(compound.canSet(201))
    }
}
