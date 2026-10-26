namespace IntakeLog.Core;

public sealed record Suggestion(string Text, bool NeedsAttention = false);

/// <summary>Trend reading and next-step suggestions. Every method takes "today" so it can be tested.</summary>
public static class Insights
{
    public const int WeeklyActiveMinutesTarget = 150;
    public const int WaterGlassesTarget = 8;

    /// <summary>Average weight over the n days ending at endDate; null with fewer than 3 weigh-ins.</summary>
    public static double? AverageWeight(AppData data, DateOnly endDate, int days = 7)
    {
        var start = endDate.AddDays(-(days - 1));
        var values = data.Days
            .Where(d => d.WeightKg is not null && d.Date >= start && d.Date <= endDate)
            .Select(d => d.WeightKg!.Value).ToList();
        return values.Count >= 3 ? values.Average() : null;
    }

    /// <summary>True when the 7-day average has dropped less than 0.2 kg over three weeks.</summary>
    public static bool IsPlateau(AppData data, DateOnly today)
    {
        var now = AverageWeight(data, today);
        var weekAgo = AverageWeight(data, today.AddDays(-7));
        var twoWeeksAgo = AverageWeight(data, today.AddDays(-14));
        return now is not null && weekAgo is not null && twoWeeksAgo is not null
               && twoWeeksAgo.Value - now.Value < 0.2;
    }

    /// <summary>Consecutive days with food logged, ending today (or yesterday if today is still empty).</summary>
    public static int LoggingStreak(AppData data, DateOnly today)
    {
        var day = data.Find(today)?.HasFood == true ? today : today.AddDays(-1);
        var streak = 0;
        while (data.Find(day)?.HasFood == true) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    /// <summary>Exercise minutes from Monday of the given date's week up to that date.</summary>
    public static int ActiveMinutesThisWeek(AppData data, DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        var monday = date.AddDays(-offset);
        return data.Days.Where(d => d.Date >= monday && d.Date <= date)
                        .Sum(d => d.Exercises.Sum(e => e.Minutes));
    }

    public static (int Logged, int WithinBudget) LastSevenDays(AppData data, DateOnly today, PlanResult plan)
    {
        int logged = 0, within = 0;
        for (var i = 0; i < 7; i++)
        {
            var d = data.Find(today.AddDays(-i));
            if (d?.HasFood != true) continue;
            logged++;
            var t = Calculator.Totals(d);
            if (t.Kcal <= Calculator.Budget(plan, t)) within++;
        }
        return (logged, within);
    }

    public static IReadOnlyList<Suggestion> For(AppData data, DateOnly date, DateOnly today)
    {
        var plan = Calculator.Plan(data.Profile, data.CurrentWeightKg, today);
        var day = data.Find(date);
        var t = Calculator.Totals(day);
        var left = (int)Math.Round(Calculator.Budget(plan, t) - t.Kcal);
        var w = plan.WeightKg;
        var list = new List<Suggestion>();

        if (t.Kcal == 0 && date == today)
            list.Add(new("Nothing logged yet. Log as you eat rather than at night: one tap per item."));

        if (left < 0)
        {
            var walk = (int)Math.Ceiling(-left / (4.3 * w / 60));
            var stairs = (int)Math.Ceiling(-left / (8.0 * w / 60));
            list.Add(new($"You're {-left} kcal over. A {walk}-minute brisk walk or {stairs} minutes of stairs covers it. Or leave it and stay on target tomorrow; one day doesn't decide anything.", true));
        }
        else if (t.Kcal > 0 && left < 250)
            list.Add(new($"{left} kcal left. Good fits: boiled egg (78), Greek yogurt (130), corn (90), kopi-O kosong (5)."));
        else if (t.Kcal > 0)
            list.Add(new($"{left} kcal left. A fishball noodle soup (360) or yong tau foo soup (350) still fits."));

        if (t.Kcal > 0 && t.Protein < plan.ProteinTargetG * 0.6)
            list.Add(new($"Protein is {Math.Round(t.Protein)} g of {plan.ProteinTargetG} g. Protein keeps cravings down: add eggs, tofu, chicken breast or sliced fish soup.", true));

        if (t.ExerciseMinutes == 0)
            list.Add(new($"No exercise yet. 30 minutes of brisk walking burns about {Calculator.ExerciseKcal(4.3, 30, w)} kcal; 15 minutes of stairs about {Calculator.ExerciseKcal(8.0, 15, w)}."));

        var week = ActiveMinutesThisWeek(data, date);
        list.Add(new($"Active minutes this week (Mon–Sun): {week} of {WeeklyActiveMinutesTarget}.", week < WeeklyActiveMinutesTarget));

        var water = day?.WaterGlasses ?? 0;
        if (water < WaterGlassesTarget)
            list.Add(new($"Water: {water} of {WaterGlassesTarget} glasses. Thirst often feels like a craving."));

        if (day?.WeightKg is null && date == today)
            list.Add(new("Log today's weight from Zepp Life. Daily weights swing about 1 kg either way; Trends smooths them into a 7-day average."));

        if (IsPlateau(data, today))
            list.Add(new("Your weight average has barely moved in 3 weeks. Usual causes: portions bigger than logged, untracked drinks and snacks, weekend overeating. Log every drink for a week before cutting more food.", true));

        return list;
    }
}
