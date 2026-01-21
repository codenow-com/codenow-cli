using CodeNOW.Cli.DataPlane.Console.Logs;
using CodeNOW.Cli.DataPlane.Console.Models;
using CodeNOW.Cli.DataPlane.Console.Runtimes;
using CodeNOW.Cli.DataPlane.Services.Operations;

namespace CodeNOW.Cli.DataPlane.Console.Actions;

/// <summary>
/// Handles keyboard input for the dashboard.
/// </summary>
internal sealed class DashboardKeyHandler
{
    private readonly IConsoleHost _console;
    private readonly IManagementService _service;
    private readonly LogTailer _logTailer;

    /// <summary>
    /// Creates a dashboard key handler.
    /// </summary>
    public DashboardKeyHandler(IConsoleHost console, IManagementService service, LogTailer logTailer)
    {
        _console = console;
        _service = service;
        _logTailer = logTailer;
    }

    /// <summary>
    /// Handles key input and updates the dashboard state.
    /// </summary>
    /// <param name="state">Current dashboard state.</param>
    /// <param name="currentView">Currently active log view.</param>
    /// <returns>Result describing UI updates after handling the key.</returns>
    public async Task<DashboardKeyResult> HandleAsync(DashboardState state, LogView currentView)
    {
        if (!_console.KeyAvailable)
            return DashboardKeyResult.Noop(currentView);

        var key = _console.ReadKey(intercept: true);
        var view = currentView;
        var needsRender = false;
        var forceTopRefresh = false;
        var forceLogsRefresh = false;
        DateTime? messageUntil = null;

        switch (char.ToUpperInvariant(key.KeyChar))
        {
            case 'R':
                await _service.RequestReconcileAsync(
                    new ManagementQuery(state.Operator.Namespace, DataPlaneConstants.StackName));
                state.StatusMessage = "Reconcile requested.";
                state.StatusSticky = false;
                messageUntil = DateTime.UtcNow.AddSeconds(3);
                forceTopRefresh = true;
                needsRender = true;
                break;

            case 'O':
                view = LogView.Operator;
                _logTailer.ResetLogCursor(state);
                forceLogsRefresh = true;
                needsRender = true;
                break;

            case 'W':
                view = LogView.Workspace;
                _logTailer.ResetLogCursor(state);
                forceLogsRefresh = true;
                needsRender = true;
                break;

            case 'D':
                var toggled = await _service.ToggleStackPreviewAsync(
                    state.Stack,
                    new ManagementQuery(state.Operator.Namespace, DataPlaneConstants.StackName));
                state.StatusMessage = toggled ? "Dry-run enabled." : "Dry-run disabled.";
                state.StatusSticky = false;
                messageUntil = DateTime.UtcNow.AddSeconds(3);
                forceTopRefresh = true;
                needsRender = true;
                break;

            case 'N':
                if (_logTailer.RequestFreshLogs(state, view))
                {
                    _logTailer.ResetLogCursor(state);
                    state.StatusSticky = false;
                    state.StatusMessage = string.Empty;
                    state.LogFollowTail = true;
                    _logTailer.ApplyUnavailableMessage(state);
                    forceLogsRefresh = true;
                    needsRender = true;
                }
                break;

            case 'Q':
                return DashboardKeyResult.Quit(view);
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                needsRender |= _logTailer.ScrollUp(state);
                break;
            case ConsoleKey.DownArrow:
                needsRender |= _logTailer.ScrollDown(state);
                break;
        }

        return new DashboardKeyResult(
            view,
            false,
            needsRender,
            forceTopRefresh,
            forceLogsRefresh,
            messageUntil);
    }
}
