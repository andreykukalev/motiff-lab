using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Projects.ProjectMergeTests;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// What an iPad keeps of project.resolve's answer once it acted on it (the second review of c-17a): a "file" it does not
/// upload is never sent (the iPad before c-17b records its hash, and no build sends a later build's file), so the next
/// download (project.pulled) writes over it, and whatever only this iPad had must be in "copy", a piece of its own that the
/// next pass sends as a new one.
/// </summary>
public class SyncAnswerTests
{
    private readonly MusicCoreFacade _facade = new();

    private static string Text(Project project) => Encoding.UTF8.GetString(ProjectFile.Write(project));

    private static Project Read(JsonElement answer, string field) => ProjectFile.Read(Encoding.UTF8.GetBytes(answer.GetProperty(field).GetString()!));

    private JsonElement Call(string name, object request)
    {
        JsonElement answer = JsonDocument.Parse(_facade.Call(0, name, JsonSerializer.Serialize(request))).RootElement;
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error}");
        return answer;
    }

    private JsonElement Resolved(Project here, string savedHere, string online, Project? @base, bool? sendsFile = null) => Call("project.resolve", new
    {
        here = Text(here),
        savedHere,
        online,
        savedOnline = "2026-11-27T10:00:00Z",
        copyTitle = CopyTitle,
        @base = @base is null ? null : Text(@base),
        sendsFile,
    });

    /// <summary>
    /// The ideas on this iPad after it acted on an answer that sends nothing, as CloudSync.resolve does (it writes "file" in
    /// place and "copy" as a new piece), and then downloaded <paramref name="laterOnline"/>: the piece's and the copy's.
    /// </summary>
    private IReadOnlyList<string> AfterTheNextDownload(JsonElement resolved, string laterOnline)
    {
        Assert.False(resolved.GetProperty("upload").GetBoolean());
        Project piece = Read(Call("project.pulled", new { file = laterOnline, here = resolved.GetProperty("file").GetString() }), "file");
        IEnumerable<string> beside = resolved.GetProperty("copy").ValueKind == JsonValueKind.Null ? [] : Ideas(Read(resolved, "copy"));
        return [.. Ideas(piece).Concat(beside).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>A copy a later build saved online (the v20 golden) and this build's copy of it with an idea added here.</summary>
    internal static (string Online, Project Here) LaterBuildOnline()
    {
        string future = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "v20-future.motiff.json"));
        JsonObject today = JsonNode.Parse(future)!.AsObject();
        today["version"] = ProjectFile.Version;
        today.Remove("future");
        today["workspace"]!.AsObject().Remove("future");
        return (future, WithIdeas(ProjectFile.Read(Encoding.UTF8.GetBytes(today.ToJsonString())), Idea("o_here", "Here.")));
    }

    [Fact]
    public void An_app_that_sends_its_own_copy_keeps_it_beside_when_the_online_one_was_saved_last()
    {
        // Finding 1: without "sendsFile" and with the online copy saved last, "file" held both ideas and "copy" was null; the
        // iPad wrote "file", recorded its hash and never sent it, and the next download dropped the idea made here.
        Project @base = Piece();
        Project here = WithIdeas(@base, Idea("o_here", "Here."));
        Project online = WithIdeas(@base, Idea("o_online", "Online."));

        JsonElement resolved = Resolved(here, "2026-11-27T09:00:00Z", Text(online), @base);
        Assert.Equal(["o_here:Here."], Ideas(Read(resolved, "copy")));
        Assert.Equal(["o_here:Here.", "o_later:Later.", "o_online:Online."], AfterTheNextDownload(resolved, Text(WithIdeas(online, Idea("o_later", "Later.")))));
    }

    [Fact]
    public void A_merge_that_fails_its_self_check_keeps_this_ipads_copy_beside_for_an_app_that_sends_its_own()
    {
        // Notes renamed online and removed here cannot be merged; this iPad's copy was saved last and has an idea of its own.
        // Kept here with the online copy beside, it was never sent by the iPad before c-17b, and the next download lost the idea.
        Project @base = WithOtherNotes(Piece());
        Project here = WithIdeas(@base.DeleteNotes("n_2"), Idea("o_here", "Here."));
        Project online = Renamed(@base, "Urtext");

        JsonElement shipped = Resolved(here, "2026-11-27T11:00:00Z", Text(online), @base);
        Assert.False(shipped.GetProperty("merged").GetBoolean());
        Assert.Equal(["o_here:Here."], Ideas(Read(shipped, "copy")));
        Assert.Contains("o_here:Here.", AfterTheNextDownload(shipped, Text(WithIdeas(online, Idea("o_later", "Later.")))));

        // An app that sends "file" sends it on the next pass, so the copy beside is the online one, as before.
        JsonElement sends = Resolved(here, "2026-11-27T11:00:00Z", Text(online), @base, sendsFile: true);
        Assert.Equal("Urtext", NameOfOther(Read(sends, "copy")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void A_later_builds_copy_online_is_kept_as_fetched_and_this_ipads_beside_to_be_sent_as_a_new_piece(bool? sendsFile)
    {
        // Finding 2: merged into a later build's copy, this iPad's idea was in a read-only "file" no build sends, and the next
        // download wrote over it.
        (string future, Project here) = LaterBuildOnline();
        JsonElement resolved = Resolved(here, "2026-11-27T11:00:00Z", future, @base: null, sendsFile);
        Assert.True(resolved.GetProperty("readOnly").GetBoolean());
        Assert.False(resolved.GetProperty("merged").GetBoolean());
        Assert.Equal("[]", resolved.GetProperty("conflicts").GetRawText());
        Project kept = Read(resolved, "file");
        Assert.True(kept.IsReadOnly);
        Assert.DoesNotContain(kept.Observations, idea => idea.Id == "o_here");
        Assert.Equal(1, JsonNode.Parse(resolved.GetProperty("file").GetString()!)!["workspace"]!["future"]!.GetValue<int>());

        Project copy = Read(resolved, "copy");
        Assert.False(copy.IsReadOnly);
        Assert.NotEqual(kept.Id, copy.Id);
        Assert.Contains("o_here:Here.", AfterTheNextDownload(resolved, future));
        // The copy can be sent: project.online takes it.
        Assert.NotNull(Call("project.online", new { file = resolved.GetProperty("copy").GetString() }).GetProperty("file").GetString());
    }

    [Fact]
    public void A_later_builds_copy_here_goes_beside_and_the_online_one_takes_its_place()
    {
        // The same with the later build's copy on this iPad: merged, it stayed read-only here and the next download lost it.
        Project @base = Piece();
        Project here = Later(WithIdeas(@base, Idea("o_here", "Here.")));
        Project online = WithIdeas(@base, Idea("o_online", "Online."));

        JsonElement resolved = Resolved(here, "2026-11-27T11:00:00Z", Text(online), @base, sendsFile: true);
        Assert.False(resolved.GetProperty("readOnly").GetBoolean());
        Assert.False(resolved.GetProperty("merged").GetBoolean());
        Assert.Equal(["o_online:Online."], Ideas(Read(resolved, "file")));
        Project copy = Read(resolved, "copy");
        Assert.True(copy.IsReadOnly);
        Assert.Equal("p_0123456789abcdef0123456789abcdef", Read(resolved, "file").Id);
        Assert.NotEqual(here.Id, copy.Id);
        Assert.Equal(["o_here:Here.", "o_later:Later.", "o_online:Online."], AfterTheNextDownload(resolved, Text(WithIdeas(online, Idea("o_later", "Later.")))));
    }
}