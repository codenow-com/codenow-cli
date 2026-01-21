using CodeNOW.Cli.DataPlane.Models;

namespace CodeNOW.Cli.DataPlane.Console.Models;

/// <summary>
/// Tracks the state displayed by the dashboard.
/// </summary>
internal sealed class DashboardState
{
    /// <summary>
    /// Kubernetes cluster version string.
    /// </summary>
    public string ClusterVersion { get; set; } = "Unknown";
    /// <summary>
    /// Aggregated cluster resource values.
    /// </summary>
    public ClusterResources Cluster { get; set; } = ClusterResources.Empty;
    /// <summary>
    /// Current operator status.
    /// </summary>
    public OperatorStatus Operator { get; set; } = OperatorStatus.NotFound;
    /// <summary>
    /// Current stack status.
    /// </summary>
    public StackStatus Stack { get; set; } = StackStatus.Unknown;
    /// <summary>
    /// All log lines currently loaded.
    /// </summary>
    public string[] LogAllLines { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Log lines currently rendered.
    /// </summary>
    public string[] LogLines { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Current log scroll offset.
    /// </summary>
    public int LogOffset { get; set; }
    /// <summary>
    /// Total log line count.
    /// </summary>
    public int LogTotal { get; set; }
    /// <summary>
    /// Number of log lines per page.
    /// </summary>
    public int LogPageSize { get; set; } = 1;
    /// <summary>
    /// Whether log view is following the tail.
    /// </summary>
    public bool LogFollowTail { get; set; }
    /// <summary>
    /// Status message displayed in the dashboard.
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;
    /// <summary>
    /// Whether the status message should remain visible.
    /// </summary>
    public bool StatusSticky { get; set; }
    /// <summary>
    /// Operator pod name.
    /// </summary>
    public string? OperatorPodName { get; set; }
    /// <summary>
    /// Operator pod UID.
    /// </summary>
    public string? OperatorPodUid { get; set; }
    /// <summary>
    /// Workspace pod name.
    /// </summary>
    public string? WorkspacePodName { get; set; }
    /// <summary>
    /// Workspace pod UID.
    /// </summary>
    public string? WorkspacePodUid { get; set; }
    /// <summary>
    /// Pending operator pod name for a fresh log read.
    /// </summary>
    public string? PendingOperatorPodName { get; set; }
    /// <summary>
    /// Pending operator pod UID for a fresh log read.
    /// </summary>
    public string? PendingOperatorPodUid { get; set; }
    /// <summary>
    /// Pending workspace pod name for a fresh log read.
    /// </summary>
    public string? PendingWorkspacePodName { get; set; }
    /// <summary>
    /// Pending workspace pod UID for a fresh log read.
    /// </summary>
    public string? PendingWorkspacePodUid { get; set; }
    /// <summary>
    /// Rendered operator logs.
    /// </summary>
    public string OperatorLogsText { get; set; } = string.Empty;
    /// <summary>
    /// Rendered workspace logs.
    /// </summary>
    public string WorkspaceLogsText { get; set; } = string.Empty;
    /// <summary>
    /// Newly fetched operator logs waiting to be applied.
    /// </summary>
    public string PendingOperatorLogsText { get; set; } = string.Empty;
    /// <summary>
    /// Newly fetched workspace logs waiting to be applied.
    /// </summary>
    public string PendingWorkspaceLogsText { get; set; } = string.Empty;
    /// <summary>
    /// Whether operator logs should force a full refresh.
    /// </summary>
    public bool OperatorForceFreshLogs { get; set; }
    /// <summary>
    /// Whether workspace logs should force a full refresh.
    /// </summary>
    public bool WorkspaceForceFreshLogs { get; set; }
}
