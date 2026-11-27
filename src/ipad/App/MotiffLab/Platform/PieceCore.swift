import Foundation

/// The port to music-core for an open piece (decision record 0031 step 1b, blueprint 5.4, step B5, #654): every call the app
/// makes by name is named here or in PieceCoreCalls.swift and nowhere else (tests/shared/facade-calls-contract.test.mjs).
/// The calls whose answers are typed run through `MusicCore.run` and `MusicCore.call` and are decoded once into their
/// typed answer (PieceAnswers.swift) by `FacadeAnswer.decode`, which reports an answer that cannot be read; the others
/// (PieceCoreCalls.swift) answer the text, and move here with their typed answers, one answer at a time.
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

    /// `drafts.sheet`: the «В черновик» sheet for the request's "span" of this piece's notes and its "part" (v1.1, ADR
    /// 0034): what can be taken, the piece's drafts and the place where the fragment is the same music. On notes opened
    /// only to be shown, the drafts are those of the piece they were opened from.
    func draftSheet(_ request: String) async -> Answer<DraftSheetAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, "drafts.sheet", request) }
        return Answer(text, of: "drafts.sheet")
    }

    /// `drafts.techniques`: the приёмы of the place `place` of the draft `draft` (v1.1, ADR 0034 section 4), asked on the
    /// piece the draft belongs to or on its view, which music-core answers for the piece it was opened from.
    func draftTechniques(draft: String, place: String) async -> Answer<DraftTechniquesAnswer> {
        let piece = piece
        let request = facadeRequest(["draft": draft, "place": place])
        let text = await MusicCore.run { MusicCore.call(piece: piece, "drafts.techniques", request) }
        return Answer(text, of: "drafts.techniques")
    }

    /// `drafts.seams`: the seams of the draft `draft` (v1.1, ADR 0034 section 5), asked on the piece it belongs to or on its
    /// view, which music-core answers for the piece it was opened from.
    func draftSeams(_ draft: String) async -> Answer<DraftSeamsAnswer> {
        let piece = piece
        let request = facadeRequest(["draft": draft])
        let text = await MusicCore.run { MusicCore.call(piece: piece, "drafts.seams", request) }
        return Answer(text, of: "drafts.seams")
    }

    /// `drafts.findOnRecording`: what «Найти на записи» finds on the piece's recordings for the place `place` of the draft
    /// `draft` (v1.4, F2), nothing changed; asked on the piece the draft belongs to.
    func draftRecording(draft: String, place: String) async -> Answer<DraftRecordingAnswer> {
        await draftCall(["draft": draft, "place": place], call: "drafts.findOnRecording")
    }

    /// `drafts.length`: how long the place `place` of the draft `draft` is in the bars of its source, and whether that can
    /// change (v1.4), nothing changed; asked on the piece the draft belongs to.
    func draftLength(draft: String, place: String) async -> Answer<DraftLengthAnswer> {
        await draftCall(["draft": draft, "place": place], call: "drafts.length")
    }

    /// `drafts.carryPlaces`: the draft's other places the приём `technique` of the place `place` (read at the place's bar
    /// `bar`) can be carried to (v1.4), nothing changed; asked on the piece the draft belongs to.
    func draftCarryPlaces(draft: String, place: String, technique: Int, bar: Int) async -> Answer<DraftCarryPlacesAnswer> {
        await draftCall(["draft": draft, "place": place, "technique": technique, "bar": bar], call: "drafts.carryPlaces")
    }

    /// `drafts.open`: the draft `draft` opened under a number of its own only to be shown ("readOnly"), with its places
    /// (v1.1, ADR 0034); asked on the piece it belongs to or on a view of it. The screen closes the number when it goes.
    func openDraft(_ draft: String) async -> Answer<DraftViewAnswer> {
        await draftCall(["draft": draft])
    }

    /// `drafts.open` with "variant": that variant of the draft alone, read-only (c-4b), as a приём is heard as the variant
    /// has it; the caller closes the number once heard.
    func openVariant(_ variant: String, of draft: String) async -> Answer<DraftVariantAnswer> {
        await draftCall(["draft": draft, "variant": variant])
    }

    /// `drafts.proposal` (v1.2, the assistant in a draft): the draft as the assistant's proposal would leave it (`fields`:
    /// the draft, the place, the tool, its parameters and the role), opened under a number of its own only to be shown and
    /// answered as `drafts.open` («▶ После»); nothing is kept, and the caller closes the number once heard.
    func openProposal(_ fields: [String: Any]) async -> Answer<DraftViewAnswer> {
        await draftCall(fields, call: "drafts.proposal")
    }

    /// A drafts call on this piece (`call`, `drafts.open` unless named) and its typed answer.
    private func draftCall<Value: Decodable & Sendable>(_ fields: [String: Any], call: String = "drafts.open") async -> Answer<Value> {
        let piece = piece
        let request = facadeRequest(fields)
        let text = await MusicCore.run { MusicCore.call(piece: piece, call, request) }
        return Answer(text, of: call)
    }

    /// The corrections calls (`CorrectionsSession` in Facade/Sessions, its adapter OpenPiece.Corrections.cs), every one
    /// answered with a bar's notes as `CorrectionsAnswer`: the request's "bar" (for `layer` and `drop`, the first bar without
    /// one). The raw value is the call's name.
    enum Correction: String, Sendable {
        /// `corrections`: the bar's notes to correct.
        case bar = "corrections"
        /// Fix notes in Check: a note stepped, taken out, put back as imported; the last correction undone (in the note
        /// editor, its last edit).
        case change = "corrections.change", remove = "corrections.remove", revert = "corrections.revert", undo = "corrections.undo"
        /// The assistant's draft accepted, and undone whole (so is a scan's variant put in, `variant(_:)`).
        case acceptDraft = "corrections.acceptDraft", undoDraft = "corrections.undoDraft"
        /// «Мои правки»: the layer on or off; the corrections that no longer fit taken out.
        case layer = "corrections.layer", drop = "corrections.drop"
        /// The roll's note editor: the draft begun, a note set or added, every correction taken out, Cancel and Save.
        case begin = "corrections.begin", set = "corrections.set", add = "corrections.add", reset = "corrections.reset"
        case discard = "corrections.discard", save = "corrections.save"
    }

    /// `corrections` and `corrections.*`: the bar's notes, or a correction made and the bar's notes as they are then.
    func corrections(_ call: Correction, _ request: String) async -> Answer<CorrectionsAnswer> {
        await correctionsCall(call.rawValue, request)
    }

    /// `corrections.variant` (v1.3 i-2): another engine's reading of a scan's bar to check put in that bar as the player's
    /// corrections, undone whole by `.undoDraft`; the bar's notes as they are then.
    func variant(_ request: String) async -> Answer<CorrectionsAnswer> {
        await correctionsCall("corrections.variant", request)
    }

    /// A corrections call by its name and its typed answer.
    private func correctionsCall(_ name: String, _ request: String) async -> Answer<CorrectionsAnswer> {
        let piece = piece
        let text = await MusicCore.run { MusicCore.call(piece: piece, name, request) }
        return Answer(text, of: name)
    }
}
