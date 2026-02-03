using System.Net;
using System.Net.Http;
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

    [Fact]
    public async Task CreatePulumiStatePvcAsync_CreatesWhenMissing()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ReadNamespacedPersistentVolumeClaimAsyncHandler = (_, _) =>
            throw CreateNotFound();

        var created = false;
        client.CoreV1.CreateNamespacedPersistentVolumeClaimAsyncHandler = (_, _) =>
        {
            created = true;
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            S3 = { Enabled = false },
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        await provisioner.CreatePulumiStatePvcAsync(client, config);

        Assert.True(created);
    }

    [Fact]
    public async Task CreatePulumiStatePvcAsync_SkipsPatchWhenStorageUnchanged()
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
                            ["storage"] = new ResourceQuantity("1000Mi")
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

        Assert.False(patchCalled);
    }

    [Fact]
    public async Task ApplyPulumiStackAsync_PatchesStackCustomObject()
    {
        var client = new FakeKubernetesClient();
        string? patchContent = null;
        string? patchName = null;
        string? patchNamespace = null;
        string? patchGroup = null;
        string? patchVersion = null;
        string? patchPlural = null;
        client.CustomObjects.PatchNamespacedCustomObjectAsyncHandler = (patch, group, version, ns, plural, name, _, _, _) =>
        {
            patchContent = GetPatchContent(patch);
            patchName = name;
            patchNamespace = ns;
            patchGroup = group;
            patchVersion = version;
            patchPlural = plural;
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Environment = { Name = "dev" }
        };

        await provisioner.ApplyPulumiStackAsync(client, "sa", config);

        Assert.Equal("pulumi.com", patchGroup);
        Assert.Equal("v1", patchVersion);
        Assert.Equal("stacks", patchPlural);
        Assert.Equal(DataPlaneConstants.StackName, patchName);
        Assert.Equal("system", patchNamespace);
        var obj = JsonNode.Parse(patchContent!)!.AsObject();
        Assert.Equal("Stack", obj["kind"]!.GetValue<string>());
        Assert.Equal(DataPlaneConstants.StackName, obj["metadata"]!.AsObject()["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyPulumiStackRbacAsync_AddsIamRoleAnnotationAndNamespaceBindings()
    {
        var client = new FakeKubernetesClient();
        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            S3 =
            {
                Enabled = true,
                AuthenticationMethod = S3AuthenticationMethod.IAMRole,
                IAMRole = "arn:aws:iam::123:role/stack"
            }
        };

        await provisioner.ApplyPulumiStackRbacAsync(
            client,
            "system",
            "sa",
            config,
            new[] { "system", "cni" });

        var serviceAccount = Assert.Single(client.AppliedObjects.OfType<V1ServiceAccount>());
        Assert.Equal("system", serviceAccount.Metadata?.NamespaceProperty);
        Assert.Equal(
            "arn:aws:iam::123:role/stack",
            serviceAccount.Metadata?.Annotations?["eks.amazonaws.com/role-arn"]);
        Assert.Equal(
            DataPlaneConstants.BootstrapAppLabelValue,
            serviceAccount.Metadata?.Labels?["app.kubernetes.io/name"]);

        var adminRoleBindings = client.AppliedObjects
            .OfType<V1RoleBinding>()
            .Where(binding => binding.Metadata?.Name == "sa-admin")
            .ToList();
        Assert.Equal(2, adminRoleBindings.Count);
        Assert.Contains(adminRoleBindings, binding => binding.Metadata?.NamespaceProperty == "system");
        Assert.Contains(adminRoleBindings, binding => binding.Metadata?.NamespaceProperty == "cni");
    }

    private static PulumiStackProvisioner BuildProvisioner()
    {
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var operatorInfoProvider = new FakeOperatorInfoProvider();
        var manifestBuilder = new PulumiStackManifestBuilder(operatorProvisioner, operatorInfoProvider);
        return new PulumiStackProvisioner(new NullLogger<PulumiStackProvisioner>(), manifestBuilder);
    }

    private sealed class FakePulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        public Task ApplyCrdManifestsAsync(IKubernetesClient client) => Task.CompletedTask;
        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace) => Task.CompletedTask;
        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout) => Task.CompletedTask;
        public Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public string GetOperatorImage(OperatorConfig config) => "operator-image";
    }

    private sealed class FakeOperatorInfoProvider : IPulumiOperatorInfoProvider
    {
        public PulumiOperatorInfo GetInfo() => new("operator", "1.0.0", "runtime", "3.2.1", "plugins", "9.9.9");
    }

    private static k8s.Autorest.HttpOperationException CreateNotFound()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var wrapper = new k8s.Autorest.HttpResponseMessageWrapper(response, string.Empty);
        return new k8s.Autorest.HttpOperationException("Not Found")
        {
            Response = wrapper
        };
    }

    private static string GetPatchContent(V1Patch patch)
    {
        var contentProperty = patch.GetType().GetProperty("Content");
        if (contentProperty?.GetValue(patch) is string content)
            return content;

        return patch.ToString() ?? string.Empty;
    }
}
