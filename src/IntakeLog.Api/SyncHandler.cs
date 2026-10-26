using IntakeLog.Core;

namespace IntakeLog.Api;

public sealed class PayloadTooLargeException(string message) : Exception(message);

/// <summary>Server-side sync rules, independent of HTTP and of the storage backend.</summary>
public sealed class SyncHandler(IDocumentStore store, TimeProvider clock)
{
    /// <summary>Downloads overlap the previous one by this much, so a write that lands
    /// mid-request is never skipped. Merging the same item twice is harmless.</summary>
    public static readonly TimeSpan PullOverlap = TimeSpan.FromMinutes(2);

    public const int MaxDaysPerPush = 500;

    // Table Storage string properties hold up to 64 KiB (32K UTF-16 characters).
    private const int MaxItemChars = 30_000;

    public async Task<SyncPayload> PullAsync(string userKey, DateTimeOffset? since, CancellationToken ct)
    {
        var serverTime = clock.GetUtcNow(); // taken before reading, so nothing written during the read is missed next time
        var docs = await store.ListAsync(userKey, since - PullOverlap, ct);

        var payload = new SyncPayload { ServerTime = serverTime };
        foreach (var d in docs)
        {
            if (d.Key == SyncKeys.Profile) payload.Profile = AppDataJson.DeserializeProfile(d.Json);
            else if (d.Key == SyncKeys.Foods) payload.Foods = AppDataJson.DeserializeFoods(d.Json);
            else if (SyncKeys.TryParseDay(d.Key, out _) && AppDataJson.DeserializeDay(d.Json) is { } day) payload.Days.Add(day);
        }
        payload.Days.Sort((a, b) => a.Date.CompareTo(b.Date));
        return payload;
    }

    /// <summary>Stores every incoming item that is newer than the stored copy. Returns how many were stored.</summary>
    public async Task<int> PushAsync(string userKey, SyncPayload incoming, CancellationToken ct)
    {
        if (incoming.Days.Count > MaxDaysPerPush)
            throw new PayloadTooLargeException($"Send at most {MaxDaysPerPush} days per request.");

        var now = clock.GetUtcNow();
        var candidates = new List<StoredDoc>();
        if (incoming.Profile is { } p)
            candidates.Add(new(SyncKeys.Profile, AppDataJson.SerializeProfile(p), p.UpdatedAt, now));
        if (incoming.Foods is { } f)
            candidates.Add(new(SyncKeys.Foods, AppDataJson.SerializeFoods(f), f.UpdatedAt, now));
        foreach (var day in incoming.Days)
            candidates.Add(new(SyncKeys.Day(day.Date), AppDataJson.SerializeDay(day), day.UpdatedAt, now));

        if (candidates.FirstOrDefault(c => c.Json.Length > MaxItemChars) is { } big)
            throw new PayloadTooLargeException($"'{big.Key}' is too large to store.");

        // Duplicate keys in one request: keep the newest.
        candidates = candidates.GroupBy(c => c.Key).Select(g => g.MaxBy(c => c.UpdatedAt)!).ToList();
        if (candidates.Count == 0) return 0;

        var existing = (await store.ListAsync(userKey, null, ct)).ToDictionary(d => d.Key, d => d.UpdatedAt);
        var toWrite = candidates
            .Where(c => SyncMerge.IsNewer(c.UpdatedAt, existing.TryGetValue(c.Key, out var t) ? t : null))
            .ToList();

        if (toWrite.Count > 0) await store.UpsertAsync(userKey, toWrite, ct);
        return toWrite.Count;
    }
}
