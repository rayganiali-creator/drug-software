using MedSmarter.BuildingBlocks;

namespace MedSmarter.Modules.Identity;

/// <summary>Per-identifier failed-login limiter (in memory; a shared store replaces it when the API scales out).</summary>
public sealed class LoginThrottle(IClock clock)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _failures = [];

    public bool IsLocked(string key, int max, TimeSpan window)
    {
        lock (_gate)
        {
            if (!_failures.TryGetValue(key, out var list))
            {
                return false;
            }

            list.RemoveAll(t => clock.UtcNow - t > window);
            return list.Count >= max;
        }
    }

    public void RecordFailure(string key)
    {
        lock (_gate)
        {
            if (!_failures.TryGetValue(key, out var list))
            {
                _failures[key] = list = [];
            }

            list.Add(clock.UtcNow);
        }
    }

    public void Reset(string key)
    {
        lock (_gate)
        {
            _failures.Remove(key);
        }
    }
}
