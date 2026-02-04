using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Console.Presentation;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Console.Supports;

/// <summary>
/// Verifies Kubernetes connectivity before running commands that need the cluster.
/// </summary>
public sealed class KubernetesConnectionGuard(
    IKubernetesClientFactory clientFactory,
    KubernetesConnectionOptions connectionOptions,
    ILogger<KubernetesConnectionGuard> logger)
{
    /// <summary>
    /// Ensures the CLI can connect to the Kubernetes cluster.
    /// </summary>
    /// <returns>true when connectivity is available; otherwise false.</returns>
    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (await CanConnectAsync(cancellationToken))
            return true;

        ConsoleErrorPrinter.PrintError(
            "Unable to connect to the Kubernetes cluster.",
            "[red]Check the KUBECONFIG environment variable or the --kube-proxy-url value.[/]");
        return false;
    }

    private async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = clientFactory.Create(logger, connectionOptions);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await client.Version.GetCodeAsync(timeoutCts.Token);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to Kubernetes.");
            return false;
        }
    }
}
