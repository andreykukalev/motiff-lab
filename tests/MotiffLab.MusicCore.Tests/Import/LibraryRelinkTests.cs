using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Facade;

namespace MotiffLab.MusicCore.Tests.Import;

/// <summary>Links that heal themselves when the player reorganises the folder (version 0.5, «Импорт папки»; Andrei 2026-11-12 17:47 UTC).</summary>
public class LibraryRelinkTests
{
    private static readonly string s_a = new('a', 64);
    private static readonly string s_b = new('b', 64);
    private static readonly string s_c = new('c', 64);

    private static LibraryOriginal Original(string project, string folder, string fileName, long bytes, string? sha, int? pages = null, string? title = null) =>
        new(project, PieceOriginal.DocumentId, sha) { Folder = folder, FileName = fileName, Bytes = bytes, Pages = pages, Title = title };

    private static FoundFile File(string path, long bytes, string? sha = null, int? pages = null) => new(path, bytes) { Sha256 = sha, Pages = pages };

    private static Relinked Of(RelinkPlan plan, string project) => plan.Originals.Single(each => each.Original.ProjectId == project);

    [Fact]
    public void An_original_at_its_place_with_its_size_is_linked_at_once_and_its_fingerprint_confirmed_later()
    {
        RelinkPlan plan = LibraryRelink.Plan(
            [Original("p_1", "Бах", "Prelude.pdf", 1000, s_a), Original("p_2", "Бах", "Fugue.pdf", 2000, s_b)],
            [File("Бах/Prelude.pdf", 1000), File("Бах/Fugue.pdf", 2000, s_b)]);

        Assert.Equal((RelinkStatus.Unconfirmed, "Бах/Prelude.pdf"), (Of(plan, "p_1").Status, Of(plan, "p_1").Path));
        Assert.Equal((RelinkStatus.Linked, "Бах/Fugue.pdf"), (Of(plan, "p_2").Status, Of(plan, "p_2").Path));
        Assert.Equal(["Бах/Prelude.pdf"], plan.ToConfirm);
        Assert.Empty(plan.ToFingerprint);
    }

    [Fact]
    public void An_original_not_at_its_place_asks_only_for_the_files_of_its_size_and_a_fingerprint_match_links_it_where_it_moved()
    {
        LibraryOriginal prelude = Original("p_1", "Бах", "Prelude.pdf", 1000, s_a);
        FoundFile[] listing = [File("Бах/ХТК/Prelude in C.pdf", 1000), File("Шопен/Nocturne.pdf", 1000), File("Шопен/Ballade.pdf", 3000)];

        RelinkPlan first = LibraryRelink.Plan([prelude], listing);
        Assert.Equal(RelinkStatus.Pending, Of(first, "p_1").Status);
        Assert.Equal(["Бах/ХТК/Prelude in C.pdf", "Шопен/Nocturne.pdf"], first.ToFingerprint);

        RelinkPlan second = LibraryRelink.Plan([prelude], [listing[0] with { Sha256 = s_a }, listing[1] with { Sha256 = s_b }, listing[2]]);
        Assert.Equal((RelinkStatus.Moved, "Бах/ХТК/Prelude in C.pdf"), (Of(second, "p_1").Status, Of(second, "p_1").Path));
        Assert.Empty(second.ToFingerprint);
    }

    [Fact]
    public void A_changed_file_at_its_place_is_never_linked_and_the_possibly_same_pieces_are_ranked()
    {
        RelinkPlan plan = LibraryRelink.Plan(
            [Original("p_1", "Шопен", "Ballade No. 1.pdf", 1000, s_a, pages: 12, title: "Ballade No. 1")],
            [
                File("Шопен/Ballade No. 1.pdf", 1200, s_b, pages: 12),
                File("Henle/Chopin - Ballade No 1.pdf", 1050, s_c, pages: 12),
                File("Шопен/Nocturne.pdf", 1000, new string('d', 64), pages: 4),
                File("Шопен/notes.txt", 10),
            ]);

        Relinked ballade = Of(plan, "p_1");
        Assert.Equal((RelinkStatus.Changed, "Шопен/Ballade No. 1.pdf"), (ballade.Status, ballade.Path));
        Assert.Equal(["Шопен/Ballade No. 1.pdf", "Henle/Chopin - Ballade No 1.pdf"], ballade.Candidates.Select(candidate => candidate.Path));
        Assert.True(ballade.Candidates[0].SamePlace);
        Assert.False(ballade.Candidates[1].SamePlace);
    }

    [Fact]
    public void A_missing_original_with_no_file_alike_has_no_candidates_and_nothing_is_hashed()
    {
        RelinkPlan plan = LibraryRelink.Plan([Original("p_1", "Бах", "Prelude.pdf", 1000, s_a)], [File("Other/Waltz.pdf", 5000)]);

        Assert.Equal((RelinkStatus.Missing, null), (Of(plan, "p_1").Status, Of(plan, "p_1").Path));
        Assert.Empty(Of(plan, "p_1").Candidates);
        Assert.Empty(plan.ToFingerprint);
    }

    [Fact]
    public void Two_originals_with_one_fingerprint_both_find_the_file_and_the_plan_is_the_same_in_any_order()
    {
        LibraryOriginal[] originals = [Original("p_2", "Сборник", "Album.pdf", 1000, s_a), Original("p_1", "Сборник", "Album.pdf", 1000, s_a)];
        FoundFile[] listing = [File("Новое/Album.pdf", 1000, s_a), File("Другое/Album copy.pdf", 1000, s_a)];

        RelinkPlan plan = LibraryRelink.Plan(originals, listing);

        Assert.Equal(["p_1", "p_2"], plan.Originals.Select(each => each.Original.ProjectId));
        Assert.All(plan.Originals, each => Assert.Equal((RelinkStatus.Moved, "Другое/Album copy.pdf"), (each.Status, each.Path)));
        Assert.Equal(plan.Originals.Select(each => each.Path), LibraryRelink.Plan(originals.Reverse(), listing.Reverse()).Originals.Select(each => each.Path));
    }

    [Theory]
    [InlineData("ballade no 1", "ballade no 1", 100)]
    [InlineData("ballade no 1", "chopin ballade no 1", 75)]
    [InlineData("prelude", "nocturne", 0)]
    [InlineData("", "prelude", 0)]
    public void Names_are_alike_by_the_pairs_of_letters_they_share(string first, string second, int similarity) =>
        Assert.Equal(similarity, LibraryRelink.Similarity(first, second));

    [Fact]
    public void Library_relink_answers_each_original_and_what_to_hash()
    {
        string sha = s_a;
        System.Text.Json.JsonElement answer = FacadeCalls.Json(new MusicCoreFacade().Call(0, "library.relink", $$"""
            {"library": [{"project": "p_1", "original": "src_pdf", "sha256": "{{sha}}", "bytes": 1000, "fileName": "Prelude.pdf", "folder": "Бах"}],
             "files": [{"path": "Бах/ХТК/Prelude.pdf", "bytes": 1000}]}
            """));

        System.Text.Json.JsonElement original = answer.GetProperty("originals")[0];
        Assert.Equal("pending", original.GetProperty("status").GetString());
        Assert.Equal("Бах/ХТК/Prelude.pdf", answer.GetProperty("fingerprint")[0].GetString());
    }
}