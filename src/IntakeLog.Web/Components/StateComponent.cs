using Microsoft.AspNetCore.Components;
using IntakeLog.Web.Services;

namespace IntakeLog.Web.Components;

/// <summary>Base for components that re-render when local data or sync status changes.</summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject] protected AppState State { get; set; } = default!;
    [Inject] protected SyncService Sync { get; set; } = default!;

    protected override void OnInitialized()
    {
        State.Changed += OnStateChanged;
        Sync.Changed += OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        State.Changed -= OnStateChanged;
        Sync.Changed -= OnStateChanged;
        GC.SuppressFinalize(this);
    }
}
