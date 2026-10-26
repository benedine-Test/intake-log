namespace IntakeLog.Core;

public sealed record Activity(string Name, double Met);

public static class Presets
{
    /// <summary>
    /// Typical Singapore portions. Values are estimates (kcal, protein g, carbs g, fat g);
    /// real portions vary a lot between stalls.
    /// </summary>
    public static readonly IReadOnlyList<FoodItem> Foods =
    [
        new("Chicken rice (roasted)", 600, 28, 70, 22),
        new("Chicken rice (steamed)", 610, 27, 70, 23),
        new("Nasi lemak (chicken wing)", 650, 24, 75, 28),
        new("Fishball noodles, soup", 360, 18, 55, 7),
        new("Sliced fish soup (no rice)", 230, 26, 10, 9),
        new("Sliced fish bee hoon soup", 380, 25, 50, 9),
        new("Yong tau foo soup, 6 pcs", 350, 22, 30, 14),
        new("Yong tau foo, 6 pcs + bee hoon", 480, 23, 60, 15),
        new("Wanton mee, dry", 410, 15, 58, 13),
        new("Char kway teow", 745, 23, 76, 38),
        new("Laksa", 590, 22, 52, 32),
        new("Hokkien mee", 620, 18, 70, 28),
        new("Fried carrot cake, white", 545, 10, 60, 30),
        new("Mee rebus", 570, 16, 82, 20),
        new("Economy rice, 2 veg 1 meat", 550, 22, 70, 20),
        new("Economy rice, 1 veg 2 meat", 620, 30, 68, 25),
        new("Thunder tea rice", 550, 18, 75, 18),
        new("Chee cheong fun, plain", 300, 6, 55, 6),
        new("Mala hotpot, medium bowl", 900, 30, 60, 60),
        new("Roti prata, plain 1 pc", 210, 4, 28, 9),
        new("Curry for prata", 120, 2, 6, 10),
        new("Kaya toast (2 slices)", 300, 6, 40, 13),
        new("Kaya toast set (toast + 2 eggs)", 450, 18, 42, 23),
        new("Soft-boiled egg", 70, 6, 0, 5),
        new("Chicken porridge", 350, 18, 50, 8),
        new("Ban mian, soup", 475, 20, 65, 14),
        new("Plain white rice, 1 bowl", 330, 6, 73, 1),
        new("Brown rice, 1 bowl", 300, 7, 62, 2),
        new("Kopi (milk + sugar)", 120, 2, 20, 3),
        new("Kopi-O kosong", 5, 0, 0, 0),
        new("Kopi-C kosong", 40, 1, 4, 2),
        new("Teh (milk + sugar)", 120, 2, 20, 3),
        new("Milo dinosaur", 320, 9, 50, 9),
        new("Bubble milk tea, 50% sugar", 300, 3, 55, 8),
        new("Bubble milk tea, 0% sugar", 200, 3, 32, 8),
        new("Soft drink can", 140, 0, 35, 0),
        new("Iced lemon tea (packet)", 160, 0, 40, 0),
        new("Boiled egg", 78, 6, 1, 5),
        new("Chicken breast 100 g, grilled", 165, 31, 0, 4),
        new("Tofu 100 g", 80, 8, 2, 5),
        new("Enoki mushroom 100 g", 37, 3, 8, 0),
        new("Corn on the cob", 90, 3, 19, 1),
        new("Tomato", 22, 1, 5, 0),
        new("Lettuce, side", 15, 1, 3, 0),
        new("Sweet potato, medium", 115, 2, 27, 0),
        new("Potato, medium boiled", 130, 3, 30, 0),
        new("Banana", 105, 1, 27, 0),
        new("Apple", 95, 0, 25, 0),
        new("Greek yogurt 150 g, plain", 130, 15, 6, 5),
        new("Milk, low fat 250 ml", 120, 9, 12, 4),
        new("Wholemeal bread, 1 slice", 80, 4, 14, 1),
        new("Instant noodles, 1 pack", 450, 9, 60, 19),
        new("Curry puff", 250, 5, 25, 15),
        new("Kueh, 1 pc", 120, 1, 22, 3),
        new("Potato chips, small bag", 160, 2, 15, 10),
        new("Chocolate bar", 230, 3, 28, 12),
        new("Ice cream, 1 scoop", 140, 2, 16, 7),
    ];

    /// <summary>No-equipment activities with MET values from the Compendium of Physical Activities.</summary>
    public static readonly IReadOnlyList<Activity> Activities =
    [
        new("Brisk walk", 4.3),
        new("Climbing HDB stairs", 8.0),
        new("Bodyweight circuit (no equipment)", 6.0),
        new("Jogging", 7.0),
        new("Skipping", 11.8),
        new("Cycling, easy", 4.0),
        new("Swimming, easy laps", 6.0),
        new("Dance workout video", 5.5),
        new("Yoga / stretching", 2.5),
        new("Walking, casual", 3.0),
        new("Badminton, social", 5.5),
        new("Pilates", 3.0),
    ];
}
