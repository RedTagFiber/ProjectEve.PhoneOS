namespace ProjectEve.PhoneOS.Services;

public class PhoneSessionService
{
    private readonly Dictionary<string, PhoneSession> _sessions = new();
    private readonly object _lock = new();

    public PhoneSession GetOrCreate(string browserId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(browserId, out var existing))
                return existing;

            var created = new PhoneSession();
            _sessions[browserId] = created;
            return created;
        }
    }
}