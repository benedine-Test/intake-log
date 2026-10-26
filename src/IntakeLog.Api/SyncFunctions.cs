using System.Globalization;
using System.Text.Json;
using Azure;
using IntakeLog.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IntakeLog.Api;

/// <summary>
/// GET  /api/sync?since=ISO-8601  → items written since then (all items when omitted)
/// POST /api/sync                 → upload changed items; newer copies win
/// Static Web Apps only lets signed-in users reach /api (see staticwebapp.config.json);
/// these functions also check the principal themselves.
/// </summary>
public sealed class SyncFunctions(SyncHandler handler, IConfiguration config, ILogger<SyncFunctions> log)
{
    private const long MaxBodyBytes = 4 * 1024 * 1024;

    [Function("SyncPull")]
    public async Task<IActionResult> Pull(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sync")] HttpRequest req, CancellationToken ct)
    {
        if (Authorize(req, out var user) is { } denied) return denied;

        DateTimeOffset? since = null;
        if (req.Query.TryGetValue("since", out var s) && !string.IsNullOrEmpty(s))
        {
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return Problem(400, "'since' must be an ISO 8601 date and time.");
            since = parsed;
        }

        try
        {
            var payload = await handler.PullAsync(user!.StorageKey, since, ct);
            return Json(200, AppDataJson.SerializePayload(payload));
        }
        catch (RequestFailedException e)
        {
            log.LogError(e, "Table storage read failed");
            return Problem(503, "Storage is unavailable. Your changes are safe on your device; try again shortly.");
        }
    }

    [Function("SyncPush")]
    public async Task<IActionResult> Push(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sync")] HttpRequest req, CancellationToken ct)
    {
        if (Authorize(req, out var user) is { } denied) return denied;
        if (req.ContentLength is > MaxBodyBytes) return Problem(413, "Upload is too large.");

        SyncPayload? payload;
        try
        {
            using var reader = new StreamReader(req.Body);
            payload = AppDataJson.DeserializePayload(await reader.ReadToEndAsync(ct));
        }
        catch (JsonException)
        {
            return Problem(400, "The request body isn't valid sync data.");
        }
        if (payload is null) return Problem(400, "The request body is empty.");

        try
        {
            var stored = await handler.PushAsync(user!.StorageKey, payload, ct);
            return Json(200, $"{{\"stored\":{stored}}}");
        }
        catch (PayloadTooLargeException e)
        {
            return Problem(413, e.Message);
        }
        catch (RequestFailedException e)
        {
            log.LogError(e, "Table storage write failed");
            return Problem(503, "Storage is unavailable. Your changes are safe on your device; try again shortly.");
        }
    }

    private IActionResult? Authorize(HttpRequest req, out ClientPrincipal? user)
    {
        user = ClientPrincipal.FromHeader(req.Headers[ClientPrincipal.HeaderName]);
        if (user is null) return Problem(401, "Sign in to sync.");
        if (!user.IsAllowed(config["ALLOWED_USERS"]))
        {
            log.LogWarning("Rejected sync for {Provider} user not in ALLOWED_USERS", user.IdentityProvider);
            return Problem(403, "This account isn't allowed to sync with this app.");
        }
        return null;
    }

    private static ContentResult Json(int status, string body) =>
        new() { StatusCode = status, Content = body, ContentType = "application/json" };

    private static ContentResult Problem(int status, string message) =>
        Json(status, JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = message }, ApiJsonContext.Default.DictionaryStringString));
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class ApiJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
