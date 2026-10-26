using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IntakeLog.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace IntakeLog.Web.Services;

public enum SyncStatus
{
    /// <summary>No sign-in service here (local dev, or hosting without Azure Static Web Apps).</summary>
    Unavailable,
    Checking,
    SignedOut,
    Syncing,
    Synced,
    /// <summary>Couldn't reach the server; changes stay queued on the device.</summary>
    Offline,
    NotAllowed
}

/// <summary>
/// Keeps this device and the server in step. The device copy is always usable on its own;
/// sync runs after every change (debounced), when the app returns to the foreground,
/// when the network comes back, and every few minutes while open.
/// </summary>
public sealed class SyncService : IAsyncDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    private readonly HttpClient http;
    private readonly AppState state;
    private readonly NavigationManager nav;
    private readonly IJSRuntime js;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? debounceCts;
    private PeriodicTimer? timer;
    private DotNetObjectReference<SyncService>? selfRef;
    private bool started;
    private bool rerun;

    public SyncService(HttpClient http, AppState state, NavigationManager nav, IJSRuntime js)
    {
        this.http = http;
        this.state = state;
        this.nav = nav;
        this.js = js;
        state.LocalChanged += ScheduleSoon;
    }

    public SyncStatus Status { get; private set; } = SyncStatus.Checking;
    public string? UserName { get; private set; }
    public string? Provider { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }

    public event Action? Changed;

    public bool IsSignedIn => Status is SyncStatus.Syncing or SyncStatus.Synced or SyncStatus.Offline;
    public int PendingCount => state.Data.Sync.Pending.Count;

    public string StatusText => Status switch
    {
        SyncStatus.Unavailable => state.StorageAvailable ? "Saved on this device" : "Not saving: browser storage is blocked",
        SyncStatus.Checking => "Saved on this device",
        SyncStatus.SignedOut => "Sign in to sync",
        SyncStatus.Syncing => "Syncing…",
        SyncStatus.Synced => PendingCount > 0 ? "Changes waiting to sync" : $"Synced {LastSyncedAt?.ToLocalTime():h:mm tt}",
        SyncStatus.Offline => "Offline · will sync later",
        SyncStatus.NotAllowed => "This account can't sync",
        _ => ""
    };

    /// <summary>Call once after local data has loaded.</summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;

        await RefreshSignInAsync();
        if (IsSignedIn || Status == SyncStatus.Offline) await SyncNowAsync();

        try
        {
            selfRef = DotNetObjectReference.Create(this);
            await js.InvokeVoidAsync("intakeLog.watchForeground", selfRef);
        }
        catch (JSException) { /* sync still runs on changes and on the timer */ }

        timer = new PeriodicTimer(Interval);
        _ = RunTimerAsync(timer);
    }

    [JSInvokable]
    public Task OnForeground() => IsSignedIn ? SyncNowAsync() : RefreshAndSyncAsync();

    public void SignIn(string provider) =>
        nav.NavigateTo($"/.auth/login/{provider}?post_login_redirect_uri={Uri.EscapeDataString("/" + nav.ToBaseRelativePath(nav.Uri))}", forceLoad: true);

    public void SignOut() =>
        nav.NavigateTo("/.auth/logout?post_logout_redirect_uri=/", forceLoad: true);

    /// <summary>Upload queued changes, then download what changed elsewhere.</summary>
    public async Task SyncNowAsync()
    {
        if (Status is SyncStatus.Unavailable or SyncStatus.SignedOut or SyncStatus.NotAllowed or SyncStatus.Checking) return;
        if (!await gate.WaitAsync(0)) { rerun = true; return; } // a sync is running; go again when it finishes
        try
        {
            do
            {
                rerun = false;
                SetStatus(SyncStatus.Syncing);
                await SyncOnceAsync();
            } while (rerun && Status == SyncStatus.Synced);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SyncOnceAsync()
    {
        var data = state.Data;
        try
        {
            SyncMerge.PrepareFirstSync(data, DateTimeOffset.UtcNow);

            var push = SyncMerge.BuildPush(data);
            if (!push.IsEmpty)
            {
                var sent = SyncMerge.Stamps(push);
                using var body = new StringContent(AppDataJson.SerializePayload(push), Encoding.UTF8, "application/json");
                using var res = await http.PostAsync("api/sync", body);
                if (!await HandleFailureAsync(res)) return;
                SyncMerge.MarkPushed(data, sent);
                await state.SaveAsync();
            }

            var since = data.Sync.LastPulledAt is { } t ? $"?since={Uri.EscapeDataString(t.ToString("O"))}" : "";
            using (var res = await http.GetAsync($"api/sync{since}"))
            {
                if (!await HandleFailureAsync(res)) return;
                var remote = AppDataJson.DeserializePayload(await res.Content.ReadAsStringAsync());
                if (remote is not null)
                {
                    SyncMerge.ApplyRemote(data, remote);
                    await state.SaveAsync();
                }
            }

            LastSyncedAt = DateTimeOffset.Now;
            LastError = null;
            SetStatus(SyncStatus.Synced);
        }
        catch (HttpRequestException)
        {
            LastError = "Couldn't reach the server.";
            SetStatus(SyncStatus.Offline);
        }
        catch (JsonException)
        {
            LastError = "The server sent something unexpected.";
            SetStatus(SyncStatus.Offline);
        }
    }

    /// <returns>True when the response succeeded.</returns>
    private async Task<bool> HandleFailureAsync(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode && IsJson(res.Content.Headers.ContentType)) return true;

        switch (res.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                UserName = null;
                SetStatus(SyncStatus.SignedOut);
                break;
            case HttpStatusCode.Forbidden:
                SetStatus(SyncStatus.NotAllowed);
                break;
            default:
                LastError = await ReadErrorAsync(res) ?? $"Sync failed ({(int)res.StatusCode}).";
                SetStatus(SyncStatus.Offline);
                break;
        }
        return false;
    }

    private static bool IsJson(MediaTypeHeaderValue? type) => type?.MediaType?.Contains("json") == true;

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage res)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Ask Static Web Apps who is signed in. Anything but a valid answer means sync isn't offered here.</summary>
    private async Task RefreshSignInAsync()
    {
        try
        {
            using var res = await http.GetAsync(".auth/me");
            if (!res.IsSuccessStatusCode || !IsJson(res.Content.Headers.ContentType))
            {
                SetStatus(SyncStatus.Unavailable);
                return;
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("clientPrincipal", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                UserName = p.TryGetProperty("userDetails", out var u) ? u.GetString() : null;
                Provider = p.TryGetProperty("identityProvider", out var ip) ? ip.GetString() : null;
                SetStatus(SyncStatus.Synced);
            }
            else
            {
                UserName = null;
                SetStatus(SyncStatus.SignedOut);
            }
        }
        catch (HttpRequestException)
        {
            // Offline at start-up: assume the previous session is still signed in and retry later.
            SetStatus(state.Data.Sync.LastPulledAt is null ? SyncStatus.Unavailable : SyncStatus.Offline);
        }
        catch (JsonException)
        {
            SetStatus(SyncStatus.Unavailable);
        }
    }

    private async Task RefreshAndSyncAsync()
    {
        if (Status is SyncStatus.Unavailable or SyncStatus.Offline) await RefreshSignInAsync();
        if (IsSignedIn) await SyncNowAsync();
    }

    private void ScheduleSoon()
    {
        if (!IsSignedIn) { Changed?.Invoke(); return; }
        debounceCts?.Cancel();
        var cts = debounceCts = new CancellationTokenSource();
        _ = Task.Delay(Debounce, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) _ = SyncNowAsync();
        }, TaskScheduler.Default);
        Changed?.Invoke();
    }

    private async Task RunTimerAsync(PeriodicTimer t)
    {
        try
        {
            while (await t.WaitForNextTickAsync())
                await RefreshAndSyncAsync();
        }
        catch (ObjectDisposedException) { }
    }

    private void SetStatus(SyncStatus s)
    {
        Status = s;
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        state.LocalChanged -= ScheduleSoon;
        timer?.Dispose();
        debounceCts?.Cancel();
        selfRef?.Dispose();
        return ValueTask.CompletedTask;
    }
}
