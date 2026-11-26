namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// One call by name on one kind of receiver (<typeparamref name="TOn"/>): what answers it and its flags. Each feature lists
/// its calls beside its handlers (an open piece's in <see cref="OpenPiece.Calls"/>); <see cref="CallRegistry"/> gathers
/// them, the one table the facade dispatches by.
/// </summary>
internal sealed record FacadeCall<TOn>(string Name, Func<TOn, Request, string> Run, CallFlags Flags = CallFlags.None)
{
    /// <summary>The same call answered on a <typeparamref name="TOuter"/>, through the <typeparamref name="TOn"/> that <paramref name="inner"/> gives for it.</summary>
    public FacadeCall<TOuter> On<TOuter>(Func<TOuter, Request, TOn> inner) => new(Name, (outer, request) => Run(inner(outer, request), request), Flags);
}

/// <summary>When a call is answered beyond where it is registered (docs/facade-contract.md, «Flags»).</summary>
[Flags]
internal enum CallFlags
{
    None = 0,

    /// <summary>Answered on Notes, a version or a draft open only to be shown; any other call there answers "readOnly".</summary>
    Showing = 1,

    /// <summary>Answered while the corrections are off; any other name starting "corrections" then answers "unavailable".</summary>
    WhileCorrectionsOff = 2,

    /// <summary>On a piece without notes, answered only when it keeps a PDF; on a piece that is only its recording it answers "noNotes".</summary>
    NeedsPdf = 4,

    /// <summary>
    /// A name without a dot that piece 0 answers "noPiece" for while nothing is open, as it does every name with a dot but
    /// settings.* and reference.*; any other name answers "unknownCall" there (<see cref="CallRegistry.OpensFirst"/>).
    /// </summary>
    OpenFirst = 8,
}

/// <summary>A call on an open piece with notes: the facade, the piece's number and the piece.</summary>
internal readonly record struct PieceCall(MusicCoreFacade Facade, int Number, OpenPiece Piece);

/// <summary>A drafts call: the facade, the piece it was sent to, and the piece whose drafts it works on with its number.</summary>
internal readonly record struct DraftCall(MusicCoreFacade Facade, OpenPiece Open, int OwnerNumber, OpenPiece Owner);