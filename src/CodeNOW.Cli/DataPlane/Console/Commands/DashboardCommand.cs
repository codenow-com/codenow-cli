using CodeNOW.Cli.Common.Console.Presentation;
using CodeNOW.Cli.DataPlane.Console.Actions;
using CodeNOW.Cli.DataPlane.Console.Logs;
using CodeNOW.Cli.DataPlane.Console.Models;
using CodeNOW.Cli.DataPlane.Console.Renders;
using CodeNOW.Cli.DataPlane.Console.Runtimes;
using CodeNOW.Cli.DataPlane.Console.Supports;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Operations;
using ConsoleAppFramework;
using Spectre.Console;

namespace CodeNOW.Cli.DataPlane.Console.Commands;

/// <summary>
/// CLI command for displaying the data plane dashboard.
/// </summary>
[HideBanner]
public class DashboardCommand(
    IManagementService managementService,
    KubernetesConnectionGuard connectionGuard)
{
    /// <summary>Displays a management dashboard with the current state of the Data Plane.</summary>
    [Command("dashboard")]
    public async Task Dashboard()
    {
        if (!await connectionGuard.EnsureConnectedAsync())
            return;

        var currentView = LogView.Workspace;
        var options = new DashboardOptions(
            RefreshMs: 1000,
            LogRefreshMs: 1000,
            LogTailLines: 1000,
            TopHeight: 11);
        const int logsChromeHeight = 2;
        var consoleHost = new SystemConsoleHost();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.Yes,
            Out = new ForcedTerminalOutput(
                new StreamWriter(consoleHost.OpenStandardOutput()) { AutoFlush = true },
                consoleHost)
        });
        var clusterVersion = await managementService.GetClusterVersionAsync();

        var layout = new Layout("root")
            .SplitRows(
                new Layout("top"),
                new Layout("bottom"));

        layout["top"].Size(options.TopHeight);
        layout["bottom"].Size(Math.Max(3, console.Profile.Height - options.TopHeight - 1));
        layout["top"].Update(new Markup("Loading..."));
        layout["bottom"].Update(new Markup("Loading logs..."));

        await console.Live(layout)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Crop)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async ctx =>
            {
                var state = new DashboardState
                {
                    Operator = OperatorStatus.NotFound,
                    Stack = StackStatus.Unknown,
                    Cluster = ClusterResources.Empty,
                    StatusMessage = string.Empty,
                    LogLines = Array.Empty<string>(),
                    ClusterVersion = clusterVersion,
                    LogFollowTail = true
                };
                var renderer = new DashboardRenderer();
                var logTailer = new LogTailer(managementService, console, options.TopHeight, logsChromeHeight);
                var inputHandler = new DashboardKeyHandler(consoleHost, managementService, logTailer);
                var messageUntil = DateTime.MinValue;
                var nextTopUpdate = DateTime.MinValue;
                var nextLogsUpdate = DateTime.MinValue;

                while (true)
                {
                    var now = DateTime.UtcNow;
                    var needsRender = false;

                    if (now >= nextTopUpdate)
                    {
                        await UpdateTopAsync(managementService, state);
                        nextTopUpdate = now.AddMilliseconds(options.RefreshMs);
                        needsRender = true;
                    }

                    if (now >= nextLogsUpdate)
                    {
                        await logTailer.UpdateLogsAsync(state, currentView, options.LogTailLines);
                        nextLogsUpdate = now.AddMilliseconds(options.LogRefreshMs);
                        needsRender = true;
                    }

                    if (!string.IsNullOrWhiteSpace(state.StatusMessage) && DateTime.UtcNow >= messageUntil)
                    {
                        if (!state.StatusSticky)
                        {
                            state.StatusMessage = string.Empty;
                            nextTopUpdate = DateTime.MinValue;
                            needsRender = true;
                        }
                    }

                    var inputResult = await inputHandler.HandleAsync(state, currentView);
                    if (inputResult.Exit)
                        return;

                    currentView = inputResult.View;
                    if (inputResult.MessageUntil.HasValue)
                        messageUntil = inputResult.MessageUntil.Value;
                    if (inputResult.ForceTopRefresh)
                        nextTopUpdate = DateTime.MinValue;
                    if (inputResult.ForceLogsRefresh)
                        nextLogsUpdate = DateTime.MinValue;
                    if (inputResult.NeedsRender)
                        needsRender = true;

                    if (needsRender)
                    {
                        renderer.Render(
                            layout,
                            state,
                            currentView,
                            console.Profile.Width,
                            console.Profile.Height,
                            options.TopHeight);
                        ctx.Refresh();
                    }

                    await Task.Delay(100);
                }
            });
    }

    private static async Task UpdateTopAsync(IManagementService managementService, DashboardState state)
    {
        state.Cluster = await managementService.GetClusterResourcesAsync();
        state.Operator = await managementService.GetOperatorStatusAsync();
        state.Stack = await managementService.GetStackStatusAsync(
            new ManagementQuery(state.Operator.Namespace, DataPlaneConstants.StackName));
    }
}
