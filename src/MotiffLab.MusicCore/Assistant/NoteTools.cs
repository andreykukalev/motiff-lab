using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

using static MotiffLab.MusicCore.Assistant.ToolArguments;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// The low-level note tools (0028 section 2): change, remove or add notes by address in a draft, check such a draft
/// against the rules and the Keep choices, and suggest a practice action. Positions and lengths are in 32nds, the finest
/// grid the operation rules allow, so a note off the grid cannot be asked for.
/// </summary>
internal static class NoteTools
{
    private const string EditTool = "notes.edit";

    private static ToolSchema EditsSchema => ToolSchema.List(
        ToolSchema.Object(
            ("kind", ToolSchema.Choice<DraftEditKind>(), "change, remove or add."),
            ("address", ToolSchema.OrNull(ToolSchema.Text(NoteAddress.Pattern)), "The note changed or removed (bar.hand.index); null when adding."),
            ("bar", ToolSchema.OrNull(ToolSchema.Integer(0, AssistantTools.MaxBarNumber)), "Adding: the bar of the new note; null otherwise."),
            ("hand", ToolSchema.OrNull(ToolSchema.Choice("right", "left")), "Adding: the hand of the new note, or null."),
            ("start", ToolSchema.OrNull(ToolSchema.Integer(0, 255)), "Where the note starts in its bar, in 32nds from the bar's start; null keeps it when changing."),
            ("length", ToolSchema.OrNull(ToolSchema.Integer(1, 128)), "The note's length in 32nds; null keeps it when changing."),
            ("pitch", ToolSchema.OrNull(ToolSchema.Integer(OperationRules.LowestPitch, OperationRules.HighestPitch)), "MIDI pitch, 21 (A0) to 108 (C8); null keeps it when changing."),
            ("velocity", ToolSchema.OrNull(ToolSchema.Integer(Note.MinVelocity, Note.MaxVelocity)), "1-127; null keeps it, or 80 for an added note.")),
        1,
        AssistantTools.MaxEdits);

    internal static IEnumerable<AssistantTool> All()
    {
        yield return new AssistantTool(
            EditTool,
            ToolKind.ProposeDraft,
            "Change, remove or add notes by address in a draft of a range, one property at a time where you can. The engine checks range, grid and voice crossing and writes what changed from the diff. Nothing is applied.",
            ToolSchema.Object(
                ("bars", BarsSchema, "The bars the draft covers, at most 32; every edit lies in them."),
                ("edits", EditsSchema, "The edits, in order; a note is edited once.")),
            static (piece, parameters) => Drafted(piece, parameters));
        yield return new AssistantTool(
            "draft.check",
            ToolKind.Check,
            "Checks note edits (as notes.edit takes them) against the operation rules and the Keep choices, without making a draft: which kept properties hold, and the rules' flags.",
            ToolSchema.Object(
                ("bars", BarsSchema, "The bars the edits lie in, at most 32."),
                ("edits", EditsSchema, "The edits to check."),
                ("keep", ToolSchema.List(ToolSchema.Choice<KeepProperty>(), 0, Enum.GetValues<KeepProperty>().Length, unique: true), "What must be kept.")),
            static (piece, parameters) => Checked(piece, parameters));
        yield return new AssistantTool(
            "draft.diff",
            ToolKind.Check,
            "The difference note edits (as notes.edit takes them) would make, without making a draft: each edit by address with the values before and after, and what changes, written by the engine from the diff.",
            ToolSchema.Object(
                ("bars", BarsSchema, "The bars the edits lie in, at most 32."),
                ("edits", EditsSchema, "The edits to compare.")),
            static (piece, parameters) => Diffed(piece, parameters));
        yield return new AssistantTool(
            "practice.suggest",
            ToolKind.SuggestAction,
            "Suggests a practice action the player can take with one tap: loop bars, at a tempo (25-150 % of the piece's), with one hand or both. The player decides; nothing changes until they tap it.",
            ToolSchema.Object(
                ("bars", BarsSchema, "The bars to practise, at most 32."),
                ("hand", HandSchema, "One hand, or both."),
                ("tempo", ToolSchema.Integer(ResumePoint.MinTempoPercent, ResumePoint.MaxTempoPercent), "The tempo as a percentage of the piece's."),
                ("loop", ToolSchema.Boolean, "Whether to loop the bars.")),
            static (piece, parameters) => Ranged(piece, parameters, (from, to) => ToolResult.Read(json => WritePractice(json, piece.Words, from, to, parameters))));
    }

