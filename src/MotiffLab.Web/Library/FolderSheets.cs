namespace MotiffLab.Web.Library;

/// <summary>
/// Which folder sheet is open over the library (version 0.5): «Что изменилось в папке» from «Проверить папку» in the library's
/// menu. It lives here, not in the menu, so it stays open when the menu closes; the import's line hosts it.
/// </summary>
public sealed class FolderSheets
{
    public ChosenFolder? Recheck { get; private set; }

    public event Action? Changed;

    public void Open(ChosenFolder folder)
    {
        Recheck = folder ?? throw new ArgumentNullException(nameof(folder));
        Changed?.Invoke();
    }

    public void Close()
    {
        Recheck = null;
        Changed?.Invoke();
    }
}