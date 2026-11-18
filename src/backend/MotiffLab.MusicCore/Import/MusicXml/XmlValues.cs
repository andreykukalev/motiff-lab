using System.Globalization;

namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>The text values of a MusicXML file as the importer reads them: trimmed, and numbers in the invariant culture.</summary>
internal static class XmlValues
{
    /// <summary>The text without surrounding white space; null when nothing is left.</summary>
    public static string? Trimmed(string? text)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static bool TryParseInt(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    public static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}