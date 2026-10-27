using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntakeLog.Core;

/// <summary>One food the AI found for a search, with nutrition facts for the stated serving.</summary>
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
/// Builds the Gemini generateContent request for a food search and reads its response.
/// No HTTP here, so it is testable; the web project sends the request.
/// </summary>
public static class AiFoodSearch
{
    /// <summary>Google's alias for its current Flash model, which has a free tier.</summary>
    public const string Model = "gemini-flash-latest";
    public const string Endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";

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

    // Gemini's responseSchema is an OpenAPI subset: no additionalProperties.
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
                "required": ["name", "serving", "kcal", "protein_g", "carbs_g", "fat_g", "fiber_g", "sugar_g", "sodium_mg", "note"]
              }
            }
          },
          "required": ["foods"]
        }
        """;

    /// <summary>The JSON request body for a search.</summary>
    public static string BuildRequest(string query)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = SystemPrompt }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = query.Trim() })
            }),
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = 8000,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = JsonNode.Parse(SchemaJson)
            }
        };
        return body.ToJsonString();
    }

    /// <summary>Reads a generateContent response body (success or error) into foods or a message.</summary>
    public static AiSearchResult ParseResponse(int statusCode, string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return AiSearchResult.Fail(StatusMessage(statusCode, null)); }

        using (doc)
        {
            var root = doc.RootElement;
            if (statusCode is < 200 or >= 300 || root.TryGetProperty("error", out _))
            {
                string? detail = root.TryGetProperty("error", out var err) ? Str(err, "message") : null;
                // An invalid key comes back as 400 INVALID_ARGUMENT, not 401.
                if (detail?.Contains("API key", StringComparison.OrdinalIgnoreCase) == true) statusCode = 401;
                return AiSearchResult.Fail(StatusMessage(statusCode, detail));
            }

            if (root.TryGetProperty("promptFeedback", out var feedback) && Str(feedback, "blockReason").Length > 0)
                return AiSearchResult.Fail(Declined);

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
                return AiSearchResult.Fail("Gemini sent an empty answer. Try again.");

            var candidate = candidates[0];
            switch (Str(candidate, "finishReason"))
            {
                case "" or "STOP": break;
                case "MAX_TOKENS": return AiSearchResult.Fail("The answer was cut off. Try a more specific search.");
                default: return AiSearchResult.Fail(Declined);
            }

            if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
                return AiSearchResult.Fail("Gemini sent an empty answer. Try again.");

            var text = string.Concat(parts.EnumerateArray()
                .Where(p => !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
                .Select(p => Str(p, "text")));
            return ParseFoods(text);
        }
    }

    private const string Declined = "Gemini declined this search. Try wording it differently.";

    /// <summary>Reads the structured JSON Gemini returns as its answer text.</summary>
    public static AiSearchResult ParseFoods(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("foods", out var foods) || foods.ValueKind != JsonValueKind.Array)
                return AiSearchResult.Fail("Gemini's answer wasn't in the expected format. Try again.");

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
            return AiSearchResult.Fail("Gemini's answer wasn't in the expected format. Try again.");
        }
    }

    private static string StatusMessage(int status, string? detail) => status switch
    {
        401 or 403 => "Your API key was rejected. Check it under My plan → AI food search.",
        429 => "Free search limit reached. Wait a minute, or try again tomorrow if the daily limit is used up.",
        >= 500 => "Gemini is busy. Try again in a moment.",
        _ => detail is { Length: > 0 } ? $"Search failed: {detail}" : $"Search failed (HTTP {status.ToString(CultureInfo.InvariantCulture)})."
    };

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? Math.Max(0, v.GetDouble()) : 0;
}
