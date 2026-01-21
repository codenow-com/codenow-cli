using CodeNOW.Cli.DataPlane.Models;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Defines bootstrap operations for the data plane components.
/// </summary>
public interface IBootstrapService
{
    /// <summary>
    /// Bootstraps or updates the data plane operator and related resources.
    /// </summary>
    Task BootstrapAsync(OperatorConfig config);
}
