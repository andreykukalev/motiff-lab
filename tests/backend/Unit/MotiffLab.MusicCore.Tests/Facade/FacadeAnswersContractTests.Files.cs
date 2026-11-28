namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>The calls and requests of each answers file (<see cref="FacadeAnswersContractTests"/>), apart from how they are answered and kept.</summary>
public partial class FacadeAnswersContractTests
{
    /// <summary>The calls and requests each file keeps, in its order.</summary>
    private static readonly Dictionary<string, (string Call, string? Request)[]> s_files = new()
    {
        // A passage, and the same narrowed to a hand: "filter" names the hand only then.
        ["select"] =
        [
            ("select", """{"bars":{"first":4,"last":6}}"""),
            ("select", """{"bars":{"first":4,"last":6},"filter":{"hand":"right"}}"""),
        ],
        // The playback score the player loads: notes, pedal, tempos, the tempo shown, beats, bars and the hands.
        ["playback"] = [("playback", null)],
        // The sources by the layers of 0030, built as SourceLayersFacadeTests builds them: the example's own file, a PDF
        // added and read again into other Notes (a group, an engine, a time), a recording kept on the iPad (a performance
        // view with its sound here, "nowhere" without it), the read Notes hidden from the switch, a second PDF removed (a
        // tombstone with its time), and a version (Notes of no original, no group). Each of the sheet's operations
        // answers the layers too.
        ["sources.layers"] =
        [
            ("sources.layers", """{"device":"ipad","here":[]}"""),
            ("sources.original.add", """{"device":"ipad","here":[],"kind":"pdf","fileName":"Nocturne.pdf","kept":"file","data":"<pdf>","pages":1,"now":"2026-11-15T09:00:00Z"}"""),
            ("sources.notes.recognise", """{"device":"ipad","here":[],"original":"src_pdf","musicXml":"<scan>","engine":"homr 0.8.0","pages":1,"now":"2026-11-15T10:00:00Z"}"""),
            ("sources.original.add", """{"device":"ipad","here":[],"kind":"audio","fileName":"Live.m4a","kept":"device","durationMs":161000,"now":"2026-11-15T11:00:00Z"}"""),
            ("sources.view.hide", """{"device":"ipad","here":["src_rec"],"id":"n_2","hidden":true}"""),
            ("sources.original.add", """{"device":"ipad","here":["src_rec"],"kind":"pdf","fileName":"Henle.pdf","kept":"file","data":"<otherPdf>","pages":1,"now":"2026-11-15T12:00:00Z"}"""),
            ("sources.original.remove", """{"device":"ipad","here":["src_rec"],"id":"src_pdf_2","withNotes":false,"now":"2026-11-15T12:30:00Z"}"""),
            ("performance.add", """{"name":"Official","file":"<scan>","fileName":"Official.musicxml","now":"2026-11-15T13:00:00Z"}"""),
            ("sources.layers", """{"device":"ipad","here":["src_rec"]}"""),
            ("sources.layers", """{"device":"ipad","here":[]}"""),
        ],
        // A bar's notes to correct on the piece opened strictly, as MusicCoreFacadeTests and AssistantFacadeTests correct
        // them: none yet (notes on the beat and off it), a note changed, one taken out and put back by an undo, the first
        // put back as imported; the note editor's draft (a note added and set, one taken out, an undo, a reset, Cancel,
        // and one taken out kept by Save); the assistant's draft accepted and undone whole; and a bar taken from a version,
        // whose notes taken are not corrected here. No layer: none of the layer's fields.
        ["corrections"] =
        [
            ("corrections", """{"bar":2}"""),
            ("corrections.change", """{"bar":2,"id":"n_0010","pitch":1,"onset":0,"length":0,"velocity":0}"""),
            ("corrections.remove", """{"bar":2,"id":"n_0013"}"""),
            ("corrections.undo", """{"bar":2}"""),
            ("corrections.revert", """{"bar":2,"id":"n_0010"}"""),
            ("corrections.begin", """{"bar":2}"""),
            ("corrections.add", """{"bar":2,"onset":1260,"pitch":62,"length":120}"""),
            ("corrections.set", """{"bar":2,"id":"a1","onset":1380,"pitch":62,"length":60}"""),
            ("corrections.remove", """{"bar":2,"id":"n_0011"}"""),
            ("corrections.undo", """{"bar":2}"""),
            ("corrections.reset", """{"bar":2}"""),
            ("corrections.discard", """{"bar":2}"""),
            ("corrections.begin", """{"bar":2}"""),
            ("corrections.remove", """{"bar":2,"id":"n_0012"}"""),
            ("corrections.save", """{"bar":2}"""),
            ("corrections.acceptDraft", """{"bar":2,"tool":"notes.edit","parameters":{"bars":{"from":2,"to":2},"edits":[{"kind":"remove","address":"2.R.1","bar":null,"hand":null,"start":null,"length":null,"pitch":null,"velocity":null}]},"index":0}"""),
            ("corrections.undoDraft", """{"bar":2}"""),
            ("performance.add", """{"name":"Live","midi":"<played>","now":"2026-11-15T13:00:00Z"}"""),
            ("performance.take", """{"id":"perf_1","bars":[5]}"""),
            ("corrections", """{"bar":5}"""),
        ],
        // The corrections laid over the notes one by one (the layer of version 0.5), on a project file built as
        // EditsLayerFacadeTests builds its stale one: a note changed and one taken out that fit, and four that no longer do
        // as the notes are now (one laid on a near note of its pitch, one whose note is gone, one whose note is different
        // now, a note added under an id the notes already have). Then one dropped, the layer off and on again, and the
        // correction laid on a near note put back.
        ["corrections.layer"] =
        [
            ("corrections", """{"bar":2}"""),
            ("corrections.drop", """{"bar":2,"noteIds":["n_9999"]}"""),
            ("corrections.layer", """{"on":false,"bar":2}"""),
            ("corrections", """{"bar":2}"""),
            ("corrections.layer", """{"on":true,"bar":2}"""),
            ("corrections.revert", """{"bar":2,"id":"n_0014"}"""),
        ],
        // A version with a bar taken into the notes, as files saved before version 1.1 hold one (the bar-by-bar merge left
        // the iPad, ADR 0034 section 8): the example played with three notes more in bar 5, as MusicCoreFacadeTests plays
        // it, every bar the comparison is sure of taken, and the bar restored.
        ["performance.taken"] =
        [
            ("performance.add", """{"name":"Live","midi":"<played>","now":"2026-11-15T13:00:00Z"}"""),
            ("performance.takeAll", """{"id":"perf_1"}"""),
            ("performance.restore", """{"id":"perf_1","bars":[5]}"""),
        ],
        // The phrase marks (the phrases spec, version 1.2): the marks with nothing changed; the start of phrase 2 dragged a
        // little before its note (snapped to the nearest note, the player's own with the found boundary it replaced) and put
        // back by Undo ("restore" with the edits before, "expected" the edits the move left, a level left out having none);
        // a start set inside phrase 3, phrase 4's start taken away and phrase 2's moved again; «Вернуть найденную» on it;
        // then «Вернуть найденные границы» over the player's two.
        ["phrases.marks"] =
        [
            ("phrases", null),
            ("phrases.edit", """{"move":{"from":2880,"to":2500}}"""),
            ("phrases.edit", """{"restore":{"short":{"added":[],"removed":[]},"phrase":{"added":[],"removed":[]},"sentence":{"added":[],"removed":[]}},"expected":{"phrase":{"added":[2520],"removed":[2880]}}}"""),
            ("phrases.edit", """{"start":7680}"""),
            ("phrases.edit", """{"remove":10560}"""),
            ("phrases.edit", """{"move":{"from":2880,"to":2400}}"""),
            ("phrases.edit", """{"putBack":2400}"""),
            ("phrases.edit", """{"resetAll":true}"""),
        ],
        // Drafts (0034, version 1.1; the iPad's step 5): the «В черновик» sheet with no draft yet, «Добавить» making a new
        // one (answered as the sources, the draft in the group and in the switch), the sheet again with the same music
        // offered first, bars 5–8 as a new place, the right hand on the shelf, the draft view with its place labels, and
        // the draft renamed.
        ["drafts"] =
        [
            ("drafts.sheet", """{"bars":{"first":1,"last":4}}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"part":"all","now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.sheet", """{"bars":{"first":1,"last":4},"part":"all"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"part":"right","draft":"d_0001","drop":"shelf","now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.open", """{"draft":"d_0001"}"""),
            ("drafts.rename", """{"device":"ipad","here":[],"draft":"d_0001","name":"Coda"}"""),
        ],
        // A place's приёмы (0034 section 4; the iPad's step 6): bars 1–4 as a new draft's place and bars 5–8 as its
        // variant, the приёмы with their numbers, positions and labels, the variant opened alone to hear it, one taken only
        // here (then taken by the notes), each step undone in turn, one left, «Взять всё уверенное», the right hand of bars 3–4 taken from the variant, and the
        // приёмы as that step left them.
        ["drafts.techniques"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"variant","place":"pl_0001","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.techniques", """{"draft":"d_0001","place":"pl_0001"}"""),
            ("drafts.open", """{"draft":"d_0001","variant":"v_0002"}"""),
            ("drafts.take", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","technique":1,"bar":4,"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.techniques", """{"draft":"d_0001","place":"pl_0001"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
            ("drafts.leave", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","technique":1,"now":"2026-11-20T10:15:00Z"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
            ("drafts.takeSure", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","now":"2026-11-20T10:20:00Z"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
            ("drafts.takeFrom", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","variant":"v_0002","part":"right","span":{"onset":1440,"end":2880},"now":"2026-11-20T10:25:00Z"}"""),
            ("drafts.techniques", """{"draft":"d_0001","place":"pl_0001"}"""),
        ],
        // A place's приёмы where its variant is notes heard in a recording: each occurrence names the recording and the
        // stretch of it that occurrence was played in.
        ["drafts.techniques.heard"] = [("drafts.techniques", """{"draft":"d_0001","place":"pl_0001"}""")],
        // A draft's seams (0034 section 5; the iPad's step 7, the seam card): bars 1–4 and bars 5–8 as two places of a new
        // draft, the seam between them as alike, then the second place transposed a tone up and set to 60 a minute, the seam
        // with the key and tempo changed and «② в ля минор» and «Темп как в ①» offered, kept as it is, and undone.
        ["drafts.seams"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.seams", """{"draft":"d_0001"}"""),
            ("drafts.transpose", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","semitones":2,"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.tempo", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","quarterNotesPerMinute":60,"now":"2026-11-20T10:15:00Z"}"""),
            ("drafts.seams", """{"draft":"d_0001"}"""),
            ("drafts.leaveSeam", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","now":"2026-11-20T10:20:00Z"}"""),
            ("drafts.seams", """{"draft":"d_0001"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // A draft's map (the iPad's step 7): two places and two variants on the shelf, the map with each place's variants
        // and the shelf, one variant put into the first place and the other as a new place, the first place transposed and
        // then given the variant as its base, which drops that step, the map again, the base undone (the step back) and then the step.
        ["drafts.map"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"part":"right","draft":"d_0001","drop":"shelf","now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"part":"left","draft":"d_0001","drop":"shelf","now":"2026-11-20T10:15:00Z"}"""),
            ("drafts.open", """{"draft":"d_0001"}"""),
            ("drafts.place", """{"device":"ipad","here":[],"draft":"d_0001","variant":"v_0003","drop":"variant","place":"pl_0001","now":"2026-11-20T10:20:00Z"}"""),
            ("drafts.place", """{"device":"ipad","here":[],"draft":"d_0001","variant":"v_0004","drop":"place","now":"2026-11-20T10:25:00Z"}"""),
            ("drafts.transpose", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","semitones":2,"now":"2026-11-20T10:30:00Z"}"""),
            ("drafts.base", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","variant":"v_0003","now":"2026-11-20T10:35:00Z"}"""),
            ("drafts.open", """{"draft":"d_0001"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // A place moved on the draft's map (version 1.4, F1): bars 5–8 put first, the map with the new order, and that undone.
        ["drafts.order"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.movePlace", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","index":0,"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.open", """{"draft":"d_0001"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // A place's length (version 1.4, c-3b): bars 1–4 asked how long they are and made bars 1–6, the map with the new
        // label, and that undone.
        ["drafts.length"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.length", """{"draft":"d_0001","place":"pl_0001"}"""),
            ("drafts.resize", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","bars":{"first":1,"last":6},"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.open", """{"draft":"d_0001"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // A приём carried into another place (version 1.4, c-3b): bars 5–8 as a variant of the place of bars 1–4, whose
        // simplification of bar 4 is carried into a second place of bars 1–4, its приёмы there, and that undone.
        ["drafts.carry"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"variant","place":"pl_0001","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"draft":"d_0001","drop":"place","now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.carryPlaces", """{"draft":"d_0001","place":"pl_0001","technique":1}"""),
            ("drafts.carry", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","technique":1,"to":"pl_0002","now":"2026-11-20T10:15:00Z"}"""),
            ("drafts.techniques", """{"draft":"d_0001","place":"pl_0002"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // «Найти на записи» (version 1.4, F2): the stretch of the recording heard as Notes n_2 that is the music of the place
        // of bars 1–4, put in as its variant, found again as that variant, and that undone.
        ["drafts.recording"] =
        [
            ("drafts.findOnRecording", """{"draft":"d_0001","place":"pl_0001"}"""),
            ("drafts.addFromRecording", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","now":"2026-11-20T10:50:00Z"}"""),
            ("drafts.findOnRecording", """{"draft":"d_0001","place":"pl_0001"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // «Новый черновик» (version 1.5, d-11): bars 13–16 found on the recording n_2 before a draft is made, then made with it.
        ["drafts.recording.new"] = [("drafts.findNewOnRecording", """{"bars":{"first":13,"last":16}}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":13,"last":16},"recording":"n_2","now":"2026-11-26T10:00:00Z"}""")],
        // The register at a seam (version 1.2, the plan's answer 3): bars 5–8 two octaves up after bars 1–4, the seam saying
        // «② на октаву выше» and offering «② на октаву ниже», a transposition by −12.
        ["drafts.register"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.transpose", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","semitones":24,"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.seams", """{"draft":"d_0001"}"""),
        ],
        // A bridge at a seam (version 1.2, the plan's answer 2): the second place a tone up, the seam offering «Связка · 3
        // кандидата», the candidates put as a place between, the draft view with it, the bass walk made its base, the place
        // left (removed) and that undone.
        ["drafts.bridge"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.transpose", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","semitones":2,"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.seams", """{"draft":"d_0001"}"""),
            ("drafts.bridge", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","now":"2026-11-20T10:15:00Z"}"""),
            ("drafts.open", """{"draft":"d_0001"}"""),
            ("drafts.base", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0003","variant":"v_0005","now":"2026-11-20T10:20:00Z"}"""),
            ("drafts.leaveBridge", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0003","now":"2026-11-20T10:25:00Z"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // Anchors (version 1.2, the plan's answer 4): bars 1–8 as a place and bars 1–4 as its variant, its bar 1 confirmed to
        // be the base's bar 3, the приёмы worked out around it, the draft's map with the anchor over its bar, the anchor taken
        // away and that undone.
        ["drafts.anchors"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":8},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"draft":"d_0001","drop":"variant","place":"pl_0001","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.anchor", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","variant":"v_0002","anchor":{"base":2880,"variant":0},"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.techniques", """{"draft":"d_0001","place":"pl_0001"}"""),
            ("drafts.open", """{"draft":"d_0001"}"""),
            ("drafts.unanchor", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","variant":"v_0002","base":2880,"now":"2026-11-20T10:15:00Z"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // The assistant in a draft (version 1.2, decision record 0034 section 6, the plan's answer 7): what a turn sends about
        // a place, the приёмы read, a take proposed and heard after, accepted as the assistant's step and undone.
        ["drafts.assistant"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"variant","place":"pl_0001","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.context", """{"draft":"d_0001","place":"pl_0001"}"""),
            ("drafts.call", """{"draft":"d_0001","place":"pl_0001","tool":"place.techniques","role":"placeAssembler"}"""),
            ("drafts.call", """{"draft":"d_0001","place":"pl_0001","tool":"place.take","parameters":{"techniques":[1],"bar":null},"role":"placeAssembler","now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.proposal", """{"draft":"d_0001","place":"pl_0001","tool":"place.take","parameters":{"techniques":[1],"bar":null},"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.accept", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0001","tool":"place.take","parameters":{"techniques":[1],"bar":null},"role":"placeAssembler","now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.undo", """{"device":"ipad","here":[],"draft":"d_0001"}"""),
        ],
        // The seams' role in a draft: the bridge candidates read at the seam before the second place, the bass walk proposed
        // and accepted as a place between.
        ["drafts.assistant.bridge"] =
        [
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":1,"last":4},"now":"2026-11-20T10:00:00Z"}"""),
            ("drafts.add", """{"device":"ipad","here":[],"bars":{"first":5,"last":8},"draft":"d_0001","drop":"place","now":"2026-11-20T10:05:00Z"}"""),
            ("drafts.transpose", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","semitones":2,"now":"2026-11-20T10:10:00Z"}"""),
            ("drafts.call", """{"draft":"d_0001","place":"pl_0002","tool":"place.bridges","role":"seamChooser"}"""),
            ("drafts.accept", """{"device":"ipad","here":[],"draft":"d_0001","place":"pl_0002","tool":"place.bridge","parameters":{"kind":"bassWalk"},"role":"seamChooser","now":"2026-11-20T10:15:00Z"}"""),
        ],
        // The collage (version 1.2, the plan's answer 5): the drafts of another piece's project file listed, and bars 1–4 of
        // this piece added to the end of its draft, answered with that piece's file to save.
        ["drafts.collage"] =
        [
            ("project.drafts", """{"file":"<pieceA>","language":"en"}"""),
            ("drafts.add", """{"bars":{"first":1,"last":4},"file":"<pieceA>","draft":"d_0001","drop":"place","now":"2026-11-20T10:00:00Z"}"""),
        ],
        // The sources of a piece that is only its PDF, with a bookmark on its pages: no working notes, and page marks.
        ["sources.layers.pdf"] = [("sources.layers", """{"device":"ipad","here":[]}""")],
        // The piano roll's data (step B7): the notes and bars packed as the roll reads them, with their names spelled for
        // the keys in the piece's language, as the roll asks it while the player chose no naming of their own.
        ["roll"] = [("roll", null)],
        // A page sent as its readings by two engines (version 1.3, ADR 0035 section 5): the example's first page as homr and
        // copisteria read it, merged staff by staff; the page answers each bar's level and the engines that gave each staff.
        ["scan.join.readings"] = [("scan.join", """{"pages":[[{"engine":"homr 0.7.0","musicxml":"<homrPage>"},{"engine":"copisteria","musicxml":"<copisteriaPage>"}]]}""")],
        // The open answer (the project's summary on a card and the piece without its notes) and the piece with its notes,
        // bars, start choices and import report.
        ["open"] = [("open", null), ("piece", null)],
        // What the player selects and is told (0018, sections 7 and 11): Piece DNA with no selection, bars 1–4 selected,
        // what was understood about them with each fact's Try chips, the circle of fifths, and Piece DNA with the selection.
        ["understand"] =
        [
            ("dna", null),
            ("select", """{"bars":{"first":1,"last":4}}"""),
            ("understand", null),
            ("circle", null),
            ("dna", """{"ideas":"musical"}"""),
        ],
        // A variation (0018, section 8): bars 1–4, a Try chip applied, the chain with its step and result, the notes of bars
        // 1–2 as written, and the catalogue of operations and Keep, in English and in Russian.
        ["variation"] =
        [
            ("select", """{"bars":{"first":1,"last":4}}"""),
            ("try", """{"chip":"v11.passing"}"""),
            ("variation", null),
            ("notation", """{"bars":{"first":1,"last":2}}"""),
            ("catalogue", null),
            ("catalogue", """{"language":"ru"}"""),
        ],
        // What the player keeps with the piece, opened at a fixed time: a stroke in the margin, a margin note, a highlight and
        // a fingering (the score's marks), a bookmark, resume points, a variation kept as an idea, Check, the settings, «Ноты крупнее» (project file 19) and the project's summary
        // after its details change.
        ["marks"] =
        [
            ("ink.add", """{"strokes":[{"id":"s1","tool":"pen","colour":"carmine","size":2,"bar":0,"u":0.1,"v":0.2,"points":[0,0,0.5,1,1,0.5],"pressure":0.5,"margin":"left"}]}"""),
            ("note.add", """{"bars":{"first":1,"last":2},"text":"Lean into the E","now":"2026-11-15T10:00:00Z"}"""),
            ("highlight.add", """{"bars":{"first":1,"last":1},"notes":[]}"""),
            ("fingering.set", """{"noteId":"n_0001","finger":3}"""),
            ("marks", null),
            ("bookmark.add", """{"bars":{"first":1,"last":2},"title":"A","now":"2026-11-15T10:00:00Z"}"""),
            ("bookmarks", null),
            ("resume.save", """{"name":"Coda","now":"2026-11-15T09:00:00Z"}"""),
            ("resume.save", """{"id":"r_0001","passage":{"span":{"onset":1920,"end":3840},"filter":{"hand":"left"}},"overlays":["insights","pianoRoll"],"tempoPercent":70,"hands":"left","loop":true,"focused":true,"now":"2026-11-15T11:00:00Z"}"""),
            ("resume", null),
            ("select", """{"bars":{"first":1,"last":4}}"""),
            ("try", """{"chip":"v11.passing"}"""),
            ("idea.keep", """{"name":"Half time","now":"2026-11-15T10:00:00Z"}"""),
            ("ideas", null),
            ("check", null),
            ("settings", null), ("largerNotes", null), ("largerNotes.set", """{"step":7}"""),
            ("project.metadata", """{"title":"Für Elise","composer":"Beethoven","tags":["practise"]}"""),
        ],
        // The marks on a PDF's pages (0023), opened at a fixed time: two strokes, one in the margin, with its bookmark, and
        // the project's summary.
        ["document.marks"] =
        [
            ("document.ink.add", """{"strokes":[{"id":"s1","tool":"pen","colour":"carmine","size":2,"page":1,"u":0.1,"v":4,"points":[0,0,0.5,1,1,0.5],"pressure":0.5,"margin":"right"},{"id":"s2","tool":"pen","colour":"graphite","size":2,"page":1,"u":0.2,"v":9,"points":[0,0,1,1,1,0.5],"pressure":0.5}]}"""),
            ("document.marks", null),
            ("project", null),
        ],
        // The reference cards with no piece open: keys, a scale, a chord, the settings' state and the harmony explorer.
        ["reference"] =
        [
            ("reference.key", """{"fifths":-3}"""),
            ("reference.scale", """{"fifths":2,"mode":"minor"}"""),
            ("reference.chord", """{"root":2,"quality":"minor"}"""),
            ("settings.state", null),
            ("reference.explorer", """{"fifths":0,"mode":"major","language":"en"}"""),
        ],
        // A folder planned for the library (version 0.4): a score with its title and composer, two PDFs with the same bytes
        // (one a duplicate), and a file it cannot take.
        ["library"] =
        [
            ("library.plan", """{"files":[{"path":"Бах/Prelude.musicxml","bytes":500,"musicXml":"PD94bWwgdmVyc2lvbj0iMS4wIj8+PHNjb3JlLXBhcnR3aXNlIHZlcnNpb249IjQuMCI+PHdvcms+PHdvcmstdGl0bGU+UHJlbHVkZSBpbiBDPC93b3JrLXRpdGxlPjwvd29yaz48aWRlbnRpZmljYXRpb24+PGNyZWF0b3IgdHlwZT0iY29tcG9zZXIiPkouIFMuIEJhY2g8L2NyZWF0b3I+PC9pZGVudGlmaWNhdGlvbj48cGFydC1saXN0Lz48L3Njb3JlLXBhcnR3aXNlPg=="},{"path":"Шопен/Chopin - Ballade No. 1.pdf","bytes":1000,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","pages":24},{"path":"Концерт/Ballade.pdf","bytes":1000,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"path":"readme.txt","bytes":3}],"library":[],"folders":"playlists"}"""),
        ],
        // The library's playlists (version 1.7, on piece 0): moved from the pieces' properties, read, a playlist made, a name
        // taken, a piece moved and two copies resolved; "<playlists>" is a playlists file with "Бах" holding two pieces.
        ["playlists"] =
        [
            ("playlists.migrate", """{"pieces":[{"id":"p_answers","playlists":["Бах","Für Elise"]},{"id":"p_other","playlists":["бах"]}]}"""),
            ("playlists", """{"file":"<playlists>"}"""),
            ("playlists.create", """{"file":"<playlists>","name":"Для рояля","pieces":["p_answers"]}"""),
            ("playlists.create", """{"file":"<playlists>","name":"БАХ"}"""),
            ("playlists.move", """{"file":"<playlists>","id":"list_0001","pieces":["p_other"],"before":"p_answers"}"""),
            ("playlists.resolve", """{"here":"<playlists>","savedHere":"2026-11-28T10:05:00Z","online":"<playlists>","savedOnline":"2026-11-28T10:00:00Z"}"""),
        ],
    };
}