using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Import;

/// <summary>The import plan of a folder (version 0.5, «Импорт папки»): what happens to each file and what its piece is suggested to be.</summary>
public class LibraryImportTests
{
    private static readonly string s_a = new('a', 64);
    private static readonly string s_b = new('b', 64);
    private static readonly string s_c = new('c', 64);

    private static FoundFile File(string path, long bytes = 1000, string? sha = null) => new(path, bytes) { Sha256 = sha };

    private static ImportPlan Plan(IEnumerable<FoundFile> files, IEnumerable<LibraryOriginal>? library = null, ImportOptions? options = null) =>
        LibraryImport.Plan(files, library ?? [], options ?? new ImportOptions());

    private static PlannedFile In(ImportPlan plan, string path) => plan.Files.Single(file => file.File.Path == path);

    [Fact]
    public void Each_file_is_new_already_in_the_library_a_duplicate_unsupported_too_big_or_unreadable()
    {
        ImportPlan plan = Plan(
            [
                File("Бах/Prelude.pdf", 1000, s_a),
                File("Шопен/Prelude.pdf", 2000, s_b),
                File("Копии/Prelude again.pdf", 1000, s_a),
                File("Шопен/notes.txt", 10),
                File("Big/Collection.pdf", PdfDocument.MaxFileBytes + 1L, s_c),
                File("Broken.pdf", 10) with { Unreadable = true },
                File("Empty.mid", 0),
            ],
            [new LibraryOriginal("p_known", PieceOriginal.DocumentId, s_b) { Bytes = 2000 }]);

        Assert.Equal(["Big/Collection.pdf", "Broken.pdf", "Empty.mid", "Бах/Prelude.pdf", "Копии/Prelude again.pdf", "Шопен/Prelude.pdf", "Шопен/notes.txt"], plan.Files.Select(file => file.File.Path));
        Assert.Equal(ImportStatus.New, In(plan, "Бах/Prelude.pdf").Status);
        Assert.Equal(ImportStatus.Duplicate, In(plan, "Копии/Prelude again.pdf").Status);
        Assert.Equal("Бах/Prelude.pdf", In(plan, "Копии/Prelude again.pdf").SameAs);
        Assert.Equal(ImportStatus.InLibrary, In(plan, "Шопен/Prelude.pdf").Status);
        Assert.Equal("p_known", In(plan, "Шопен/Prelude.pdf").Project);
        Assert.Equal(ImportStatus.Unsupported, In(plan, "Шопен/notes.txt").Status);
        Assert.Equal(ImportStatus.TooBig, In(plan, "Big/Collection.pdf").Status);
        Assert.Equal(ImportStatus.Unreadable, In(plan, "Broken.pdf").Status);
        Assert.Equal(ImportStatus.Unreadable, In(plan, "Empty.mid").Status);
        Assert.Equal((7, 1, 1, 1, 4), (plan.Found, plan.New, plan.InLibrary, plan.Duplicates, plan.Unsuitable));
    }

    [Fact]
    public void A_pdf_left_in_the_folder_has_no_size_limit_but_notes_files_keep_theirs()
    {
        ImportPlan plan = Plan(
            [File("Big.pdf", PdfDocument.MaxFileBytes + 1L, s_a), File("Big.mid", PieceImporter.MaxFileBytes + 1L, s_b)],
            options: new ImportOptions(Linked: true));

        Assert.Equal(ImportStatus.New, In(plan, "Big.pdf").Status);
        Assert.Equal(ImportStatus.TooBig, In(plan, "Big.mid").Status);
    }

    [Fact]
    public void The_same_file_in_two_folders_is_one_piece_on_both_playlists()
    {
        ImportPlan plan = Plan([File("Концерт в мае/Nocturne.pdf", 1000, s_a), File("Шопен/Nocturne.pdf", 1000, s_a)]);

        PlannedFile piece = In(plan, "Концерт в мае/Nocturne.pdf");
        Assert.Equal(["Концерт в мае", "Шопен"], piece.Playlists);
        Assert.Equal(ImportStatus.Duplicate, In(plan, "Шопен/Nocturne.pdf").Status);
    }

