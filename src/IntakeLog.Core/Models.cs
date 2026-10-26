namespace IntakeLog.Core;

public enum Sex { Female, Male }

public enum Meal { Breakfast, Lunch, Snack, Dinner }

/// <summary>The user's body numbers and goal. Drives every target in the app.</summary>
public sealed class Profile
{
    public Sex Sex { get; set; } = Sex.Female;
    public int Age { get; set; } = 30;
    public double HeightCm { get; set; } = 160;
    public double StartWeightKg { get; set; } = 65.15;
    public double GoalWeightKg { get; set; } = 55;
    public DateOnly StartDate { get; set; } = new(2026, 10, 26);

    /// <summary>Multiplier on BMR for day-to-day activity, excluding logged exercise.</summary>
    public double ActivityFactor { get; set; } = 1.2;

    /// <summary>Desired loss in kg per week.</summary>
    public double PaceKgPerWeek { get; set; } = 0.5;

    /// <summary>False until the user has reviewed and saved the plan once.</summary>
    public bool Confirmed { get; set; }
}

/// <summary>A food that can be added in one tap (preset or saved by the user).</summary>
public sealed record FoodItem(string Name, double Kcal, double Protein, double Carbs = 0, double Fat = 0);

public sealed class FoodEntry
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "";
    public double Kcal { get; set; }
    public double Protein { get; set; }
    public double Carbs { get; set; }
    public double Fat { get; set; }
    public double Quantity { get; set; } = 1;
    public Meal Meal { get; set; }

    public static FoodEntry From(FoodItem item, Meal meal) => new()
    {
        Name = item.Name, Kcal = item.Kcal, Protein = item.Protein,
        Carbs = item.Carbs, Fat = item.Fat, Meal = meal
    };

    internal static string NewId() => Guid.NewGuid().ToString("N")[..12];
}

public sealed class ExerciseEntry
{
    public string Id { get; set; } = FoodEntry.NewId();
    public string Name { get; set; } = "";
    public int Minutes { get; set; }
    public double Kcal { get; set; }
}

public sealed class DayLog
{
    public DateOnly Date { get; set; }
    public List<FoodEntry> Foods { get; set; } = [];
    public List<ExerciseEntry> Exercises { get; set; } = [];
    public int WaterGlasses { get; set; }
    public int? Steps { get; set; }
    public double? WeightKg { get; set; }

    public bool HasFood => Foods.Count > 0;
}

/// <summary>Everything the app stores. Serialized as one JSON document.</summary>
public sealed class AppData
{
    public int Version { get; set; } = 1;
    public Profile Profile { get; set; } = new();
    public List<FoodItem> SavedFoods { get; set; } = [];
    public List<DayLog> Days { get; set; } = [];

    public DayLog? Find(DateOnly date) => Days.FirstOrDefault(d => d.Date == date);

    /// <summary>Returns the day's log, creating and attaching it if missing.</summary>
    public DayLog GetOrCreate(DateOnly date)
    {
        var day = Find(date);
        if (day is null)
        {
            day = new DayLog { Date = date };
            Days.Add(day);
        }
        return day;
    }

    /// <summary>Most recent logged weight, or the start weight if none.</summary>
    public double CurrentWeightKg =>
        Days.Where(d => d.WeightKg is not null).OrderBy(d => d.Date).LastOrDefault()?.WeightKg
        ?? Profile.StartWeightKg;
}
