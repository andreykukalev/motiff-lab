namespace MotiffLab.MusicCore.Settings;

/// <summary>
/// Every setting of the app (0018, section 10; preparation item 5): the one list both apps show, in sheet
/// order. The presets are rows of this table: Learning turns every guide on and reads the piece in short
/// phrases, Practising (the default) keeps the brackets and the Insights strip and turns the hints off,
/// Clean leaves the score, the ink and the bookmarks. A setting's scope says whether its value stays on
/// the device, travels with the account (Pro) or can be overridden for one piece in the project file.
/// </summary>
public static class SettingsCatalogue
{
    private static readonly string[] s_switch = [Setting.On, Setting.Off];

    public static IReadOnlyList<Setting> All { get; } =
    [
        Choice("level.player", SettingSection.Level, ["beginner", "intermediate", "advanced"], "intermediate", null, SettingScope.Account),

        Switch("guidance.brackets", SettingSection.Guidance, Governs(true, true, false), SettingScope.Account),
        Switch("guidance.insightsStrip", SettingSection.Guidance, Governs(true, true, false), SettingScope.Account),
        Switch("guidance.hints", SettingSection.Guidance, Governs(true, false, false), SettingScope.Account),
        Switch("guidance.focusSuggestion", SettingSection.Guidance, Governs(true, false, false), SettingScope.Account),
        Switch("guidance.assistantHints", SettingSection.Guidance, Governs(true, false, false), SettingScope.Account),
        Choice("guidance.transportPill", SettingSection.Guidance, ["selection", "playing"], "selection", new("selection", "selection", "playing"), SettingScope.Account),
        Choice("guidance.barFade", SettingSection.Guidance, ["1", "2"], "2", new("2", "2", "1"), SettingScope.Account),
        Switch("guidance.proOffers", SettingSection.Guidance, Governs(true, true, false), SettingScope.Account),

        Choice("phrases.granularity", SettingSection.Score, ["short", "phrases", "sentences"], "phrases", new("short", "phrases", "phrases"), SettingScope.Piece),
        Choice("score.layout", SettingSection.Score, ["pages", "strip"], "pages", null, SettingScope.Device),
        Switch("score.noteNames", SettingSection.Score, null, SettingScope.Device),
        // Even bars (testing item 35): the bars take room by their length, so the playhead keeps one pace; off, by their
        // notes, and more music fits on a page.
        Switch("score.evenBars", SettingSection.Score, null, SettingScope.Device, on: true),

        Switch("layers.ink", SettingSection.MarksAndLayers, Governs(true, true, true), SettingScope.Piece),
        Switch("layers.notes", SettingSection.MarksAndLayers, Governs(true, true, false), SettingScope.Piece),
        Switch("layers.bookmarks", SettingSection.MarksAndLayers, Governs(true, true, true), SettingScope.Piece),
        Switch("layers.analysis", SettingSection.MarksAndLayers, Governs(true, true, false), SettingScope.Piece),
        Switch("layers.check", SettingSection.MarksAndLayers, Governs(true, true, false), SettingScope.Piece),
        Switch("layers.performance", SettingSection.MarksAndLayers, Governs(true, true, false), SettingScope.Piece),
        // Extra information (testing item 33): what the app adds over the notes (a version's differences, the bars taken from
        // it, Check flags) shows only while this is on; off by default, so the score shows the player's notes and marks alone.
        Switch("layers.extra", SettingSection.MarksAndLayers, null, SettingScope.Piece),
        // A thin plum line under each bar taken from another version, with extra information on.
        Switch("layers.taken", SettingSection.MarksAndLayers, null, SettingScope.Piece, on: true),
        // The comparison layer over the whole score (0022, section 4) and its artefacts, no longer shown (testing item 33): a
        // version is compared in the merge draft. Kept so files that set them still open.
        Switch("layers.version", SettingSection.MarksAndLayers, null, SettingScope.Piece),
        Switch("layers.artefacts", SettingSection.MarksAndLayers, null, SettingScope.Piece),
        Switch("pen.pencilOnly", SettingSection.MarksAndLayers, null, SettingScope.Device, on: true),
        Choice("pen.ink", SettingSection.MarksAndLayers, ["graphite", "carmine", "forest", "ochre"], "graphite", null, SettingScope.Device),

        // The rows of the harmony lane in Piece DNA (testing item 30): chords and functions shown, the steps of the key on request.
        Switch("dna.chords", SettingSection.Score, null, SettingScope.Device, on: true),
        Switch("dna.degrees", SettingSection.Score, null, SettingScope.Device),
        Switch("dna.functions", SettingSection.Score, null, SettingScope.Device, on: true),

        // A choice on a version's page applied at once, without the merge draft (testing item 34); Undo stays in its toast.
        Switch("merge.direct", SettingSection.Score, null, SettingScope.Device),

        Switch("playback.metronome", SettingSection.Playback, null, SettingScope.Device),
        Switch("playback.countIn", SettingSection.Playback, null, SettingScope.Device),
        // The score scrolls to keep the playing system in view (testing item 4); the playhead shows either way.
        Switch("playback.follow", SettingSection.Playback, null, SettingScope.Device, on: true),

        Switch("roll.followPlayback", SettingSection.PianoRoll, null, SettingScope.Device, on: true),
        Switch("roll.noteNames", SettingSection.PianoRoll, null, SettingScope.Device, on: true),
        Choice("notes.naming", SettingSection.PianoRoll, ["auto", "letters", "syllables"], "auto", null, SettingScope.Device),
        Choice("roll.colour", SettingSection.PianoRoll, ["hand", "track", "voice"], "hand", null, SettingScope.Device),

        Choice("assistant.answers", SettingSection.Assistant, ["onRequest", "offered"], "onRequest", new("offered", "onRequest", "onRequest"), SettingScope.Account),

        Choice("appearance.theme", SettingSection.Appearance, ["auto", "daylight", "night", "slate"], "auto", null, SettingScope.Device),
        Choice("appearance.language", SettingSection.Appearance, ["en", "ru"], "en", null, SettingScope.Device),

        Switch("account.saveOnline", SettingSection.Account, null, SettingScope.Device),
        Switch("account.analytics", SettingSection.Account, null, SettingScope.Device),

        // Experiments, each off until the player turns it on on this device. Several pieces in a score or recording (v0.5,
        // replacing automatic bookmarks and pieces in a file): where the pieces of a long score or recording begin, proposed
        // by music-core (SectionFinder) and set by hand, kept as the pieces layer, and each piece made a library piece of
        // its own (PieceParts); the file itself is not cut.
        Switch("experimental.pieces", SettingSection.Experimental, null, SettingScope.Device),

        // My corrections on or off (v0.5): the player's note corrections laid on one by one, the ones that no longer fit shown.
        Switch("experimental.editsLayer", SettingSection.Experimental, null, SettingScope.Device),

        // Live performance tempo (v0.5): a recording's beat grid found again after each pause and tempo jump.
        Switch("experimental.liveTempo", SettingSection.Experimental, null, SettingScope.Device),

        // «Для чтения» with the marks that matter (v0.5): a recording's score for reading with marked dynamics, rit. and
        // accel., and fermatas (PlayingMarks.Significant).
        Switch("experimental.readingMarks", SettingSection.Experimental, null, SettingScope.Device),

        // Reference: the circle of fifths one tap away from the library and the piece, with any key's card (KeyReference).
        Switch("experimental.reference", SettingSection.Experimental, null, SettingScope.Device),

        // «Справочник» (v0.5): the Reference with three tabs, keys on a circle shaded by their accidentals, chords and scales
        // with how they sound (ReferenceSounding), and the harmony explorer (HarmonyExplorer); nothing is written into a piece.
        Switch("experimental.harmony", SettingSection.Experimental, null, SettingScope.Device),

        // Photos of pages (v0.4, iPad): pages photographed in a row become one PDF piece, read by the scan server page by page.
        Switch("experimental.photoPages", SettingSection.Experimental, null, SettingScope.Device),

        // At the instrument, on the iPad. Resume where you stopped (#327): the passage, focus, tempo, loop and hands of the
        // latest resume point come back when the piece opens, and are kept in it when it closes.
        Switch("experimental.resume", SettingSection.Experimental, null, SettingScope.Device),

        // Hands separately (#328): both hands, the right or the left in the pill, and the next or previous phrase outside focus.
        Switch("experimental.hands", SettingSection.Experimental, null, SettingScope.Device),

        // Selection by beats (#349): the selection's edges snap to beats, so a passage can start and end mid-bar.
        Switch("experimental.beats", SettingSection.Experimental, null, SettingScope.Device),

        // Two playheads and modes (v0.5, specs two-playheads and interaction-modes): a global playhead and one inside the
        // selection, the selection by beats changed only by its handles, and the modes Navigate, Select, Edit notes and Pen.
        Switch("experimental.playheads", SettingSection.Experimental, null, SettingScope.Device),

        // Inside that experiment: while the piece plays, the bars other than the one playing fade, to try by feel.
        Switch("experimental.playheadsFade", SettingSection.Experimental, null, SettingScope.Device, on: true),

        // Changes of signature in a recording (v0.5, a preview): where the bars read better in 5/8, 7/8, 9/8, 5/4 or 12/8
        // (LiveBars.OddMeters), said in the Check drawer's head; nothing is changed or saved.
        Switch("experimental.meters", SettingSection.Experimental, null, SettingScope.Device),

        // Import a folder (v0.5, «Импорт папки»): the PDF, MIDI and MusicXML files of a folder the player chose added in one go,
        // with repeats by fingerprint, suggested titles and composers, and folders as playlists (LibraryImport); the
        // player's files are only read.
        Switch("experimental.libraryImport", SettingSection.Experimental, null, SettingScope.Device),
    ];

