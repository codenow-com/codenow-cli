using System;
using System.Text.Json;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class FluxCDInfoProviderTests
{
    [Fact]
    public void GetInfo_ReadsSourceControllerMetadata()
    {
        var resourceName = FluxCDInfoProvider.FluxcdManifestsResourceRoot
            + FluxCDInfoProvider.FluxcdInfoFileName;
        var json = ProvisioningCommonTools.ReadEmbeddedResourceText(resourceName);
        if (string.IsNullOrWhiteSpace(json))
        {
            var root = Path.Combine(AppContext.BaseDirectory, "DataPlane", "Manifests", "FluxCD");
            Directory.CreateDirectory(root);
            var infoPath = Path.Combine(root, FluxCDInfoProvider.FluxcdInfoFileName);
            var payload = new
            {
                sourceController = new { image = "ghcr.io/fluxcd/source-controller", version = "v9.9.9" }
            };
            json = JsonSerializer.Serialize(payload);
            File.WriteAllText(infoPath, json);
        }

        var provider = new FluxCDInfoProvider();

        var info = provider.GetInfo();

        using var doc = JsonDocument.Parse(json);
        var sourceController = doc.RootElement.GetProperty("sourceController");
        var image = sourceController.GetProperty("image").GetString();
        var version = sourceController.GetProperty("version").GetString();

        Assert.Equal($"{image}:{version}", info.SourceControllerImage);
        Assert.Equal(version, info.SourceControllerVersion);
    }
}
