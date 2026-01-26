using CodeNOW.Cli.DataPlane.Models;

namespace CodeNOW.Cli.DataPlane.Console.Supports;

/// <summary>
/// Applies CLI overrides to the operator configuration.
/// </summary>
internal static class BootstrapConfigOverrides
{
    /// <summary>
    /// Applies FluxCD related overrides to the configuration.
    /// </summary>
    /// <param name="opConfig">Target operator configuration.</param>
    /// <param name="fluxcdEnable">Whether FluxCD should be enabled.</param>
    /// <param name="fluxcdSkipCrds">Whether to skip FluxCD CRD installation.</param>
    public static void ApplyFluxcdFlags(OperatorConfig opConfig, bool fluxcdEnable, bool fluxcdSkipCrds)
    {
        if (!fluxcdEnable)
        {
            opConfig.FluxCD = null;
            return;
        }

        opConfig.FluxCD ??= new FluxCDConfig();
        opConfig.FluxCD.Enabled = true;
        opConfig.FluxCD.InstallCrds = !fluxcdSkipCrds;
    }

    /// <summary>
    /// Applies Pulumi related overrides to the configuration.
    /// </summary>
    /// <param name="opConfig">Target operator configuration.</param>
    /// <param name="pulumiSkipCrds">Whether to skip Pulumi CRD installation.</param>
    public static void ApplyPulumiFlags(OperatorConfig opConfig, bool pulumiSkipCrds)
    {
        opConfig.Pulumi.InstallCrds = !pulumiSkipCrds;
    }
}
