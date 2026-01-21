using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Yaml;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class PulumiOperatorProvisionerTests
{
    [Fact]
    public void GetOperatorImage_PrependsRegistry()
    {
        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            ContainerRegistry = { Hostname = "registry.local" }
        };

        var image = provisioner.GetOperatorImage(config);

        Assert.StartsWith("registry.local/", image, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForOperatorReadyAsync_ReturnsWhenDeploymentReady()
    {
        var provisioner = BuildProvisioner();
        var client = new FakeKubernetesClient();
        client.AppsV1.ReadNamespacedDeploymentAsyncHandler = (_, _) =>
            Task.FromResult(new V1Deployment
            {
                Spec = new V1DeploymentSpec { Replicas = 1 },
                Status = new V1DeploymentStatus { ReadyReplicas = 1 }
            });

        await provisioner.WaitForOperatorReadyAsync(client, "ns", TimeSpan.FromSeconds(1));
    }

    private static PulumiOperatorProvisioner BuildProvisioner()
    {
        var logger = new NullLogger<PulumiOperatorProvisioner>();
        var yaml = new YamlToJsonConverter();
        var infoProvider = new FakeOperatorInfoProvider();
        return new PulumiOperatorProvisioner(logger, yaml, infoProvider);
    }

    private sealed class FakeOperatorInfoProvider : IOperatorInfoProvider
    {
        public OperatorInfo GetInfo() => new("operator", "1.2.3", "runtime", "plugins");
    }
}
