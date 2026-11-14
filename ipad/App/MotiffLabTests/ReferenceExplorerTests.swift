import XCTest

@testable import MotiffLab

/// The Reference's «Аккорды и гаммы» and «Исследователь» on the iPad (v0.5, #479 item 6): music-core's `reference.sounding`
/// and `reference.explorer` on piece 0 as the iPad reads them, the explorer's choices, and the notation's size.
final class ReferenceExplorerTests: XCTestCase {
    func testTheExplorerStartsAKeyOnTheTonicSubdominantDominantAndTonic() throws {
        let choice = ExplorerChoice()
        let request = try XCTUnwrap(String(data: JSONSerialization.data(withJSONObject: choice.request(language: "en", naming: "letters")), encoding: .utf8))
        let answer = try XCTUnwrap(ExplorerAnswer(answer: MusicCore.call(piece: 0, "reference.explorer", request)))

        XCTAssertEqual(answer.keys.count, 30)
        XCTAssertEqual(answer.key.name, "C major")
        XCTAssertEqual(answer.chords.map(\.numeral), ["I", "ii", "iii", "IV", "V", "V7", "vi", "vii°"])
        XCTAssertEqual(answer.bars.map(\.numeral), ["I", "IV", "V", "I"])
        XCTAssertEqual(answer.steps, [.init(chord: "I"), .init(chord: "IV"), .init(chord: "V"), .init(chord: "I")])
        XCTAssertEqual(answer.textures, ["together", "arpeggio", "broken", "alberti"])
        XCTAssertEqual(answer.tempos, [60, 72, 90, 108, 120])
        XCTAssertEqual(answer.maxBars, 8)
        XCTAssertEqual(answer.score?.notes.count, 36)
        XCTAssertEqual(answer.score?.timeline.tempos.first?.microsecondsPerQuarter, 833_333)
        XCTAssertFalse(answer.mei.isEmpty)
    }

    func testABarInAnotherPositionSoundsWithItsFiguresAndBass() throws {
        var choice = ExplorerChoice()
        choice.start(fifths: 0, minor: true)
        choice.steps = [.init(chord: "i"), .init(chord: "V7", inversion: 1)]
        choice.texture = "together"
        let request = try XCTUnwrap(String(data: JSONSerialization.data(withJSONObject: choice.request(language: "ru", naming: "syllables")), encoding: .utf8))
        let answer = try XCTUnwrap(ExplorerAnswer(answer: MusicCore.call(piece: 0, "reference.explorer", request)))

        XCTAssertEqual(answer.key.name, "ля минор")
        XCTAssertEqual(answer.bars.last?.figure, "6/5")
        XCTAssertEqual(answer.bars.last?.symbol, "E7/G♯")
        XCTAssertEqual(answer.chord("V7")?.positions.count, 4)
        XCTAssertEqual(answer.sound(of: 0)?.pitches.count, 4)
        XCTAssertNil(answer.sound(of: 2))
    }

    func testTheExplorersChoicesAddTurnRemoveAndCycleTheTempo() {
        var choice = ExplorerChoice()
        choice.steps = [.init(chord: "I"), .init(chord: "V")]

        choice.add("IV", maxBars: 3)
        choice.add("vi", maxBars: 3)
        XCTAssertEqual(choice.steps?.map(\.chord), ["I", "V", "IV"])
        choice.turn(bar: 1, inversion: 2)
        XCTAssertEqual(choice.steps?[1].inversion, 2)
        choice.select(bar: 1)
        XCTAssertEqual(choice.selected, 1)
        choice.select(bar: 1)
        XCTAssertNil(choice.selected)
        choice.select(bar: 0)
        choice.remove(bar: 0)
        XCTAssertEqual(choice.steps?.map(\.chord), ["V", "IV"])
        XCTAssertNil(choice.selected)
        choice.nextTempo([60, 72, 90, 108, 120])
        XCTAssertEqual(choice.tempo, 90)
        choice.tempo = 120
        choice.nextTempo([60, 72, 90, 108, 120])
        XCTAssertEqual(choice.tempo, 60)
        choice.start(fifths: -3, minor: true)
        XCTAssertNil(choice.steps)
        XCTAssertNil(choice.request(language: "en", naming: "letters")["steps"])
        XCTAssertEqual(ExplorerChoice.key("upAndDown"), "UpAndDown")
    }

