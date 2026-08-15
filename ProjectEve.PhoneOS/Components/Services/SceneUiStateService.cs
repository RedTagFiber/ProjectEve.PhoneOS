namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Presentation-only projection of what the current player can perceive in the
/// active scene. It is NOT world truth and must never reveal hidden NPCs.
/// Future scene/orchestrator code should feed this service from real perception.
/// </summary>
public sealed class SceneUiStateService
{
    private readonly object _gate = new();
    private string _locationName = "Unknown location";
    private string _locationId = "";
    private List<PerceivedPresenceRow> _visible = new();

    public event Action? Changed;

    public string LocationName
    {
        get { lock (_gate) return _locationName; }
    }

    public string LocationId
    {
        get { lock (_gate) return _locationId; }
    }

    public IReadOnlyList<PerceivedPresenceRow> VisiblePeople
    {
        get
        {
            lock (_gate)
                return _visible.Select(Clone).ToList();
        }
    }

    public void SetScene(
        string locationId,
        string locationName,
        IEnumerable<PerceivedPresenceRow> visiblePeople)
    {
        lock (_gate)
        {
            _locationId = locationId?.Trim() ?? "";
            _locationName = string.IsNullOrWhiteSpace(locationName)
                ? "Unknown location"
                : locationName.Trim();
            _visible = visiblePeople
                .Where(x => x.IsKnownToPlayer)
                .OrderBy(x => x.DistanceFeet)
                .Take(12) // practical active-scene UI cap: 10 NPCs + 2 players
                .Select(Clone)
                .ToList();
        }

        try { Changed?.Invoke(); } catch { }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _locationId = "";
            _locationName = "Unknown location";
            _visible.Clear();
        }

        try { Changed?.Invoke(); } catch { }
    }

    private static PerceivedPresenceRow Clone(PerceivedPresenceRow x)
        => new()
        {
            CharacterId = x.CharacterId,
            DisplayName = x.DisplayName,
            DistanceFeet = x.DistanceFeet,
            IsPlayer = x.IsPlayer,
            IsLocalPlayer = x.IsLocalPlayer,
            IsKnownToPlayer = x.IsKnownToPlayer,
            Note = x.Note
        };
}

public sealed class PerceivedPresenceRow
{
    public int? CharacterId { get; set; }
    public string DisplayName { get; set; } = "";
    public double DistanceFeet { get; set; }
    public bool IsPlayer { get; set; }
    public bool IsLocalPlayer { get; set; }
    public bool IsKnownToPlayer { get; set; } = true;
    public string Note { get; set; } = "";
}
