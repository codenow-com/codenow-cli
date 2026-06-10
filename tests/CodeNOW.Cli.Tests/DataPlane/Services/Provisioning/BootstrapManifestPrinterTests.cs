using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using k8s;
using k8s.Models;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class BootstrapManifestPrinterTests
{
    /// <summary>
    /// Simulates recording operations for a list of applied objects.
    /// </summary>
    private static (List<RecordedOperation> Operations, List<IKubernetesObject<V1ObjectMeta>> AppliedObjects)
        SimulateApply(params IKubernetesObject<V1ObjectMeta>[] objects)
    {
        var ops = new List<RecordedOperation>();
        var applied = new List<IKubernetesObject<V1ObjectMeta>>(objects);

        foreach (var obj in objects)
        {
            var (apiGroup, resource) = obj switch
            {
                V1Namespace => ("", "namespaces"),
                V1ServiceAccount => ("", "serviceaccounts"),
                V1Secret => ("", "secrets"),
                V1Service => ("", "services"),
                V1PersistentVolumeClaim => ("", "persistentvolumeclaims"),
                V1Deployment => ("apps", "deployments"),
                V1ClusterRole => ("rbac.authorization.k8s.io", "clusterroles"),
                V1ClusterRoleBinding => ("rbac.authorization.k8s.io", "clusterrolebindings"),
                V1Role => ("rbac.authorization.k8s.io", "roles"),
                V1RoleBinding => ("rbac.authorization.k8s.io", "rolebindings"),
                _ => ("", "unknown")
            };
            var name = obj.Metadata?.Name;
            ops.Add(new RecordedOperation(apiGroup, resource, "get", name));
            ops.Add(new RecordedOperation(apiGroup, resource, "create", name));
            ops.Add(new RecordedOperation(apiGroup, resource, "patch", name));
            ops.Add(new RecordedOperation(apiGroup, resource, "update", name));
        }

        return (ops, applied);
    }

    private static void AddCrdOperations(List<RecordedOperation> ops, params string[] crdNames)
    {
        foreach (var name in crdNames)
        {
            ops.Add(new RecordedOperation("apiextensions.k8s.io", "customresourcedefinitions", "get", name));
            ops.Add(new RecordedOperation("apiextensions.k8s.io", "customresourcedefinitions", "create", name));
            ops.Add(new RecordedOperation("apiextensions.k8s.io", "customresourcedefinitions", "patch", name));
            ops.Add(new RecordedOperation("apiextensions.k8s.io", "customresourcedefinitions", "update", name));
        }
    }

    private static void AddStackOperations(List<RecordedOperation> ops, string stackName)
    {
        ops.Add(new RecordedOperation("pulumi.com", "stacks", "get", stackName));
        ops.Add(new RecordedOperation("pulumi.com", "stacks", "create", stackName));
        ops.Add(new RecordedOperation("pulumi.com", "stacks", "patch", stackName));
        ops.Add(new RecordedOperation("pulumi.com", "stacks", "update", stackName));
    }

    [Fact]
    public void BuildDeployerClusterRole_ContainsRbacResourceNames()
    {
        var (ops, applied) = SimulateApply(
            new V1ClusterRole { Metadata = new V1ObjectMeta { Name = "my-cluster-role" } },
            new V1ClusterRoleBinding { Metadata = new V1ObjectMeta { Name = "my-binding" } },
            new V1Role { Metadata = new V1ObjectMeta { Name = "my-role" } },
            new V1RoleBinding { Metadata = new V1ObjectMeta { Name = "my-role-binding" } },
            new V1ServiceAccount { Metadata = new V1ObjectMeta { Name = "sa" } });

        var clusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole("test", ops, applied);

        var rbacRule = clusterRole.Rules.First(r =>
            r.ApiGroups.Contains("rbac.authorization.k8s.io") &&
            r.ResourceNames is not null && r.ResourceNames.Count > 0);

        Assert.Contains("my-cluster-role", rbacRule.ResourceNames);
        Assert.Contains("my-binding", rbacRule.ResourceNames);
        Assert.Contains("my-role", rbacRule.ResourceNames);
        Assert.Contains("my-role-binding", rbacRule.ResourceNames);
        Assert.DoesNotContain("sa", rbacRule.ResourceNames);
    }

    [Fact]
    public void BuildDeployerClusterRole_ContainsCrdResourceNames()
    {
        var ops = new List<RecordedOperation>();
        AddCrdOperations(ops, "stacks.pulumi.com", "programs.pulumi.com");

        var clusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole("test", ops, []);

        var crdRule = clusterRole.Rules.First(r =>
            r.ApiGroups.Contains("apiextensions.k8s.io") &&
            r.Resources.Contains("customresourcedefinitions"));

        Assert.Contains("programs.pulumi.com", crdRule.ResourceNames);
        Assert.Contains("stacks.pulumi.com", crdRule.ResourceNames);
    }

    [Fact]
    public void BuildDeployerClusterRole_RbacNamesUpdateWhenResourcesChange()
    {
        var (ops1, applied1) = SimulateApply(
            new V1ClusterRole { Metadata = new V1ObjectMeta { Name = "role-a" } });
        var clusterRole1 = BootstrapManifestPrinter.BuildDeployerClusterRole("test", ops1, applied1);

        var (ops2, applied2) = SimulateApply(
            new V1ClusterRole { Metadata = new V1ObjectMeta { Name = "role-a" } },
            new V1ClusterRole { Metadata = new V1ObjectMeta { Name = "role-b" } });
        var clusterRole2 = BootstrapManifestPrinter.BuildDeployerClusterRole("test", ops2, applied2);

        var rbacRule1 = clusterRole1.Rules.First(r =>
            r.ApiGroups.Contains("rbac.authorization.k8s.io") && r.ResourceNames is { Count: > 0 });
        var rbacRule2 = clusterRole2.Rules.First(r =>
            r.ApiGroups.Contains("rbac.authorization.k8s.io") && r.ResourceNames is { Count: > 0 });

        Assert.Single(rbacRule1.ResourceNames);
        Assert.Equal(2, rbacRule2.ResourceNames.Count);
        Assert.Contains("role-b", rbacRule2.ResourceNames);
    }

    [Fact]
    public void BuildDeployerClusterRole_ContainsExpectedRules()
    {
        var (ops, applied) = SimulateApply(
            new V1Namespace { Metadata = new V1ObjectMeta { Name = "ns" } },
            new V1Secret { Metadata = new V1ObjectMeta { Name = DataPlaneConstants.OperatorConfigSecretName } },
            new V1Secret { Metadata = new V1ObjectMeta { Name = KubernetesConstants.SystemImagePullSecret } });
        AddStackOperations(ops, DataPlaneConstants.StackName);

        var clusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole("test", ops, applied);

        var podsRule = clusterRole.Rules.First(r => r.Resources.Contains("pods"));
        Assert.Contains("pods/log", podsRule.Resources);

        var execRule = clusterRole.Rules.First(r => r.Resources.Contains("pods/exec"));
        Assert.Contains(DataPlaneConstants.StackName + "-workspace-0", execRule.ResourceNames);

        var secretsRule = clusterRole.Rules.First(r =>
            r.Resources.Contains("secrets") && r.ResourceNames is { Count: > 0 });
        Assert.Contains(DataPlaneConstants.OperatorConfigSecretName, secretsRule.ResourceNames);
        Assert.Contains(KubernetesConstants.SystemImagePullSecret, secretsRule.ResourceNames);
        Assert.Contains("cn-system-secrets", secretsRule.ResourceNames);
        Assert.Contains("cn-tenant-secrets", secretsRule.ResourceNames);

        var pulumiRule = clusterRole.Rules.First(r => r.ApiGroups.Contains("pulumi.com"));
        Assert.Contains("stacks", pulumiRule.Resources);

        var nsRule = clusterRole.Rules.First(r => r.Resources.Contains("namespaces"));
        Assert.Contains("get", nsRule.Verbs);
        Assert.Contains("create", nsRule.Verbs);
    }

    [Fact]
    public void BuildDeployerClusterRole_IncludesEscalationRulesFromClusterRoles()
    {
        var authRole = new V1ClusterRole
        {
            Metadata = new V1ObjectMeta { Name = "my-auth-role" },
            Rules =
            [
                new V1PolicyRule
                {
                    ApiGroups = ["authentication.k8s.io"],
                    Resources = ["tokenreviews"],
                    Verbs = ["create"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["authorization.k8s.io"],
                    Resources = ["subjectaccessreviews"],
                    Verbs = ["create"]
                }
            ]
        };
        var (ops, applied) = SimulateApply(authRole);

        var clusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole("test", ops, applied);

        var tokenRule = clusterRole.Rules.FirstOrDefault(r =>
            r.ApiGroups.Contains("authentication.k8s.io") && r.Resources.Contains("tokenreviews"));
        Assert.NotNull(tokenRule);
        Assert.Contains("create", tokenRule.Verbs);

        var sarRule = clusterRole.Rules.FirstOrDefault(r =>
            r.ApiGroups.Contains("authorization.k8s.io") && r.Resources.Contains("subjectaccessreviews"));
        Assert.NotNull(sarRule);
        Assert.Contains("create", sarRule.Verbs);
    }

    [Fact]
    public void BuildDeployerClusterRole_NoEscalationRulesWhenNoClusterRoles()
    {
        var (ops, applied) = SimulateApply(
            new V1ServiceAccount { Metadata = new V1ObjectMeta { Name = "sa" } });

        var clusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole("test", ops, applied);

        var tokenRule = clusterRole.Rules.FirstOrDefault(r =>
            r.ApiGroups.Contains("authentication.k8s.io"));
        Assert.Null(tokenRule);
    }

    [Fact]
    public void BuildDeployerClusterRole_SetsMetadataName()
    {
        var clusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole("my-deployer", [], []);

        Assert.Equal("my-deployer", clusterRole.Metadata.Name);
        Assert.Equal("rbac.authorization.k8s.io/v1", clusterRole.ApiVersion);
        Assert.Equal("ClusterRole", clusterRole.Kind);
    }

    [Fact]
    public async Task BuildDeployerClusterRole_FromRecordedBootstrap_ContainsAllExpectedResources()
    {
        // Run actual bootstrap against recording client
        var recordingClient = new RecordingKubernetesClient();
        var factory = new RecordingKubernetesClientFactory(recordingClient);
        var config = new CodeNOW.Cli.DataPlane.Models.OperatorConfig();
        config.Kubernetes.Namespaces.System.Name = "test-system";
        config.Kubernetes.Namespaces.Cni.Name = "test-cni";
        config.Kubernetes.Namespaces.CiPipelines.Name = "test-ci";

        var service = new BootstrapService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BootstrapService>.Instance,
            factory,
            new KubernetesConnectionOptions(),
            new NamespaceProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<NamespaceProvisioner>.Instance),
            new FluxCDProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FluxCDProvisioner>.Instance,
                new CodeNOW.Cli.Common.Yaml.YamlToJsonConverter(),
                new FluxCDInfoProvider()),
            new PulumiOperatorProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PulumiOperatorProvisioner>.Instance,
                new CodeNOW.Cli.Common.Yaml.YamlToJsonConverter(),
                new DataPlaneConfigSecretBuilder(),
                new PulumiOperatorInfoProvider()),
            new PulumiStackProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PulumiStackProvisioner>.Instance,
                new PulumiStackManifestBuilder(
                    new PulumiOperatorProvisioner(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<PulumiOperatorProvisioner>.Instance,
                        new CodeNOW.Cli.Common.Yaml.YamlToJsonConverter(),
                        new DataPlaneConfigSecretBuilder(),
                        new PulumiOperatorInfoProvider()),
                    new PulumiOperatorInfoProvider())));

        await service.BootstrapAsync(config);

        var clusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole(
            "test-deployer", recordingClient.Operations, recordingClient.AppliedObjects);

        // Namespaces: all three should be in resourceNames
        var nsRule = clusterRole.Rules.First(r => r.Resources.Contains("namespaces"));
        Assert.Contains("test-system", nsRule.ResourceNames);
        Assert.Contains("test-cni", nsRule.ResourceNames);
        Assert.Contains("test-ci", nsRule.ResourceNames);

        // Secrets should be recorded
        var secretsRule = clusterRole.Rules.First(r =>
            r.Resources.Contains("secrets") && r.ResourceNames is { Count: > 0 });
        Assert.Contains(DataPlaneConstants.OperatorConfigSecretName, secretsRule.ResourceNames);
        Assert.Contains(KubernetesConstants.SystemImagePullSecret, secretsRule.ResourceNames);

        // Deployments recorded
        Assert.Contains(clusterRole.Rules, r => r.Resources.Contains("deployments"));

        // ServiceAccounts recorded
        Assert.Contains(clusterRole.Rules, r => r.Resources.Contains("serviceaccounts"));

        // Services recorded
        Assert.Contains(clusterRole.Rules, r => r.Resources.Contains("services"));

        // PVCs recorded
        Assert.Contains(clusterRole.Rules, r => r.Resources.Contains("persistentvolumeclaims"));

        // CRDs recorded with specific names
        var crdRule = clusterRole.Rules.First(r =>
            r.ApiGroups.Contains("apiextensions.k8s.io") &&
            r.Resources.Contains("customresourcedefinitions"));
        Assert.NotNull(crdRule.ResourceNames);
        Assert.True(crdRule.ResourceNames.Count > 0);

        // Pulumi stacks recorded
        Assert.Contains(clusterRole.Rules, r =>
            r.ApiGroups.Contains("pulumi.com") && r.Resources.Contains("stacks"));

        // RBAC rules present with resourceNames
        var rbacRule = clusterRole.Rules.First(r =>
            r.ApiGroups.Contains("rbac.authorization.k8s.io") &&
            r.ResourceNames is { Count: > 0 });
        Assert.True(rbacRule.ResourceNames.Count > 0);

        // Escalation rules from ClusterRoles are present (e.g., tokenreviews from metrics-auth-role)
        Assert.Contains(clusterRole.Rules, r =>
            r.ApiGroups.Contains("authentication.k8s.io") && r.Resources.Contains("tokenreviews"));
    }
}
