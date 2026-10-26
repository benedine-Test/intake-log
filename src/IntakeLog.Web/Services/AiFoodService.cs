using System.Text;
using IntakeLog.Core;
using Microsoft.JSInterop;

namespace IntakeLog.Web.Services;

/// <summary>
/// Sends food searches straight from the browser to the Claude API with the user's own key.
/// The key is kept in localStorage under its own name, so it never goes into exported backups.
/// </summary>
public sealed class AiFoodService(IJSRuntime js, HttpClient http)
{
    private const string KeyStorageKey = "intake-log-ai-key";

    private readonly Dictionary<string, AiSearchResult> cache = new(StringComparer.OrdinalIgnoreCase);
    private string? apiKey;
    private bool loaded;

    public bool HasKey => !string.IsNullOrWhiteSpace(apiKey);

    public async Task LoadAsync()
    {
        if (loaded) return;
        try { apiKey = await js.InvokeAsync<string?>("intakeLog.load", KeyStorageKey); }
        catch (Exception) { apiKey = null; }
        loaded = true;
    }

    public async Task SetKeyAsync(string? key)
    {
        apiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        cache.Clear();
        if (apiKey is null) await js.InvokeVoidAsync("intakeLog.remove", KeyStorageKey);
        else await js.InvokeVoidAsync("intakeLog.save", KeyStorageKey, apiKey);
    }

    /// <summary>Searches once per query text per session; repeats come from memory.</summary>
    public async Task<AiSearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return new AiSearchResult([]);
        await LoadAsync();
        if (!HasKey) return AiSearchResult.Fail("Add your Claude API key under My plan → AI food search first.");
        if (cache.TryGetValue(query, out var hit)) return hit;

        using var request = new HttpRequestMessage(HttpMethod.Post, AiFoodSearch.Endpoint)
        {
            Content = new StringContent(AiFoodSearch.BuildRequest(query), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AiFoodSearch.ApiVersion);
        request.Headers.Add("anthropic-beta", AiFoodSearch.FallbackBeta);
        // Required for calls made from a web page; the key is the user's own and stays on their device.
        request.Headers.Add("anthropic-dangerous-direct-browser-access", "true");

        AiSearchResult result;
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            result = AiFoodSearch.ParseResponse((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AiSearchResult.Fail("Couldn't reach Claude. AI search needs an internet connection.");
        }

        if (result.Error is null) cache[query] = result;
        return result;
    }
}
