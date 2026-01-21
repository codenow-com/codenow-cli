using CodeNOW.Cli.DataPlane.Console.Models;
using CodeNOW.Cli.DataPlane.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CodeNOW.Cli.DataPlane.Console.Renders;

/// <summary>
/// Renders the dashboard layout using the current state.
/// </summary>
internal sealed class DashboardRenderer
{
    /// <summary>
    /// Renders the dashboard layout using the current state.
    /// </summary>
    public void Render(
        Layout layout,
        DashboardState state,
        LogView view,
        int consoleWidth,
        int consoleHeight,
        int topHeight)
    {
        layout["top"].Size(topHeight);
        layout["bottom"].Size(Math.Max(3, consoleHeight - topHeight - 1));
        layout["top"].Update(RenderTopPanel(state));
        layout["bottom"].Update(RenderLogsPanel(
            view,
            state.LogLines,
            Math.Max(1, consoleWidth - 1)));
    }

    private static IRenderable RenderTopPanel(DashboardState state)
    {
        var table = new Table()
            .Expand()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]🟦 Cluster[/]") { Width = 30 })
            .AddColumn(new TableColumn("[bold]🧠 Data Plane Operator[/]") { Width = 30 })
            .AddColumn(new TableColumn("[bold]📦 Data Plane Stack[/]") { Width = 30 });

        table.Rows.Add(new IRenderable[]
        {
            ClusterBlock(state.ClusterVersion, state.Cluster),
            new Markup(OperatorBlock(state.Operator)),
            new Markup(StackBlock(state.Stack))
        });