    [Fact]
    public void Folders_become_playlists_tags_composers_or_nothing_as_the_player_chose()
    {
        FoundFile[] files = [File("Бах/ХТК/Prelude 1.pdf", 1, s_a), File("Шопен/Прелюдии/Prelude 4.pdf", 2, s_b), File("Бах/Прелюдии/Little Prelude.pdf", 3, s_c), File("Loose.pdf", 4)];

        ImportPlan playlists = Plan(files);
        Assert.Equal(["ХТК"], In(playlists, "Бах/ХТК/Prelude 1.pdf").Playlists);
        Assert.Equal(["Шопен / Прелюдии"], In(playlists, "Шопен/Прелюдии/Prelude 4.pdf").Playlists);
        Assert.Equal(["Бах / Прелюдии"], In(playlists, "Бах/Прелюдии/Little Prelude.pdf").Playlists);
        Assert.Empty(In(playlists, "Loose.pdf").Playlists);
        Assert.All(playlists.Files, file => Assert.Empty(file.Tags));

        ImportPlan tags = Plan(files, options: new ImportOptions(FolderMapping.Tags));
        Assert.Equal(["Бах", "ХТК"], In(tags, "Бах/ХТК/Prelude 1.pdf").Tags);
        Assert.All(tags.Files, file => Assert.Empty(file.Playlists));

        ImportPlan composers = Plan(files, options: new ImportOptions(FolderMapping.Composers));
        Assert.Equal(new Suggestion("Шопен", SuggestionSource.FolderChoice, 0.8), In(composers, "Шопен/Прелюдии/Prelude 4.pdf").Composer);
        Assert.Null(In(composers, "Loose.pdf").Composer);

        ImportPlan nothing = Plan(files, options: new ImportOptions(FolderMapping.Nothing));
        Assert.All(nothing.Files, file => Assert.True(file.Playlists.Count == 0 && file.Tags.Count == 0));
    }

    [Fact]
    public void Suggestions_come_from_the_header_then_the_pdf_information_then_the_name_then_the_folder_and_are_guesses()
    {
        ImportPlan plan = Plan(
            [
                File("Scores/Moonlight.musicxml") with { Header = new ScoreHeader("Piano Sonata No. 14", "Ludwig van Beethoven") },
                File("Scores/scan0001.pdf", 2) with { PdfTitle = "Nocturne in E-flat major", PdfAuthor = "Frédéric Chopin" },
                File("Scores/Bach - Prelude in C.pdf", 3) with { PdfTitle = "Microsoft Word - Prelude.docx", PdfAuthor = "Administrator" },
                File("Шопен/IMG_0042.pdf", 4),
            ]);

        PlannedFile header = In(plan, "Scores/Moonlight.musicxml");
        Assert.Equal(new Suggestion("Piano Sonata No. 14", SuggestionSource.ScoreHeader, 0.9), header.Title);
        Assert.Equal("Ludwig van Beethoven", header.Composer!.Value);
        Assert.Equal(Provenance.Inferred, header.Title!.Attribution.Provenance);

        PlannedFile pdf = In(plan, "Scores/scan0001.pdf");
        Assert.Equal(("Nocturne in E-flat major", SuggestionSource.PdfInfo), (pdf.Title!.Value, pdf.Title.Source));
        Assert.Equal(("Frédéric Chopin", SuggestionSource.PdfInfo), (pdf.Composer!.Value, pdf.Composer.Source));

        PlannedFile named = In(plan, "Scores/Bach - Prelude in C.pdf");
        Assert.Equal(("Prelude in C", SuggestionSource.FileName), (named.Title!.Value, named.Title.Source));
        Assert.Equal(("Bach", SuggestionSource.FileName), (named.Composer!.Value, named.Composer.Source));

        PlannedFile folder = In(plan, "Шопен/IMG_0042.pdf");
        Assert.Equal(("IMG_0042", SuggestionSource.FileName, 0.3), (folder.Title!.Value, folder.Title.Source, folder.Title.Confidence));
        Assert.Equal(("Шопен", SuggestionSource.Folder), (folder.Composer!.Value, folder.Composer.Source));
    }

    [Fact]
    public void A_folder_whose_pieces_name_one_composer_suggests_it_for_the_others()
    {
        ImportPlan plan = Plan(
            [File("Мои ноты/Gieseking - Song 1.pdf", 1), File("Мои ноты/Gieseking - Song 2.pdf", 2), File("Мои ноты/Rondo.pdf", 3), File("Разное/Petrov - Romance.pdf", 4), File("Разное/Sidorov - Waltz.pdf", 5), File("Разное/Etude.pdf", 6)]);

        Assert.Equal(new Suggestion("Gieseking", SuggestionSource.Folder, 0.4), In(plan, "Мои ноты/Rondo.pdf").Composer);
        Assert.Null(In(plan, "Разное/Etude.pdf").Composer);
    }

