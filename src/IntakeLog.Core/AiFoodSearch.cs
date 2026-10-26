using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntakeLog.Core;

/// <summary>One food Claude found for a search, with nutrition facts for the stated serving.</summary>
public sealed record AiFood(
    string Name, string Serving, double Kcal, double Protein, double Carbs, double Fat,
    double Fiber, double Sugar, double SodiumMg, string Note)
{
    /// <summary>The loggable item. The serving goes in the name so the log shows what the numbers cover.</summary>
    public FoodItem ToFoodItem() => new(
        string.IsNullOrWhiteSpace(Serving) ? Name : $"{Name}, {Serving}",
        Math.Round(Kcal), Math.Round(Protein, 1), Math.Round(Carbs, 1), Math.Round(Fat, 1));
}

/// <summary>Foods found, or an error message to show instead.</summary>
public sealed record AiSearchResult(IReadOnlyList<AiFood> Foods, string? Error = null)
{
    public static AiSearchResult Fail(string error) => new([], error);
}

/// <summary>
/// Builds the Claude Messages API request for a food search and reads its response.
/// No HTTP here, so it is testable; the web project sends the request.
/// </summary>
public static class AiFoodSearch
{
    public const string Endpoint = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";
    public const string Model = "claude-opus-5-5";

    /// <summary>Lets Claude retry on another model if a safety classifier declines a harmless food query.</summary>
    public const string FallbackBeta = "server-side-fallback-2026-07-01";

    public const string SystemPrompt =
        """
        You are the food lookup for a calorie tracker used in Singapore. The user types a food or meal,
        sometimes with a quantity ("2 eggs", "kopi c siew dai", "mcspicy meal", "100 g brown rice").

        Return up to 5 likely matches, most likely first. For each, give nutrition facts for one typical
        serving as eaten in Singapore (hawker, kopitiam, chain restaurant or supermarket product), unless
        the user states a quantity, in which case use exactly that quantity. Describe the serving in plain
        words (for example "1 plate", "1 cup, 250 ml", "100 g").

        Use published data (Singapore HPB energy and nutrient composition, USDA, brand nutrition pages)
        where you know it, otherwise estimate from typical recipes. Use "note" to say which, in under 15
        words, and flag big variation between stalls or brands. Never invent a precise brand figure you
        are unsure of; give a typical value and say it is an estimate.

        If the text is not a food or drink, return an empty list.
        """;

    private const string SchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "foods": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "serving": { "type": "string" },
                  "kcal": { "type": "number" },
                  "protein_g": { "type": "number" },
                  "carbs_g": { "type": "number" },
                  "fat_g": { "type": "number" },
                  "fiber_g": { "type": "number" },
                  "sugar_g": { "type": "number" },
                  "sodium_mg": { "type": "number" },
                  "note": { "type": "string" }
                },
                "required": ["name", "serving", "kcal", "protein_g", "carbs_g", "fat_g", "fiber_g", "sugar_g", "sodium_mg", "note"],
                "additionalProperties": false
              }
            }
          },
          "required": ["foods"],
          "additionalProperties": false
        }
        """;

    /// <summary>The JSON request body for a search.</summary>
    public static string BuildRequest(string query)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 4000,
            ["system"] = SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = query.Trim() }),
            ["output_config"] = new JsonObject
            {
                ["effort"] = "low",
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = JsonNode.Parse(SchemaJson) }
            },
            ["fallbacks"] = "default"
        };
        return body.ToJsonString();
    }

    /// <summary>Reads a Messages API response body (success or error) into foods or a message.</summary>
    public static AiSearchResult ParseResponse(int statusCode, string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return AiSearchResult.Fail(StatusMessage(statusCode, null)); }

        using (doc)
        {
            var root = doc.RootElement;
            if (statusCode is < 200 or >= 300 || Str(root, "type") == "error")
            {
                string? detail = root.TryGetProperty("error", out var err) ? Str(err, "message") : null;
                return AiSearchResult.Fail(StatusMessage(statusCode, detail));
            }

            switch (Str(root, "stop_reason"))
            {
                case "refusal": return AiSearchResult.Fail("Claude declined this search. Try wording it differently.");
                case "max_tokens": return AiSearchResult.Fail("The answer was cut off. Try a more specific search.");
            }

            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                return AiSearchResult.Fail("Claude sent an empty answer. Try again.");

            var text = string.Concat(content.EnumerateArray()
                .Where(b => Str(b, "type") == "text")
                .Select(b => Str(b, "text")));
            return ParseFoods(text);
        }
    }

    /// <summary>Reads the structured JSON Claude returns as its answer text.</summary>
    public static AiSearchResult ParseFoods(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("foods", out var foods) || foods.ValueKind != JsonValueKind.Array)
                return AiSearchResult.Fail("Claude's answer wasn't in the expected format. Try again.");

            var list = new List<AiFood>();
            foreach (var f in foods.EnumerateArray())
            {
                var name = Str(f, "name").Trim();
                if (name.Length == 0) continue;
                list.Add(new AiFood(name, Str(f, "serving").Trim(), Num(f, "kcal"),
                    Num(f, "protein_g"), Num(f, "carbs_g"), Num(f, "fat_g"),
                    Num(f, "fiber_g"), Num(f, "sugar_g"), Num(f, "sodium_mg"), Str(f, "note").Trim()));
            }
            return new AiSearchResult(list);
        }
        catch (JsonException)
        {
            return AiSearchResult.Fail("Claude's answer wasn't in the expected format. Try again.");
        }
    }

    private static string StatusMessage(int status, string? detail) => status switch
    {
        401 or 403 => "Your API key was rejected. Check it under My plan → AI food search.",
        429 => "Too many searches right now, or your API credit has run out. Wait a minute and try again.",
        >= 500 => "Claude is busy. Try again in a moment.",
        _ => detail is { Length: > 0 } ? $"Search failed: {detail}" : $"Search failed (HTTP {status.ToString(CultureInfo.InvariantCulture)})."
    };

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? Math.Max(0, v.GetDouble()) : 0;
}
