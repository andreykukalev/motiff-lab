using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A stretch of measures cut from a partwise MusicXML score into a score of its own, for <see cref="PieceParts"/>: the
/// header and part list as they are, each part's measures from the first to the last, numbered again from 1 (0 for a
/// pickup), and the context in effect where the stretch begins carried into its first measure, so its notes sound and
/// read as they did: the divisions, key, time, staves, clefs and transposition, and the last tempo set.
/// </summary>
/// <remarks>
/// The first measure loses a scan's <c>new-page</c> mark, since it begins the score; the later ones keep theirs, so the
/// stretch's own pages are known (<see cref="PageMap.Of"/>). Pure and deterministic; nothing is read from outside the text.
/// </remarks>
internal static class ScoreCut
{
    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

    /// <summary>The children of <c>attributes</c> in the order MusicXML gives them; the ones carried into a stretch are <see cref="Carried"/>.</summary>
    private static readonly string[] Order = ["footnote", "level", "divisions", "key", "time", "staves", "part-symbol", "instruments", "clef", "staff-details", "transpose", "for-part", "directive", "measure-style"];

    private static readonly HashSet<string> Carried = ["divisions", "key", "time", "staves", "part-symbol", "instruments", "clef", "staff-details", "transpose", "for-part"];

    /// <summary>The measures from <paramref name="first"/> to <paramref name="last"/> (indices from 0, both included) as a score.</summary>
    /// <exception cref="ArgumentException">The score is not partwise MusicXML, or a part has no such measures.</exception>
    public static byte[] Cut(byte[] score, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(score);
        XElement root = Parse(score);
        XElement copy = new(root);
        copy.Elements("part").Remove();
        foreach (XElement part in root.Elements("part"))
        {
            List<XElement> measures = [.. part.Elements("measure")];
            if (first < 0 || last < first || last >= measures.Count)
            {
                throw new ArgumentException($"Part {(string?)part.Attribute("id")} has no measures {first + 1}–{last + 1}.", nameof(last));
            }

            List<XElement> kept = [.. measures.Skip(first).Take(last - first + 1).Select(static measure => new XElement(measure))];
            if (first > 0)
            {
                CarryInto(kept[0], measures.Take(first));
                EndPageMark(kept[0]);
            }

            int number = (string?)kept[0].Attribute("implicit") == "yes" ? 0 : 1;
            foreach (XElement measure in kept)
            {
                measure.SetAttributeValue("number", (number++).ToString(CultureInfo.InvariantCulture));
            }

            copy.Add(new XElement(part.Name, part.Attributes(), kept));
        }

        return Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + copy.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>The attributes and tempo in effect after <paramref name="before"/>, put at the start of <paramref name="measure"/> where it does not set them itself.</summary>
    private static void CarryInto(XElement measure, IEnumerable<XElement> before)
    {
        var state = new List<XElement>();
        string? tempo = null;
        foreach (XElement element in before.SelectMany(static earlier => earlier.Elements()))
        {
            if (element.Name.LocalName == "attributes")
            {
                foreach (XElement child in element.Elements().Where(static child => Carried.Contains(child.Name.LocalName)))
                {
                    Set(state, child);
                }
            }
            else
            {
                tempo = TempoOf(element) ?? tempo;
            }
        }

        List<XElement> opening = [.. measure.Elements().TakeWhile(static element => element.Name.LocalName is not ("note" or "backup" or "forward"))];
        XElement? own = opening.FirstOrDefault(static element => element.Name.LocalName == "attributes");
        foreach (XElement child in own?.Elements() ?? [])
        {
            Set(state, child);
        }

        if (state.Count > 0)
        {
            XElement attributes = new("attributes", state.Select((child, index) => (child, index))
                .OrderBy(static pair => Array.IndexOf(Order, pair.child.Name.LocalName) is >= 0 and var at ? at : Order.Length)
                .ThenBy(static pair => pair.index)
                .Select(static pair => new XElement(pair.child)));
            if (own is not null)
            {
                own.ReplaceWith(attributes);
            }
            else
            {
                Insert(measure, attributes);
            }
        }

        if (tempo is not null && !opening.Any(static element => TempoOf(element) is not null))
        {
            XElement sound = new("sound", new XAttribute("tempo", tempo));
            if (measure.Element("attributes") is { } placed)
            {
                placed.AddAfterSelf(sound);
            }
            else
            {
                Insert(measure, sound);
            }
        }
    }

    /// <summary>
    /// An attribute in effect: it takes the place of the one of its kind for the same staff (<c>number</c>); one for every
    /// staff (no number) takes the place of all of its kind.
    /// </summary>
    private static void Set(List<XElement> state, XElement child)
    {
        string name = child.Name.LocalName;
        string? number = (string?)child.Attribute("number");
        int at = state.FindIndex(kept => kept.Name.LocalName == name && (number is null || (string?)kept.Attribute("number") == number));
        state.RemoveAll(kept => kept.Name.LocalName == name && (number is null || (string?)kept.Attribute("number") == number));
        state.Insert(at >= 0 ? Math.Min(at, state.Count) : state.Count, child);
    }

    /// <summary>The tempo a direction or sound sets, as written; null when it sets none.</summary>
    private static string? TempoOf(XElement element) => element.Name.LocalName switch
    {
        "sound" => (string?)element.Attribute("tempo"),
        "direction" => element.Elements("sound").Select(static sound => (string?)sound.Attribute("tempo")).LastOrDefault(static tempo => tempo is not null),
        _ => null,
    };

    /// <summary>Puts an element at the start of a measure, after its layout (<c>print</c>) and its left bar line.</summary>
    private static void Insert(XElement measure, XElement element)
    {
        XElement? after = measure.Elements().TakeWhile(static child => child.Name.LocalName is "print" or "barline" or "attributes").LastOrDefault();
        if (after is null)
        {
            measure.AddFirst(element);
        }
        else
        {
            after.AddAfterSelf(element);
        }
    }

    /// <summary>Takes away a <c>new-page</c> mark; a print element left with nothing in it goes too.</summary>
    private static void EndPageMark(XElement measure)
    {
        foreach (XAttribute mark in measure.Elements("print").Attributes("new-page").Where(static mark => mark.Value == "yes").ToList())
        {
            XElement print = mark.Parent!;
            mark.Remove();
            if (print is { HasAttributes: false, HasElements: false })
            {
                print.Remove();
            }
        }
    }

    private static XElement Parse(byte[] score)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(score, writable: false);
            using var reader = XmlReader.Create(stream, Safe);
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new ArgumentException("The score is damaged.", exception);
        }

        return document.Root is { Name.LocalName: "score-partwise" } root
            ? root
            : throw new ArgumentException("The score is not partwise MusicXML.");
    }
}