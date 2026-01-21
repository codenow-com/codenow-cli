using CodeNOW.Cli.DataPlane.Services.Operations;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class KubernetesStatusReaderTests
{
    [Fact]
    public async Task GetClusterVersionAsync_ReturnsGitVersion()
    {
        var client = new FakeKubernetesClient();
        client.Version.GetCodeAsyncHandler = _ => Task.FromResult(new VersionInfo { GitVersion = "v1.27.1" });

        var reader = new KubernetesStatusReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var version = await reader.GetClusterVersionAsync(CancellationToken.None);

        Assert.Equal("v1.27.1", version);
    }

    [Fact]
    public async Task GetClusterResourcesAsync_AggregatesRequestsAndCapacity()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ListNodeAsyncHandler = _ =>
            Task.FromResult(new V1NodeList
            {
                Items =
                [
                    new V1Node
                    {
                        Status = new V1NodeStatus
                        {
                            Allocatable = new Dictionary<string, ResourceQuantity>
                            {
                                ["cpu"] = new ResourceQuantity("2"),
                                ["memory"] = new ResourceQuantity("4Gi")
                            }
                        }
                    }
                ]
            });
        client.CoreV1.ListPodForAllNamespacesAsyncHandler = (_, _) =>
            Task.FromResult(new V1PodList
            {
                Items =
                [
                    new V1Pod
                    {
                        Spec = new V1PodSpec
                        {
                            NodeName = "node",
                            Containers =
                            [
                                new V1Container
                                {
                                    Resources = new V1ResourceRequirements
                                    {
                                        Requests = new Dictionary<string, ResourceQuantity>
                                        {
                                            ["cpu"] = new ResourceQuantity("500m"),
                                            ["memory"] = new ResourceQuantity("256Mi")
                                        }
                                    }
                                }
                            ]
                        },
                        Status = new V1PodStatus { Phase = "Running" }
                    }
                ]
            });

        var reader = new KubernetesStatusReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var resources = await reader.GetClusterResourcesAsync(CancellationToken.None);

        Assert.Equal(500, resources.CpuRequestedMilli);
        Assert.Equal(2000, resources.CpuCapacityMilli);
        Assert.Equal(268435456, resources.MemoryRequestedBytes);
        Assert.Equal(4294967296, resources.MemoryCapacityBytes);
    }
}
