using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class PulumiStackProvisionerTests
{
    [Fact]
    public async Task CreatePulumiStatePvcAsync_SkipsWhenS3Enabled()
    {
        var client = new FakeKubernetesClient();
        var readCalled = false;
        client.CoreV1.ReadNamespacedPersistentVolumeClaimAsyncHandler = (_, _) =>
        {
            readCalled = true;
            return Task.FromResult(new V1PersistentVolumeClaim());
        };

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            S3 = { Enabled = true }
        };

        await provisioner.CreatePulumiStatePvcAsync(client, config);

        Assert.False(readCalled);
    }

    [Fact]
    public async Task CreatePulumiStatePvcAsync_PatchesWhenStorageChanges()
    {
        var client = new FakeKubernetesClient();
        var patchCalled = false;
        client.CoreV1.ReadNamespacedPersistentVolumeClaimAsyncHandler = (_, _) =>
        {
            var pvc = new V1PersistentVolumeClaim
            {
                Spec = new V1PersistentVolumeClaimSpec
                {
                    Resources = new V1VolumeResourceRequirements
                    {
                        Requests = new Dictionary<string, ResourceQuantity>
                        {
                            ["storage"] = new ResourceQuantity("500Mi")
                        }
                    }
                }
            };
            return Task.FromResult(pvc);
        };
        client.CoreV1.PatchNamespacedPersistentVolumeClaimAsyncHandler = (_, _, _) =>
        {
            patchCalled = true;
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            S3 = { Enabled = false },
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        await provisioner.CreatePulumiStatePvcAsync(client, config);

        Assert.True(patchCalled);
    }

    private static PulumiStackProvisioner BuildProvisioner()
    {
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var configSecretBuilder = new DataPlaneConfigSecretBuilder();
        var operatorInfoProvider = new FakeOperatorInfoProvider();
        var manifestBuilder = new PulumiStackManifestBuilder(operatorProvisioner, configSecretBuilder, operatorInfoProvider);
        return new PulumiStackProvisioner(new NullLogger<PulumiStackProvisioner>(), manifestBuilder);
    }

    private sealed class FakePulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        public Task ApplyCrdManifestsAsync(IKubernetesClient client) => Task.CompletedTask;
        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace) => Task.CompletedTask;
        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout) => Task.CompletedTask;
        public string GetOperatorImage(OperatorConfig config) => "operator-image";
    }

    private sealed class FakeOperatorInfoProvider : IOperatorInfoProvider
    {
        public OperatorInfo GetInfo() => new("operator", "1.0.0", "runtime", "plugins");
    }
}
