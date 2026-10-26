using Microsoft.AspNetCore.Components;
using IntakeLog.Web.Services;

namespace IntakeLog.Web.Components;

/// <summary>Base for components that re-render when <see cref="AppState"/> changes.</summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject] protected AppState State { get; set; } = default!;

    protected override void OnInitialized() => State.Changed += OnStateChanged;

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        State.Changed -= OnStateChanged;
        GC.SuppressFinalize(this);
    }
}
