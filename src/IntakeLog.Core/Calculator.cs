namespace IntakeLog.Core;

public sealed record PlanResult(
    double WeightKg,
    double Bmr,
    double Tdee,
    int TargetKcal,
    bool HeldAtFloor,
    double RealPaceKgPerWeek,
    double KgToGo,
    DateOnly? EstimatedGoalDate,
    int ProteinTargetG);

public sealed record DayTotals(double Kcal, double Protein, double Carbs, double Fat, double ExerciseKcal, int ExerciseMinutes)
{
    /// <summary>Only half of exercise calories are added back: watch and MET estimates usually run high.</summary>
    public int ExerciseCredit => (int)Math.Round(ExerciseKcal * Calculator.ExerciseCreditShare);
}

public static class Calculator
{
    public const double KcalPerKg = 7700;
    public const double ExerciseCreditShare = 0.5;
    public const int FemaleFloorKcal = 1200;
    public const int MaleFloorKcal = 1500;

    /// <summary>Mifflin-St Jeor resting energy expenditure.</summary>
    public static double Bmr(Sex sex, double weightKg, double heightCm, int age) =>
        10 * weightKg + 6.25 * heightCm - 5 * age + (sex == Sex.Male ? 5 : -161);

    public static int FloorFor(Sex sex) => sex == Sex.Male ? MaleFloorKcal : FemaleFloorKcal;

    public static double ExerciseKcal(double met, int minutes, double weightKg) =>
        Math.Round(met * weightKg * minutes / 60.0);

    public static PlanResult Plan(Profile p, double currentWeightKg, DateOnly today)
    {
        var bmr = Bmr(p.Sex, currentWeightKg, p.HeightCm, p.Age);
        var tdee = bmr * p.ActivityFactor;
        var floor = FloorFor(p.Sex);
        var wanted = tdee - p.PaceKgPerWeek * KcalPerKg / 7;
        var target = Math.Max(floor, (int)Math.Round(wanted / 10) * 10);
        var realPace = Math.Max(0, (tdee - target) * 7 / KcalPerKg);
        var toGo = Math.Max(0, currentWeightKg - p.GoalWeightKg);

        DateOnly? eta = toGo == 0 ? today
            : realPace > 0 ? today.AddDays((int)Math.Ceiling(toGo / realPace * 7))
            : null;

        return new PlanResult(
            currentWeightKg, bmr, tdee, target, wanted < floor, realPace, toGo, eta,
            ProteinTargetG: (int)Math.Round(1.6 * p.GoalWeightKg));
    }

    public static DayTotals Totals(DayLog? day)
    {
        if (day is null) return new DayTotals(0, 0, 0, 0, 0, 0);
        double k = 0, pr = 0, c = 0, f = 0;
        foreach (var x in day.Foods)
        {
            k += x.Kcal * x.Quantity; pr += x.Protein * x.Quantity;
            c += x.Carbs * x.Quantity; f += x.Fat * x.Quantity;
        }
        return new DayTotals(k, pr, c, f,
            day.Exercises.Sum(e => e.Kcal),
            day.Exercises.Sum(e => e.Minutes));
    }

    public static int Budget(PlanResult plan, DayTotals totals) => plan.TargetKcal + totals.ExerciseCredit;

    public static double BmiAt(double weightKg, double heightCm) => weightKg / Math.Pow(heightCm / 100, 2);
}
