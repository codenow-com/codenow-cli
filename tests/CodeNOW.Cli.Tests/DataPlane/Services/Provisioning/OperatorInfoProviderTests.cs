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
        var root = Path.Combine(AppContext.BaseDirectory, "DataPlane", "Manifests", "Operator");
        Directory.CreateDirectory(root);
        var infoPath = Path.Combine(root, DataPlaneConstants.OperatorInfoFileName);
        var payload = new
        {
            @operator = new { image = "operator", version = "1.2.3" },
            runtime = new { image = "runtime" },
            plugins = new { image = "plugins" }
        };
        File.WriteAllText(infoPath, JsonSerializer.Serialize(payload));

        var provider = new OperatorInfoProvider();

        var info = provider.GetInfo();

        Assert.Equal("operator:1.2.3", info.OperatorImage);
        Assert.Equal("1.2.3", info.OperatorVersion);
        Assert.Equal("runtime", info.RuntimeImage);
        Assert.Equal("plugins", info.PluginsImage);
    }
}
