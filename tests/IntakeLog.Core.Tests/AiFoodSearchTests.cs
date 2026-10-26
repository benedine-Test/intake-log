using System.Text.Json;
using IntakeLog.Core;

namespace IntakeLog.Core.Tests;

public class AiFoodSearchTests
{
    private const string Answer =
        """{"foods":[{"name":"Kopi C siew dai","serving":"1 cup, 250 ml","kcal":95,"protein_g":2.4,"carbs_g":14,"fat_g":3.1,"fiber_g":0,"sugar_g":12,"sodium_mg":40,"note":"HPB estimate; sugar varies by stall"}]}""";

    private static string Response(string text, string stopReason = "end_turn") =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "message",
            ["stop_reason"] = stopReason,
            ["content"] = new object[]
            {
                new Dictionary<string, string> { ["type"] = "thinking", ["thinking"] = "" },
                new Dictionary<string, string> { ["type"] = "text", ["text"] = text }
            }
        });

    [Fact]
    public void Request_HasModelSchemaAndQuery()
    {
        using var doc = JsonDocument.Parse(AiFoodSearch.BuildRequest("  kopi c siew dai "));
        var root = doc.RootElement;

        Assert.Equal(AiFoodSearch.Model, root.GetProperty("model").GetString());
        Assert.Equal("kopi c siew dai", root.GetProperty("messages")[0].GetProperty("content").GetString());
        var format = root.GetProperty("output_config").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.False(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Response_ParsesFoodsFromTextBlock()
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
    public void Refusal_ReturnsError()
    {
        var result = AiFoodSearch.ParseResponse(200, Response("", "refusal"));

        Assert.NotNull(result.Error);
        Assert.Empty(result.Foods);
    }

    [Fact]
    public void BadKey_ReturnsKeyMessage()
    {
        const string body = """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""";

        var result = AiFoodSearch.ParseResponse(401, body);

        Assert.Contains("API key", result.Error);
    }

    [Fact]
    public void MalformedAnswer_ReturnsError()
    {
        Assert.NotNull(AiFoodSearch.ParseResponse(200, Response("not json")).Error);
        Assert.NotNull(AiFoodSearch.ParseResponse(502, "<html>Bad gateway</html>").Error);
    }
}
