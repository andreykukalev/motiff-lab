using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Tests.Import;

/// <summary>What a sheet-music file's name suggests (version 0.5, «Импорт папки»), on names as people keep them.</summary>
public class FileNameDetailsTests
{
    [Theory]
    [InlineData("Бах - Прелюдия до мажор BWV 846.pdf", "Прелюдия до мажор BWV 846", "Бах", "BWV 846")]
    [InlineData("Bach - Prelude in C major BWV846.pdf", "Prelude in C major BWV 846", "Bach", "BWV 846")]
    [InlineData("Prelude (Bach).pdf", "Prelude", "Bach", null)]
    [InlineData("Chopin_Op28_No4.pdf", "Op. 28 No. 4", "Chopin", "Op. 28 No. 4")]
    [InlineData("Chopin - Nocturne Op.9 No.2.pdf", "Nocturne Op. 9 No. 2", "Chopin", "Op. 9 No. 2")]
    [InlineData("Nocturne in E-flat major, Op. 9 No. 2 - Chopin.pdf", "Nocturne in E-flat major, Op. 9 No. 2", "Chopin", "Op. 9 No. 2")]
    [InlineData("Mozart_-_Sonata_K331.pdf", "Sonata K. 331", "Mozart", "K. 331")]
    [InlineData("Schubert Impromptu D899 No3.musicxml", "Impromptu D. 899 No. 3", "Schubert", "D. 899 No. 3")]
    [InlineData("Haydn - Sonata Hob XVI 52.pdf", "Sonata Hob. XVI:52", "Haydn", "Hob. XVI:52")]
    [InlineData("Haydn_Sonata_Hob.XVI-37.pdf", "Sonata Hob. XVI:37", "Haydn", "Hob. XVI:37")]
    [InlineData("Чайковский - Детский альбом, соч. 39 - 02 Утреннее размышление.pdf", "Детский альбом, соч. 39 - 02 Утреннее размышление", "Чайковский", "соч. 39")]
    [InlineData("П.И. Чайковский - Времена года. Октябрь.pdf", "Времена года. Октябрь", "П.И. Чайковский", null)]
    [InlineData("Рахманинов Прелюдия до-диез минор соч.3 №2.pdf", "Прелюдия до-диез минор соч. 3 № 2", "Рахманинов", "соч. 3 № 2")]
    [InlineData("Шопен Ф. - Вальс ля минор.pdf", "Вальс ля минор", "Шопен Ф.", null)]
    [InlineData("J.S. Bach - Invention No. 1 BWV 772.pdf", "Invention No. 1 BWV 772", "J.S. Bach", "No. 1 BWV 772")]
    [InlineData("Johann Sebastian Bach Air on the G String.pdf", "Air on the G String", "Johann Sebastian Bach", null)]
    [InlineData("Ave Maria Schubert.mid", "Ave Maria", "Schubert", null)]
    [InlineData("01 - Gymnopedie No.1.pdf", "Gymnopedie No. 1", null, "No. 1")]
    [InlineData("03. Satie - Gnossienne 1.pdf", "Gnossienne 1", "Satie", null)]
    [InlineData("IMSLP02297-PMLP02297-Chopin_-_Op.28_No.4.pdf", "Op. 28 No. 4", "Chopin", "Op. 28 No. 4")]
    [InlineData("Fur Elise (1).pdf", "Fur Elise", null, null)]
    [InlineData("Für Elise - Copy.pdf", "Für Elise", null, null)]
    [InlineData("Мелодия (копия).pdf", "Мелодия", null, null)]
    [InlineData("Einaudi - Nuvole Bianche.pdf", "Nuvole Bianche", "Einaudi", null)]
    [InlineData("Yiruma - River Flows in You.pdf", "River Flows in You", "Yiruma", null)]
    [InlineData("Пьяццолла - Либертанго.pdf", "Либертанго", "Пьяццолла", null)]
    [InlineData("Rimsky-Korsakov - Flight of the Bumblebee.pdf", "Flight of the Bumblebee", "Rimsky-Korsakov", null)]
    [InlineData("Prelude - Fugue.pdf", "Prelude - Fugue", null, null)]
    [InlineData("Альбомный лист.pdf", "Альбомный лист", null, null)]
    [InlineData("Лунная соната.musicxml", "Лунная соната", null, null)]
    [InlineData("Beethoven - Sonata No 14 Op 27 No 2 Moonlight.pdf", "Sonata No. 14 Op. 27 No. 2 Moonlight", "Beethoven", "No. 14 Op. 27 No. 2")]
    [InlineData("Ivan Petrov - Romance.pdf", "Romance", "Ivan Petrov", null)]
    [InlineData("Romance - Ivan Petrov.pdf", "Romance", "Ivan Petrov", null)]
    public void A_name_suggests_its_title_composer_and_catalogue(string fileName, string? title, string? composer, string? catalogue)
    {
        FileNameDetails details = FileNameDetails.Of(fileName);

        Assert.Equal(title, details.Title);
        Assert.Equal(composer, details.Composer);
        Assert.Equal(catalogue, details.Catalogue);
    }

