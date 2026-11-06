using CodeNOW.Cli.DataPlane.Console.Supports;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using ConsoleAppFramework;
using Spectre.Console;

namespace CodeNOW.Cli.DataPlane.Console.Commands;

/// <summary>CLI command installing the Update Controller model.</summary>
public class InstallCommand(InstallService installService, KubernetesConnectionGuard connectionGuard)
{
    /// <summary>
    /// Installs the Data Plane Update Controller. Unlike the operator-based dp bootstrap, setup continues
    /// asynchronously: the scheduled Controller registers the cluster and deploys the Data Plane.
    /// </summary>
    /// <param name="config">Path to the installation configuration (cn-data-plane-install.schema.json).</param>
    /// <param name="recoverController">Restore the existing CronJob to :latest after any uncertain Controller execution is confirmed stopped.</param>
    /// <returns>Process exit code.</returns>
    [Command("install")]
    public async Task<int> Install(string config, bool recoverController = false)
    {
        CodeNOW.Cli.DataPlane.Models.InstallConfig document;
        try
        {
            document = InstallConfigLoader.Load(config);
        }
        catch (InstallConfigException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
            return 1;
        }

        if (!await connectionGuard.EnsureConnectedAsync())
            return 1;

        try
        {
            await installService.InstallAsync(document, recoverController);
        }
        catch (InstallBlockedException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
            return 1;
        }

        // Not through AnsiConsole, which wraps long lines and would break the commands for copying.
        foreach (var line in SuccessLines(document.NamespaceName!))
            global::System.Console.Out.WriteLine(line);

        return 0;
    }

    /// <summary>Installation output: completion means the Controller is installed, not deployed.</summary>
    public static IEnumerable<string> SuccessLines(string namespaceName)
    {
        yield return "Update Controller installed. Registration and Data Plane deployment continue asynchronously on the schedule.";
        yield return "cn dp dashboard does not support this installation model. Inspect it with:";
        yield return $"  kubectl -n {namespaceName} get cronjob cn-dp-update-controller";
        yield return $"  kubectl -n {namespaceName} get jobs -l app.kubernetes.io/name=cn-dp-deployer";
        yield return $"  kubectl -n {namespaceName} get pods -l app.kubernetes.io/part-of=codenow-data-plane";
        yield return $"  kubectl -n {namespaceName} logs -l app.kubernetes.io/name=cn-dp-update-controller --tail=100";
        yield return $"  kubectl -n {namespaceName} logs -l app.kubernetes.io/name=cn-dp-deployer --tail=100";
    }
}
