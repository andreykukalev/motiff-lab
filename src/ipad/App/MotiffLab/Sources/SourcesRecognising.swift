import Foundation

/// Recognising an original in Sources, the readers of `SourcesController.recognise` (v1.6 i-13, out of
/// SourcesController.swift): the pages kept in the piece read by the scan server, a recording's sound heard by Motiff Lab's,
/// the fields each gives music-core and the words of the toast. They read the controller's state and write none: the one
/// path, `recognise`, and the notes kept, `keepRecognised`, are the controller's own.
extension SourcesController {
    /// What recognising pages says with no scan server set (scanning off in Settings, or a build without Motiff Lab's): what
    /// the PDF's own panel says (`ReadNotesPanel`), never that a server found no notes, which none was asked (v1.5 r-2b).
    static func noScanServer(_ text: UIText) -> String {
        text["import.scanSetUp"] + " " + text["import.scanSetUpLink"] + "."
    }

    /// E-sources-1: the original being recognised now when it is not `original`, by the name its row shows, in the
    /// player's words; nil when none is,
    /// or it is this one. One original is recognised at a time, as the library recognises one piece at a time: a second
    /// Send while the first is on its way would go to the server too, and the first, coming back, would clear the second's
    /// progress and give its Send back (`reading` holds one original).
    func readingOther(than original: SourceLayers.Original) -> String? {
        Self.readingOther(reading, than: original, in: layers, text: text)
    }

    /// The words of `readingOther` for the original `reading` (nil: none is being recognised).
    static func readingOther(_ reading: String?, than original: SourceLayers.Original, in layers: SourceLayers, text: UIText) -> String? {
        guard let reading, reading != original.id else { return nil }
        return text.format("sources.sheet.readingOther", layers.original(reading)?.shown ?? "")
    }

    /// What recognising an original gives music-core to keep as its new Notes: the call's fields, a field to send again
    /// without when music-core refuses it ("badRequest"), and the toast once the notes are kept.
    struct Recognised {
        var fields: [String: Any]
        var dropped: String?
        var toast: String
    }

    /// Why an original was not recognised, in the player's words.
    struct Refusal: Error {
        let words: String
    }

    /// A toast's text, then music-core's line of what became of the player's work, an answer's object's "text", when it
    /// says something: the corrections on notes recognised again («Как напечатано», step 2: "carried"), the bookmarks and
    /// phrases on notes made working (v1.5 c-11b: "followed").
    nonisolated static func toastText(_ done: String, adding part: Any?) -> String {
        guard let line = (part as? [String: Any])?["text"] as? String, !line.isEmpty else { return done }
        return "\(done) · \(line)"
    }

    /// The line before an original's pages are sent as a scan job (0035 section 6; v1.5 i-7, the 1.4 plan's answer 9: in
    /// Sources too): what the iPad reads itself, what goes to the server and about how long; nil when the server takes no jobs.
    func jobPlanLine(_ original: SourceLayers.Original, address: URL) async -> String? {
        guard case .success(let kept) = await keptPdf(original) else { return nil }
        return await ScanServer.jobPlanLine(data: kept.data, address: address, text: text)
    }

    /// The original's pages, as kept in the piece, read by the scan server, for `sources.notes.recognise`.
    func readPages(_ original: SourceLayers.Original, address: URL, account: Account) async -> Result<Recognised, Refusal> {
        let kept: (data: Data, fileName: String)
        switch await keptPdf(original) {
        case .failure(let refusal): return .failure(refusal)
        case .success(let pdf): kept = pdf
        }
        let result: ScanResult
        switch await ScanServer.recognise(fileName: kept.fileName, data: kept.data, address: address, account: account) {
        case .failure(let problem): return .failure(Refusal(words: problem.text(text)))
        case .success(let read): result = read
        }
        let pages = max(1, original.pages ?? result.pages)
        var fields: [String: Any] = ["original": original.id, "musicXml": result.musicXml.base64EncodedString(), "engine": result.engine, "pages": pages, "now": Self.now()]
        if let reports = result.reports { fields["reports"] = reports }
        // The reports may not match the pages read: then the notes are kept without them.
        let dropped: String? = result.reports == nil ? nil : "reports"
        return .success(Recognised(fields: fields, dropped: dropped, toast: text.count("sources.sheet.toastRead", pages)))
    }

    /// The recording's sound, kept on this iPad, heard by Motiff Lab's scan server part by part (0014), for
    /// `sources.notes.hear`: the notes of the whole recording, their bars found as for a live performance ("tempo":
    /// "live"); the working notes' metre and tempo as the hint, none on a piece with no notes yet (v1.1, finding 29).
    /// How the hearing goes is told to `progress`.
    func hearRecording(_ original: SourceLayers.Original, account: Account, progress: (String) -> Void) async -> Result<Recognised, Refusal> {
        guard let projectId, let url = OriginalSounds.file(project: projectId, original: original.id) else {
            return .failure(Refusal(words: text["sources.sheet.soundNotHere"]))
        }
        switch await RecordingHearing.hear(url, account: account, text: text, progress: progress) {
        case .failure(let problem):
            return .failure(Refusal(words: problem.message))
        case .success(let heard):
            let hint = await gridHint()
            let fields = Self.hearFields(heard, original: original.id, hint: hint, now: Self.now())
            // A metre music-core offers no grid for is refused: the notes heard are sent again without the hint.
            return .success(Recognised(fields: fields, dropped: hint == nil ? nil : "hint", toast: text["sources.sheet.toastHeard"]))
        }
    }

    /// The fields of `sources.notes.hear` for the whole recording heard: the transcription's ("tempo": "live"), the
    /// original's id, the time and, when there is one, the working notes' metre and tempo as the grid's "hint", so the
    /// recording's bars come out in the piece's metre (v0.10 s-3).
    nonisolated static func hearFields(_ heard: HeardRecording, original: String, hint: [String: Any]?, now: String) -> [String: Any] {
        var fields = heard.request
        fields["tempo"] = "live"
        fields["original"] = original
        fields["now"] = now
        if let hint { fields["hint"] = hint }
        return fields
    }

    /// The grid hint a recording is heard with: the working notes' metre and tempo (`hint`); none on a piece with no notes
    /// yet, which has neither (v1.1, finding 29).
    func gridHint() async -> [String: Any]? {
        guard layers.working != nil else { return nil }
        return Self.hint(await PieceCore(piece: piece).playback().value)
    }

    /// The working notes' metre and tempo as music-core's grid hint ({"signature": "3/4", "quarterNotesPerMinute"}): the
    /// signature of their first bar, and the tempo the player set for the piece, else the one its notes state or
    /// music-core inferred; no tempo when nothing states one (the default, or a recording's clock); nil without bars.
    nonisolated static func hint(_ playback: PlaybackAnswer?) -> [String: Any]? {
        // A whole note in music-core's ticks (480 a quarter): the beat's note value is counted from it.
        let wholeNote = 1920.0
        guard let bar = playback?.bars.first, bar.beatTicks > 0, bar.beatsPerBar >= 1,
              wholeNote.truncatingRemainder(dividingBy: bar.beatTicks) == 0 else { return nil }
        var hint: [String: Any] = ["signature": "\(Int(bar.beatsPerBar))/\(Int(wholeNote / bar.beatTicks))"]
        let stated = playback?.tempo?.piece.flatMap { ["notated", "inferred"].contains($0.source ?? "") ? $0.perMinute : nil }
        if let perMinute = playback?.tempo?.player ?? stated {
            hint["quarterNotesPerMinute"] = perMinute
        }
        return hint
    }
}
