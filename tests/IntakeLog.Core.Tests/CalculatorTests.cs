using IntakeLog.Core;

namespace IntakeLog.Core.Tests;

public class CalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void Bmr_UsesMifflinStJeor()
    {
        // 10*65.15 + 6.25*160 - 5*30 - 161
        Assert.Equal(1340.5, Calculator.Bmr(Sex.Female, 65.15, 160, 30), 1);
        Assert.Equal(1506.5, Calculator.Bmr(Sex.Male, 65.15, 160, 30), 1);
    }

    [Fact]
    public void Plan_HoldsTargetAtFloor_WhenPaceWouldGoBelowIt()
    {
        var plan = Calculator.Plan(new Profile(), 65.15, Today);

        Assert.Equal(1200, plan.TargetKcal);
        Assert.True(plan.HeldAtFloor);
        Assert.InRange(plan.RealPaceKgPerWeek, 0.36, 0.38);
        Assert.Equal(88, plan.ProteinTargetG);
        Assert.NotNull(plan.EstimatedGoalDate);
    }

    [Fact]
    public void Plan_UsesRequestedPace_WhenAboveFloor()
    {
        var p = new Profile { ActivityFactor = 1.55, PaceKgPerWeek = 0.25 };
        var plan = Calculator.Plan(p, 65.15, Today);

        Assert.False(plan.HeldAtFloor);
        Assert.InRange(plan.RealPaceKgPerWeek, 0.24, 0.26);
    }

    [Fact]
    public void Plan_GoalReached_EtaIsToday()
    {
        var plan = Calculator.Plan(new Profile(), 54.8, Today);
        Assert.Equal(0, plan.KgToGo);
        Assert.Equal(Today, plan.EstimatedGoalDate);
    }

    [Fact]
    public void Totals_MultiplyByQuantity_AndCreditHalfOfExercise()
    {
        var day = new DayLog { Date = Today };
        day.Foods.Add(new FoodEntry { Name = "Rice", Kcal = 330, Protein = 6, Quantity = 0.5 });
        day.Foods.Add(new FoodEntry { Name = "Egg", Kcal = 78, Protein = 6, Quantity = 2 });
        day.Exercises.Add(new ExerciseEntry { Name = "Walk", Minutes = 30, Kcal = 141 });

        var t = Calculator.Totals(day);

        Assert.Equal(321, t.Kcal, 1);
        Assert.Equal(15, t.Protein, 1);
        Assert.Equal(30, t.ExerciseMinutes);
        Assert.Equal(70, t.ExerciseCredit); // 70.5 rounds to even
    }

    [Fact]
    public void ExerciseKcal_FromMet()
    {
        Assert.Equal(140, Calculator.ExerciseKcal(4.3, 30, 65.15));
    }
}
