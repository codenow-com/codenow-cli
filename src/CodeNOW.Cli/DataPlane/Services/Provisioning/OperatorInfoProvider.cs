using System.Text.Json;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Operator image and version metadata loaded from packaged manifests.
/// </summary>
/// <param name="OperatorImage">Full operator image reference.</param>
/// <param name="OperatorVersion">Operator image version tag.</param>
/// <param name="RuntimeImage">Pulumi runtime base image.</param>
/// <param name="PluginsImage">Pulumi plugins base image.</param>
internal sealed record OperatorInfo(
    string OperatorImage,
    string OperatorVersion,
    string RuntimeImage,
    string PluginsImage);

/// <summary>
/// Provides operator image metadata from the manifest bundle.
/// </summary>
internal interface IOperatorInfoProvider
{
    /// <summary>
    /// Returns operator image metadata.
    /// </summary>
    OperatorInfo GetInfo();
}

/// <summary>
/// Loads operator image metadata from the bundled JSON file.
/// </summary>
internal sealed class OperatorInfoProvider : IOperatorInfoProvider
{
    /// <inheritdoc />
    public OperatorInfo GetInfo()
    {
        var manifestsRoot = Path.Combine(AppContext.BaseDirectory, "DataPlane", "Manifests", "Operator");
        var infoPath = Path.Combine(manifestsRoot, DataPlaneConstants.OperatorInfoFileName);
        if (!File.Exists(infoPath))
        {
            throw new FileNotFoundException($"Operator metadata file not found at '{infoPath}'");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(infoPath));
        if (!doc.RootElement.TryGetProperty("operator", out var operatorProp))
            throw new InvalidOperationException($"Operator metadata file '{infoPath}' does not contain 'operator'.");
        if (!doc.RootElement.TryGetProperty("runtime", out var runtimeProp))
            throw new InvalidOperationException($"Operator metadata file '{infoPath}' does not contain 'runtime'.");
        if (!doc.RootElement.TryGetProperty("plugins", out var pluginsProp))
            throw new InvalidOperationException($"Operator metadata file '{infoPath}' does not contain 'plugins'.");

        if (!operatorProp.TryGetProperty("image", out var operatorImageProp))
            throw new InvalidOperationException($"Operator metadata file '{infoPath}' does not contain 'operator.image'.");
        if (!operatorProp.TryGetProperty("version", out var operatorVersionProp))
            throw new InvalidOperationException($"Operator metadata file '{infoPath}' does not contain 'operator.version'.");
        if (!runtimeProp.TryGetProperty("image", out var runtimeImageProp))
            throw new InvalidOperationException($"Operator metadata file '{infoPath}' does not contain 'runtime.image'.");
        if (!pluginsProp.TryGetProperty("image", out var pluginsImageProp))
            throw new InvalidOperationException($"Operator metadata file '{infoPath}' does not contain 'plugins.image'.");

        var operatorImage = operatorImageProp.GetString()
            ?? throw new InvalidOperationException("Operator image is null.");
        var operatorVersion = operatorVersionProp.GetString()
            ?? throw new InvalidOperationException("Operator version is null.");
        var runtimeImage = runtimeImageProp.GetString()
            ?? throw new InvalidOperationException("Runtime image is null.");
        var pluginsImage = pluginsImageProp.GetString()
            ?? throw new InvalidOperationException("Plugins image is null.");

        return new OperatorInfo($"{operatorImage}:{operatorVersion}", operatorVersion, runtimeImage, pluginsImage);
    }
}
