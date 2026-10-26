using System.Globalization;
using System.Text.Json.Serialization;

namespace IntakeLog.Core;

/// <summary>Names for the independently synced pieces of data.</summary>
public static class SyncKeys
{
    public const string Profile = "profile";
    public const string Foods = "foods";
    private const string DayPrefix = "day_";

    public static string Day(DateOnly date) => DayPrefix + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool TryParseDay(string key, out DateOnly date)
    {
        date = default;
        return key.StartsWith(DayPrefix, StringComparison.Ordinal)
               && DateOnly.TryParseExact(key[DayPrefix.Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}

/// <summary>Local-only sync bookkeeping, stored with the app data on each device.</summary>
public sealed class SyncState
{
    /// <summary>Server time of the last successful download; null if this device has never synced.</summary>
    public DateTimeOffset? LastPulledAt { get; set; }

    /// <summary>Keys changed on this device and not yet uploaded.</summary>
    public HashSet<string> Pending { get; set; } = [];
}

public sealed class FoodsDoc
{
    public List<FoodItem> Items { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>What travels between device and server, in both directions.</summary>
public sealed class SyncPayload
{
    /// <summary>Set by the server on downloads: pass it back as "since" next time.</summary>
    public DateTimeOffset? ServerTime { get; set; }
    public Profile? Profile { get; set; }
    public FoodsDoc? Foods { get; set; }
    public List<DayLog> Days { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Profile is null && Foods is null && Days.Count == 0;
}

/// <summary>
/// Last-writer-wins sync. Each day, the profile and the saved-food list carry their own
/// UpdatedAt stamp; the newer copy wins wherever two copies meet.
/// </summary>
public static class SyncMerge
{
    public static bool IsNewer(DateTimeOffset incoming, DateTimeOffset? existing) =>
        existing is null || incoming > existing.Value;

    /// <summary>
    /// Before a device's first sync, queue what it already holds so nothing is lost.
    /// Untouched defaults (an unconfirmed profile, an empty food list) are not queued,
    /// so a fresh install never overwrites real data from another device.
    /// </summary>
    public static void PrepareFirstSync(AppData data, DateTimeOffset now)
    {
        if (data.Sync.LastPulledAt is not null) return;

        foreach (var day in data.Days)
        {
            if (day.UpdatedAt == default) day.UpdatedAt = now;
            data.Sync.Pending.Add(SyncKeys.Day(day.Date));
        }
        if (data.SavedFoods.Count > 0)
        {
            if (data.SavedFoodsUpdatedAt == default) data.SavedFoodsUpdatedAt = now;
            data.Sync.Pending.Add(SyncKeys.Foods);
        }
        if (data.Profile.Confirmed)
        {
            if (data.Profile.UpdatedAt == default) data.Profile.UpdatedAt = now;
            data.Sync.Pending.Add(SyncKeys.Profile);
        }
    }

    /// <summary>Everything this device has changed and not yet uploaded.</summary>
    public static SyncPayload BuildPush(AppData data)
    {
        var p = new SyncPayload();
        foreach (var key in data.Sync.Pending)
        {
            if (key == SyncKeys.Profile) p.Profile = data.Profile;
            else if (key == SyncKeys.Foods) p.Foods = new FoodsDoc { Items = [.. data.SavedFoods], UpdatedAt = data.SavedFoodsUpdatedAt };
            else if (SyncKeys.TryParseDay(key, out var date) && data.Find(date) is { } day) p.Days.Add(day);
        }
        return p;
    }

    /// <summary>
    /// After an upload succeeds, clear the keys that were sent, unless the item
    /// changed again while the upload was in flight (its stamp moved on).
    /// </summary>
    public static void MarkPushed(AppData data, IReadOnlyDictionary<string, DateTimeOffset> sentStamps)
    {
        foreach (var (key, stamp) in sentStamps)
            if (CurrentStamp(data, key) == stamp) data.Sync.Pending.Remove(key);
    }

    public static Dictionary<string, DateTimeOffset> Stamps(SyncPayload p)
    {
        var d = new Dictionary<string, DateTimeOffset>();
        if (p.Profile is not null) d[SyncKeys.Profile] = p.Profile.UpdatedAt;
        if (p.Foods is not null) d[SyncKeys.Foods] = p.Foods.UpdatedAt;
        foreach (var day in p.Days) d[SyncKeys.Day(day.Date)] = day.UpdatedAt;
        return d;
    }

    /// <summary>Take every downloaded item that is newer than this device's copy. Returns how many changed.</summary>
    public static int ApplyRemote(AppData data, SyncPayload remote)
    {
        var changed = 0;

        if (remote.Profile is { } rp && rp.UpdatedAt > data.Profile.UpdatedAt)
        {
            data.Profile = rp;
            data.Sync.Pending.Remove(SyncKeys.Profile);
            changed++;
        }

        if (remote.Foods is { } rf && rf.UpdatedAt > data.SavedFoodsUpdatedAt)
        {
            data.SavedFoods = [.. rf.Items];
            data.SavedFoodsUpdatedAt = rf.UpdatedAt;
            data.Sync.Pending.Remove(SyncKeys.Foods);
            changed++;
        }

        foreach (var rd in remote.Days)
        {
            var local = data.Find(rd.Date);
            if (local is not null && rd.UpdatedAt <= local.UpdatedAt) continue;
            if (local is not null) data.Days.Remove(local);
            data.Days.Add(rd);
            data.Sync.Pending.Remove(SyncKeys.Day(rd.Date));
            changed++;
        }

        if (remote.ServerTime is { } t) data.Sync.LastPulledAt = t;
        return changed;
    }

    private static DateTimeOffset? CurrentStamp(AppData data, string key) =>
        key == SyncKeys.Profile ? data.Profile.UpdatedAt
        : key == SyncKeys.Foods ? data.SavedFoodsUpdatedAt
        : SyncKeys.TryParseDay(key, out var date) ? data.Find(date)?.UpdatedAt
        : null;
}
