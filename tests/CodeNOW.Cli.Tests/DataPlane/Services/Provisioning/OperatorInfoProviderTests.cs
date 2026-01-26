using System.Text.Json;
using CodeNOW.Cli.DataPlane;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class OperatorInfoProviderTests
{
    [Fact]
    public void GetInfo_ReadsOperatorMetadata()
    {
        var resourceName = PulumiOperatorProvisioner.PulumiOperatorManifestsResourceRoot
            + PulumiOperatorInfoProvider.PulumiOperatorInfoFileName;
        var json = ProvisioningCommonTools.ReadEmbeddedResourceText(resourceName);
        if (string.IsNullOrWhiteSpace(json))
        {
            var root = Path.Combine(AppContext.BaseDirectory, "DataPlane", "Manifests", "PulumiOperator");
            Directory.CreateDirectory(root);
            var infoPath = Path.Combine(root, PulumiOperatorInfoProvider.PulumiOperatorInfoFileName);
            var payload = new
            {
                @operator = new { image = "operator", version = "1.2.3" },
                runtime = new { image = "runtime", version = "3.2.1" },
                plugins = new { image = "plugins", version = "9.9.9" }
            };
            json = JsonSerializer.Serialize(payload);
            File.WriteAllText(infoPath, json);
        }

        var provider = new PulumiOperatorInfoProvider();

        var info = provider.GetInfo();

        using var doc = JsonDocument.Parse(json);
        var operatorProp = doc.RootElement.GetProperty("operator");
        var runtimeProp = doc.RootElement.GetProperty("runtime");
        var pluginsProp = doc.RootElement.GetProperty("plugins");
        var operatorImage = operatorProp.GetProperty("image").GetString();
        var operatorVersion = operatorProp.GetProperty("version").GetString();
        var runtimeImage = runtimeProp.GetProperty("image").GetString();
        var runtimeVersion = runtimeProp.GetProperty("version").GetString();
        var pluginsImage = pluginsProp.GetProperty("image").GetString();
        var pluginsVersion = pluginsProp.GetProperty("version").GetString();

        Assert.Equal($"{operatorImage}:{operatorVersion}", info.OperatorImage);
        Assert.Equal(operatorVersion, info.OperatorVersion);
        Assert.Equal(runtimeImage, info.RuntimeImage);
        Assert.Equal(runtimeVersion, info.RuntimeVersion);
        Assert.Equal(pluginsImage, info.PluginsImage);
        Assert.Equal(pluginsVersion, info.PluginsVersion);
    }
}
