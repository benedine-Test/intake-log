using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntakeLog.Core;

/// <summary>Source-generated JSON so the trimmed WebAssembly build keeps working.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(AppData))]
[JsonSerializable(typeof(SyncPayload))]
public partial class IntakeJsonContext : JsonSerializerContext;

public static class AppDataJson
{
    private static readonly IntakeJsonContext Indented =
        new(new JsonSerializerOptions(IntakeJsonContext.Default.Options) { WriteIndented = true });

    public static string Serialize(AppData data, bool indented = false) =>
        JsonSerializer.Serialize(data, indented ? Indented.AppData : IntakeJsonContext.Default.AppData);

    public static AppData? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, IntakeJsonContext.Default.AppData);

    public static string SerializePayload(SyncPayload payload) =>
        JsonSerializer.Serialize(payload, IntakeJsonContext.Default.SyncPayload);

    public static SyncPayload? DeserializePayload(string json) =>
        JsonSerializer.Deserialize(json, IntakeJsonContext.Default.SyncPayload);

    // Single items, as stored server-side (one table row per item).
    public static string SerializeDay(DayLog day) => JsonSerializer.Serialize(day, IntakeJsonContext.Default.DayLog);
    public static DayLog? DeserializeDay(string json) => JsonSerializer.Deserialize(json, IntakeJsonContext.Default.DayLog);
    public static string SerializeProfile(Profile p) => JsonSerializer.Serialize(p, IntakeJsonContext.Default.Profile);
    public static Profile? DeserializeProfile(string json) => JsonSerializer.Deserialize(json, IntakeJsonContext.Default.Profile);
    public static string SerializeFoods(FoodsDoc f) => JsonSerializer.Serialize(f, IntakeJsonContext.Default.FoodsDoc);
    public static FoodsDoc? DeserializeFoods(string json) => JsonSerializer.Deserialize(json, IntakeJsonContext.Default.FoodsDoc);
}
