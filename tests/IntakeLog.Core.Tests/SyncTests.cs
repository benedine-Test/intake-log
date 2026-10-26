using IntakeLog.Core;

namespace IntakeLog.Core.Tests;

public class SyncTests
{
    private static readonly DateOnly D1 = new(2026, 10, 26);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static DayLog Day(DateOnly date, DateTimeOffset stamp, double kcal) => new()
    {
        Date = date, UpdatedAt = stamp, Foods = [new FoodEntry { Name = "x", Kcal = kcal }]
    };

    [Fact]
    public void MarkChanged_StampsAndQueues()
    {
        var data = new AppData();
        data.MarkChanged(SyncKeys.Day(D1), T0);

        Assert.Equal(T0, data.Find(D1)!.UpdatedAt);
        Assert.Contains("day_2026-10-26", data.Sync.Pending);
    }

    [Fact]
    public void FirstSync_QueuesRealData_ButNotFreshDefaults()
    {
        var data = new AppData();
        data.GetOrCreate(D1).Foods.Add(new FoodEntry { Name = "egg", Kcal = 78 });

        SyncMerge.PrepareFirstSync(data, T0);

        Assert.Equal([SyncKeys.Day(D1)], data.Sync.Pending);
        Assert.Equal(T0, data.Find(D1)!.UpdatedAt);
        Assert.Equal(default, data.Profile.UpdatedAt); // unconfirmed profile never overwrites another device
    }

    [Fact]
    public void FirstSync_QueuesConfirmedProfileAndSavedFoods()
    {
        var data = new AppData();
        data.Profile.Confirmed = true;
        data.SavedFoods.Add(new FoodItem("Mum's bee hoon", 450, 12));

        SyncMerge.PrepareFirstSync(data, T0);

        Assert.Contains(SyncKeys.Profile, data.Sync.Pending);
        Assert.Contains(SyncKeys.Foods, data.Sync.Pending);
    }

    [Fact]
    public void ApplyRemote_NewerWins_OlderIgnored()
    {
        var data = new AppData();
        data.Days.Add(Day(D1, T0.AddMinutes(10), 500));
        data.Days.Add(Day(D1.AddDays(-1), T0, 300));

        var remote = new SyncPayload
        {
            ServerTime = T0.AddHours(1),
            Days = [Day(D1, T0.AddMinutes(5), 999), Day(D1.AddDays(-1), T0.AddMinutes(1), 777)]
        };

        var changed = SyncMerge.ApplyRemote(data, remote);

        Assert.Equal(1, changed);
        Assert.Equal(500, data.Find(D1)!.Foods[0].Kcal);            // local newer, kept
        Assert.Equal(777, data.Find(D1.AddDays(-1))!.Foods[0].Kcal); // remote newer, taken
        Assert.Equal(T0.AddHours(1), data.Sync.LastPulledAt);
    }

    [Fact]
    public void ApplyRemote_TakingNewerCopy_ClearsPending()
    {
        var data = new AppData();
        data.MarkChanged(SyncKeys.Day(D1), T0);

        SyncMerge.ApplyRemote(data, new SyncPayload { Days = [Day(D1, T0.AddMinutes(1), 100)] });

        Assert.Empty(data.Sync.Pending);
    }

    [Fact]
    public void ApplyRemote_UnconfirmedFreshProfile_IsReplacedByServerProfile()
    {
        var data = new AppData(); // fresh install, default profile with no stamp
        var serverProfile = new Profile { GoalWeightKg = 55, Confirmed = true, UpdatedAt = T0 };

        SyncMerge.ApplyRemote(data, new SyncPayload { Profile = serverProfile });

        Assert.True(data.Profile.Confirmed);
    }

    [Fact]
    public void BuildPush_ThenMarkPushed_KeepsItemsChangedMidFlight()
    {
        var data = new AppData();
        data.MarkChanged(SyncKeys.Day(D1), T0);
        data.MarkChanged(SyncKeys.Profile, T0);

        var push = SyncMerge.BuildPush(data);
        var sent = SyncMerge.Stamps(push);
        data.MarkChanged(SyncKeys.Day(D1), T0.AddSeconds(5)); // edited while uploading

        SyncMerge.MarkPushed(data, sent);

        Assert.Equal([SyncKeys.Day(D1)], data.Sync.Pending);
        Assert.Single(push.Days);
        Assert.NotNull(push.Profile);
    }

    [Fact]
    public void Payload_RoundTripsThroughJson()
    {
        var p = new SyncPayload
        {
            ServerTime = T0,
            Profile = new Profile { Sex = Sex.Female, UpdatedAt = T0 },
            Foods = new FoodsDoc { Items = [new FoodItem("Kopi-O kosong", 5, 0)], UpdatedAt = T0 },
            Days = [Day(D1, T0, 600)]
        };

        var back = AppDataJson.DeserializePayload(AppDataJson.SerializePayload(p))!;

        Assert.Equal(T0, back.ServerTime);
        Assert.Equal("Kopi-O kosong", back.Foods!.Items[0].Name);
        Assert.Equal(600, back.Days[0].Foods[0].Kcal);
        Assert.DoesNotContain("hasFood", AppDataJson.SerializePayload(p));
    }

    [Fact]
    public void SyncKeys_RoundTrip()
    {
        Assert.True(SyncKeys.TryParseDay(SyncKeys.Day(D1), out var d));
        Assert.Equal(D1, d);
        Assert.False(SyncKeys.TryParseDay("profile", out _));
    }
}
