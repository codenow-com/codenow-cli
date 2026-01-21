using CodeNOW.Cli.DataPlane.Services.Operations;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class OperatorStatusReaderTests
{
    [Fact]
    public async Task GetOperatorStatusAsync_ReturnsNotFoundWhenNoPods()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ListPodForAllNamespacesAsyncHandler = (_, _) =>
            Task.FromResult(new V1PodList());

        var reader = new OperatorStatusReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var status = await reader.GetOperatorStatusAsync(CancellationToken.None);

        Assert.Equal("Unknown", status.Status);
        Assert.Equal("Unknown", status.Namespace);
    }

    [Fact]
    public async Task GetOperatorStatusAsync_ReturnsVersionFromLabel()
    {
        var pod = new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = "operator",
                NamespaceProperty = "ns",
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/version"] = "1.2.3"
                }
            },
            Status = new V1PodStatus { Phase = "Running" }
        };

        var client = new FakeKubernetesClient();
        client.CoreV1.ListPodForAllNamespacesAsyncHandler = (_, _) =>
            Task.FromResult(new V1PodList { Items = [pod] });

        var reader = new OperatorStatusReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var status = await reader.GetOperatorStatusAsync(CancellationToken.None);

        Assert.Equal("1.2.3", status.Version);
        Assert.Equal("ns", status.Namespace);
    }
}
