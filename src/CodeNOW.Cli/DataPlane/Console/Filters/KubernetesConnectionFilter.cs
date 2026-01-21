using CodeNOW.Cli.Adapters.Kubernetes;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace CodeNOW.Cli.DataPlane.Console.Filters;

/// <summary>
/// Verifies Kubernetes connectivity before running selected commands.
/// </summary>
internal sealed class KubernetesConnectionFilter(
    IKubernetesClientFactory clientFactory,
    KubernetesConnectionOptions connectionOptions,
    ILogger<KubernetesConnectionFilter> logger,
    ConsoleAppFilter next) : ConsoleAppFilter(next)
{
    public override async Task InvokeAsync(ConsoleAppContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.CommandName))
        {
            await Next.InvokeAsync(context, cancellationToken);
            return;
        }
        if (IsHelpInvocation(context))
        {
            await Next.InvokeAsync(context, cancellationToken);
            return;
        }

        if (!await CanConnectAsync(cancellationToken))
        {
            // Keep error text visible and exit early before running the command.
            AnsiConsole.MarkupLine(
                "[red]Error: Unable to connect to the Kubernetes cluster.[/]");
            AnsiConsole.MarkupLine(
                "[red]Check the KUBECONFIG environment variable or the --kube-proxy-url value.[/]");
            AnsiConsole.WriteLine();
            Environment.ExitCode = 1;
            return;
        }

        await Next.InvokeAsync(context, cancellationToken);
    }

    private static bool IsHelpInvocation(ConsoleAppContext context)
    {
        return HasHelpFlag(context.Arguments)
            || HasHelpFlag(context.CommandArguments)
            || HasHelpFlag(context.EscapedArguments);
    }

    private static bool HasHelpFlag(ReadOnlySpan<string> args)
    {
        foreach (var arg in args)
        {
            if (arg is "-h" or "--help")
                return true;
        }

        return false;
    }

    private static bool HasHelpFlag(IReadOnlyList<string>? args)
    {
        if (args is null)
            return false;

        foreach (var arg in args)
        {
            if (arg is "-h" or "--help")
                return true;
        }

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
