using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class NamespaceProvisionerTests
{
    [Fact]
    public async Task StartNamespaceProvisioning_SkipsNonDedicatedNamespaces()
    {
        var config = new OperatorConfig
        {
            Kubernetes =
            {
                Namespaces =
                {
                    System = { Name = "system" },
                    Cni = { Name = "cni" },
                    CiPipelines = { Name = "ci" }
                }
            },
            ContainerRegistry =
            {
                Hostname = "registry",
                Username = "user",
                Password = "pass"
            }
        };

        var client = new FakeKubernetesClient();
        var patchCount = 0;
        client.CoreV1.PatchNamespaceAsyncHandler = (_, _, _, _) =>
        {
            patchCount++;
            return Task.CompletedTask;
        };
        client.CoreV1.ReadNamespacedSecretAsyncHandler = (_, _) =>
            Task.FromResult(new V1Secret { Metadata = new V1ObjectMeta { ResourceVersion = "1" } });
        client.CoreV1.ReplaceNamespacedSecretAsyncHandler = (_, _, _) => Task.CompletedTask;

        var provisioner = new NamespaceProvisioner(new NullLogger<NamespaceProvisioner>());

        var tasks = provisioner.StartNamespaceProvisioning(client, config);
        await Task.WhenAll(tasks.SystemNamespace, tasks.CniNamespace, tasks.CiPipelinesNamespace);

        Assert.Equal(3, patchCount);
    }
}
