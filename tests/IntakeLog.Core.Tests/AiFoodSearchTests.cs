using System.Text.Json;
using IntakeLog.Core;

namespace IntakeLog.Core.Tests;

public class AiFoodSearchTests
{
    private const string Answer =
        """{"foods":[{"name":"Kopi C siew dai","serving":"1 cup, 250 ml","kcal":95,"protein_g":2.4,"carbs_g":14,"fat_g":3.1,"fiber_g":0,"sugar_g":12,"sodium_mg":40,"note":"HPB estimate; sugar varies by stall"}]}""";

    private static string Response(string text, string finishReason = "STOP") =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["candidates"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["finishReason"] = finishReason,
                    ["content"] = new Dictionary<string, object>
                    {
                        ["role"] = "model",
                        ["parts"] = new object[]
                        {
                            new Dictionary<string, object> { ["text"] = "thinking about kopi", ["thought"] = true },
                            new Dictionary<string, object> { ["text"] = text }
                        }
                    }
                }
            }
        });

    [Fact]
    public void Request_HasModelSchemaAndQuery()
    {
        using var doc = JsonDocument.Parse(AiFoodSearch.BuildRequest("  kopi c siew dai "));
        var root = doc.RootElement;

        Assert.Contains(AiFoodSearch.Model, AiFoodSearch.Endpoint);
        Assert.Equal("kopi c siew dai", root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        var config = root.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal("array", config.GetProperty("responseSchema").GetProperty("properties").GetProperty("foods").GetProperty("type").GetString());
    }

    [Fact]
    public void Response_ParsesFoodsFromAnswerPartAndSkipsThoughts()
    {
        var result = AiFoodSearch.ParseResponse(200, Response(Answer));

        Assert.Null(result.Error);
        var f = Assert.Single(result.Foods);
        Assert.Equal("Kopi C siew dai", f.Name);
        Assert.Equal(95, f.Kcal);
        Assert.Equal(12, f.Sugar);
        Assert.Equal(40, f.SodiumMg);
    }

    [Fact]
    public void FoodItem_PutsServingInName()
    {
        var item = AiFoodSearch.ParseFoods(Answer).Foods[0].ToFoodItem();

        Assert.Equal("Kopi C siew dai, 1 cup, 250 ml", item.Name);
        Assert.Equal(95, item.Kcal);
        Assert.Equal(2.4, item.Protein);
    }

    [Fact]
    public void SafetyStop_ReturnsError()
    {
        var result = AiFoodSearch.ParseResponse(200, Response("", "SAFETY"));

        Assert.NotNull(result.Error);
        Assert.Empty(result.Foods);
    }

    [Fact]
    public void BadKey_ReturnsKeyMessage()
    {
        const string body = """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}""";

        var result = AiFoodSearch.ParseResponse(400, body);

        Assert.Contains("API key", result.Error);
    }

    [Fact]
    public void BlockedPrompt_ReturnsError()
    {
        const string body = """{"promptFeedback":{"blockReason":"SAFETY"}}""";

        Assert.NotNull(AiFoodSearch.ParseResponse(200, body).Error);
    }

    [Fact]
    public void FreeLimit_ReturnsLimitMessage()
    {
        const string body = """{"error":{"code":429,"message":"Resource has been exhausted","status":"RESOURCE_EXHAUSTED"}}""";

        Assert.Contains("limit", AiFoodSearch.ParseResponse(429, body).Error);
    }

    [Fact]
    public void MalformedAnswer_ReturnsError()
    {
        Assert.NotNull(AiFoodSearch.ParseResponse(200, Response("not json")).Error);
        Assert.NotNull(AiFoodSearch.ParseResponse(502, "<html>Bad gateway</html>").Error);
    }
}
