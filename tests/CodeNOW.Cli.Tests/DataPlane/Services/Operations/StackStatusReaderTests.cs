using System.Text.Json.Nodes;
using CodeNOW.Cli.DataPlane.Services.Operations;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class StackStatusReaderTests
{
    [Fact]
    public async Task GetStackStatusAsync_ParsesConditionsAndPreview()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ListNamespacedPodAsyncHandler = (_, _, _) =>
            Task.FromResult(new V1PodList
            {
                Items =
                [
                    new V1Pod
                    {
                        Metadata = new V1ObjectMeta { Name = "workspace", NamespaceProperty = "ns" },
                        Status = new V1PodStatus { Phase = "Running" }
                    }
                ]
            });

        var stack = new JsonObject
        {
            ["status"] = new JsonObject
            {
                ["conditions"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "Ready",
                        ["status"] = "True"
                    },
                    new JsonObject
                    {
                        ["type"] = "Reconciling",
                        ["reason"] = "Applying"
                    }
                }
            },
            ["spec"] = new JsonObject
            {
                ["preview"] = true
            }
        };

        client.CustomObjects.GetNamespacedCustomObjectAsyncHandler = (_, _, _, _, _, _) =>
            Task.FromResult<object>(stack);

        var reader = new StackStatusReader(client, new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>()));

        var result = await reader.GetStackStatusAsync(new ManagementQuery("ns", "stack"), CancellationToken.None);

        Assert.Equal("Running", result.WorkspaceStatus);
        Assert.Equal("True", result.Ready);
        Assert.Equal("Applying", result.ReconcilingReason);
        Assert.Equal("Enabled", result.DryRun);
    }
}