    [Fact]
    public void Two_different_files_with_the_same_title_and_composer_are_possibly_the_same_piece_and_not_merged()
    {
        ImportPlan plan = Plan(
            [File("Henle/Chopin - Ballade No. 1.pdf", 1, s_a), File("Peters/CHOPIN – Ballade no 1.pdf", 2, s_b), File("Other/Chopin - Ballade No. 2.pdf", 3, s_c)],
            [new LibraryOriginal("p_old", PieceOriginal.DocumentId, null) { Title = "Ballade No. 1", Composer = "chopin" }]);

        PlannedFile henle = In(plan, "Henle/Chopin - Ballade No. 1.pdf");
        Assert.Equal(ImportStatus.New, henle.Status);
        Assert.Equal(["Peters/CHOPIN – Ballade no 1.pdf"], henle.PossiblySameFiles);
        Assert.Equal(["p_old"], henle.PossiblySamePieces);
        Assert.Equal(ImportStatus.New, In(plan, "Peters/CHOPIN – Ballade no 1.pdf").Status);
        Assert.Empty(In(plan, "Other/Chopin - Ballade No. 2.pdf").PossiblySameFiles);
    }

    [Fact]
    public void A_long_pdf_is_a_possible_collection()
    {
        ImportPlan plan = Plan([File("Album.pdf", 1) with { Pages = LibraryImport.CollectionPages + 1 }, File("Piece.pdf", 2) with { Pages = LibraryImport.CollectionPages }]);

        Assert.True(In(plan, "Album.pdf").PossibleCollection);
        Assert.False(In(plan, "Piece.pdf").PossibleCollection);
    }

    [Fact]
    public void Only_files_of_a_size_another_file_or_original_has_are_fingerprinted_before_the_plan_is_sure()
    {
        ImportPlan plan = Plan(
            [File("a.pdf", 100), File("b.pdf", 100), File("c.pdf", 200), File("d.pdf", 300), File("e.pdf", 300, s_a)],
            [new LibraryOriginal("p_1", PieceOriginal.DocumentId, s_b) { Bytes = 200 }]);

        Assert.Equal(["a.pdf", "b.pdf", "c.pdf", "d.pdf"], plan.ToFingerprint);
        Assert.True(In(plan, "c.pdf").Provisional);
        Assert.False(In(plan, "e.pdf").Provisional);
    }

    [Fact]
    public void Names_are_compared_in_one_unicode_form()
    {
        ImportPlan plan = Plan([File("Für Elise/Beethoven - Für Elise.pdf", 1, s_a), File("Für Elise/Beethoven - Für Elise (1).pdf", 2, s_b)]);

        PlannedFile mac = In(plan, "Für Elise/Beethoven - Für Elise.pdf");
        Assert.Equal("Für Elise", mac.Title!.Value);
        Assert.Equal(["Für Elise"], mac.Playlists);
        Assert.Equal(["Für Elise/Beethoven - Für Elise (1).pdf"], mac.PossiblySameFiles);
    }

    [Fact]
    public void The_plan_is_the_same_for_the_same_files_in_any_order()
    {
        FoundFile[] files = [File("b/x.pdf", 1, s_a), File("a/x.pdf", 1, s_a), File("c.mid", 1)];

        Assert.Equal(Plan(files).Files.Select(Describe), Plan(files.Reverse()).Files.Select(Describe));
        Assert.Throws<ArgumentException>(() => Plan([File("a.pdf"), File("a.pdf")]));
        Assert.Throws<ArgumentException>(() => File("../a.pdf"));
    }

    [Fact]
    public void Confirmed_suggestions_never_replace_what_the_player_set_and_add_to_their_lists()
    {
        var existing = new PieceMetadata("My title", null, ["exam"], ["Recital"]);

        PieceMetadata confirmed = LibraryImport.Confirmed(existing, "Prelude", "Bach", ["Бах", "Exam"], ["ХТК", "recital"]);

        Assert.Equal(new PieceMetadata("My title", "Bach", ["exam", "Бах"], ["Recital", "ХТК"]), confirmed);
        Assert.Equal(new PieceMetadata("Prelude", null, [], []), LibraryImport.Confirmed(PieceMetadata.None, "Prelude", null, [], []));
    }

    [Fact]
    public void Library_originals_carry_the_pieces_title_and_composer()
    {
        Project project = Project.CreateDocument("p_pdf", "Chopin - Ballade.pdf", "%PDF-1.4 x"u8.ToArray(), DateTimeOffset.UnixEpoch) with
        {
            Metadata = new PieceMetadata("Ballade No. 1", "Chopin", null, null),
        };

        LibraryOriginal original = Assert.Single(LibraryOriginal.Of(project));

        Assert.Equal(("p_pdf", PieceOriginal.DocumentId, "Ballade No. 1", "Chopin"), (original.ProjectId, original.OriginalId, original.Title, original.Composer));
        Assert.Equal(PieceOriginal.Fingerprint("%PDF-1.4 x"u8), original.Sha256);
    }

    private static string Describe(PlannedFile file) => $"{file.File.Path} {file.Status} {file.SameAs} {string.Join(',', file.Playlists)}";
}