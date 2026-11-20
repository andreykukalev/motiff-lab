using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Assistant.Drafts;

/// <summary>
/// The place of a draft the assistant is asked about (decision record 0034, section 6; the plan's answer 7): the draft, the
/// place and the variant being built from, the place's base and that variant each read as a piece of its own (the base's
/// bars are the place's, from 1, as its приёмы name them), and the time a step it proposes takes. Only these two variants'
/// notes, at most <see cref="MotifSelection.MaxBars"/> bars each, are what a question in the draft is about; never a file, a
/// name or a recording's sound. Immutable.
/// </summary>
public sealed class DraftScope
{
    private DraftScope(Draft draft, DraftPlace place, DraftVariant? variant, Func<string, AssistantPiece> piece, DateTimeOffset at, Words words)
    {
        Draft = draft;
        Place = place;
        Index = draft.Places.ToList().IndexOf(place);
        Variant = variant;
        At = at;
        Words = words;
        Base = piece(place.Base);
        Heard = variant is null ? null : piece(variant.Id);
    }

    public Draft Draft { get; }

    public DraftPlace Place { get; }

    /// <summary>The place's position in the draft, from 0.</summary>
    public int Index { get; }

    /// <summary>The variant being built from (not the base); null in a place of one variant, or a bridge place.</summary>
    public DraftVariant? Variant { get; }

    /// <summary>The place's base as a piece, its bars from 1; the tools read it, and it carries this scope.</summary>
    public AssistantPiece Base { get; }

    /// <summary>The variant as a piece of its own, its bars from 1; null without one.</summary>
    public AssistantPiece? Heard { get; }

    /// <summary>When a step the assistant proposes is made: the time of the call, so a proposal made again is the same.</summary>
    public DateTimeOffset At { get; }

    public Words Words { get; }

    /// <summary>
    /// The place <paramref name="placeId"/> of the draft <paramref name="draftId"/> of <paramref name="project"/>, with the
    /// variant <paramref name="variantId"/> or, when null, the place's first variant that is not its base.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such draft, place or variant.</exception>
    /// <exception cref="ArgumentException">The variant is the base, or the base or the variant spans more bars than a question takes.</exception>
    public static DraftScope Of(Project project, string draftId, string placeId, string? variantId, DateTimeOffset at, Words words)
    {
        ArgumentNullException.ThrowIfNull(project);
        Draft draft = project.FindDraft(draftId) ?? throw new KeyNotFoundException($"There is no draft {draftId}.");
        DraftPlace place = draft.FindPlace(placeId) ?? throw new KeyNotFoundException($"The draft has no place {placeId}.");
        DraftVariant? variant = variantId is null
            ? place.Bridge ? null : place.Variants.FirstOrDefault(each => each.Id != place.Base)
            : place.Find(variantId) ?? throw new KeyNotFoundException($"The place {placeId} has no variant {variantId}.");
        if (variant?.Id == place.Base)
        {
            throw new ArgumentException(words.T("The variant built from is not the place's base.", "Вариант, из которого собирают, — не основа места."), nameof(variantId));
        }

        var scope = new DraftScope(draft, place, variant, id => Piece(project, draftId, id, words), at, words);
        foreach (AssistantPiece piece in new[] { scope.Base, scope.Heard }.OfType<AssistantPiece>())
        {
            if (piece.LastBar - piece.FirstBar + 1 > MotifSelection.MaxBars)
            {
                throw new ArgumentException(
                    words.T($"The assistant takes a place of at most {MotifSelection.MaxBars} bars.", $"Ассистент берёт место не длиннее {MotifSelection.MaxBars} тактов."),
                    nameof(placeId));
            }
        }

        scope.Base.Draft = scope;
        return scope;
    }

    /// <summary>The variant's own music read as a piece, as the draft view plays it («как в нотах»).</summary>
    private static AssistantPiece Piece(Project project, string draftId, string variantId, Words words)
    {
        OpenedProject opened = OpenedProject.Open(project.ViewOfDraft(draftId, variantId));
        return new AssistantPiece(opened.Piece, opened.Notes, [], words);
    }
}