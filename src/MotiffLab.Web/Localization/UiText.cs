using System.Globalization;
using System.Net.Http.Json;

using Microsoft.JSInterop;

namespace MotiffLab.Web.Localization;

/// <summary>
/// The interface's words in the player's language (docs/decisions/0012-workspace-structure.md, localization).
/// Strings live in <c>wwwroot/i18n/{language}.json</c>, one flat file per language keyed by
/// <c>area.name</c>; English is the fallback for a key a language has not got yet, and the key itself
/// is the last resort, so a missing string is visible rather than a crash. Components read
/// <c>Text["workspace.focus"]</c>; nothing is translated in conditionals in the code.
/// </summary>
/// <remarks>
/// The language is a preference on the device (local storage), not part of a project. Musical facts
/// and explanations come from music-core's templates and are not translated here yet; see the issue
/// on localising the musical layer.
/// </remarks>
public sealed class UiText(HttpClient http, IJSRuntime js)
{
    /// <summary>The languages the interface has strings for: the tag and the language's own name.</summary>
    public static readonly IReadOnlyList<(string Tag, string Name)> Languages = [("en", "English"), ("ru", "Русский")];

    public const string Default = "en";

    private const string PreferenceKey = "ml-language";

    private Dictionary<string, string> _fallback = [];
    private Dictionary<string, string> _strings = [];
    private IJSObjectReference? _preferences;

    /// <summary>The language in use: "en" or "ru".</summary>
    public string Language { get; private set; } = Default;

    /// <summary>Raised after the language changes, so open pages render again.</summary>
    public event Action? Changed;

    /// <summary>The string for a key, in the language in use, else in English, else the key.</summary>
    public string this[string key] => _strings.TryGetValue(key, out string? text) || _fallback.TryGetValue(key, out text) ? text : key;

    /// <summary>The string for a key with its placeholders filled, as <see cref="string.Format(IFormatProvider, string, object[])"/> does.</summary>
    public string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, this[key], args);

    /// <summary>Whether the key has a string in the language in use or in English.</summary>
    public bool Has(string key) => _strings.ContainsKey(key) || _fallback.ContainsKey(key);

    /// <summary>The culture for dates and numbers shown to the player.</summary>
    public CultureInfo Culture => Language == "ru" ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Loads English and the language the device prefers, before the first page renders.</summary>
    public async Task InitializeAsync()
    {
        _fallback = await LoadAsync(Default);
        string? preferred = null;
        try
        {
            _preferences = await js.InvokeAsync<IJSObjectReference>("import", "./js/storage/preferences.js");
            preferred = await _preferences.InvokeAsync<string?>("getPreference", PreferenceKey);
        }
        catch (JSException)
        {
            // Storage is blocked: English it is, for this visit.
        }

        await UseAsync(Normalize(preferred), save: false);
    }

    /// <summary>Switches the interface to a language and remembers it on the device.</summary>
    public Task SetLanguageAsync(string tag) => UseAsync(Normalize(tag), save: true);

    /// <summary>"en" or "ru" for a tag such as "ru-RU"; English for anything else.</summary>
    public static string Normalize(string? tag)
    {
        string lower = (tag ?? "").Trim().ToLowerInvariant();
        return Languages.FirstOrDefault(language => lower == language.Tag || lower.StartsWith(language.Tag + "-", StringComparison.Ordinal)).Tag ?? Default;
    }

    private async Task UseAsync(string tag, bool save)
    {
        _strings = tag == Default ? _fallback : await LoadAsync(tag);
        Language = tag;
        if (_preferences is not null)
        {
            try
            {
                await _preferences.InvokeVoidAsync("applyLanguage", tag);
                if (save)
                {
                    await _preferences.InvokeVoidAsync("setPreference", PreferenceKey, tag);
                }
            }
            catch (JSException)
            {
            }
        }

        Changed?.Invoke();
    }

    private async Task<Dictionary<string, string>> LoadAsync(string tag)
    {
        try
        {
            return await http.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{tag}.json") ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}