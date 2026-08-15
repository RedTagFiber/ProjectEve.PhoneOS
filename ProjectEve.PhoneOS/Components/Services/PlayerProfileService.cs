using System.Text.Json;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Holds the active player profile for this Phone OS session.
/// Saves a simple JSON under EveData (or local Data) so New Game persists.
/// </summary>
public class PlayerProfileService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    private PlayerProfile? _current;
    private readonly string _savePath;

    public PlayerProfileService()
    {
        // Prefer EveData on D: when present; else app Data folder
        var eveData = @"D:\ProjectEve\EveData\player";
        var local = Path.Combine(AppContext.BaseDirectory, "Data", "player");
        var dir = Directory.Exists(@"D:\ProjectEve\EveData") ? eveData : local;
        Directory.CreateDirectory(dir);
        _savePath = Path.Combine(dir, "player_profile.json");
        TryLoad();
    }

    public PlayerProfile? Current => _current;

    public bool HasPlayer =>
        _current is { IsComplete: true } &&
        !string.IsNullOrWhiteSpace(_current.FirstName);

    public PlayerProfile StartNew()
    {
        _current = new PlayerProfile();
        return _current;
    }

    public PlayerProfile GetOrStart()
    {
        if (_current != null) return _current;
        return StartNew();
    }

    public void Save()
    {
        if (_current == null) return;
        try
        {
            var json = JsonSerializer.Serialize(_current, JsonOpts);
            File.WriteAllText(_savePath, json);
        }
        catch
        {
            // non-fatal — profile still lives in memory this session
        }
    }

    public void CompleteAndSave()
    {
        if (_current == null) return;
        _current.IsComplete = true;
        if (string.IsNullOrWhiteSpace(_current.PreferredName))
            _current.PreferredName = _current.FirstName;
        Save();
    }

    public void Clear()
    {
        _current = null;
        try
        {
            if (File.Exists(_savePath))
                File.Delete(_savePath);
        }
        catch { }
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_savePath)) return;
            var json = File.ReadAllText(_savePath);
            _current = JsonSerializer.Deserialize<PlayerProfile>(json, JsonOpts);
        }
        catch
        {
            _current = null;
        }
    }
}