        return new Rows(
            table,
            new Markup(BuildActionsLine(state)));
    }

    private static string BuildActionsLine(DashboardState state)
    {
        var baseLine =
            "[bold][[R]][/] Reconcile  " +
            "[bold][[D]][/] Dry-run  " +
            "[bold][[O]][/] Operator logs  " +
            "[bold][[W]][/] Workspace logs  " +
            "[bold][[Q]][/] Quit  " +
            "[bold][[↑/↓]][/] Scroll";

        var position = BuildLogPosition(state);
        if (!string.IsNullOrWhiteSpace(position))
            baseLine = $"{baseLine}   [grey]{position}[/]";

        if (string.IsNullOrWhiteSpace(state.StatusMessage))
            return baseLine;

        return $"{baseLine}   [grey]{state.StatusMessage}[/]";
    }

    private static string BuildLogPosition(DashboardState state)
    {
        if (state.LogTotal == 0)
            return string.Empty;

        var start = Math.Min(state.LogOffset + 1, state.LogTotal);
        var end = Math.Min(state.LogOffset + state.LogPageSize, state.LogTotal);
        return $"Logs {start}-{end}/{state.LogTotal}";
    }

    private static IRenderable RenderLogsPanel(LogView view, string[] lines, int maxWidth)
    {
        var title = view == LogView.Operator
            ? "Operator Logs"
            : "Workspace Logs";

        if (lines.Length == 0)
        {
            return new Rows(
                new Markup($"[bold]{title}[/]"),
                new Rule(),
                new Markup("No logs available."));
        }

        var table = new Table
        {
            Border = TableBorder.None,
            ShowHeaders = false,
            Expand = false
        };
        table.AddColumn(new TableColumn(string.Empty)
        {
            Padding = new Padding(0, 0, 0, 0),
            NoWrap = true,
            Width = maxWidth
        });

        foreach (var line in lines)
            table.AddRow(new IRenderable[] { LogLineFormatter.FormatLine(line) });

        return new Rows(
            new Markup($"[bold]{title}[/]"),
            new Rule(),
            table);
    }

    private static IRenderable ClusterBlock(string clusterVersion, ClusterResources resources)
    {
        if (resources.CpuCapacityMilli <= 0 && resources.MemoryCapacityBytes <= 0)
        {
            return new Markup(string.Join("\n", new[]
            {
                Field("Version", clusterVersion),
                Field("CPU (req)", "Unknown"),
                Field("Memory (req)", "Unknown")
            }));
        }

        var table = new Table
        {
            Border = TableBorder.None,
            ShowHeaders = false,
            Expand = true
        };

        table.AddColumn(new TableColumn(string.Empty)
        {
            NoWrap = true,
            Padding = new Padding(0, 0, 0, 0)
        });
        table.AddColumn(new TableColumn(string.Empty)
        {
            Padding = new Padding(0, 0, 0, 0)
        });

        table.Rows.Add(new IRenderable[]
        {
            new Markup($"[grey]{"Version",-15}:[/] "),
            new Markup(clusterVersion)
        });

        table.Rows.Add(new IRenderable[]
        {
            new Markup($"[grey]{"CPU (req)",-15}:[/] "),
            BuildResourceRow(resources.CpuRequestedMilli, resources.CpuCapacityMilli, FormatCpu, FormatCpu)
        });

        table.Rows.Add(new IRenderable[]
        {
            new Markup($"[grey]{"Memory (req)",-15}:[/] "),
            BuildResourceRow(resources.MemoryRequestedBytes, resources.MemoryCapacityBytes, FormatBytesBinary, FormatBytesBinary)
        });

        return table;
    }

    private static string OperatorBlock(OperatorStatus status) =>
        string.Join("\n", new[]
        {
            Field("Version", status.Version),
            Field("Namespace", status.Namespace),
            Field("Status", FormatPodStatus(status.Status))
        });

    private static string StackBlock(StackStatus status) =>
        string.Join("\n", new[]
        {
            Field("Workspace", FormatPodStatus(status.WorkspaceStatus)),
            Field("Stack", FormatStackReady(status.Ready)),
            Field("Reconciling", FormatReconciling(status.ReconcilingReason)),
            Field("Dry-run", status.DryRun)
        });

    private static string Field(string label, string value)
        => $"[grey]{label,-15}:[/] {value}";

    private static IRenderable BuildResourceRow(
        long requested,
        long capacity,
        Func<long, string> requestedFormatter,
        Func<long, string> capacityFormatter)
    {
        if (capacity <= 0)
        {
            return new Markup("Unknown");
        }

        var used = Math.Clamp(requested, 0, capacity);
        var free = Math.Max(0, capacity - used);
        var percent = (int)Math.Round((double)used / capacity * 100d);
        var chart = new BreakdownChart()
            .Width(18)
            .Compact();
        chart.ShowTagValues = false;
        chart.ShowTags = false;

        chart.AddItem("Used", used, GetUsageColor(percent));
        chart.AddItem("Free", free, Color.Grey);

        return new Columns(
            Align.Left(chart),
            new Markup($"{requestedFormatter(requested)} / {capacityFormatter(capacity)}"))
        {
            Padding = new Padding(0, 0, 0, 0),
            Expand = false
        };
    }

    private static Color GetUsageColor(int percent)
    {
        if (percent >= 90)
            return Color.Red;
        if (percent >= 70)
            return Color.Yellow;
        return Color.Green;
    }

    private static string FormatCpu(long milliCores)
    {
        if (milliCores < 1000)
            return $"{milliCores}m";

        return $"{milliCores / 1000d:0.##}";
    }

    private static string FormatBytesBinary(long bytes)
    {
        const double Gi = 1024d * 1024d * 1024d;
        const double Mi = 1024d * 1024d;
        if (bytes >= Gi)
            return $"{bytes / Gi:0.##}Gi";
        if (bytes >= Mi)
            return $"{bytes / Mi:0.##}Mi";
        return $"{bytes}B";
    }

    private static string FormatPodStatus(string status)
    {
        if (string.Equals(status, "Unknown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Not found", StringComparison.OrdinalIgnoreCase))
            return status;

        var color = GetStatusColor(status);
        return $"[{color}]{status}[/]";
    }

    private static string GetStatusColor(string status)
    {
        if (status.Contains("Running", StringComparison.OrdinalIgnoreCase))
            return "green";

        if (status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("CrashLoopBackOff", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("ImagePullBackOff", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("ErrImagePull", StringComparison.OrdinalIgnoreCase))
            return "red";

        return "yellow";
    }

    private static string FormatStackReady(string readyStatus)
    {
        if (string.Equals(readyStatus, "True", StringComparison.OrdinalIgnoreCase))
            return "[green]Ready[/]";
        if (string.Equals(readyStatus, "False", StringComparison.OrdinalIgnoreCase))
            return "[red]Not ready[/]";
        return readyStatus;
    }

    private static string FormatReconciling(string reason)
    {
        if (!string.IsNullOrWhiteSpace(reason) &&
            !string.Equals(reason, "Unknown", StringComparison.OrdinalIgnoreCase))
            return reason.Equals("StackProcessing", StringComparison.OrdinalIgnoreCase) ||
                reason.Equals("RetryingAfterFailure", StringComparison.OrdinalIgnoreCase)
                ? $"[yellow]{reason}[/]"
                : reason;

        return string.Empty;
    }
}
