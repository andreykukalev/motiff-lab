import Foundation

/// The port to music-core for an open piece (decision record 0031 step 1b, blueprint 5.4, step B5): the calls the features
/// make whose answers are typed, each run through `MusicCore.run` and `MusicCore.call` and decoded once into its typed answer
/// (PieceAnswers.swift) by `FacadeAnswer.decode`, which reports an answer that cannot be read. The calls it makes are named
/// here and nowhere else in the app (tests/shared/facade-calls-contract.test.mjs); the others move here with their typed
/// answers, one answer at a time.
struct PieceCore: Sendable {
    /// The piece's number in the facade.
    let piece: Int32

    /// What music-core answered: the typed answer, nil for a refusal or an answer that cannot be read, and the text it came in.
    struct Answer<Value: Decodable & Sendable>: Sendable {
        let value: Value?
        let text: String

        init(_ text: String, of name: String) {
            self.text = text
            value = FacadeAnswer.decode(Value.self, text, of: name)
        }

        /// music-core's words in its answer ("message", a refusal's), nil without them.
        var message: String? { FacadeAnswer.message(text) }
    }

    /// `select`: the passage a request names (bars, a span, notes picked, narrowed by a filter).
    func select(_ request: String) async -> Answer<SelectionAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "select", request) }
        return Answer(text, of: "select")
    }

    /// `playback`: the piece's playback score; the request chooses one hand, or the piece with the variation being tried in
    /// place or another version's notes.
    func playback(_ request: String? = nil) async -> Answer<PlaybackAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "playback", request) }
        return Answer(text, of: "playback")
    }

    /// `sources.layers`: the piece's sources by the layers of decision record 0030 as the request's "device" sees them,
    /// with "here", the originals whose bytes the app keeps on this device.
    func sourceLayers(_ request: String) async -> Answer<SourceLayersAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "sources.layers", request) }
        return Answer(text, of: "sources.layers")
    }
}
