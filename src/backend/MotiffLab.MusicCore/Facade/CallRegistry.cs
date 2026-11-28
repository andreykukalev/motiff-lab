using System.Collections.Frozen;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The facade's calls by name (audit W1.6): every call, what answers it on each kind of receiver and its flags, gathered
/// from the lists the features keep beside their handlers. <see cref="MusicCoreFacade.Call"/> dispatches by it alone;
/// tests/contracts/facade-calls.json and docs/facade-contract.md are checked against it. A static table of delegates, so the
/// NativeAOT build keeps it as it is (no reflection).
/// </summary>
internal static class CallRegistry
{
    /// <summary>The calls on piece 0, with or without a piece open.</summary>
    public static readonly FrozenDictionary<string, FacadeCall<MusicCoreFacade>> WithoutPiece = Index([.. MusicCoreFacade.CallsWithoutPiece, .. ReferenceRequest.Calls, .. PlaylistsRequest.Calls]);

    /// <summary>The calls on an open piece with notes, drafts.* apart (<see cref="OnDrafts"/>).</summary>
    public static readonly FrozenDictionary<string, FacadeCall<PieceCall>> OnPiece = Index(
    [
        .. MusicCoreFacade.CallsOnPiece,
        .. Lifted([.. OpenPiece.Calls.Piece, .. OpenPiece.Calls.Selection, .. OpenPiece.Calls.Project, .. OpenPiece.Calls.Marks, .. OpenPiece.Calls.Performances, .. OpenPiece.Calls.Sources, .. OpenPiece.Calls.Corrections], static (PieceCall call, Request _) => call.Piece),
        .. Lifted(SourceRequest.Calls, static (PieceCall call, Request _) => call.Piece.HeldSources),
        .. Lifted(AssistantRequest.Calls, static (PieceCall call, Request _) => call.Piece.Assistant),
    ]);

    /// <summary>The drafts' calls on an open piece, answered for the piece whose drafts they are (<see cref="DraftCall.Owner"/>).</summary>
    public static readonly FrozenDictionary<string, FacadeCall<DraftCall>> OnDrafts = Index(MusicCoreFacade.DraftCalls);

    /// <summary>The calls on an open piece without notes: a PDF piece, or one that is only its recording.</summary>
    public static readonly FrozenDictionary<string, FacadeCall<OpenDocument>> OnDocument = Index(
        [.. OpenDocument.Calls, .. Lifted(SourceRequest.Calls, static (OpenDocument document, Request request) => document.HeldSources(request))]);

    /// <summary>
    /// Whether piece 0 answers "noPiece" for <paramref name="name"/> while nothing is open: a name with a dot but settings.*
    /// and reference.*, or one flagged <see cref="CallFlags.OpenFirst"/>.
    /// </summary>
    public static bool OpensFirst(string name) => name.Contains('.', StringComparison.Ordinal)
        ? !name.StartsWith("settings.", StringComparison.Ordinal) && !name.StartsWith("reference.", StringComparison.Ordinal)
        : OnPiece.TryGetValue(name, out FacadeCall<PieceCall>? call) && call.Flags.HasFlag(CallFlags.OpenFirst);

    private static IEnumerable<FacadeCall<TOuter>> Lifted<TOn, TOuter>(IEnumerable<FacadeCall<TOn>> calls, Func<TOuter, Request, TOn> inner) =>
        calls.Select(call => call.On(inner));

    /// <summary>The calls by name; a name listed twice for one receiver stops the facade at its first call (and every test).</summary>
    private static FrozenDictionary<string, FacadeCall<T>> Index<T>(IEnumerable<FacadeCall<T>> calls) =>
        calls.ToDictionary(static call => call.Name, StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);
}