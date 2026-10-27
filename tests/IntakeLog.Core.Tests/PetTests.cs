using IntakeLog.Core;

namespace IntakeLog.Core.Tests;

public class PetTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static AppData WithFoodOn(params int[] daysAgo)
    {
        var data = new AppData();
        foreach (var n in daysAgo)
            data.GetOrCreate(Today.AddDays(-n)).Foods.Add(new FoodEntry { Name = "x", Kcal = 100 });
        return data;
    }

    [Fact]
    public void NoLogs_IsAnEggWaitingToHatch()
    {
        var pet = Pet.For(new AppData(), Today);
        Assert.Equal(PetStage.Egg, pet.Stage);
        Assert.Equal(PetMood.Waiting, pet.Mood);
        Assert.Equal(1, pet.NextStageAt);
    }

    [Fact]
    public void FirstLog_Hatches_AndIsHappy()
    {
        var pet = Pet.For(WithFoodOn(0), Today);
        Assert.Equal(PetStage.Hatchling, pet.Stage);
        Assert.Equal(PetMood.Happy, pet.Mood);
    }

    [Fact]
    public void Growth_CountsTotalDaysFed_NotTheStreak()
    {
        // Seven scattered days, no streak longer than one.
        var pet = Pet.For(WithFoodOn(0, 2, 4, 6, 8, 10, 12), Today);
        Assert.Equal(PetStage.Sprout, pet.Stage);
        Assert.Equal(7, pet.DaysFed);
        Assert.Equal(1, pet.Streak);
    }

    [Fact]
    public void MissedDays_MakeItSleepy_ButNeverSmaller()
    {
        var pet = Pet.For(WithFoodOn(5, 6, 7), Today);
        Assert.Equal(PetStage.Chick, pet.Stage);
        Assert.Equal(PetMood.Sleepy, pet.Mood);
    }

    [Fact]
    public void NotFedToday_ButFedYesterday_IsHungry()
    {
        Assert.Equal(PetMood.Hungry, Pet.For(WithFoodOn(1), Today).Mood);
    }

    [Fact]
    public void Progress_IsFractionOfCurrentStage()
    {
        var pet = Pet.For(WithFoodOn(0, 1, 2, 3, 4), Today); // 5 days: Chick (3) → Sprout (7)
        Assert.Equal(PetStage.Chick, pet.Stage);
        Assert.Equal(0.5, pet.Progress, 3);
    }

    [Fact]
    public void ThirtyDays_IsFullyGrown()
    {
        var pet = Pet.For(WithFoodOn(Enumerable.Range(0, 30).ToArray()), Today);
        Assert.Equal(PetStage.Grown, pet.Stage);
        Assert.Null(pet.NextStageAt);
        Assert.Equal(1, pet.Progress);
    }

    [Fact]
    public void FutureDays_AreIgnored()
    {
        Assert.Equal(PetStage.Egg, Pet.For(WithFoodOn(-1), Today).Stage);
    }
}
