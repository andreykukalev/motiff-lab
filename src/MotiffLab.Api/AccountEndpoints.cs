using System.Text.Json;

using MotiffLab.MusicCore.Accounts;

namespace MotiffLab.Api;

/// <summary>
/// The player's plan, so an app shows what Pro allows (redesign preparation item 14). The servers check it on every
/// request themselves; this answer only tells the app what to offer.
/// </summary>
internal static class AccountEndpoints
{
    public static void MapAccount(this IEndpointRouteBuilder app) =>
        app.MapGet("/account", (HttpContext http) => Results.Text(Answer(Player.Plan(http)), "application/json"))
            .RequireAuthorization(Player.Policy);

    /// <summary><c>{"plan":"pro","tester":true,"chosen":"free"|null,"features":{"scan":true,…}}</c></summary>
    internal static string Answer(PlanState state)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("plan", Plans.Id(state.Plan));
            json.WriteBoolean("tester", state.IsTester);
            if (state.Chosen is { } chosen)
            {
                json.WriteString("chosen", Plans.Id(chosen));
            }
            else
            {
                json.WriteNull("chosen");
            }

            json.WriteStartObject("features");
            foreach (ProFeature feature in Plans.Features)
            {
                json.WriteBoolean(Plans.Id(feature), state.Allows(feature));
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}