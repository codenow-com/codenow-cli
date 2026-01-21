namespace CodeNOW.Cli.DataPlane.Console.Models;

/// <summary>
/// Represents the source of the operator configuration.
/// </summary>
internal enum ConfigSource
{
    /// <summary>
    /// Generate a new configuration using the wizard.
    /// </summary>
    Generate,
    /// <summary>
    /// Load an existing configuration from disk.
    /// </summary>
    Existing
}
