using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;
using static MotiffLab.MusicCore.Tests.Projects.Drafts.DraftFixtures;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>A draft's seams through the facade (decision record 0034, section 5; the iPad's step 7, the seam card): described, offered, saved as steps, refused where there is nothing to do.</summary>
public class DraftSeamsFacadeTests
{
    private const string Named = """ "draft": "d_0001" """;

    /// <summary>The example open in Russian with bars 1–4 and bars 5–8 as two places of a draft, the second a tone higher.</summary>
    private static MusicCoreFacade Collage()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language": "ru", "id": "p_seams"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:05:00Z"}""");
        Call(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0002", "semitones": 2, "now": "2026-11-20T10:10:00Z"}""");
        return facade;
    }

    [Fact]
    public void The_seam_names_its_keys_and_offers_in_the_language_of_the_piece()
    {
        JsonElement seam = Call(Collage(), "drafts.seams", $$"""{{{Named}}}""").GetProperty("seams")[0];

        Assert.Equal(("ля минор", "си минор"), (seam.GetProperty("sides")[0].GetProperty("key").GetProperty("name").GetString(), seam.GetProperty("sides")[1].GetProperty("key").GetProperty("name").GetString()));
        JsonElement offer = Assert.Single(seam.GetProperty("offers").EnumerateArray());
        Assert.Equal(("transpose", -2, "② в ля минор"), (offer.GetProperty("kind").GetString(), offer.GetProperty("semitones").GetInt32(), offer.GetProperty("label").GetString()));
    }

    [Fact]
    public void The_seams_read_the_same_once_the_piece_is_saved_and_opened_again()
    {
        MusicCoreFacade facade = Collage();
        Call(facade, "drafts.tempo", $$"""{{{Named}}, "place": "pl_0002", "quarterNotesPerMinute": 60, "now": "2026-11-20T10:15:00Z"}""");
        string before = facade.Call(1, "drafts.seams", $$"""{{{Named}}}""");
        var reopened = new MusicCoreFacade();
        Json(reopened.Open("FurElise.motiff.json", Encoding.UTF8.GetBytes(facade.Call(1, "project.save")), """{"language": "ru"}"""));

        Assert.Equal(before, reopened.Call(1, "drafts.seams", $$"""{{{Named}}}"""));
    }

    [Fact]
    public void The_offer_taken_is_a_step_and_undone_it_is_offered_again()
    {
        MusicCoreFacade facade = Collage();

        JsonElement taken = Call(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0002", "semitones": -2, "now": "2026-11-20T10:20:00Z"}""");

        Assert.Equal(2, taken.GetProperty("step").GetInt32());
        Assert.Empty(Call(facade, "drafts.seams", $$"""{{{Named}}}""").GetProperty("seams")[0].GetProperty("offers").EnumerateArray());
        Call(facade, "drafts.undo", $$"""{{{Named}}}""");
        Assert.Single(Call(facade, "drafts.seams", $$"""{{{Named}}}""").GetProperty("seams")[0].GetProperty("offers").EnumerateArray());
        JsonElement kept = Assert.Single(Json(facade.Call(1, "project.save")).GetProperty("workspace").GetProperty("drafts")[0].GetProperty("steps").EnumerateArray());
        Assert.Equal(("transpose", "pl_0002", 2), (kept.GetProperty("kind").GetString(), kept.GetProperty("place").GetString(), kept.GetProperty("semitones").GetInt32()));
    }

    /// <summary>
    /// A piece whose draft has a place in 6/8 at 150 quarters a minute worked out from a recording, then one whose tempo is a
    /// recording's own clock, assumed.
    /// </summary>
    private static MusicCoreFacade Marked()
    {
        SourceMusic score = Score();
        SourceMusic compound = SourceMusic.Of(score.Notes, new MeterMap([new MeterChange(Tick.Zero, new TimeSignature(6, 8))]), score.End, [new PlaybackTempo(Tick.Zero, 400_000, TempoSource.Inferred)], score.Keys);
        Draft draft = Draft.Start("d_0001", "x", At, "pl_0001", Variant("v_0001", compound, 1, 3))
            .With(Variant("v_0002", score with { Tempos = [new PlaybackTempo(Tick.Zero, 500_000)] }, 1, 2), DraftDrop.Place, "pl_0002");
        Project piece = Project.Create("p_7a1b2c3d4e5f40718293a4b5c6d7e8f9", "Etude.mid", MidiExporter.Export(score.Notes, score.Tempos, score.Meter, "Etude"), At).WithNewDraft(draft);
        var facade = new MusicCoreFacade();
        Json(facade.Open("Etude.motiff.json", ProjectFile.Write(piece), """{"language": "en"}"""));
        return facade;
    }

    [Fact]
    public void A_tempo_is_counted_in_the_beat_of_its_meter_and_marked_as_the_score_marks_it()
    {
        JsonElement sides = Call(Marked(), "drafts.seams", $$"""{{{Named}}}""").GetProperty("seams")[0].GetProperty("sides");

        JsonElement inferred = sides[0].GetProperty("tempo");
        Assert.Equal((150, "inferred", "dottedQuarter", 100, "≈ 100"), (inferred.GetProperty("perMinute").GetInt32(), inferred.GetProperty("source").GetString(), inferred.GetProperty("beat").GetString(), inferred.GetProperty("beatsPerMinute").GetInt32(), inferred.GetProperty("mark").GetString()));
        JsonElement assumed = sides[1].GetProperty("tempo");
        Assert.Equal(("assumed", "quarter", JsonValueKind.Null), (assumed.GetProperty("source").GetString(), assumed.GetProperty("beat").GetString(), assumed.GetProperty("mark").ValueKind));
    }

    [Fact]
    public void What_is_not_there_or_left_to_do_is_refused()
    {
        MusicCoreFacade facade = Collage();

        Assert.Equal("badRequest", Error(facade, "drafts.seams", """{"draft": "d_9"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0002"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.transpose", $$"""{{{Named}}, "place": "pl_0002", "semitones": 0}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.tempo", $$"""{{{Named}}, "place": "pl_0002", "quarterNotesPerMinute": 400}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.tempo", $$"""{{{Named}}, "place": "pl_9", "quarterNotesPerMinute": 60}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.leaveSeam", $$"""{{{Named}}, "place": "pl_0001"}"""));
        Call(facade, "drafts.leaveSeam", $$"""{{{Named}}, "place": "pl_0002", "now": "2026-11-20T10:20:00Z"}""");
        Assert.Equal("unavailable", Error(facade, "drafts.leaveSeam", $$"""{{{Named}}, "place": "pl_0002"}"""));
    }
}