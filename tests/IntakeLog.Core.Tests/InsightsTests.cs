using IntakeLog.Core;

namespace IntakeLog.Core.Tests;

public class InsightsTests
{
    private static readonly DateOnly Today = new(2026, 10, 26); // a Monday

    private static AppData WithFoodOn(params int[] daysAgo)
    {
        var data = new AppData();
        foreach (var n in daysAgo)
            data.GetOrCreate(Today.AddDays(-n)).Foods.Add(new FoodEntry { Name = "x", Kcal = 100 });
        return data;
    }

    [Fact]
    public void Streak_CountsBackFromToday()
    {
        Assert.Equal(3, Insights.LoggingStreak(WithFoodOn(0, 1, 2, 4), Today));
    }

    [Fact]
    public void Streak_StartsFromYesterday_WhenTodayEmpty()
    {
        Assert.Equal(2, Insights.LoggingStreak(WithFoodOn(1, 2), Today));
    }

    [Fact]
    public void ActiveMinutes_ResetOnMonday()
    {
        var data = new AppData();
        data.GetOrCreate(Today.AddDays(-1)).Exercises.Add(new ExerciseEntry { Minutes = 40 }); // Sunday
        data.GetOrCreate(Today).Exercises.Add(new ExerciseEntry { Minutes = 20 });

        Assert.Equal(20, Insights.ActiveMinutesThisWeek(data, Today));
        Assert.Equal(40, Insights.ActiveMinutesThisWeek(data, Today.AddDays(-1)));
    }

    [Fact]
    public void AverageWeight_NeedsThreeWeighIns()
    {
        var data = new AppData();
        data.GetOrCreate(Today).WeightKg = 65;
        data.GetOrCreate(Today.AddDays(-1)).WeightKg = 66;
        Assert.Null(Insights.AverageWeight(data, Today));

        data.GetOrCreate(Today.AddDays(-2)).WeightKg = 67;
        Assert.Equal(66, Insights.AverageWeight(data, Today)!.Value, 2);
    }

    [Fact]
    public void Plateau_DetectedWhenAverageFlatForThreeWeeks()
    {
        var data = new AppData();
        for (var i = 0; i < 21; i++) data.GetOrCreate(Today.AddDays(-i)).WeightKg = 63.5 + (i % 2) * 0.1;
        Assert.True(Insights.IsPlateau(data, Today));
    }

    [Fact]
    public void Plateau_NotDetectedWhenLosing()
    {
        var data = new AppData();
        for (var i = 0; i < 21; i++) data.GetOrCreate(Today.AddDays(-i)).WeightKg = 63 + i * 0.05;
        Assert.False(Insights.IsPlateau(data, Today));
    }

    [Fact]
    public void Suggestions_FlagOverBudget()
    {
        var data = new AppData();
        data.GetOrCreate(Today).Foods.Add(new FoodEntry { Name = "Mala", Kcal = 1500, Protein = 50 });

        var s = Insights.For(data, Today, Today);

        Assert.Contains(s, x => x.NeedsAttention && x.Text.StartsWith("You're 300 kcal over"));
    }
}
