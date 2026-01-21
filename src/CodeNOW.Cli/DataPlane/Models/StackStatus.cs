namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Captures the status of the Pulumi stack and workspace.
/// </summary>
/// <param name="WorkspaceStatus">Workspace pod status text.</param>
/// <param name="Ready">Ready condition status.</param>
/// <param name="ReconcilingReason">Reconciling reason value.</param>
/// <param name="DryRun">Dry-run preview flag.</param>
public sealed record StackStatus(
    string WorkspaceStatus,
    string Ready,
    string ReconcilingReason,
    string DryRun)
{
    /// <summary>
    /// Unknown stack status fallback.
    /// </summary>
    public static readonly StackStatus Unknown = new("Unknown", "Unknown", "Unknown", "Disabled");
}
