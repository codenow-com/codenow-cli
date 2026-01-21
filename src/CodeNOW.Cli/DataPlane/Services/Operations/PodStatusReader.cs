using CodeNOW.Cli.Adapters.Kubernetes;
using k8s.Models;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Loads pod status information by label selector.
/// </summary>
internal sealed class PodStatusReader
{
    private readonly IKubernetesClient client;
    private readonly KubernetesReadExecutor executor;

    /// <summary>
    /// Creates a pod status reader.
    /// </summary>
    public PodStatusReader(IKubernetesClient client, KubernetesReadExecutor executor)
    {
        this.client = client;
        this.executor = executor;
    }

    /// <summary>
    /// Loads the first matching pod in a namespace and returns its status.
    /// </summary>
    /// <param name="namespaceName">Namespace to search.</param>
    /// <param name="labelSelector">Label selector for the target pod.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Pod status result.</returns>
    public Task<PodStatusResult> GetPodStatusAsync(
        string namespaceName,
        string labelSelector,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(async () =>
        {
            var pods = await client.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);

            if (pods.Items.Count == 0)
                return PodStatusResult.Unavailable;

            var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running") ?? pods.Items[0];
            return new PodStatusResult(
                pod,
                PodStatusTextBuilder.BuildPodStatusText(pod),
                pod.Metadata?.NamespaceProperty ?? namespaceName);
        }, $"Failed to list pods in namespace {namespaceName}.", PodStatusResult.Unavailable);
    }

    /// <summary>
    /// Loads the first matching pod across namespaces and returns its status.
    /// </summary>
    /// <param name="labelSelector">Label selector for the target pod.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Pod status result.</returns>
    public Task<PodStatusResult> GetPodStatusForAnyNamespaceAsync(
        string labelSelector,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(async () =>
        {
            var pods = await client.CoreV1.ListPodForAllNamespacesAsync(
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);

            if (pods.Items.Count == 0)
                return PodStatusResult.Unavailable;

            var pod = pods.Items.FirstOrDefault(p => p.Status?.Phase == "Running") ?? pods.Items[0];
            return new PodStatusResult(
                pod,
                PodStatusTextBuilder.BuildPodStatusText(pod),
                pod.Metadata?.NamespaceProperty ?? "Unknown");
        }, "Failed to list pods across namespaces.", PodStatusResult.Unavailable);
    }
}

/// <summary>
/// Result of a pod status lookup.
/// </summary>
/// <param name="Pod">The resolved pod instance, if any.</param>
/// <param name="StatusText">Kubectl-style pod status text.</param>
/// <param name="Namespace">Namespace where the pod was found.</param>
internal readonly record struct PodStatusResult(
    V1Pod? Pod,
    string StatusText,
    string Namespace)
{
    /// <summary>
    /// Represents a missing pod or unavailable status.
    /// </summary>
    public static PodStatusResult Unavailable => new(null, "Unknown", "Unknown");
    /// <summary>
    /// Indicates whether a pod was found.
    /// </summary>
    public bool IsAvailable => Pod is not null;
}
