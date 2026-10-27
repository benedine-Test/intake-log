namespace IntakeLog.Core;

public enum PetStage { Egg, Hatchling, Chick, Sprout, Fluffball, Grown }

public enum PetMood { Waiting, Happy, Hungry, Sleepy }

public sealed record PetState(
    PetStage Stage, PetMood Mood, int DaysFed, int Streak, int? NextStageAt, string Message)
{
    /// <summary>0–1 progress from the current stage towards the next; 1 once fully grown.</summary>
    public double Progress { get; init; }
}

/// <summary>
/// A small companion that grows with every day the user logs food. It is derived entirely from
/// the logged days, so it needs no storage of its own and syncs for free.
/// Growth counts total days fed, not the streak, and never goes backwards: a missed day makes the
/// pet sleepy, not smaller. It rewards logging, never eating less, so an honest big day feeds it
/// the same as a light one.
/// </summary>
public static class Pet
{
    public const string Name = "Mochi";

    /// <summary>Days fed needed to reach each stage, in stage order.</summary>
    public static readonly IReadOnlyList<int> StageAt = [0, 1, 3, 7, 14, 30];

    public static PetState For(AppData data, DateOnly today)
    {
        var fedDays = data.Days.Where(d => d.HasFood && d.Date <= today).Select(d => d.Date).ToHashSet();
        var daysFed = fedDays.Count;
        var stage = (PetStage)StageAt.Count(n => daysFed >= n) - 1;
        int? next = (int)stage + 1 < StageAt.Count ? StageAt[(int)stage + 1] : null;
        var progress = next is int n2
            ? (double)(daysFed - StageAt[(int)stage]) / (n2 - StageAt[(int)stage])
            : 1;

        var mood =
            daysFed == 0 ? PetMood.Waiting :
            fedDays.Contains(today) ? PetMood.Happy :
            fedDays.Contains(today.AddDays(-1)) ? PetMood.Hungry :
            PetMood.Sleepy;

        var streak = Insights.LoggingStreak(data, today);
        return new PetState(stage, mood, daysFed, streak, next, Message(stage, mood, daysFed, next)) { Progress = progress };
    }

    private static string Message(PetStage stage, PetMood mood, int daysFed, int? next)
    {
        var toGo = next is int n ? n - daysFed : 0;
        var grow = toGo switch
        {
            0 => "",
            1 => " One more day of logging and it grows!",
            _ => $" {toGo} more days of logging until it grows."
        };
        return mood switch
        {
            PetMood.Waiting => $"An egg! Log your first food and {Name} will hatch.",
            PetMood.Happy when stage == PetStage.Grown => $"{Name} is all grown up and full of snacks. Thanks for showing up every day.",
            PetMood.Happy when daysFed == 1 => $"{Name} hatched! Every day you log, it grows a little.",
            PetMood.Happy => $"{Name} is fed and happy.{grow}",
            PetMood.Hungry => $"{Name} is peeking at your plate. Log something today to feed it.{grow}",
            _ => $"{Name} napped while you were away. It missed you, not your numbers. Log anything to wake it up."
        };
    }
}
