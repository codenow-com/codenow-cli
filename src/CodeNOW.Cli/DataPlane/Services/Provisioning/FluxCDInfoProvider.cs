using System.Text.Json;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// FluxCD image and version metadata loaded from packaged manifests.
/// </summary>
/// <param name="SourceControllerImage">Full source-controller image reference.</param>
/// <param name="SourceControllerVersion">Source-controller image version tag.</param>
public sealed record FluxCDInfo(
    string SourceControllerImage,
    string SourceControllerVersion);

/// <summary>
/// Provides FluxCD image metadata from the manifest bundle.
/// </summary>
public interface IFluxCDInfoProvider
{
    /// <summary>
    /// Returns FluxCD image metadata.
    /// </summary>
    FluxCDInfo GetInfo();
}

/// <summary>
/// Loads FluxCD image metadata from the bundled JSON file.
/// </summary>
internal sealed class FluxCDInfoProvider : IFluxCDInfoProvider
{
    /// <summary>
    /// Relative path to embedded FluxCD manifests on disk.
    /// </summary>
    internal const string FluxcdManifestsRelativePath = "DataPlane/Manifests/FluxCD";
    /// <summary>
    /// Embedded resource root for FluxCD manifests.
    /// </summary>
    internal const string FluxcdManifestsResourceRoot = "DataPlane/Manifests/FluxCD/";
    /// <summary>
    /// FluxCD source-controller manifest file name.
    /// </summary>
    internal const string FluxcdSourceControllerManifestFileName = "source-controller.yaml";
    /// <summary>
    /// FluxCD info file name bundled with the manifests.
    /// </summary>
    internal const string FluxcdInfoFileName = "fluxcd-info.json";

    /// <inheritdoc />
    public FluxCDInfo GetInfo()
    {
        var resourceName = FluxcdManifestsResourceRoot + FluxcdInfoFileName;
        var json = ProvisioningCommonTools.ReadEmbeddedResourceText(resourceName);
        var sourceName = resourceName;
        if (string.IsNullOrWhiteSpace(json))
        {
            var infoPath = Path.Combine(
                AppContext.BaseDirectory,
                FluxcdManifestsRelativePath,
                FluxcdInfoFileName);
            if (!File.Exists(infoPath))
            {
                throw new FileNotFoundException(
                    $"FluxCD metadata file not found at '{infoPath}' and embedded resource '{resourceName}' is missing.");
            }

            json = File.ReadAllText(infoPath);
            sourceName = infoPath;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("sourceController", out var sourceControllerProp))
            throw new InvalidOperationException($"FluxCD metadata file '{sourceName}' does not contain 'sourceController'.");
        if (!sourceControllerProp.TryGetProperty("image", out var imageProp))
            throw new InvalidOperationException($"FluxCD metadata file '{sourceName}' does not contain 'sourceController.image'.");
        if (!sourceControllerProp.TryGetProperty("version", out var versionProp))
            throw new InvalidOperationException($"FluxCD metadata file '{sourceName}' does not contain 'sourceController.version'.");

        var image = imageProp.GetString()
            ?? throw new InvalidOperationException("FluxCD source-controller image is null.");
        var version = versionProp.GetString()
            ?? throw new InvalidOperationException("FluxCD source-controller version is null.");

        return new FluxCDInfo($"{image}:{version}", version);
    }
}
