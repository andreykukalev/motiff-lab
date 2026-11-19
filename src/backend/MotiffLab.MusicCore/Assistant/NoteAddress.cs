using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// A note named the way a musician and a language model can read it (0028, the AI companion study section 4.3): the bar it
/// starts in, its hand (or its track when the note has no hand) and its place among that hand's notes starting in the bar,
/// counted from 1. Written "6.L.1": bar 6, left hand, first note; "12.R.3"; "4.T2.1" for the first note of the second
/// track in bar 4 when the notes carry no hands. The internal note ids never leave the device; the address does.
/// </summary>
/// <remarks>
/// Within a bar and hand the notes are counted by onset, then from the lowest pitch up, then by id, so the notes of a
/// chord are counted bottom to top. A note tied over the bar line is one note (the importers merge ties) and belongs to
/// the bar it starts in only. Grace notes take no notated time and are not notes of the event model, so they take no
/// place. Addresses are read from the notes as they are now (with the player's corrections): correcting a bar can renumber
/// its notes, never those of another bar.
/// </remarks>
/// <param name="Bar">The bar number as the workspace shows it (the pickup is bar 0).</param>
/// <param name="Hand">The note's hand, or null when it has none.</param>
/// <param name="Track">The note's track, counted from 0; it names the line only when <paramref name="Hand"/> is null.</param>
/// <param name="Index">The note's place among its line's notes starting in the bar, from 1.</param>
public readonly record struct NoteAddress(int Bar, Hand? Hand, int Track, int Index)
{
    /// <summary>The pattern of an address, for the tools' JSON schemas.</summary>
    public const string Pattern = "^[0-9]+\\.(L|R|T[1-9][0-9]*)\\.[1-9][0-9]*$";

    /// <summary>"R", "L" or "T1": the line part of the address.</summary>
    public string Line => Hand switch
    {
        Events.Hand.Right => "R",
        Events.Hand.Left => "L",
        _ => string.Create(CultureInfo.InvariantCulture, $"T{Track + 1}"),
    };

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Bar}.{Line}.{Index}");

    /// <summary>Reads "6.L.1", "6.l.1" or "4.T2.1"; false for anything else.</summary>
    public static bool TryParse(string? text, out NoteAddress address)
    {
        address = default;
        string[] parts = (text ?? "").Trim().Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int bar)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1)
        {
            return false;
        }

        string line = parts[1].ToUpperInvariant();
        if (line is "L" or "R")
        {
            address = new NoteAddress(bar, line == "L" ? Events.Hand.Left : Events.Hand.Right, 0, index);
            return true;
        }

        if (line.Length > 1 && line[0] == 'T' && int.TryParse(line[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int track) && track >= 1)
        {
            address = new NoteAddress(bar, null, track - 1, index);
            return true;
        }

        return false;
    }
}

/// <summary>
/// Every note of a piece with its <see cref="NoteAddress"/>, both ways: the address of a note id and the note at an
/// address. Pure: the same notes and meter give the same addresses.
/// </summary>
public sealed class NoteAddressBook
{
    private readonly Dictionary<string, NoteAddress> _byId;
    private readonly Dictionary<NoteAddress, Note> _byAddress;
    private readonly MeterMap _meter;

    private NoteAddressBook(Dictionary<string, NoteAddress> byId, Dictionary<NoteAddress, Note> byAddress, MeterMap meter) =>
        (_byId, _byAddress, _meter) = (byId, byAddress, meter);

