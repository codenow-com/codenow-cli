using k8s;
using Microsoft.Extensions.Logging;
using K8sClient = k8s.Kubernetes;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Creates Kubernetes API clients using local configuration defaults.
/// </summary>
public static class KubernetesClientFactory
{
    /// <summary>
    /// Creates a Kubernetes client configured to use the kubectl proxy.
    /// </summary>
    /// <param name="logger">Logger used to report connection details.</param>
    /// <param name="options">Connection options.</param>
    /// <returns>Configured Kubernetes client instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public static K8sClient Create(ILogger logger, KubernetesConnectionOptions options)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var kubeConfigPath = ResolveKubeConfigPath(logger);
        if (!string.IsNullOrWhiteSpace(kubeConfigPath))
        {
            logger.LogInformation("Connecting to Kubernetes using kubeconfig at {Path} ...", kubeConfigPath);
            var kubeConfig = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeConfigPath);
            return new K8sClient(kubeConfig);
        }

        var proxyUrl = ResolveProxyUrl(options.ProxyUrl);
        logger.LogInformation("Connecting to Kubernetes via kubectl proxy at {ProxyUrl} ...", proxyUrl);

        var proxyConfig = new KubernetesClientConfiguration
        {
            Host = proxyUrl,
        };

        return new K8sClient(proxyConfig);
    }

    private static string? ResolveKubeConfigPath(ILogger logger)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        var env = Environment.GetEnvironmentVariable("KUBECONFIG");
        if (string.IsNullOrWhiteSpace(env))
            return null;

        foreach (var path in env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = path.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            if (File.Exists(trimmed))
                return trimmed;
        }

        logger.LogWarning("KUBECONFIG was set but no readable files were found.");
        return null;
    }

    private static string ResolveProxyUrl(string? proxyUrl)
    {
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            if (!proxyUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !proxyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return $"http://{proxyUrl}";
            }

            return proxyUrl;
        }

        return "http://127.0.0.1:8001";
    }
}
