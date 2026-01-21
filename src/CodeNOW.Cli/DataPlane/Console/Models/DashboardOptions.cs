namespace CodeNOW.Cli.DataPlane.Console.Models;

/// <summary>
/// Configures dashboard refresh intervals and layout sizing.
/// </summary>
internal sealed record DashboardOptions(
    /// <summary>
    /// Interval, in milliseconds, for refreshing the top summary.
    /// </summary>
    int RefreshMs,
    /// <summary>
    /// Interval, in milliseconds, for refreshing logs.
    /// </summary>
    int LogRefreshMs,
    /// <summary>
    /// Number of log lines to request from the cluster.
    /// </summary>
    int LogTailLines,
    /// <summary>
    /// Height of the top dashboard panel.
    /// </summary>
    int TopHeight);
