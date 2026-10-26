using System.Text;
using Azure.Data.Tables;
using IntakeLog.Api;
using IntakeLog.Core;

namespace IntakeLog.Api.Tests;

internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

public class ClientPrincipalTests
{
    private static string Header(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Parses_StaticWebAppsHeader()
    {
        var p = ClientPrincipal.FromHeader(Header(
            """{"identityProvider":"aad","userId":"d75b260a64504067bfc5b2905e3b8182","userDetails":"ben@example.com","userRoles":["anonymous","authenticated"]}"""));

        Assert.NotNull(p);
        Assert.Equal("aad_d75b260a64504067bfc5b2905e3b8182", p!.StorageKey);
        Assert.Equal("ben@example.com", p.UserDetails);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    public void Rejects_MissingOrGarbage(string? header) => Assert.Null(ClientPrincipal.FromHeader(header));

    [Fact]
    public void Rejects_HeaderWithoutUserId() =>
        Assert.Null(ClientPrincipal.FromHeader(Header("""{"identityProvider":"github"}""")));

    [Fact]
    public void StorageKey_StripsUnsafeCharacters()
    {
        var p = new ClientPrincipal("git/hub", "a#b?c", "x");
        Assert.Equal("github_abc", p.StorageKey);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("ben@example.com", true)]
    [InlineData("other@example.com, BEN@example.com", true)]
    [InlineData("someone@example.com", false)]
    public void Allowlist(string? allowed, bool expected) =>
        Assert.Equal(expected, new ClientPrincipal("aad", "1", "ben@example.com").IsAllowed(allowed));
}

public class SyncHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly D1 = new(2026, 10, 26);

    private static DayLog Day(DateOnly d, DateTimeOffset stamp, double kcal) =>
        new() { Date = d, UpdatedAt = stamp, Foods = [new FoodEntry { Name = "x", Kcal = kcal }] };

    [Fact]
    public async Task Push_ThenPull_ReturnsEverything()
    {
        var handler = new SyncHandler(new InMemoryDocumentStore(), new ManualClock(T0));
        var stored = await handler.PushAsync("u1", new SyncPayload
        {
            Profile = new Profile { GoalWeightKg = 55, UpdatedAt = T0 },
            Foods = new FoodsDoc { Items = [new FoodItem("Kopi-O kosong", 5, 0)], UpdatedAt = T0 },
            Days = [Day(D1, T0, 600)]
        }, default);

        var pulled = await handler.PullAsync("u1", null, default);

        Assert.Equal(3, stored);
        Assert.Equal(55, pulled.Profile!.GoalWeightKg);
        Assert.Single(pulled.Foods!.Items);
        Assert.Equal(600, pulled.Days.Single().Foods[0].Kcal);
        Assert.Equal(T0, pulled.ServerTime);
    }

    [Fact]
    public async Task Push_OlderCopy_DoesNotOverwriteNewer()
    {
        var handler = new SyncHandler(new InMemoryDocumentStore(), new ManualClock(T0));
        await handler.PushAsync("u1", new SyncPayload { Days = [Day(D1, T0.AddMinutes(10), 500)] }, default);

        var stored = await handler.PushAsync("u1", new SyncPayload { Days = [Day(D1, T0.AddMinutes(5), 999)] }, default);

        Assert.Equal(0, stored);
        Assert.Equal(500, (await handler.PullAsync("u1", null, default)).Days[0].Foods[0].Kcal);
    }

    [Fact]
    public async Task Pull_Since_UsesServerWriteTime_NotDeviceEditTime()
    {
        // A day edited offline at 08:00 but uploaded at 12:00 must still reach a device that last pulled at 10:00.
        var clock = new ManualClock(T0.AddHours(4));
        var handler = new SyncHandler(new InMemoryDocumentStore(), clock);
        await handler.PushAsync("u1", new SyncPayload { Days = [Day(D1, T0, 400)] }, default);

        var pulled = await handler.PullAsync("u1", T0.AddHours(2), default);

        Assert.Single(pulled.Days);
    }

    [Fact]
    public async Task Pull_Since_SkipsItemsWrittenEarlier()
    {
        var clock = new ManualClock(T0);
        var handler = new SyncHandler(new InMemoryDocumentStore(), clock);
        await handler.PushAsync("u1", new SyncPayload { Days = [Day(D1, T0, 400)] }, default);

        clock.Now = T0.AddHours(1);
        var pulled = await handler.PullAsync("u1", T0.AddMinutes(30), default);

        Assert.Empty(pulled.Days);
    }

    [Fact]
    public async Task Users_AreIsolated()
    {
        var handler = new SyncHandler(new InMemoryDocumentStore(), new ManualClock(T0));
        await handler.PushAsync("u1", new SyncPayload { Days = [Day(D1, T0, 400)] }, default);

        Assert.Empty((await handler.PullAsync("u2", null, default)).Days);
    }

    [Fact]
    public async Task Push_RejectsOversizedItem()
    {
        var handler = new SyncHandler(new InMemoryDocumentStore(), new ManualClock(T0));
        var day = Day(D1, T0, 1);
        day.Foods[0].Name = new string('x', 40_000);

        await Assert.ThrowsAsync<PayloadTooLargeException>(() =>
            handler.PushAsync("u1", new SyncPayload { Days = [day] }, default));
    }
}

/// <summary>
/// Runs against a real Table Storage endpoint when TABLES_TEST_CONNECTION is set
/// (for example Azurite: "UseDevelopmentStorage=true"). Without it the test returns early.
/// </summary>
public class TableDocumentStoreTests
{
    [Fact]
    public async Task RoundTrip_AndSinceFilter_AgainstRealTableStorage()
    {
        var conn = Environment.GetEnvironmentVariable("TABLES_TEST_CONNECTION");
        if (string.IsNullOrEmpty(conn)) return;

        var table = "IntakeLogTest" + Guid.NewGuid().ToString("N")[..8];
        var service = new TableServiceClient(conn);
        var store = new TableDocumentStore(service, table);
        var t0 = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
        try
        {
            var docs = Enumerable.Range(0, 150) // more than one 100-item transaction
                .Select(i => new StoredDoc($"day_2026-01-{i:000}", "{}", t0, t0.AddMinutes(i)))
                .ToList();
            await store.UpsertAsync("aad_user1", docs, default);
            await store.UpsertAsync("aad_user2", [new StoredDoc("profile", "{}", t0, t0)], default);

            Assert.Equal(150, (await store.ListAsync("aad_user1", null, default)).Count);
            Assert.Equal(50, (await store.ListAsync("aad_user1", t0.AddMinutes(100), default)).Count);
            Assert.Single(await store.ListAsync("aad_user2", null, default));
        }
        finally
        {
            await service.DeleteTableAsync(table);
        }
    }
}
