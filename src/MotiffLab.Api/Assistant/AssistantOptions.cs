using System.Collections.Concurrent;

namespace MotiffLab.Api.Assistant;

/// <summary>
/// Who may use the assistant and how much (0003 section 6; 0028 section 4): only the accounts on the allow-list
/// (Assistant:Allowed, their object ids, comma-separated; empty lets nobody in), with Pro, at most
/// Assistant:DailyRequests turns a day each, a model answer within Assistant:TimeoutSeconds.
/// </summary>
public sealed record AssistantOptions(IReadOnlySet<string> Allowed, int DailyRequests, TimeSpan Timeout)
{
    public const int DefaultDailyRequests = 200;

    public const int DefaultTimeoutSeconds = 30;

    public static AssistantOptions From(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var allowed = (config["Assistant:Allowed"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        int daily = int.TryParse(config["Assistant:DailyRequests"], out int requests) && requests >= 0 ? requests : DefaultDailyRequests;
        int seconds = int.TryParse(config["Assistant:TimeoutSeconds"], out int timeout) && timeout > 0 ? timeout : DefaultTimeoutSeconds;
        return new AssistantOptions(allowed, daily, TimeSpan.FromSeconds(seconds));
    }
}

/// <summary>
/// The turns each player took today (UTC), counted in this instance's memory: with at most two instances and an
/// allow-list of one, a cap per instance is close enough, and nothing about a player is stored.
/// </summary>
public sealed class AssistantUsage(TimeProvider clock)
{
    private readonly ConcurrentDictionary<(string Player, DateOnly Day), int> _turns = new();

    /// <summary>Counts a turn; false, without counting, when the player has already taken <paramref name="cap"/> today.</summary>
    public bool TryTake(string player, int cap)
    {
        var key = (player, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        foreach (var old in _turns.Keys.Where(old => old.Day < key.Item2))
        {
            _turns.TryRemove(old, out _);
        }

        while (true)
        {
            int taken = _turns.GetOrAdd(key, 0);
            if (taken >= cap)
            {
                return false;
            }

            if (_turns.TryUpdate(key, taken + 1, taken))
            {
                return true;
            }
        }
    }

    /// <summary>How many turns the player has left today.</summary>
    public int Left(string player, int cap) => Math.Max(0, cap - _turns.GetValueOrDefault((player, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))));
}