    [Theory]
    [InlineData("IMG_0042.pdf")]
    [InlineData("scan0001.pdf")]
    [InlineData("Document1.pdf")]
    [InlineData("Без названия.pdf")]
    [InlineData("20231105_101010.pdf")]
    [InlineData("3f2b9c1d-4e5a-46b7-9c1d-0a1b2c3d4e5f.pdf")]
    public void A_name_that_names_nothing_suggests_no_title(string fileName) => Assert.Null(FileNameDetails.Of(fileName).Title);

    [Fact]
    public void A_name_written_with_combining_marks_is_read_as_the_same_composed_name()
    {
        FileNameDetails mac = FileNameDetails.Of("Dvořák - Humoresque.pdf");
        FileNameDetails windows = FileNameDetails.Of("Dvořák - Humoresque.pdf");

        Assert.Equal(windows, mac);
        Assert.Equal("Dvořák", mac.Composer);
    }

    [Theory]
    [InlineData("Microsoft Word - Prelude.docx", null)]
    [InlineData("Microsoft Word - Document1", null)]
    [InlineData("Finale 2014 - [Prelude.MUS]", null)]
    [InlineData("Untitled", null)]
    [InlineData("untitled-1", null)]
    [InlineData("Prelude.mus", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [InlineData("Nocturne in E-flat major", "Nocturne in E-flat major")]
    [InlineData("Времена  года", "Времена года")]
    public void A_pdf_title_from_an_editor_or_a_file_name_is_left_out(string? title, string? expected) =>
        Assert.Equal(expected, FileNameDetails.PdfTitle(title));

    [Theory]
    [InlineData("Administrator", null)]
    [InlineData("Microsoft Office User", null)]
    [InlineData("Пользователь", null)]
    [InlineData("musescore", null)]
    [InlineData("someone@example.com", null)]
    [InlineData("12345", null)]
    [InlineData("Frédéric Chopin", "Frédéric Chopin")]
    [InlineData("С. Рахманинов", "С. Рахманинов")]
    public void A_pdf_author_that_is_a_computer_user_or_a_program_is_left_out(string author, string? expected) =>
        Assert.Equal(expected, FileNameDetails.PdfAuthor(author));

    [Theory]
    [InlineData("Бах", "Бах")]
    [InlineData("Chopin", "Chopin")]
    [InlineData("J.S. Bach", "J.S. Bach")]
    [InlineData("Bach - Partitas", "Bach")]
    [InlineData("Ноты", null)]
    [InlineData("Прелюдии", null)]
    [InlineData("Ivan Petrov", null)]
    [InlineData("2024", null)]
    public void A_folder_suggests_a_composer_only_when_its_name_is_one_known(string folder, string? composer) =>
        Assert.Equal(composer, FileNameDetails.ComposerOfFolder(folder));
}