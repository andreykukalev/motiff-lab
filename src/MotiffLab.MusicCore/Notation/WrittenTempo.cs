using System.Globalization;
using System.Xml;
using System.Xml.Linq;

using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// A MusicXML score shown as written with the tempo the player set for the piece (version 0.5.4, #511 section 17), or the
/// piece's default when nothing states one (version 0.5.5, #521 section 12): its metronome mark at the start of the first
/// part's first bar, "♩ = 92" ("♩. = 60" in 6/8), in place of one the file marks there. A score with its own tempo is left
/// as written: its own marks are its own.
/// </summary>
public static class WrittenTempo
{
    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

    /// <param name="musicXml">A partwise MusicXML score, as written.</param>
    /// <param name="played">The score as played; only the player's tempo or the default at the start changes the score.</param>
    public static string With(string musicXml, PlaybackScore played)
    {
        ArgumentNullException.ThrowIfNull(musicXml);
        ArgumentNullException.ThrowIfNull(played);
        IReadOnlyList<PlaybackTempo> tempos = played.Tempos;
        if (tempos.Count == 0 || tempos[0].Source is not (TempoSource.Player or TempoSource.Default))
        {
            return musicXml;
        }

        XDocument document;
        try
        {
            using var text = new StringReader(musicXml);
            using var reader = XmlReader.Create(text, Safe);
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return musicXml;
        }

        XElement? measure = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "part")?
            .Elements().FirstOrDefault(element => element.Name.LocalName == "measure");
        if (measure is null)
        {
            return musicXml;
        }

        XNamespace ns = measure.Name.Namespace;
        // The file's own metronome marks before the first note of the bar give way to the player's.
        foreach (XElement direction in measure.Elements()
            .TakeWhile(element => element.Name.LocalName is not ("note" or "forward" or "backup"))
            .Where(element => element.Name.LocalName == "direction" && element.Descendants().Any(child => child.Name.LocalName == "metronome"))
            .ToList())
        {
            direction.Remove();
        }

        TempoBeat beat = TempoBeats.At(played.Meter, Tick.Zero);
        var metronome = new XElement(ns + "metronome", new XElement(ns + "beat-unit", "quarter"));
        if (beat == TempoBeat.DottedQuarter)
        {
            metronome.Add(new XElement(ns + "beat-unit-dot"));
        }

        metronome.Add(new XElement(ns + "per-minute", Number(beat.PerMinute(tempos[0].MicrosecondsPerQuarter))));
        // A sound's tempo is quarter notes a minute, whatever the beat.
        var mark = new XElement(
            ns + "direction",
            new XAttribute("placement", "above"),
            new XElement(ns + "direction-type", metronome),
            new XElement(ns + "sound", new XAttribute("tempo", Number(tempos[0].PerMinute))));
        XElement? attributes = measure.Elements().FirstOrDefault(element => element.Name.LocalName == "attributes");
        if (attributes is not null)
        {
            attributes.AddAfterSelf(mark);
        }
        else
        {
            measure.AddFirst(mark);
        }

        return document.Declaration is { } declaration ? declaration + document.ToString(SaveOptions.DisableFormatting) : document.ToString(SaveOptions.DisableFormatting);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}