    private static readonly Dictionary<string, Setting> s_byId = All.ToDictionary(setting => setting.Id, StringComparer.Ordinal);

    /// <summary>The settings the presets govern, in sheet order.</summary>
    public static IEnumerable<Setting> Governed => All.Where(setting => setting.IsGoverned);

    /// <summary>The settings a piece can override, in sheet order.</summary>
    public static IEnumerable<Setting> PerPiece => All.Where(setting => setting.Scope == SettingScope.Piece);

    public static Setting? Find(string id) => s_byId.GetValueOrDefault(id);

    /// <summary>The setting with the id.</summary>
    /// <exception cref="KeyNotFoundException">No setting has the id.</exception>
    public static Setting Get(string id) => Find(id) ?? throw new KeyNotFoundException($"There is no setting \"{id}\".");

    /// <summary>What every setting is under the preset: the preset's value where it governs, the default elsewhere.</summary>
    public static IReadOnlyDictionary<string, string> ValuesOf(Preset preset)
    {
        if (preset == Preset.Custom)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Custom is not a preset to apply.");
        }

        return All.ToDictionary(setting => setting.Id, setting => setting.Presets?.For(preset) ?? setting.Default, StringComparer.Ordinal);
    }

    private static PresetValues Governs(bool learning, bool practising, bool clean) => new(Text(learning), Text(practising), Text(clean));

    private static string Text(bool on) => on ? Setting.On : Setting.Off;

    private static Setting Switch(string id, SettingSection section, PresetValues? presets, SettingScope scope, bool on = false) =>
        new(id, section, SettingKind.Switch, s_switch, presets?.Practising ?? Text(on), presets, scope);

    private static Setting Choice(string id, SettingSection section, string[] choices, string fallback, PresetValues? presets, SettingScope scope) =>
        new(id, section, SettingKind.Choice, choices, presets?.Practising ?? fallback, presets, scope);
}