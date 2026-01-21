using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Creates Kubernetes API clients.
/// </summary>
public interface IKubernetesClientFactory
{
    /// <summary>
    /// Creates a Kubernetes client using the provided logger.
    /// </summary>
    IKubernetesClient Create(ILogger logger, KubernetesConnectionOptions options);
}
