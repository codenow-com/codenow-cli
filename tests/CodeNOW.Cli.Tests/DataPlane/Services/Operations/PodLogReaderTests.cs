using System.Text;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Operations;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class PodLogReaderTests
{
    [Fact]
    public async Task ReadLogsAsync_ReturnsUnavailableWhenNoPods()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ListNamespacedPodAsyncHandler = (_, _, _) =>
            Task.FromResult(new V1PodList());

        var reader = new PodLogReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var result = await reader.ReadLogsAsync(new ManagementLogQuery("ns", 10), "app=demo", "warn", CancellationToken.None);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task ReadLogsAsync_ReturnsLogsForRunningPod()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ListNamespacedPodAsyncHandler = (_, _, _) =>
            Task.FromResult(new V1PodList
            {
                Items =
                [
                    new V1Pod
                    {
                        Metadata = new V1ObjectMeta { Name = "pod", Uid = "uid" },
                        Status = new V1PodStatus { Phase = "Running" }
                    }
                ]
            });
        client.CoreV1.ReadNamespacedPodLogAsyncHandler = (_, _, _, _) =>
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("log-line"));
            return Task.FromResult<Stream>(stream);
        };

        var reader = new PodLogReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var result = await reader.ReadLogsAsync(new ManagementLogQuery("ns", 10), "app=demo", "warn", CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Contains("log-line", result.Logs);
    }
}
