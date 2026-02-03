using System.Net;
using System.Net.Http;
using System.Text;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Yaml;
using CodeNOW.Cli.DataPlane;
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

    [Fact]
    public async Task ApplyOperatorDeploymentAsync_SetsNamespaceImageAndLabels()
    {
        WriteManifests(
            rbacYaml: "apiVersion: v1\nkind: ServiceAccount\nmetadata:\n  name: controller-manager\n",
            crdYaml: "apiVersion: apiextensions.k8s.io/v1\nkind: CustomResourceDefinition\nmetadata:\n  name: stacks.pulumi.com\n",
            managerYaml:
                """
                apiVersion: apps/v1
                kind: Deployment
                metadata:
                  name: controller-manager
                spec:
                  template:
                    spec:
                      serviceAccountName: controller-manager
                      containers:
                        - name: manager
                          image: old
                ---
                apiVersion: v1
                kind: Service
                metadata:
                  name: controller-manager
                """);

        var provisioner = BuildProvisioner();
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            ContainerRegistry = { Hostname = "registry.local" }
        };

        await provisioner.ApplyOperatorDeploymentAsync(client, config);

        var deployment = client.AppliedObjects.OfType<V1Deployment>().Single();
        Assert.Equal("system", deployment.Metadata?.NamespaceProperty);
        Assert.Equal(
            DataPlaneConstants.PulumiOperatorAppLabelValue,
            deployment.Metadata?.Labels?["app.kubernetes.io/name"]);
        Assert.StartsWith("registry.local/", deployment.Spec?.Template?.Spec?.Containers?.First().Image, StringComparison.Ordinal);

        var service = client.AppliedObjects.OfType<V1Service>().Single();
        Assert.Equal("system", service.Metadata?.NamespaceProperty);
        Assert.Equal(
            DataPlaneConstants.PulumiOperatorAppLabelValue,
            service.Metadata?.Labels?["app.kubernetes.io/name"]);
    }

    [Fact]
    public async Task ApplyOperatorDeploymentAsync_AddsCustomCaVolumeAndMount()
    {
        WriteManifests(
            rbacYaml: "apiVersion: v1\nkind: ServiceAccount\nmetadata:\n  name: controller-manager\n",
            crdYaml: "apiVersion: apiextensions.k8s.io/v1\nkind: CustomResourceDefinition\nmetadata:\n  name: stacks.pulumi.com\n",
            managerYaml:
                """
                apiVersion: apps/v1
                kind: Deployment
                metadata:
                  name: controller-manager
                spec:
                  template:
                    spec:
                      serviceAccountName: controller-manager
                      containers:
                        - name: manager
                          image: old
                """);

        var provisioner = BuildProvisioner();
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Security = { CustomCaBase64 = "ZHVtbXk=" }
        };

        await provisioner.ApplyOperatorDeploymentAsync(client, config);

        var deployment = client.AppliedObjects.OfType<V1Deployment>().Single();
        var container = deployment.Spec?.Template?.Spec?.Containers?.First();
        var mount = container?.VolumeMounts?.Single(volumeMount =>
            string.Equals(volumeMount.Name, "ca-certificates", StringComparison.Ordinal));
        Assert.NotNull(mount);
        Assert.Equal(
            $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}",
            mount?.MountPath);
        Assert.Equal(DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert, mount?.SubPath);
        Assert.True(mount?.ReadOnlyProperty);

        var volume = deployment.Spec?.Template?.Spec?.Volumes?.Single(volumeEntry =>
            string.Equals(volumeEntry.Name, "ca-certificates", StringComparison.Ordinal));
        Assert.NotNull(volume?.Secret);
        Assert.Equal(DataPlaneConstants.OperatorConfigSecretName, volume?.Secret?.SecretName);
        Assert.NotNull(volume?.Secret?.Items);
        Assert.Single(volume!.Secret!.Items);
        Assert.Equal(DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert, volume?.Secret?.Items?.First().Key);
        Assert.Equal(DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert, volume?.Secret?.Items?.First().Path);
    }

    [Fact]
    public async Task ApplyCrdManifestsAsync_PatchesCustomResourceDefinitions()
    {
        WriteManifests(
            rbacYaml: "apiVersion: v1\nkind: ServiceAccount\nmetadata:\n  name: controller-manager\n",
            crdYaml:
                """
                apiVersion: apiextensions.k8s.io/v1
                kind: CustomResourceDefinition
                metadata:
                  name: stacks.pulumi.com
                ---
                apiVersion: apiextensions.k8s.io/v1
                kind: CustomResourceDefinition
                metadata:
                  name: programs.pulumi.com
                """,
            managerYaml: "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: controller-manager\n");

        var provisioner = BuildProvisioner();
        var client = new FakeKubernetesClient();
        var patched = new List<string>();
        client.ApiextensionsV1.PatchCustomResourceDefinitionAsyncHandler = (patch, name, _, _) =>
        {
            patched.Add(name);
            return Task.CompletedTask;
        };

        await provisioner.ApplyCrdManifestsAsync(client);

        Assert.Contains("stacks.pulumi.com", patched);
        Assert.Contains("programs.pulumi.com", patched);
    }

    [Fact]
    public async Task ApplyRbacManifestsAsync_PrefixesNamesAndBindsNamespace()
    {
        WriteManifests(
            rbacYaml:
                """
                apiVersion: v1
                kind: ServiceAccount
                metadata:
                  name: controller-manager
                ---
                apiVersion: rbac.authorization.k8s.io/v1
                kind: RoleBinding
                metadata:
                  name: controller-manager
                subjects:
                  - kind: ServiceAccount
                    name: controller-manager
                roleRef:
                  apiGroup: rbac.authorization.k8s.io
                  kind: Role
                  name: controller-manager
                """,
            crdYaml: "apiVersion: apiextensions.k8s.io/v1\nkind: CustomResourceDefinition\nmetadata:\n  name: stacks.pulumi.com\n",
            managerYaml: "apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: controller-manager\n");

        var provisioner = BuildProvisioner();
        var client = new FakeKubernetesClient();

        await provisioner.ApplyRbacManifestsAsync(client, "system");

        var serviceAccounts = client.AppliedObjects.OfType<V1ServiceAccount>().ToList();
        Assert.NotEmpty(serviceAccounts);
        Assert.Contains(serviceAccounts, serviceAccount =>
            string.Equals(serviceAccount.Metadata?.NamespaceProperty, "system", StringComparison.Ordinal) &&
            serviceAccount.Metadata?.Name?.StartsWith(
                PulumiOperatorProvisioner.PulumiOperatorNamePrefix,
                StringComparison.Ordinal) == true);

        var roleBindings = client.AppliedObjects.OfType<V1RoleBinding>().ToList();
        Assert.NotEmpty(roleBindings);
        Assert.Contains(roleBindings, roleBinding =>
            string.Equals(roleBinding.Metadata?.NamespaceProperty, "system", StringComparison.Ordinal) &&
            roleBinding.Metadata?.Name?.StartsWith(
                PulumiOperatorProvisioner.PulumiOperatorNamePrefix,
                StringComparison.Ordinal) == true &&
            roleBinding.RoleRef.Name.StartsWith(
                PulumiOperatorProvisioner.PulumiOperatorNamePrefix,
                StringComparison.Ordinal) &&
            roleBinding.Subjects.All(subject =>
                string.Equals(subject.NamespaceProperty, "system", StringComparison.Ordinal) &&
                subject.Name.StartsWith(
                    PulumiOperatorProvisioner.PulumiOperatorNamePrefix,
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CreateDataPlaneConfigSecretAsync_ReplacesWhenPresent()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ReadNamespacedSecretAsyncHandler = (_, _) =>
            Task.FromResult(new V1Secret { Metadata = new V1ObjectMeta { ResourceVersion = "1" } });

        var replaced = false;
        client.CoreV1.ReplaceNamespacedSecretAsyncHandler = (secret, _, _) =>
        {
            replaced = true;
            Assert.Equal("1", secret.Metadata.ResourceVersion);
            return Task.CompletedTask;
        };

        var created = false;
        client.CoreV1.CreateNamespacedSecretAsyncHandler = (_, _) =>
        {
            created = true;
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        await provisioner.CreateDataPlaneConfigSecretAsync(client, config);

        Assert.True(replaced);
        Assert.False(created);
    }

    [Fact]
    public async Task CreateDataPlaneConfigSecretAsync_CreatesWhenMissing()
    {
        var client = new FakeKubernetesClient();
        client.CoreV1.ReadNamespacedSecretAsyncHandler = (_, _) =>
            throw CreateNotFound();

        var created = false;
        client.CoreV1.CreateNamespacedSecretAsyncHandler = (_, _) =>
        {
            created = true;
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        await provisioner.CreateDataPlaneConfigSecretAsync(client, config);

        Assert.True(created);
    }

    private static PulumiOperatorProvisioner BuildProvisioner()
    {
        var logger = new NullLogger<PulumiOperatorProvisioner>();
        var yaml = new YamlToJsonConverter();
        var configSecretBuilder = new DataPlaneConfigSecretBuilder();
        var infoProvider = new FakeOperatorInfoProvider();
        return new PulumiOperatorProvisioner(logger, yaml, configSecretBuilder, infoProvider);
    }

    private sealed class FakeOperatorInfoProvider : IPulumiOperatorInfoProvider
    {
        public PulumiOperatorInfo GetInfo() => new("operator", "1.2.3", "runtime", "3.2.1", "plugins", "9.9.9");
    }

    private static void WriteManifests(string rbacYaml, string crdYaml, string managerYaml)
    {
        var baseDir = AppContext.BaseDirectory;
        var root = Path.Combine(baseDir, PulumiOperatorProvisioner.PulumiOperatorManifestsRelativePath);
        Directory.CreateDirectory(Path.Combine(root, "rbac"));
        Directory.CreateDirectory(Path.Combine(root, "crd"));
        Directory.CreateDirectory(Path.Combine(root, "manager"));

        File.WriteAllText(Path.Combine(root, "rbac", "rbac.yaml"), rbacYaml);
        File.WriteAllText(Path.Combine(root, "crd", "crd.yaml"), crdYaml);
        File.WriteAllText(Path.Combine(root, "manager", "manager.yaml"), managerYaml);
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
}
