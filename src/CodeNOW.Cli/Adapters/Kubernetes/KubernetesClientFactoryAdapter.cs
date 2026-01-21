using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Adapter for the static <see cref="KubernetesClientFactory"/> to enable DI.
/// </summary>
public sealed class KubernetesClientFactoryAdapter : IKubernetesClientFactory
{
    private readonly object sync = new();
    private IKubernetesClient? cachedClient;

    /// <inheritdoc />
    public IKubernetesClient Create(ILogger logger, KubernetesConnectionOptions options)
    {
        // Reuse a single client instance to avoid repeated connection setup and extra sockets.
        if (cachedClient is not null)
            return cachedClient;

        lock (sync)
        {
            if (cachedClient is not null)
                return cachedClient;

            var client = KubernetesClientFactory.Create(logger, options);
            cachedClient = new KubernetesClientAdapter(client);
            return cachedClient;
        }
    }
}
