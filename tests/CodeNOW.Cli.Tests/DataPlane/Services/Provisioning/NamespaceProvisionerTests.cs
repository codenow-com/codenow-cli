using System.Text;
using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane;
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
    public async Task StartNamespaceProvisioning_CreatesPatchesAndSecretsForDedicatedNamespaces()
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
                },
                PodPlacementMode = PodPlacementMode.PodNodeSelector,
                NodeLabels =
                {
                    System = { Key = "sys", Value = "node" },
                    Application = { Key = "app", Value = "node" }
                }
            },
            ContainerRegistry =
            {
                Hostname = "registry.example.com",
                Username = "user",
                Password = "pass"
            }
        };

        var client = new FakeKubernetesClient();
        var patches = new Dictionary<string, JsonObject>();
        client.CoreV1.PatchNamespaceAsyncHandler = (patch, name, _, _) =>
        {
            patches[name] = JsonNode.Parse(GetPatchContent(patch))!.AsObject();
            return Task.CompletedTask;
        };

        var secrets = new List<V1Secret>();
        client.CoreV1.ReadNamespacedSecretAsyncHandler = (_, _) =>
            Task.FromResult(new V1Secret { Metadata = new V1ObjectMeta { ResourceVersion = "1" } });
        client.CoreV1.ReplaceNamespacedSecretAsyncHandler = (secret, _, _) =>
        {
            secrets.Add(secret);
            return Task.CompletedTask;
        };

        var provisioner = new NamespaceProvisioner(new NullLogger<NamespaceProvisioner>());
        var tasks = provisioner.StartNamespaceProvisioning(client, config);
        await Task.WhenAll(tasks.SystemNamespace, tasks.CniNamespace, tasks.CiPipelinesNamespace);

        Assert.Equal(3, patches.Count);
        Assert.Contains("system", patches.Keys);
        Assert.Contains("cni", patches.Keys);
        Assert.Contains("ci", patches.Keys);

        var systemPatch = patches["system"];
        var systemLabels = systemPatch["metadata"]!.AsObject()["labels"]!.AsObject();
        Assert.Equal(
            DataPlaneConstants.NamespaceTypeSystemLabelValue,
            systemLabels[KubernetesConstants.Labels.NamespaceType]!.GetValue<string>());
        Assert.Equal(
            DataPlaneConstants.PartOfDataPlaneLabelValue,
            systemLabels["app.kubernetes.io/part-of"]!.GetValue<string>());

        var systemAnnotations = systemPatch["metadata"]!.AsObject()["annotations"]!.AsObject();
        Assert.Equal(
            "sys=node",
            systemAnnotations[KubernetesConstants.Labels.PodNodeSelector]!.GetValue<string>());

        var cniAnnotations = patches["cni"]["metadata"]!.AsObject()["annotations"]!.AsObject();
        Assert.Equal(
            "app=node",
            cniAnnotations[KubernetesConstants.Labels.PodNodeSelector]!.GetValue<string>());

        Assert.Equal(3, secrets.Count);
        Assert.All(secrets, secret =>
        {
            Assert.Equal("kubernetes.io/dockerconfigjson", secret.Type);
            Assert.NotNull(secret.Metadata?.Labels);
            foreach (var label in ProvisioningCommonTools.BootstrapLabels)
                Assert.Equal(label.Value, secret.Metadata!.Labels![label.Key]);
            Assert.Contains(
                "registry.example.com",
                Encoding.UTF8.GetString(secret.Data![".dockerconfigjson"]));
        });
    }

    [Fact]
    public async Task StartNamespaceProvisioning_SkipsDedicatedNamespacesWhenSharedWithSystem()
    {
        var config = new OperatorConfig
        {
            Kubernetes =
            {
                Namespaces =
                {
                    System = { Name = "system" },
                    Cni = { Name = "system" },
                    CiPipelines = { Name = "system" }
                }
            },
            ContainerRegistry =
            {
                Hostname = "registry.example.com",
                Username = "user",
                Password = "pass"
            }
        };

        var client = new FakeKubernetesClient();
        var patchNames = new List<string>();
        client.CoreV1.PatchNamespaceAsyncHandler = (patch, name, _, _) =>
        {
            patchNames.Add(name);
            return Task.CompletedTask;
        };

        var secretCount = 0;
        client.CoreV1.ReadNamespacedSecretAsyncHandler = (_, _) =>
            Task.FromResult(new V1Secret { Metadata = new V1ObjectMeta { ResourceVersion = "1" } });
        client.CoreV1.ReplaceNamespacedSecretAsyncHandler = (_, _, _) =>
        {
            secretCount++;
            return Task.CompletedTask;
        };

        var provisioner = new NamespaceProvisioner(new NullLogger<NamespaceProvisioner>());
        var tasks = provisioner.StartNamespaceProvisioning(client, config);
        await Task.WhenAll(tasks.SystemNamespace, tasks.CniNamespace, tasks.CiPipelinesNamespace);

        Assert.Single(patchNames);
        Assert.Equal("system", patchNames[0]);
        Assert.Equal(1, secretCount);
    }

    private static string GetPatchContent(V1Patch patch)
    {
        var contentProperty = patch.GetType().GetProperty("Content");
        if (contentProperty?.GetValue(patch) is string content)
            return content;

        return patch.ToString() ?? string.Empty;
    }
}
