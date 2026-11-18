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

    /// `roll`: the piano roll's data; the request's "naming" ("letters" or "syllables") spells the names, else the piece's
    /// language's.
    func roll(_ request: String?) async -> Answer<RollAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "roll", request) }
        return Answer(text, of: "roll")
    }

    /// `sources.layers`: the piece's sources by the layers of decision record 0030 as the request's "device" sees them,
    /// with "here", the originals whose bytes the app keeps on this device.
    func sourceLayers(_ request: String) async -> Answer<SourceLayersAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "sources.layers", request) }
        return Answer(text, of: "sources.layers")
    }

    /// The corrections calls (OpenPiece.Corrections.cs and OpenPiece.EditsLayer.cs), every one answered with a bar's notes
    /// as `CorrectionsAnswer`: the request's "bar" (for `layer` and `drop`, the first bar without one).
    enum Correction: Sendable {
        /// `corrections`: the bar's notes to correct.
        case bar
        /// Fix notes in Check: a note stepped, taken out, put back as imported; the last correction undone (in the note
        /// editor, its last edit).
        case change, remove, revert, undo
        /// The assistant's draft accepted, and undone whole.
        case acceptDraft, undoDraft
        /// «Мои правки»: the layer on or off; the corrections that no longer fit taken out.
        case layer, drop
        /// The roll's note editor: the draft begun, a note set or added, every correction taken out, Cancel and Save.
        case begin, set, add, reset, discard, save
    }

    /// `corrections` and `corrections.*`: the bar's notes, or a correction made and the bar's notes as they are then.
    func corrections(_ call: Correction, _ request: String) async -> Answer<CorrectionsAnswer> {
        switch call {
        case .bar:
            return await correctionsCall("corrections", request)
        case .change:
            return await correctionsCall("corrections.change", request)
        case .remove:
            return await correctionsCall("corrections.remove", request)
        case .revert:
            return await correctionsCall("corrections.revert", request)
        case .undo:
            return await correctionsCall("corrections.undo", request)
        case .acceptDraft:
            return await correctionsCall("corrections.acceptDraft", request)
        case .undoDraft:
            return await correctionsCall("corrections.undoDraft", request)
        case .layer:
            return await correctionsCall("corrections.layer", request)
        case .drop:
            return await correctionsCall("corrections.drop", request)
        case .begin:
            return await correctionsCall("corrections.begin", request)
        case .set:
            return await correctionsCall("corrections.set", request)
        case .add:
            return await correctionsCall("corrections.add", request)
        case .reset:
            return await correctionsCall("corrections.reset", request)
        case .discard:
            return await correctionsCall("corrections.discard", request)
        case .save:
            return await correctionsCall("corrections.save", request)
        }
    }

    // Each name written where the call is made, one per case (the facade call scan reads them here).
    private func correctionsCall(_ name: String, _ request: String) async -> Answer<CorrectionsAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, name, request) }
        return Answer(text, of: name)
    }

    /// `phrases.level`: another level shown, and the selection moved to the unit that is, encloses or starts it there.
    func phraseLevel(_ request: String) async -> Answer<PhraseLevelAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "phrases.level", request) }
        return Answer(text, of: "phrases.level")
    }

    /// The merge draft's calls (OpenPiece.Merge.cs), every one answered with the draft as `MergeAnswer`.
    enum Merge: Sendable {
        /// The draft opened for a version, with the choices the request's "start" names.
        case open
        /// Bars chosen from either side, or undecided again.
        case choose, undecide
        /// The draft's last step taken back, done again, or every step taken back.
        case undo, redo, reset
        /// The draft gone with nothing changed: none open.
        case cancel
    }

    /// `performance.merge.open` and the draft's changes: the draft as it is then.
    func merge(_ call: Merge, _ request: String) async -> Answer<MergeAnswer> {
        switch call {
        case .open:
            return await mergeDraftCall("performance.merge.open", request)
        case .choose:
            return await mergeDraftCall("performance.merge.choose", request)
        case .undecide:
            return await mergeDraftCall("performance.merge.undecide", request)
        case .undo:
            return await mergeDraftCall("performance.merge.undo", request)
        case .redo:
            return await mergeDraftCall("performance.merge.redo", request)
        case .reset:
            return await mergeDraftCall("performance.merge.reset", request)
        case .cancel:
            return await mergeDraftCall("performance.merge.cancel", request)
        }
    }

    // As `correctionsCall`: one literal per case.
    private func mergeDraftCall(_ name: String, _ request: String) async -> Answer<MergeAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, name, request) }
        return Answer(text, of: name)
    }

    /// `performance.merge.playback`: one of the draft's views to hear (the request's "view"), the whole piece with its notes.
    func mergePlayback(_ request: String) async -> Answer<PlaybackAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "performance.merge.playback", request) }
        return Answer(text, of: "performance.merge.playback")
    }

    /// `performance.merge.apply`: the draft's bars taken into the score; the version as it is compared then.
    func mergeApply(_ request: String) async -> Answer<PerformanceAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "performance.merge.apply", request) }
        return Answer(text, of: "performance.merge.apply")
    }

    /// `performance.merge.undoApplied`: the player's notes back in every bar the merge took; the version compared then.
    func mergeUndoApplied(_ request: String) async -> Answer<PerformanceAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "performance.merge.undoApplied", request) }
        return Answer(text, of: "performance.merge.undoApplied")
    }
}
