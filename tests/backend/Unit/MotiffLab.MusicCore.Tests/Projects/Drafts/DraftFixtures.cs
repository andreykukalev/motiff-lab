using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects.Drafts;

/// <summary>Music for the drafts' tests, built in code: bars of 4/4, four quarters in the right hand over a whole note in the left.</summary>
internal static class DraftFixtures
{
    public const long Quarter = 480;

    public const long BarLength = 4 * Quarter;

    public static readonly DateTimeOffset At = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    public static readonly NotesProvenance Read = new(NotesProvenance.PdfRecognition)
    {
        From = [new ProvenanceSource("src_pdf") { Sha256 = new string('a', 64), Pages = (1, 2) }],
        Engine = "homr 0.8.0",
        At = At,
    };

    public static Note Note(string id, int pitch, long onset, long duration, Hand? hand = Hand.Right, int? voice = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 72,
        Track = hand == Hand.Left ? 1 : 0,
        Voice = voice,
        Hand = hand is { } played ? new Attributed<Hand>(played, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    /// <summary><paramref name="bars"/> bars in 4/4: the right hand's four quarters from <paramref name="melody"/>, the bass a whole note.</summary>
    public static SourceMusic Score(int bars = 8, Func<int, int[]>? melody = null, int fifths = 0, int perMinute = 90, string prefix = "n")
    {
        List<Note> notes = [];
        for (int bar = 0; bar < bars; bar++)
        {
            int[] pitches = melody?.Invoke(bar) ?? [72, 74, 76, 77];
            for (int beat = 0; beat < pitches.Length; beat++)
            {
                notes.Add(Note($"{prefix}{bar}r{beat}", pitches[beat], (bar * BarLength) + (beat * Quarter), Quarter));
            }

            notes.Add(Note($"{prefix}{bar}l", 48, bar * BarLength, BarLength, Hand.Left));
        }

        MeterMap meter = new([new MeterChange(Tick.Zero, TimeSignature.CommonTime)]);
        return SourceMusic.Of(notes, meter, new Tick(bars * BarLength), [new PlaybackTempo(Tick.Zero, 60_000_000 / perMinute)], [new KeySignature(Tick.Zero, fifths, KeyMode.Major)]);
    }

    /// <summary>Bars <paramref name="first"/> to <paramref name="last"/> (from 1) of a score as a span.</summary>
    public static TickSpan Bars(int first, int last) => TickSpan.FromBounds(new Tick((first - 1) * BarLength), new Tick(last * BarLength));

    public static DraftVariant Variant(string id, SourceMusic music, int first, int last, DraftPart part = DraftPart.All, string notes = "n_1") =>
        DraftFragment.Variant(id, At, music, Bars(first, last), part, notes, Read);
}