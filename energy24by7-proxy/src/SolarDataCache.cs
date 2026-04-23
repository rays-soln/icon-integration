namespace Energy24by7Proxy;

public class SolarDataCache
{
    private readonly object _lock = new();
    private SolarApiResponse? _today;
    private SolarApiResponse? _monthly;
    private DateTime?         _lastUpdated;
    private string?           _error;

    public void Update(SolarApiResponse today, SolarApiResponse monthly)
    {
        lock (_lock)
        {
            _today       = today;
            _monthly     = monthly;
            _lastUpdated = DateTime.UtcNow;
            _error       = null;
        }
    }

    public void SetError(string error)
    {
        lock (_lock) { _error = error; }
    }

    public CacheSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new CacheSnapshot
            {
                Today       = _today,
                Monthly     = _monthly,
                LastUpdated = _lastUpdated,
                Error       = _error
            };
        }
    }
}