    /// <summary>The addresses of <paramref name="notes"/> in <paramref name="meter"/>.</summary>
    public static NoteAddressBook Of(IReadOnlyList<Note> notes, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        var byId = new Dictionary<string, NoteAddress>(StringComparer.Ordinal);
        var byAddress = new Dictionary<NoteAddress, Note>();
        IEnumerable<IGrouping<(int Bar, Hand? Hand, int Track), Note>> lines = notes
            .GroupBy(note => (meter.BarAt(note.Span.Onset).Number, note.Hand?.Value, note.Hand is null ? note.Track : 0));
        foreach (IGrouping<(int Bar, Hand? Hand, int Track), Note> line in lines)
        {
            int index = 0;
            foreach (Note note in line.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal))
            {
                var address = new NoteAddress(line.Key.Bar, line.Key.Hand, line.Key.Track, ++index);
                byId[note.Id] = address;
                byAddress[address] = note;
            }
        }

        return new NoteAddressBook(byId, byAddress, meter);
    }

    /// <summary>How many notes have an address.</summary>
    public int Count => _byId.Count;

    /// <summary>The address of the note with this id, or null when the piece has no such note.</summary>
    public NoteAddress? AddressOf(string noteId) => _byId.TryGetValue(noteId, out NoteAddress address) ? address : null;

    /// <summary>The address as text, or null when the piece has no such note.</summary>
    public string? TextOf(string noteId) => AddressOf(noteId)?.ToString();

    /// <summary>The note at an address, or null when nothing is there.</summary>
    public Note? NoteAt(NoteAddress address) => _byAddress.GetValueOrDefault(address);

    /// <summary>
    /// The note at an address written as text, or why there is none: the text is not an address, the bar is not in the
    /// piece, the bar has no notes in that hand, or fewer than the index asks for.
    /// </summary>
    public Note? Resolve(string? text, Words words, out string? reason)
    {
        if (!NoteAddress.TryParse(text, out NoteAddress address))
        {
            reason = words.T(
                $"\"{text}\" is not a note address: write bar.hand.index, such as 6.L.1 or 12.R.3.",
                $"«{text}» не адрес ноты: пишите такт.рука.номер, например 6.L.1 или 12.R.3.");
            return null;
        }

        if (NoteAt(address) is { } note)
        {
            reason = null;
            return note;
        }

        int inLine = _byAddress.Keys.Count(other => other.Bar == address.Bar && other.Hand == address.Hand && other.Track == address.Track);
        reason = inLine == 0
            ? words.T(
                $"Bar {address.Bar} has no notes in {address.Line}, so there is no note {address}.",
                $"В такте {address.Bar} нет нот в {address.Line}, поэтому ноты {address} нет.")
            : words.T(
                $"Bar {address.Bar} has {inLine} notes in {address.Line}, so there is no note {address}.",
                $"В такте {address.Bar} в {address.Line} {words.Count(inLine, "note", "notes", "нота", "ноты", "нот")}, поэтому ноты {address} нет.");
        return null;
    }

    /// <summary>
    /// The note in a line a model reads: "7.R.4 E4 1/16 at 3/16", its address, pitch, length and where it starts in its bar,
    /// both as fractions of a whole note.
    /// </summary>
    public string Describe(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        NoteAddress address = AddressOf(note.Id) ?? throw new ArgumentException($"Note {note.Id} has no address.", nameof(note));
        return $"{address} {note.Pitch} {Fraction(note.Span.Duration)} at {Fraction(InBar(note.Span.Onset))}";
    }

    /// <summary>How far into its bar a tick is, measured from where the bar would start if it were complete (a pickup counts from its virtual start).</summary>
    public long InBar(Tick tick)
    {
        Bar bar = _meter.BarAt(tick);
        return tick.Value - bar.Origin;
    }

    /// <summary>Ticks as a reduced fraction of a whole note: "1/16", "3/8", "1", "0".</summary>
    public static string Fraction(long ticks)
    {
        long whole = CanonicalTime.TicksPerQuarter * 4;
        long divisor = Gcd(Math.Abs(ticks), whole);
        if (divisor == 0)
        {
            return "0";
        }

        long numerator = ticks / divisor, denominator = whole / divisor;
        return denominator == 1
            ? numerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{numerator}/{denominator}");
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
}