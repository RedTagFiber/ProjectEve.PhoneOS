using System.Collections.Concurrent;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Short-lived UX signal: is this player actively viewing this text thread?
/// This affects real-player pacing only. NPCs must never know this value.
/// </summary>
public sealed class PhoneThreadPresenceService
{
    private readonly ConcurrentDictionary<string, DateTime> _activeUntilUtc = new();

    public void Touch(string playerId, int npcId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || npcId <= 0)
            return;

        _activeUntilUtc[Key(playerId, npcId)] = DateTime.UtcNow.AddSeconds(8);
    }

    public void Clear(string playerId, int npcId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || npcId <= 0)
            return;

        _activeUntilUtc.TryRemove(Key(playerId, npcId), out _);
    }

    public bool IsActive(string playerId, int npcId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || npcId <= 0)
            return false;

        var key = Key(playerId, npcId);
        if (!_activeUntilUtc.TryGetValue(key, out var until))
            return false;

        if (until <= DateTime.UtcNow)
        {
            _activeUntilUtc.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    private static string Key(string playerId, int npcId)
        => playerId.Trim() + ":" + npcId;
}
