using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The JSON adapter of the selection session (<see cref="SelectionSession"/>, audit W1.7): each call's request read into
/// typed values, the session asked, and its typed result written as the answer. The session's state is changed only by
/// the session; this file holds no state.
/// </summary>
internal sealed partial class OpenPiece
{
    internal static partial class Calls
    {
        /// <summary>The selection and what is understood about it: the Try chips, the variation chain and Piece DNA.</summary>
        public static readonly FacadeCall<OpenPiece>[] Selection =
        [
            new("select", static (piece, request) => piece.Select(request), CallFlags.Showing | CallFlags.OpenFirst),
            new("understand", static (piece, _) => piece.Understand(), CallFlags.OpenFirst),
            new("circle", static (piece, _) => piece.Circle(), CallFlags.OpenFirst),
            new("try", static (piece, request) => piece.ChainAnswer(piece.Selection.Try(piece.State, request.Required("chip"))), CallFlags.OpenFirst),
            new("variation", static (piece, _) => piece.ChainAnswer(piece.Selection.Variation())),
            new("variation.clear", static (piece, _) => piece.ClearVariation()),
            new("dna", static (piece, request) => piece.Dna(request), CallFlags.Showing | CallFlags.OpenFirst),
        ];
    }

    /// <summary>
    /// select: "notes" [ids] ("picked": true for the iPad's selection by the place of the touch), "span" {onset, end} ("snap":
    /// "beat" takes it out to whole beats) or "bars" {first, last}, narrowed by "filter"; "empty": true selects a span or bars
    /// with no notes in them (a passage of time).
    /// </summary>
    private string Select(Request request)
    {
        SelectionFilter filter = request.Filter("filter");
        MotifSelection selection;
        MotifSelectionKind kind = MotifSelectionKind.Range;
        if (request.Has("notes"))
        {
            // "picked": true (v0.6.2, #601, the iPad's selection by the place of the touch): one note or more, within the bars.
            selection = request.Bool("picked") == true
                ? MotifSelector.SelectPicked(Notes, Piece.Meter, request.Texts("notes"))
                : MotifSelector.SelectNotes(Notes, Piece.Meter, request.Texts("notes"));
            kind = MotifSelectionKind.Notes;
        }
        else if (request.Span("span") is { } span)
        {
            // "snap": "beat" (#349, within contract 12) takes the span out to whole beats, so it can start and end mid-bar.
            TickSpan chosen = request.Text("snap") switch
            {
                null => span,
                "beat" => MotifSelector.ToBeats(Piece.Meter, span),
                _ => throw FacadeException.BadRequest("\"snap\" is \"beat\"."),
            };
            selection = MotifSelector.Select(Notes, Piece.Meter, chosen, filter);
        }
        else
        {
            Request bars = request.Object("bars");
            int first = bars.Int("first") ?? throw FacadeException.BadRequest("Send \"bars\" {first, last}, \"span\" {onset, end} or \"notes\" [ids].");
            int last = bars.Int("last") ?? first;
            selection = MotifSelector.Select(Notes, Piece.Meter, MotifSelector.Bars(Piece.Meter, first, last, Piece.End), filter);
        }

        // "empty": true (v0.6.4, #614 item 4, the iPad's selection of time): a span or bars with no notes in them (rests, a
        // hand resting, a place between notes) are selected as they are; the calls that read notes then answer NoNotes.
        if (selection.NoteIds.Count == 0 && (kind == MotifSelectionKind.Notes || request.Bool("empty") != true))
        {
            throw SelectionSession.NoNotes();
        }

        Selection.Select(State, selection, kind);
        return Answer.Write(json => SelectionAnswer(json, selection));
    }

    /// <summary>The selection as select, idea.open and the summary answer it: its passage, the key in force there and its bars.</summary>
    private void SelectionAnswer(Utf8JsonWriter json, MotifSelection selection) =>
        Answer.Selection(json, "selection", selection, KeySignature.InForce(Piece.Keys, selection.Span.Onset), Piece.Meter);

    private string Understand()
    {
        SelectionUnderstanding understood = Selection.Understand(State);
        return Answer.Write(json => Answer.Understanding(json, understood.Context, understood.SentenceFacts, understood.Chips));
    }

    private string Circle()
    {
        SelectionCircle circle = Selection.Circle(State);
        return Answer.Write(json => Answer.Circle(json, circle.View, circle.At, Piece.End));
    }

    /// <summary>dna: "ideas": "musical" for the musical ideas.</summary>
    private string Dna(Request request)
    {
        PieceDna dna = Selection.Dna(State, musicalIdeas: request.Text("ideas") == "musical");
        return Answer.Write(json => Answer.Dna(json, dna, Words, Piece.End, Piece.Meter));
    }

    private string ClearVariation()
    {
        Selection.Clear();
        return Answer.Write(json => json.WriteBoolean("cleared", true));
    }

    /// <summary>try and variation: the chain with the chip that started it.</summary>
    private string ChainAnswer(VariationChain chain) => Answer.Write(json =>
    {
        Answer.Chain(json, chain, Words);
        // No call starts an experiment since contract 10; the field stays so that "try" and "variation" answer as before.
        json.WriteNull("experiment");

        json.WriteString("chip", Selection.Chip?.Id);
    });
}