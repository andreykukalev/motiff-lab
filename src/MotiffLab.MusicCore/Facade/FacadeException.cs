namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// A call the facade could not answer, with the code the client reads (0020): import, noPiece,
/// unknownCall, badRequest, noSelection, noVariation, unavailable or internal.
/// </summary>
public sealed class FacadeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static FacadeException BadRequest(string message) => new("badRequest", message);

    public static FacadeException NoSelection() => new("noSelection", "Select a passage first.");

    public static FacadeException NoVariation() => new("noVariation", "There is no variation yet.");

    public static FacadeException Unavailable(string reason) => new("unavailable", reason);
}