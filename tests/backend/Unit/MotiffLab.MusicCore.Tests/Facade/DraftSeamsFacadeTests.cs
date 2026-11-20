using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;

using static MotiffLab.MusicCore.Tests.Facade.FacadeCalls;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>A draft's seams through the facade (decision record 0034, section 5; the iPad's step 7, the seam card): described, offered, saved as steps, refused where there is nothing to do.</summary>
public class DraftSeamsFacadeTests
{
    private const string Draft = """ "draft": "d_0001" """;

    /// <summary>The example open in Russian with bars 1–4 and bars 5–8 as two places of a draft, the second a tone higher.</summary>
    private static MusicCoreFacade Collage()
    {
        var facade = new MusicCoreFacade();
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml"));
        Json(facade.Open("FurElise.musicxml", data, """{"language": "ru", "id": "p_seams"}"""));
        Call(facade, "drafts.add", """{"bars": {"first": 1, "last": 4}, "now": "2026-11-20T10:00:00Z"}""");
        Call(facade, "drafts.add", """{"bars": {"first": 5, "last": 8}, "draft": "d_0001", "drop": "place", "now": "2026-11-20T10:05:00Z"}""");
        Call(facade, "drafts.transpose", $$"""{{{Draft}}, "place": "pl_0002", "semitones": 2, "now": "2026-11-20T10:10:00Z"}""");
        return facade;
    }

    [Fact]
    public void The_seam_names_its_keys_and_offers_in_the_language_of_the_piece()
    {
        JsonElement seam = Call(Collage(), "drafts.seams", $$"""{{{Draft}}}""").GetProperty("seams")[0];

        Assert.Equal(("ля минор", "си минор"), (seam.GetProperty("sides")[0].GetProperty("key").GetProperty("name").GetString(), seam.GetProperty("sides")[1].GetProperty("key").GetProperty("name").GetString()));
        JsonElement offer = Assert.Single(seam.GetProperty("offers").EnumerateArray());
        Assert.Equal(("transpose", -2, "② в ля минор"), (offer.GetProperty("kind").GetString(), offer.GetProperty("semitones").GetInt32(), offer.GetProperty("label").GetString()));
    }

    [Fact]
    public void The_seams_read_the_same_once_the_piece_is_saved_and_opened_again()
    {
        MusicCoreFacade facade = Collage();
        Call(facade, "drafts.tempo", $$"""{{{Draft}}, "place": "pl_0002", "quarterNotesPerMinute": 60, "now": "2026-11-20T10:15:00Z"}""");
        string before = facade.Call(1, "drafts.seams", $$"""{{{Draft}}}""");
        var reopened = new MusicCoreFacade();
        Json(reopened.Open("FurElise.motiff.json", Encoding.UTF8.GetBytes(facade.Call(1, "project.save")), """{"language": "ru"}"""));

        Assert.Equal(before, reopened.Call(1, "drafts.seams", $$"""{{{Draft}}}"""));
    }

    [Fact]
    public void The_offer_taken_is_a_step_and_undone_it_is_offered_again()
    {
        MusicCoreFacade facade = Collage();

        JsonElement taken = Call(facade, "drafts.transpose", $$"""{{{Draft}}, "place": "pl_0002", "semitones": -2, "now": "2026-11-20T10:20:00Z"}""");

        Assert.Equal(2, taken.GetProperty("step").GetInt32());
        Assert.Empty(Call(facade, "drafts.seams", $$"""{{{Draft}}}""").GetProperty("seams")[0].GetProperty("offers").EnumerateArray());
        Call(facade, "drafts.undo", $$"""{{{Draft}}}""");
        Assert.Single(Call(facade, "drafts.seams", $$"""{{{Draft}}}""").GetProperty("seams")[0].GetProperty("offers").EnumerateArray());
        JsonElement kept = Assert.Single(Json(facade.Call(1, "project.save")).GetProperty("workspace").GetProperty("drafts")[0].GetProperty("steps").EnumerateArray());
        Assert.Equal(("transpose", "pl_0002", 2), (kept.GetProperty("kind").GetString(), kept.GetProperty("place").GetString(), kept.GetProperty("semitones").GetInt32()));
    }

    [Fact]
    public void What_is_not_there_or_left_to_do_is_refused()
    {
        MusicCoreFacade facade = Collage();

        Assert.Equal("badRequest", Error(facade, "drafts.seams", """{"draft": "d_9"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.transpose", $$"""{{{Draft}}, "place": "pl_0002"}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.transpose", $$"""{{{Draft}}, "place": "pl_0002", "semitones": 0}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.tempo", $$"""{{{Draft}}, "place": "pl_0002", "quarterNotesPerMinute": 400}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.tempo", $$"""{{{Draft}}, "place": "pl_9", "quarterNotesPerMinute": 60}"""));
        Assert.Equal("badRequest", Error(facade, "drafts.leaveSeam", $$"""{{{Draft}}, "place": "pl_0001"}"""));
        Call(facade, "drafts.leaveSeam", $$"""{{{Draft}}, "place": "pl_0002", "now": "2026-11-20T10:20:00Z"}""");
        Assert.Equal("unavailable", Error(facade, "drafts.leaveSeam", $$"""{{{Draft}}, "place": "pl_0002"}"""));
    }
}