using System.Text.Json;
using CodeNOW.Cli.DataPlane.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Console.Supports;

/// <summary>
/// Reads Pulumi operator image metadata from the bundled manifest.
/// </summary>
internal sealed class PulumiOperatorInfoProvider
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes the provider with a logger for metadata warnings.
    /// </summary>
    public PulumiOperatorInfoProvider(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ensures the runtime image version is set on the config.
    /// </summary>
    public void EnsurePulumiRuntimeVersion(OperatorConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Pulumi.Images.RuntimeVersion))
            return;

        config.Pulumi.Images.RuntimeVersion = LoadRuntimeImageVersion();
    }

    /// <summary>
    /// Ensures the plugins image version is set on the config.
    /// </summary>
    public void EnsurePulumiPluginsVersion(OperatorConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Pulumi.Images.PluginsVersion))
            return;

        config.Pulumi.Images.PluginsVersion = LoadPluginsImageVersion();
    }

    /// <summary>
    /// Loads the runtime image version from the operator metadata file.
    /// </summary>
    public string LoadRuntimeImageVersion()
    {
        var infoPath = GetOperatorInfoPath();
        if (!File.Exists(infoPath))
        {
            _logger.LogWarning("Operator metadata file not found at {Path}.", infoPath);
            return "";
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(infoPath));
        if (!doc.RootElement.TryGetProperty("runtime", out var runtimeProp))
        {
            _logger.LogWarning("Operator metadata file does not contain 'runtime'.");
            return "";
        }
        if (!runtimeProp.TryGetProperty("version", out var runtimeVersionProp))
        {
            _logger.LogWarning("Operator metadata file does not contain 'runtime.version'.");
            return "";
        }

        return runtimeVersionProp.GetString() ?? "";
    }

    /// <summary>
    /// Loads the plugins image version from the operator metadata file.
    /// </summary>
    public string LoadPluginsImageVersion()
    {
        var infoPath = GetOperatorInfoPath();
        if (!File.Exists(infoPath))
        {
            _logger.LogWarning("Operator metadata file not found at {Path}.", infoPath);
            return "";
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(infoPath));
        if (!doc.RootElement.TryGetProperty("plugins", out var pluginsProp))
        {
            _logger.LogWarning("Operator metadata file does not contain 'plugins'.");
            return "";
        }
        if (!pluginsProp.TryGetProperty("version", out var pluginsVersionProp))
        {
            _logger.LogWarning("Operator metadata file does not contain 'plugins.version'.");
            return "";
        }

        return pluginsVersionProp.GetString() ?? "";
    }

    private static string GetOperatorInfoPath()
    {
        var manifestsRoot = Path.Combine(AppContext.BaseDirectory, "DataPlane", "Manifests", "Operator");
        return Path.Combine(manifestsRoot, "operator-info.json");
    }
}
