using System.Text;
using CodeNOW.Cli.Common.Yaml;
using CodeNOW.Cli.DataPlane;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class FluxCDProvisionerTests
{
    [Fact]
    public async Task ApplyCrdManifestsAsync_PatchesOnlyCrds()
    {
        WriteFluxcdManifests(
            """
            apiVersion: apiextensions.k8s.io/v1
            kind: CustomResourceDefinition
            metadata:
              name: gitrepositories.source.toolkit.fluxcd.io
            ---
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: source-controller
            """,
            rbacYaml: "apiVersion: rbac.authorization.k8s.io/v1\nkind: ClusterRole\nmetadata:\n  name: crd-controller\nrules: []\n");

        var patched = new List<string>();
        var client = new FakeKubernetesClient();
        client.ApiextensionsV1.PatchCustomResourceDefinitionAsyncHandler = (patch, name, _, _) =>
        {
            patched.Add(name);
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();

        await provisioner.ApplyCrdManifestsAsync(client, BuildConfig());

        Assert.Contains("gitrepositories.source.toolkit.fluxcd.io", patched);
    }

    [Fact]
    public async Task ApplySourceControllerAsync_ConfiguresDeploymentAndCredentials()
    {
        WriteFluxcdManifests(
            """
            apiVersion: apiextensions.k8s.io/v1
            kind: CustomResourceDefinition
            metadata:
              name: gitrepositories.source.toolkit.fluxcd.io
            ---
            apiVersion: v1
            kind: ServiceAccount
            metadata:
              name: source-controller
            ---
            apiVersion: v1
            kind: Service
            metadata:
              name: source-controller
            spec:
              selector:
                app: source-controller
            ---
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: source-controller
            spec:
              template:
                spec:
                  containers:
                    - name: manager
                      image: ghcr.io/fluxcd/source-controller:v1.2.3
                      args:
                        - --storage-adv-addr=old
            """,
            rbacYaml: "apiVersion: rbac.authorization.k8s.io/v1\nkind: ClusterRole\nmetadata:\n  name: crd-controller\nrules: []\n");

        var client = new FakeKubernetesClient();
        V1Secret? appliedSecret = null;
        client.CoreV1.ReadNamespacedSecretAsyncHandler = (_, _) =>
            Task.FromResult(new V1Secret { Metadata = new V1ObjectMeta { ResourceVersion = "1" } });
        client.CoreV1.ReplaceNamespacedSecretAsyncHandler = (secret, _, _) =>
        {
            appliedSecret = secret;
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();
        var config = BuildConfig();
        config.HttpProxy.Enabled = true;
        config.HttpProxy.Hostname = "proxy.example.com";
        config.HttpProxy.Port = 3128;
        config.HttpProxy.NoProxy = ".cluster.local";
        config.Security.CustomCaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("CA"));
        config.Kubernetes.PodPlacementMode = PodPlacementMode.NodeSelectorAndTaints;
        config.Kubernetes.NodeLabels.System.Key = "node-role.kubernetes.io/system";
        config.Kubernetes.NodeLabels.System.Value = "true";

        await provisioner.ApplySourceControllerAsync(client, config);

        var deployment = client.AppliedObjects.OfType<V1Deployment>().FirstOrDefault();
        Assert.NotNull(deployment);
        Assert.Equal(FluxCDProvisioner.FluxcdSourceControllerName, deployment!.Metadata?.Name);
        Assert.Equal("system", deployment.Metadata?.NamespaceProperty);

        var container = deployment.Spec?.Template?.Spec?.Containers?.FirstOrDefault();
        Assert.NotNull(container);
        Assert.Equal("ghcr.io/fluxcd/source-controller:v9.9.9", container!.Image);
        var args = container.Args ?? new List<string>();
        Assert.Contains(
            $"--storage-adv-addr={FluxCDProvisioner.FluxcdSourceControllerName}.$(RUNTIME_NAMESPACE).svc.cluster.local.",
            args);
        var env = container.Env ?? new List<V1EnvVar>();
        Assert.Contains(env, e => e.Name == "HTTP_PROXY" && e.Value == "proxy.example.com:3128");
        Assert.Contains(env, e => e.Name == "HTTPS_PROXY" && e.Value == "proxy.example.com:3128");
        Assert.Contains(env, e => e.Name == "NO_PROXY" && e.Value == ".cluster.local");

        var mount = container.VolumeMounts?.Single(volumeMount =>
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

        var toleration = deployment.Spec?.Template?.Spec?.Tolerations?.FirstOrDefault();
        Assert.NotNull(toleration);
        Assert.Equal("NoExecute", toleration!.Effect);
        Assert.Equal("node-role.kubernetes.io/system", toleration.Key);
        Assert.Equal("Equal", toleration.OperatorProperty);
        Assert.Equal("true", toleration.Value);

        var affinity = deployment.Spec?.Template?.Spec?.Affinity;
        var nodeAffinity = affinity?.NodeAffinity;
        var required = nodeAffinity?.RequiredDuringSchedulingIgnoredDuringExecution;
        var requirement = required?.NodeSelectorTerms?.FirstOrDefault()?.MatchExpressions?.FirstOrDefault();
        Assert.NotNull(requirement);
        Assert.Equal("node-role.kubernetes.io/system", requirement!.Key);
        Assert.Equal("In", requirement.OperatorProperty);
        Assert.Equal(new[] { "true" }, requirement.Values);

        Assert.NotNull(appliedSecret);
        Assert.True(appliedSecret!.Data.ContainsKey("bearerToken"));
        Assert.True(appliedSecret.Data.ContainsKey("ca.crt"));
    }

    private static FluxCDProvisioner BuildProvisioner()
    {
        var infoProvider = new FakeFluxcdInfoProvider();
        return new FluxCDProvisioner(
            new NullLogger<FluxCDProvisioner>(),
            new YamlToJsonConverter(),
            infoProvider);
    }

    private static OperatorConfig BuildConfig()
    {
        return new OperatorConfig
        {
            Kubernetes =
            {
                Namespaces = { System = { Name = "system" } }
            },
            Environment = { Name = "dev" },
            Scm =
            {
                Url = "https://git.example.com/repo",
                AuthenticationMethod = ScmAuthenticationMethod.AccessToken,
                AccessToken = "token"
            },
            FluxCD = new FluxCDConfig
            {
                Images = { SourceControllerVersion = "v9.9.9" }
            }
        };
    }

    private static void WriteFluxcdManifests(string sourceControllerYaml, string rbacYaml)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "DataPlane", "Manifests", "FluxCD");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, FluxCDInfoProvider.FluxcdSourceControllerManifestFileName), sourceControllerYaml);
        File.WriteAllText(Path.Combine(root, "rbac.yaml"), rbacYaml);
    }

    private sealed class FakeFluxcdInfoProvider : IFluxCDInfoProvider
    {
        public FluxCDInfo GetInfo() => new(
            "ghcr.io/fluxcd/source-controller:v1.2.3",
            "v9.9.9");
    }
}
