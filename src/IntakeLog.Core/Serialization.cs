using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntakeLog.Core;

/// <summary>Source-generated JSON so the trimmed WebAssembly build keeps working.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(AppData))]
public partial class IntakeJsonContext : JsonSerializerContext;

public static class AppDataJson
{
    private static readonly IntakeJsonContext Indented =
        new(new JsonSerializerOptions(IntakeJsonContext.Default.Options) { WriteIndented = true });

    public static string Serialize(AppData data, bool indented = false) =>
        JsonSerializer.Serialize(data, indented ? Indented.AppData : IntakeJsonContext.Default.AppData);

    public static AppData? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, IntakeJsonContext.Default.AppData);
}
