using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.Adapters.Kubernetes;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Reads operator status and logs.
/// </summary>
internal sealed class OperatorStatusReader
{
    private readonly IKubernetesClient client;
    private readonly KubernetesReadExecutor executor;
    private readonly PodLogReader podLogReader;
    private readonly PodStatusReader podStatusReader;

    /// <summary>
    /// Creates a reader for operator status and logs.
    /// </summary>
    public OperatorStatusReader(IKubernetesClient client, KubernetesReadExecutor executor)
    {
        this.client = client;
        this.executor = executor;
        podLogReader = new PodLogReader(client, executor);
        podStatusReader = new PodStatusReader(client, executor);
    }

    /// <summary>
    /// Returns the operator pod status across all namespaces.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Operator status values.</returns>
    public async Task<OperatorStatus> GetOperatorStatusAsync(CancellationToken cancellationToken)
    {
        var result = await podStatusReader.GetPodStatusForAnyNamespaceAsync(
            DataPlaneConstants.OperatorPodLabelSelector,
            cancellationToken);
        if (!result.IsAvailable)
            return OperatorStatus.NotFound;

        var version = GetOperatorVersion(result.Pod!);
        return new OperatorStatus(result.Namespace, version, result.StatusText);
    }

    /// <summary>
    /// Reads operator logs from the specified namespace.
    /// </summary>
    /// <param name="query">Namespace and tail lines for the log request.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Log read result.</returns>
    public async Task<LogReadResult> GetOperatorLogsAsync(
        ManagementLogQuery query,
        CancellationToken cancellationToken)
    {
        return await podLogReader.ReadLogsAsync(
            query,
            DataPlaneConstants.OperatorPodLabelSelector,
            "Failed to read operator logs.",
            cancellationToken);
    }

    private static string GetOperatorVersion(k8s.Models.V1Pod pod)
    {
        if (pod.Metadata?.Labels is not null &&
            pod.Metadata.Labels.TryGetValue("app.kubernetes.io/version", out var labelVersion) &&
            !string.IsNullOrWhiteSpace(labelVersion))
        {
            return labelVersion;
        }
        return "Unknown";
    }
}