    private static ToolResult Drafted(AssistantPiece piece, JsonElement parameters)
    {
        (AssistantDraft? draft, string? refusal) = Edited(piece, parameters, new HashSet<KeepProperty>());
        return draft is not null ? ToolResult.Proposed([draft]) : ToolResult.Refused(refusal!);
    }

    private static ToolResult Checked(AssistantPiece piece, JsonElement parameters)
    {
        var keep = parameters.GetProperty("keep").EnumerateArray().Select(ToolSchema.Read<KeepProperty>).ToHashSet();
        (AssistantDraft? draft, string? refusal) = Edited(piece, parameters, keep);
        if (refusal is not null)
        {
            return ToolResult.Refused(refusal);
        }

        return ToolResult.Read(json =>
        {
            json.WriteStartObject();
            json.WriteBoolean("passes", true);
            json.WriteStartObject("keep");
            foreach (KeepCheck check in draft!.Variation.Checks)
            {
                json.WriteString(ToolSchema.Camel(check.Property.ToString()), ToolSchema.Camel(check.Status.ToString()));
            }

            json.WriteEndObject();
            json.WriteStartArray("flags");
            foreach (string flag in draft.Variation.Flags)
            {
                json.WriteStringValue(flag);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        });
    }

    private static ToolResult Diffed(AssistantPiece piece, JsonElement parameters)
    {
        (AssistantDraft? draft, string? refusal) = Edited(piece, parameters, new HashSet<KeepProperty>());
        return draft is null ? ToolResult.Refused(refusal!) : ToolResult.Read(json => (draft with { Tool = "draft.diff" }).Write(json, piece));
    }

    /// <summary>The range's notes with the edits made, as a draft, or why it cannot be made.</summary>
    private static (AssistantDraft? Draft, string? Refusal) Edited(AssistantPiece piece, JsonElement parameters, IReadOnlySet<KeepProperty> keep)
    {
        if (RangeRefusal(piece, parameters) is { } range)
        {
            return (null, range);
        }

        (int from, int to) = BarsOf(parameters);
        RangeReading reading = piece.Read(from, to);
        IReadOnlyList<Note> before = reading.NotesOf(piece.Notes);
        var after = before.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        var edited = new HashSet<string>(StringComparer.Ordinal);
        int added = 0;
        foreach (JsonElement edit in parameters.GetProperty("edits").EnumerateArray())
        {
            string? refusal = ToolSchema.Read<DraftEditKind>(edit.GetProperty("kind")) == DraftEditKind.Add
                ? Add(piece, edit, from, to, before, after, ++added)
                : ChangeOrRemove(piece, edit, after, edited);
            if (refusal is not null)
            {
                return (null, refusal);
            }
        }

        return Made(piece, EditTool, reading, before, [.. after.Values], keep);
    }

    /// <summary>The draft of <paramref name="tool"/> from the range's notes to <paramref name="after"/>, checked by the operation rules, or why it cannot be made.</summary>
    internal static (AssistantDraft? Draft, string? Refusal) Made(AssistantPiece piece, string tool, RangeReading reading, IReadOnlyList<Note> before, IReadOnlyList<Note> after, IReadOnlySet<KeepProperty> keep)
    {
        RuleReport rules = OperationRules.Check(before, after, piece.Words);
        if (rules.Violation is { } violation)
        {
            return (null, piece.Words.T($"The edits {violation}.", $"Правка невозможна: {violation}."));
        }

        Variation variation = VariationFacts.Build(tool, reading.Selection.Span, before, after, keep, rules.Flags, reading.Motif.Key?.Key, piece.Meter, piece.Words);
        return (new AssistantDraft(tool, before, variation), null);
    }

    private static string? ChangeOrRemove(AssistantPiece piece, JsonElement edit, Dictionary<string, Note> notes, HashSet<string> edited)
    {
        Words w = piece.Words;
        string? text = edit.GetProperty("address").GetString();
        if (piece.Addresses.Resolve(text, w, out string? reason) is not { } note)
        {
            return reason ?? w.T("Changing or removing a note needs its address.", "Чтобы изменить или убрать ноту, нужен её адрес.");
        }

        if (!notes.ContainsKey(note.Id))
        {
            return w.T($"Note {text} is outside the bars of the draft.", $"Нота {text} вне тактов черновика.");
        }

        if (!edited.Add(note.Id))
        {
            return w.T($"Note {text} is edited twice; edit each note once.", $"Нота {text} правится дважды; правьте каждую ноту один раз.");
        }

        if (ToolSchema.Read<DraftEditKind>(edit.GetProperty("kind")) == DraftEditKind.Remove)
        {
            notes.Remove(note.Id);
            return null;
        }

        Bar bar = piece.Meter.BarAt(note.Span.Onset);
        long onset = Int(edit, "start") is int start ? Origin(bar) + (start * AssistantTools.ThirtySecond) : note.Span.Onset.Value;
        long length = Int(edit, "length") is int thirtySeconds ? thirtySeconds * AssistantTools.ThirtySecond : note.Span.Duration;
        if (onset < bar.Span.Onset.Value || onset >= bar.Span.End.Value)
        {
            return OutsideBar(bar, w);
        }

        notes[note.Id] = note with
        {
            Pitch = Int(edit, "pitch") is int pitch ? new Pitch(pitch) : note.Pitch,
            Span = new TickSpan(new Tick(onset), length),
            Velocity = Int(edit, "velocity") ?? note.Velocity,
            Attribution = Attribution.UserEdited,
        };
        return null;
    }

    private static string? Add(AssistantPiece piece, JsonElement edit, int from, int to, IReadOnlyList<Note> before, Dictionary<string, Note> notes, int number)
    {
        Words w = piece.Words;
        if (Int(edit, "bar") is not int barNumber || Int(edit, "start") is not int start || Int(edit, "length") is not int length || Int(edit, "pitch") is not int pitch)
        {
            return w.T("Adding a note needs its bar, start, length and pitch.", "Чтобы добавить ноту, нужны её такт, начало, длина и высота.");
        }

        if (barNumber < from || barNumber > to)
        {
            return w.T($"Bar {barNumber} is outside the bars of the draft.", $"Такт {barNumber} вне тактов черновика.");
        }

        Bar bar = piece.Meter.GetBar(barNumber);
        long onset = Origin(bar) + (start * AssistantTools.ThirtySecond);
        if (onset < bar.Span.Onset.Value || onset >= bar.Span.End.Value)
        {
            return OutsideBar(bar, w);
        }

        Hand? hand = edit.GetProperty("hand").GetString() switch { "right" => Hand.Right, "left" => Hand.Left, _ => null };
        string id = $"draft-{number}";
        notes[id] = new Note
        {
            Id = id,
            Pitch = new Pitch(pitch),
            Span = new TickSpan(new Tick(onset), length * AssistantTools.ThirtySecond),
            Velocity = Int(edit, "velocity") ?? AssistantTools.AddedVelocity,
            Track = before.FirstOrDefault(note => hand is null || note.Hand?.Value == hand)?.Track ?? 0,
            Hand = hand is { } played ? new Attributed<Hand>(played, Attribution.UserEdited) : null,
            Attribution = Attribution.UserEdited,
        };
        return null;
    }

    /// <summary>Where a bar's 32nds are counted from: its start, or for a pickup the start it would have if complete.</summary>
    private static long Origin(Bar bar) => bar.IsPickup ? bar.Span.End.Value - bar.Signature.BarTicks : bar.Span.Onset.Value;

    private static string OutsideBar(Bar bar, Words w)
    {
        long first = (bar.Span.Onset.Value - Origin(bar)) / AssistantTools.ThirtySecond, last = ((bar.Span.End.Value - Origin(bar)) / AssistantTools.ThirtySecond) - 1;
        return w.T(
            $"In bar {bar.Number} a note starts at 32nd {first} to {last}.",
            $"В такте {bar.Number} нота начинается с {first}-й по {last}-ю тридцать вторую.");
    }

    private static int? Int(JsonElement edit, string name) => edit.GetProperty(name) is { ValueKind: JsonValueKind.Number } number ? number.GetInt32() : null;

    private static void WritePractice(Utf8JsonWriter json, Words w, int from, int to, JsonElement parameters)
    {
        string hand = parameters.GetProperty("hand").GetString()!;
        int tempo = parameters.GetProperty("tempo").GetInt32();
        bool loop = parameters.GetProperty("loop").GetBoolean();
        string bars = from == to ? w.T($"bar {from}", $"такт {from}") : w.T($"bars {from}–{to}", $"такты {from}–{to}");
        string hands = hand switch
        {
            "right" => w.T("right hand", "правая рука"),
            "left" => w.T("left hand", "левая рука"),
            _ => w.T("both hands", "обе руки"),
        };
        json.WriteStartObject();
        json.WriteStartObject("action");
        json.WriteStartObject("bars");
        json.WriteNumber("from", from);
        json.WriteNumber("to", to);
        json.WriteEndObject();
        json.WriteString("hand", hand);
        json.WriteNumber("tempo", tempo);
        json.WriteBoolean("loop", loop);
        json.WriteEndObject();
        json.WriteString("label", $"{(loop ? w.T("Loop", "Цикл") : w.T("Play", "Играть"))} {bars} · {tempo} % · {hands}");
        json.WriteEndObject();
    }
}