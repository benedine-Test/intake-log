using IntakeLog.Core;
using Microsoft.JSInterop;

namespace IntakeLog.Web.Services;

/// <summary>
/// Holds the app's data in memory and persists it to the browser's localStorage
/// as one JSON document. Components subscribe to <see cref="Changed"/> to re-render.
/// </summary>
public sealed class AppState(IJSRuntime js)
{
    private const string StorageKey = "intake-log-v1";

    public AppData Data { get; private set; } = new();
    public bool Loaded { get; private set; }
    public bool StorageAvailable { get; private set; } = true;
    public DateOnly SelectedDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public Meal SelectedMeal { get; set; } = DefaultMeal();

    public event Action? Changed;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public PlanResult Plan => Calculator.Plan(Data.Profile, Data.CurrentWeightKg, Today);

    public static Meal DefaultMeal() => DateTime.Now.Hour switch
    {
        < 11 => Meal.Breakfast,
        < 15 => Meal.Lunch,
        < 18 => Meal.Snack,
        _ => Meal.Dinner
    };

    public async Task LoadAsync()
    {
        if (Loaded) return;
        try
        {
            var json = await js.InvokeAsync<string?>("intakeLog.load", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
                Data = AppDataJson.Deserialize(json) ?? new AppData();
            _ = js.InvokeAsync<bool>("intakeLog.persist"); // ask the browser not to evict our data
        }
        catch (Exception)
        {
            StorageAvailable = false;
        }
        Loaded = true;
        Changed?.Invoke();
    }

    /// <summary>Apply a change, then save and notify.</summary>
    public async Task UpdateAsync(Action<AppData> change)
    {
        change(Data);
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        try
        {
            await js.InvokeVoidAsync("intakeLog.save", StorageKey, AppDataJson.Serialize(Data));
            StorageAvailable = true;
        }
        catch (Exception)
        {
            StorageAvailable = false;
        }
        Changed?.Invoke();
    }

    public async Task ExportAsync()
    {
        var name = $"intake-log-{Today:yyyy-MM-dd}.json";
        await js.InvokeVoidAsync("intakeLog.download", name, AppDataJson.Serialize(Data, indented: true));
    }

    /// <summary>Replace all data with an exported backup. Returns an error message, or null on success.</summary>
    public async Task<string?> ImportAsync(string json)
    {
        AppData? imported;
        try { imported = AppDataJson.Deserialize(json); }
        catch (Exception) { return "That file isn't an Intake Log backup. Choose a .json file exported from this app."; }

        if (imported is null) return "The backup file is empty.";
        Data = imported;
        await SaveAsync();
        return null;
    }
}