    func testAChordSoundsInItsPositionSpacingAndWay() throws {
        var choice = SoundingChoice()
        choice.inversion = 1
        choice.spacing = "open"
        choice.pattern = "together"
        let fields = choice.request(root: ReferenceRoot(step: 4, alter: 0), type: "dominant7", form: "major", language: "en", naming: "letters")
        let request = try XCTUnwrap(String(data: JSONSerialization.data(withJSONObject: fields), encoding: .utf8))
        let sounded = try XCTUnwrap(ReferenceSounding(answer: MusicCore.call(piece: 0, "reference.sounding", request)))

        XCTAssertEqual(sounded.score?.notes.map(\.pitch), [47, 53, 62, 67])
        XCTAssertEqual(sounded.names, ["B", "F", "D", "G"])
        XCTAssertEqual(sounded.figure, "6/5")
        XCTAssertEqual(sounded.bass, "B")
        XCTAssertEqual(sounded.spacings, ["close", "open"])
        XCTAssertEqual(sounded.patterns, ["together", "up", "down", "upAndDown", "broken"])
        XCTAssertFalse(sounded.mei.isEmpty)
    }

    func testAScaleSoundsUpAndDownOverTheOctavesChosen() throws {
        var choice = SoundingChoice()
        choice.scale = true
        choice.octaves = 2
        choice.scalePattern = "up"
        let fields = choice.request(root: ReferenceRoot(step: 1, alter: 0), type: nil, form: "harmonicMinor", language: "en", naming: "letters")
        let request = try XCTUnwrap(String(data: JSONSerialization.data(withJSONObject: fields), encoding: .utf8))
        let sounded = try XCTUnwrap(ReferenceSounding(answer: MusicCore.call(piece: 0, "reference.sounding", request)))

        XCTAssertEqual(sounded.score?.notes.count, 15)
        XCTAssertEqual(sounded.patterns, ["up", "down", "upAndDown"])
        XCTAssertNil(sounded.inversion)
    }

    func testTheChordsAndScalesOfferedTellTheirGroups() throws {
        let chords = try XCTUnwrap(ReferenceLookup(answer: MusicCore.call(piece: 0, "reference.chord", #"{"language": "en"}"#)))
        let scales = try XCTUnwrap(ReferenceLookup(answer: MusicCore.call(piece: 0, "reference.scale", #"{"language": "ru"}"#)))

        XCTAssertEqual(chords.choices.first?.family, "Triad")
        XCTAssertEqual(scales.choices.filter { $0.family == "Pentatonic" }.map(\.short), ["мажорная", "минорная"])
    }

    func testANotationsSizeComesFromItsViewBox() throws {
        let size = try XCTUnwrap(ReferenceEngraving.viewBox(#"<svg width="10px" viewBox="0 0 900 300"><svg viewBox="0 0 1 1"/></svg>"#))
        let engraving = ReferenceEngraving(svg: "", width: size.width, height: size.height)

        XCTAssertEqual(size.width, 900)
        XCTAssertEqual(engraving.height(at: 450, stretch: true), 150)
        XCTAssertEqual(engraving.height(at: 1800, stretch: false), 300)
        XCTAssertNil(ReferenceEngraving.viewBox("<svg/>"))
    }

    func testAPositionIsNamedByItsTitleOrFigures() {
        let text = UIText(language: "en")

        XCTAssertEqual(ReferencePositionName.of(inversion: 0, title: "root position", figure: nil, text: text), "Root position")
        XCTAssertEqual(ReferencePositionName.of(inversion: 1, title: "first inversion", figure: "6", text: text), "6")
    }
}
