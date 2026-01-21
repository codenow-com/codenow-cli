using CodeNOW.Cli.DataPlane.Models;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Identifies a stack resource by namespace and name.
/// </summary>
/// <param name="Namespace">Namespace containing the stack.</param>
/// <param name="Name">Stack custom resource name.</param>
public readonly record struct ManagementQuery(string Namespace, string Name);

/// <summary>
/// Identifies a log read request by namespace and tail length.
/// </summary>
/// <param name="Namespace">Namespace containing the pod.</param>
/// <param name="TailLines">Number of log lines to tail.</param>
public readonly record struct ManagementLogQuery(string Namespace, int TailLines);

/// <summary>
/// Provides management operations against the data plane Kubernetes resources.
/// </summary>
public interface IManagementService
{
    /// <summary>
    /// Loads the Kubernetes server version string.
    /// </summary>
    Task<string> GetClusterVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads aggregated cluster CPU and memory capacity and requests.
    /// </summary>
    Task<ClusterResources> GetClusterResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the operator pod status and version details.
    /// </summary>
    Task<OperatorStatus> GetOperatorStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the stack and workspace status values.
    /// </summary>
    Task<StackStatus> GetStackStatusAsync(ManagementQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads workspace pod logs.
    /// </summary>
    Task<LogReadResult> GetWorkspaceLogsAsync(ManagementLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads operator pod logs.
    /// </summary>
    Task<LogReadResult> GetOperatorLogsAsync(ManagementLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a reconcile request for the stack.
    /// </summary>
    Task RequestReconcileAsync(ManagementQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles the stack preview (dry-run) flag.
    /// </summary>
    Task<bool> ToggleStackPreviewAsync(
        StackStatus stackStatus,
        ManagementQuery query,
        CancellationToken cancellationToken = default);
}
