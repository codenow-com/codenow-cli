namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Connection settings for Kubernetes client initialization.
/// </summary>
public sealed class KubernetesConnectionOptions
{
    /// <summary>
    /// Proxy URL to use when connecting via kubectl proxy.
    /// </summary>
    public string? ProxyUrl { get; init; }
}
