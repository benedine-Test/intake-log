using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntakeLog.Api;

/// <summary>
/// The signed-in user, as Azure Static Web Apps passes it to managed functions in the
/// base64-encoded x-ms-client-principal header. Only the platform can set this header
/// on requests that reach the API, so it can be trusted.
/// </summary>
public sealed partial record ClientPrincipal(string IdentityProvider, string UserId, string UserDetails)
{
    public const string HeaderName = "x-ms-client-principal";

    /// <summary>Partition key for this user's rows: provider + id, restricted to table-safe characters.</summary>
    public string StorageKey => Unsafe().Replace($"{IdentityProvider}_{UserId}", "");

    public static ClientPrincipal? FromHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var provider = root.TryGetProperty("identityProvider", out var p) ? p.GetString() : null;
            var id = root.TryGetProperty("userId", out var u) ? u.GetString() : null;
            var details = root.TryGetProperty("userDetails", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(id)) return null;
            return new ClientPrincipal(provider, id, details ?? "");
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// ALLOWED_USERS is an optional comma-separated list of sign-in names (email for
    /// Microsoft accounts, username for GitHub). Empty means any signed-in user, each
    /// seeing only their own data.
    /// </summary>
    public bool IsAllowed(string? allowedUsers)
    {
        if (string.IsNullOrWhiteSpace(allowedUsers)) return true;
        return allowedUsers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .Any(a => a.Equals(UserDetails, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex Unsafe();
}
