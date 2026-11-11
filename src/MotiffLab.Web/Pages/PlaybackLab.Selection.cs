using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The selection on the score (decision record 0018, section 3): bars dragged across or a handle moved, and the one
/// contextual menu above the selection. The menu holds only what applies: Loop, the piano roll and Insights, and in
/// its overflow Make phrase for a range that is not a unit, Merge with next for a unit with one after it, and Split
/// here for a unit the playhead stands in after its first bar. The phrase edits come from music-core's
/// <see cref="PhraseLevels"/>; the page never moves a boundary itself.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>Whether the menu's overflow is open.</summary>
    private bool _menuMore;

    /// <summary>The selection the overflow was opened for; another selection closes it.</summary>
    private TickSpan? _menuMoreFor;

    /// <summary>Whether the contextual menu shows: something is selected on the page and the pen is not writing.</summary>
    private bool MenuShown => _selection is not null && !_penOn && _score is not null && !Focused;

    /// <summary>Whether the overflow is open for the selection shown now.</summary>
    private bool MenuMoreOpen => _menuMore && _menuMoreFor == _selection?.Span;

    /// <summary>
    /// The unit of the level shown that the selection is: exactly, or as much of it as can be selected when it runs
    /// longer than a selection may (<see cref="PhraseLevels.Selectable"/>); null for another range or nothing.
    /// </summary>
    private PhraseUnit? SelectedUnit =>
        _selection is { } selection && Levels is { } levels
            ? levels.Matching(_level, selection.Span) ?? levels.Units(_level).FirstOrDefault(unit => levels.Selectable(unit) == selection.Span)
            : null;

    /// <summary>Make phrase, at the level shown: the selection is not a unit of it yet.</summary>
    private bool CanMakeUnit => Levels is not null && _selection is not null && SelectedUnit is null;

    /// <summary>Merge with next: the selected unit has one after it.</summary>
    private bool CanMerge => SelectedUnit is { } unit && unit.Number < Levels!.Units(_level).Count;

    /// <summary>
    /// Where Split here splits the selected unit: the start of the bar the playhead stands in, when that is a bar of
    /// the unit after its first; null otherwise, and for a unit of one bar.
    /// </summary>
    private Tick? SplitPoint
    {
        get
        {
            if (SelectedUnit is not { BarCount: > 1 } unit || _meter is null || _score is null)
            {
                return null;
            }

            Tick position = Player.Position;
            if (position < unit.Span.Onset || position >= unit.Span.End)
            {
                return null;
            }

            int bar = _meter.BarAt(position).Number;
            return bar > unit.FirstBar && bar <= unit.LastBar ? MotifSelector.Bars(_meter, bar, bar, _score.End).Onset : null;
        }
    }

    /// <summary>The bar Split here splits at, for its label.</summary>
    private int SplitBar => SplitPoint is { } at ? _meter!.BarAt(at).Number : 0;

    private string MakeLabel => Text[_level switch
    {
        PhraseLevel.Short => "menu.makeShort",
        PhraseLevel.Sentence => "menu.makeSentence",
        _ => "menu.makePhrase",
    }];

    private string MergeLabel => Text[_level switch
    {
        PhraseLevel.Short => "menu.mergeShort",
        PhraseLevel.Sentence => "menu.mergeSentence",
        _ => "menu.mergePhrase",
    }];

    /// <summary>
    /// Bars chosen on the score (0018 section 3): a drag across them selects them; a handle moved on a selected unit
    /// moves that unit's edge, which sticks at this level until Reset to detected; a handle moved on any other range
    /// resizes the selection.
    /// </summary>
    private async Task OnScoreBarsChosenAsync((Tick First, Tick Last, bool ByHandle) chosen)
    {
        if (_meter is null || _score is null)
        {
            return;
        }

        Tick last = new(_score.End.Value - 1);
        int from = _meter.BarAt(chosen.First > last ? last : chosen.First).Number;
        int to = _meter.BarAt(chosen.Last > last ? last : chosen.Last).Number;
        if (chosen.ByHandle && SelectedUnit is { } unit && Levels is { } levels)
        {
            TickSpan span = MotifSelector.Bars(_meter, Math.Min(from, to), Math.Max(from, to), _score.End);
            if (span != unit.Span && span != _selection?.Span)
            {
                await ChangePhrasesAsync(levels.MakeUnit(_level, span), span);
            }

            return;
        }

        await SelectBarRangeAsync(from, to);
    }

    private void ToggleMenuMore()
    {
        _menuMore = !MenuMoreOpen;
        _menuMoreFor = _selection?.Span;
    }

    /// <summary>Make phrase: the selection becomes one unit at the level shown.</summary>
    private Task MakeUnitAsync()
    {
        _menuMore = false;
        return Levels is { } levels && _selection is { } selection ? ChangePhrasesAsync(levels.MakeUnit(_level, selection.Span), selection.Span) : Task.CompletedTask;
    }

    /// <summary>Merge with next: the boundary after the selected unit goes, and the merged unit is selected.</summary>
    private Task MergeWithNextAsync()
    {
        _menuMore = false;
        return Levels is { } levels && SelectedUnit is { } unit ? ChangePhrasesAsync(levels.JoinWithNext(_level, unit), unit.Span) : Task.CompletedTask;
    }

    /// <summary>Split here: a boundary at the playhead's bar; the first part stays selected.</summary>
    private Task SplitHereAsync()
    {
        _menuMore = false;
        return Levels is { } levels && SelectedUnit is { } unit && SplitPoint is { } at ? ChangePhrasesAsync(levels.SplitAt(_level, at), unit.Span) : Task.CompletedTask;
    }

    /// <summary>The piano roll from the menu: opened (or put away) with the selection kept.</summary>
    private void MenuRoll()
    {
        _menuMore = false;
        ToggleRoll();
    }

    /// <summary>Insights from the menu: the sheet opens on the selection.</summary>
    private void MenuInsights()
    {
        _menuMore = false;
        ToggleInsights();
    }
}