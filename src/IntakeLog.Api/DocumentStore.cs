using System.Collections.Concurrent;
using Azure;
using Azure.Data.Tables;

namespace IntakeLog.Api;

/// <summary>
/// One synced item (a day, the profile, or the saved-food list) as stored for one user.
/// <paramref name="UpdatedAt"/> is the device's edit time and decides which copy wins;
/// <paramref name="WrittenAt"/> is the server's write time and drives "what's new since".
/// </summary>
public sealed record StoredDoc(string Key, string Json, DateTimeOffset UpdatedAt, DateTimeOffset WrittenAt);

public interface IDocumentStore
{
    /// <summary>All of a user's items, or only those written at or after <paramref name="writtenSince"/>.</summary>
    Task<IReadOnlyList<StoredDoc>> ListAsync(string userKey, DateTimeOffset? writtenSince, CancellationToken ct);

    Task UpsertAsync(string userKey, IReadOnlyList<StoredDoc> docs, CancellationToken ct);
}

/// <summary>Azure Table Storage: one table, PartitionKey = user, RowKey = item key.</summary>
public sealed class TableDocumentStore(TableServiceClient service, string tableName = "IntakeLog") : IDocumentStore
{
    private readonly TableClient table = service.GetTableClient(tableName);
    private readonly SemaphoreSlim initLock = new(1, 1);
    private bool initialized;

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (initialized) return;
        await initLock.WaitAsync(ct);
        try
        {
            if (!initialized)
            {
                await table.CreateIfNotExistsAsync(ct);
                initialized = true;
            }
        }
        finally { initLock.Release(); }
    }

    public async Task<IReadOnlyList<StoredDoc>> ListAsync(string userKey, DateTimeOffset? writtenSince, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        var filter = writtenSince is { } since
            ? TableClient.CreateQueryFilter($"PartitionKey eq {userKey} and WrittenAt ge {since}")
            : TableClient.CreateQueryFilter($"PartitionKey eq {userKey}");

        var list = new List<StoredDoc>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter, cancellationToken: ct))
        {
            list.Add(new StoredDoc(
                e.RowKey,
                e.GetString("Json") ?? "",
                e.GetDateTimeOffset("UpdatedAt") ?? default,
                e.GetDateTimeOffset("WrittenAt") ?? default));
        }
        return list;
    }

    public async Task UpsertAsync(string userKey, IReadOnlyList<StoredDoc> docs, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        // Entity-group transactions: up to 100 operations, all in one partition.
        foreach (var chunk in docs.Chunk(100))
        {
            var actions = chunk.Select(d => new TableTransactionAction(TableTransactionActionType.UpsertReplace,
                new TableEntity(userKey, d.Key)
                {
                    ["Json"] = d.Json,
                    ["UpdatedAt"] = d.UpdatedAt,
                    ["WrittenAt"] = d.WrittenAt
                }));
            try
            {
                await table.SubmitTransactionAsync(actions, ct);
            }
            catch (TableTransactionFailedException e)
            {
                throw new RequestFailedException(e.Status, $"Saving to table storage failed: {e.Message}", e);
            }
        }
    }
}

/// <summary>In-memory store for tests and for running the API without storage.</summary>
public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<(string User, string Key), StoredDoc> docs = new();

    public Task<IReadOnlyList<StoredDoc>> ListAsync(string userKey, DateTimeOffset? writtenSince, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredDoc>>(docs
            .Where(kv => kv.Key.User == userKey && (writtenSince is null || kv.Value.WrittenAt >= writtenSince))
            .Select(kv => kv.Value).ToList());

    public Task UpsertAsync(string userKey, IReadOnlyList<StoredDoc> items, CancellationToken ct)
    {
        foreach (var d in items) docs[(userKey, d.Key)] = d;
        return Task.CompletedTask;
    }
}
