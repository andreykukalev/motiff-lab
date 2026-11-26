using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Playback;

namespace MotiffLab.MusicCore.Tests.Playback;

/// <summary>#330, criterion 4: the voices a file writes get names a player understands only where the file says which.</summary>
public class VoiceNamesTests
{
    /// <summary>The fixture's notes: voices 1 and 2 on the upper staff and 5 on the lower one.</summary>
    private static IReadOnlyList<Note> Written() =>
        MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "grand-staff.musicxml"))).Notes;

    /// <summary>Its voices 1 and 5 only: one voice on each staff.</summary>
    private static IReadOnlyList<Note> GrandStaff() => [.. Written().Where(note => note.Voice != 2)];

    private static int StaffOf(Note note) => ((MusicXmlSourceLocation)note.Source!).Staff;

    [Fact]
    public void TwoVoicesEachOnItsOwnStaffAreTheUpperAndTheLowerVoiceInBothLanguages()
    {
        IReadOnlyList<Note> notes = GrandStaff();
        int upper = notes.First(note => StaffOf(note) == 1).Voice!.Value;
        int lower = notes.First(note => StaffOf(note) == 2).Voice!.Value;

        Assert.Equal([new VoiceName(0, upper, "upper voice"), new VoiceName(0, lower, "lower voice")], VoiceNames.Of(notes, Words.English).OrderBy(name => name.Name == "lower voice"));
        Assert.Equal(["верхний голос", "нижний голос"], VoiceNames.Of(notes, Words.Russian).OrderBy(name => name.Voice != upper).Select(name => name.Name));
    }

    [Fact]
    public void ANoteThePlayerAddedLeavesTheNamesAsTheFileSaysThem()
    {
        IReadOnlyList<Note> notes = GrandStaff();
        Note added = notes[0] with { Id = "n_added", Source = null };

        Assert.Equal(2, VoiceNames.Of([.. notes, added], Words.English).Count);
    }

    [Fact]
    public void VoicesTheFileDoesNotPutAboveOneAnotherGetNoName()
    {
        IReadOnlyList<Note> notes = GrandStaff();
        // Both voices on one staff: the file does not say which is above.
        Note[] oneStaff = [.. notes.Select(note => note with { Source = (MusicXmlSourceLocation)note.Source! with { Staff = 1 } })];
        // A voice written across both staves.
        Note crossing = notes.First(note => StaffOf(note) == 1) with { Id = "n_cross", Source = (MusicXmlSourceLocation)notes.First(note => StaffOf(note) == 2).Source! };
        // MIDI notes have no voices.
        Note[] midi = [.. notes.Select(note => note with { Voice = null, Source = null })];

        Assert.Empty(VoiceNames.Of(oneStaff, Words.English));
        Assert.Empty(VoiceNames.Of([.. notes, crossing], Words.English));
        Assert.Empty(VoiceNames.Of(midi, Words.English));
        // Two voices on the upper staff and one on the lower: which of the two is above, the file does not say.
        Assert.Empty(VoiceNames.Of(Written(), Words.English));
        Assert.Empty(VoiceNames.Of([.. notes.Where(note => StaffOf(note) == 1)], Words.English));
    }
}