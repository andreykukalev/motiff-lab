namespace MotiffLab.MusicCore.Accounts;

/// <summary>
/// The name an app shows for a signed-in player, chosen from the sign-in's claims the same way in the web app and the iPad
/// app (Account.swift): name, then email (External ID may send <c>emails</c>), then preferred_username, then given and
/// family name, then the account's user name. External ID names a player who gave no name at sign-up "unknown", which is
/// no name, so it is passed over wherever it appears.
/// </summary>
public static class PlayerName
{
    private const string Unknown = "unknown";

    public static string Of(IEnumerable<(string Type, string Value)> claims, string? userName = null)
    {
        ArgumentNullException.ThrowIfNull(claims);
        List<(string Type, string Value)> all = [.. claims];
        string? Text(string type) => all.Where(claim => claim.Type == type).Select(claim => Usable(claim.Value)).FirstOrDefault(value => value is not null);

        string given = string.Join(" ", new[] { Text("given_name"), Text("family_name") }.OfType<string>());
        return Text("name") ?? Text("email") ?? Text("emails") ?? Text("preferred_username")
            ?? (given.Length > 0 ? given : null)
            ?? Usable(userName)
            ?? "";
    }

    private static string? Usable(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Equals(Unknown, StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }
}