using System.Text.Json;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Pulumi Operator image and version metadata loaded from packaged manifests.
/// </summary>
/// <param name="OperatorImage">Full operator image reference.</param>
/// <param name="OperatorVersion">Operator image version tag.</param>
/// <param name="RuntimeImage">Pulumi runtime base image.</param>
/// <param name="RuntimeVersion">Pulumi runtime image version tag.</param>
/// <param name="PluginsImage">Pulumi plugins base image.</param>
/// <param name="PluginsVersion">Pulumi plugins image version tag.</param>
public sealed record PulumiOperatorInfo(
    string OperatorImage,
    string OperatorVersion,
    string RuntimeImage,
    string RuntimeVersion,
    string PluginsImage,
    string PluginsVersion);

/// <summary>
/// Provides Pulumi operator image metadata from the manifest bundle.
/// </summary>
public interface IPulumiOperatorInfoProvider
{
    /// <summary>
    /// Returns operator image metadata.
    /// </summary>
    PulumiOperatorInfo GetInfo();
}

/// <summary>
/// Loads Pulumi operator image metadata from the bundled JSON file.
/// </summary>
internal sealed class PulumiOperatorInfoProvider : IPulumiOperatorInfoProvider
{
    /// <summary>
    /// Pulumi operator info file name bundled with the manifests.
    /// </summary>
    public const string PulumiOperatorInfoFileName = "pulumi-operator-info.json";

    /// <inheritdoc />
    public PulumiOperatorInfo GetInfo()
    {
        var resourceName = PulumiOperatorProvisioner.PulumiOperatorManifestsResourceRoot + PulumiOperatorInfoFileName;
        var json = ProvisioningCommonTools.ReadEmbeddedResourceText(resourceName);
        var sourceName = resourceName;
        if (string.IsNullOrWhiteSpace(json))
        {
            var infoPath = Path.Combine(
                AppContext.BaseDirectory,
                PulumiOperatorProvisioner.PulumiOperatorManifestsRelativePath,
                PulumiOperatorInfoFileName);
            if (!File.Exists(infoPath))
            {
                throw new FileNotFoundException(
                    $"Operator metadata file not found at '{infoPath}' and embedded resource '{resourceName}' is missing.");
            }

            json = File.ReadAllText(infoPath);
            sourceName = infoPath;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("operator", out var operatorProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'operator'.");
        if (!doc.RootElement.TryGetProperty("runtime", out var runtimeProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'runtime'.");
        if (!doc.RootElement.TryGetProperty("plugins", out var pluginsProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'plugins'.");

        if (!operatorProp.TryGetProperty("image", out var operatorImageProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'operator.image'.");
        if (!operatorProp.TryGetProperty("version", out var operatorVersionProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'operator.version'.");
        if (!runtimeProp.TryGetProperty("image", out var runtimeImageProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'runtime.image'.");
        if (!runtimeProp.TryGetProperty("version", out var runtimeVersionProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'runtime.version'.");
        if (!pluginsProp.TryGetProperty("image", out var pluginsImageProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'plugins.image'.");
        if (!pluginsProp.TryGetProperty("version", out var pluginsVersionProp))
            throw new InvalidOperationException($"Operator metadata file '{sourceName}' does not contain 'plugins.version'.");

        var operatorImage = operatorImageProp.GetString()
            ?? throw new InvalidOperationException("Operator image is null.");
        var operatorVersion = operatorVersionProp.GetString()
            ?? throw new InvalidOperationException("Operator version is null.");
        var runtimeImage = runtimeImageProp.GetString()
            ?? throw new InvalidOperationException("Runtime image is null.");
        var runtimeVersion = runtimeVersionProp.GetString()
            ?? throw new InvalidOperationException("Runtime version is null.");
        var pluginsImage = pluginsImageProp.GetString()
            ?? throw new InvalidOperationException("Plugins image is null.");
        var pluginsVersion = pluginsVersionProp.GetString()
            ?? throw new InvalidOperationException("Plugins version is null.");

        return new PulumiOperatorInfo(
            $"{operatorImage}:{operatorVersion}",
            operatorVersion,
            runtimeImage,
            runtimeVersion,
            pluginsImage,
            pluginsVersion);
    }

}
