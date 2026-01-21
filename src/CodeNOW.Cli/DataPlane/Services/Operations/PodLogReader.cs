using System.IO;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.Adapters.Kubernetes;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Reads pod logs by label selector and namespace.
/// </summary>
internal sealed class PodLogReader
{
    private readonly IKubernetesClient client;
    private readonly KubernetesReadExecutor executor;

    /// <summary>
    /// Creates a pod log reader.
    /// </summary>
    public PodLogReader(IKubernetesClient client, KubernetesReadExecutor executor)
    {
        this.client = client;
        this.executor = executor;
    }

    /// <summary>
    /// Reads logs for the first matching pod, preferring a running pod when available.
    /// </summary>
    /// <param name="query">Namespace and tail lines for the log request.</param>
    /// <param name="labelSelector">Label selector for the target pod.</param>
    /// <param name="warningMessage">Warning message to log on failure.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Log read result.</returns>
    public Task<LogReadResult> ReadLogsAsync(
        ManagementLogQuery query,
        string labelSelector,
        string warningMessage,
        CancellationToken cancellationToken)
    {
        string? podName = null;
        string? podUid = null;

        return executor.ExecuteAsync(async () =>
        {
            var pods = await client.CoreV1.ListNamespacedPodAsync(
                query.Namespace,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);
            if (pods.Items.Count == 0)
                return LogReadResult.Unavailable;

            var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running") ?? pods.Items[0];
            podName = pod.Metadata?.Name;
            if (string.IsNullOrWhiteSpace(podName))
                return LogReadResult.Unavailable;

            podUid = pod.Metadata?.Uid;

            await using var logStream = await client.CoreV1.ReadNamespacedPodLogAsync(
                podName,
                query.Namespace,
                tailLines: query.TailLines,
                cancellationToken: cancellationToken);
            using var reader = new StreamReader(logStream);
            return LogReadResult.FromLogs(await reader.ReadToEndAsync(), podName, podUid);
        }, _ =>
        {
            return string.IsNullOrWhiteSpace(podUid)
                ? LogReadResult.Unavailable
                : LogReadResult.UnavailableWithPod(podName, podUid);
        }, warningMessage);
    }
}
