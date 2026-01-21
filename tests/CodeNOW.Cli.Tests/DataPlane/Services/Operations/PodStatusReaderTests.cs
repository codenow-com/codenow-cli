using CodeNOW.Cli.DataPlane.Services.Operations;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class PodStatusReaderTests
{
    [Fact]
    public async Task GetPodStatusAsync_ReturnsUnavailableWhenNoPods()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ListNamespacedPodAsyncHandler = (_, _, _) =>
            Task.FromResult(new V1PodList());

        var reader = new PodStatusReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var result = await reader.GetPodStatusAsync("ns", "app=demo", CancellationToken.None);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task GetPodStatusAsync_PrefersRunningPod()
    {
        var client = new FakeKubernetesClient();
        var running = new V1Pod
        {
            Metadata = new V1ObjectMeta { Name = "running", NamespaceProperty = "ns" },
            Status = new V1PodStatus { Phase = "Running" }
        };
        var pending = new V1Pod
        {
            Metadata = new V1ObjectMeta { Name = "pending", NamespaceProperty = "ns" },
            Status = new V1PodStatus { Phase = "Pending" }
        };
        client.CoreV1.ListNamespacedPodAsyncHandler = (_, _, _) =>
            Task.FromResult(new V1PodList { Items = [pending, running] });

        var reader = new PodStatusReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var result = await reader.GetPodStatusAsync("ns", "app=demo", CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal("running", result.Pod?.Metadata?.Name);
    }
}
