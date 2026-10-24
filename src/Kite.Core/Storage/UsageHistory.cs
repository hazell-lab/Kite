namespace Kite.Core.Storage;

/// <summary>how often and when one result was used.</summary>
public sealed class UsageRecord
{
    public int Count { get; set; }

    public DateTime LastUsedUtc { get; set; }
}

/// <summary>the saved form of the launch history.</summary>
public sealed class UsageHistoryData
{
    public Dictionary<string, UsageRecord> Launches { get; set; } = [];

    /// <summary>for each typed query, which results were picked and how often.</summary>
    public Dictionary<string, Dictionary<string, int>> QueryPicks { get; set; } = [];
}

/// <summary>
/// remembers which results the user opens, so they can be ranked higher next time.
/// safe to use from several threads.
/// </summary>
public sealed class UsageHistory
{
    /// <summary>the most a history boost can add to a score.</summary>
    public const int MaxBoost = 90;

    private const int MaxEntries = 2000;
    private const int MaxQueries = 500;

    private readonly object _gate = new();
    private UsageHistoryData _data;

    public UsageHistory(UsageHistoryData? data = null)
    {
        _data = data ?? new UsageHistoryData();
        _data.Launches ??= [];
        _data.QueryPicks ??= [];
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _data.Launches.Count;
            }
        }
    }

    public void RecordLaunch(string resultId, string query, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_data.Launches.TryGetValue(resultId, out var record))
            {
                record = new UsageRecord();
                _data.Launches[resultId] = record;
            }

            record.Count++;
            record.LastUsedUtc = nowUtc;

            var key = NormalizeQuery(query);
            if (key.Length > 0)
            {
                if (!_data.QueryPicks.TryGetValue(key, out var picks))
                {
                    picks = [];
                    _data.QueryPicks[key] = picks;
                }

                picks[resultId] = picks.GetValueOrDefault(resultId) + 1;
            }

            Trim();
        }
    }

    /// <summary>
    /// extra points for a result based on past use. it is kept small so an
    /// exact name match always stays above a partial match.
    /// </summary>
    public int GetBoost(string resultId, string query, DateTime nowUtc)
    {
        lock (_gate)
        {
            double boost = 0;
            if (_data.Launches.TryGetValue(resultId, out var record))
            {
                boost += Math.Min(40, 12 * Math.Log2(1 + record.Count));

                var age = nowUtc - record.LastUsedUtc;
                if (age < TimeSpan.FromDays(1))
                {
                    boost += 20;
                }
                else if (age < TimeSpan.FromDays(7))
                {
                    boost += 10;
                }
            }

            var key = NormalizeQuery(query);
            if (key.Length > 0 &&
                _data.QueryPicks.TryGetValue(key, out var picks) &&
                picks.TryGetValue(resultId, out var pickCount))
            {
                boost += Math.Min(30, 15 * pickCount);
            }

            return (int)Math.Min(MaxBoost, Math.Round(boost));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _data = new UsageHistoryData();
        }
    }

    /// <summary>a copy of the data that is safe to save.</summary>
    public UsageHistoryData Snapshot()
    {
        lock (_gate)
        {
            return new UsageHistoryData
            {
                Launches = _data.Launches.ToDictionary(
                    p => p.Key,
                    p => new UsageRecord { Count = p.Value.Count, LastUsedUtc = p.Value.LastUsedUtc }),
                QueryPicks = _data.QueryPicks.ToDictionary(p => p.Key, p => new Dictionary<string, int>(p.Value)),
            };
        }
    }

    private static string NormalizeQuery(string query) => Search.FuzzyMatcher.Normalize(query);

    private void Trim()
    {
        // drop the oldest entries so the file does not grow forever
        if (_data.Launches.Count > MaxEntries)
        {
            foreach (var old in _data.Launches.OrderBy(p => p.Value.LastUsedUtc).Take(_data.Launches.Count - MaxEntries).ToList())
            {
                _data.Launches.Remove(old.Key);
            }
        }

        if (_data.QueryPicks.Count > MaxQueries)
        {
            foreach (var old in _data.QueryPicks.OrderBy(p => p.Value.Values.Sum()).Take(_data.QueryPicks.Count - MaxQueries).ToList())
            {
                _data.QueryPicks.Remove(old.Key);
            }
        }
    }
}
