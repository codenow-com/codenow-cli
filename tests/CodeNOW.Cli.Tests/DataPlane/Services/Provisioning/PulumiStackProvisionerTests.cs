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

    [Fact]
    public async Task ApplyPulumiStackRbacAsync_NamesAuthDelegatorClusterRoleBindingByServiceAccount()
    {
        var client = new FakeKubernetesClient();
        var provisioner = BuildProvisioner();
        var config = new OperatorConfig();

        await provisioner.ApplyPulumiStackRbacAsync(
            client,
            "system",
            "sa",
            config,
            new[] { "system" });

        var binding = Assert.Single(
            client.AppliedObjects.OfType<V1ClusterRoleBinding>(),
            binding => binding.RoleRef.Name == "system:auth-delegator");
        Assert.Equal("sa:system:auth-delegator", binding.Metadata?.Name);
    }

    [Fact]
    public async Task DeletePulumiWorkspaceAsync_DeletesRetainedWorkspace()
    {
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };
        var workspace = BuildWorkspace(config, "sa", "operator-generated-workspace");
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        string? deleteGroup = null;
        string? deleteVersion = null;
        string? deleteNamespace = null;
        string? deletePlural = null;
        string? deleteName = null;
        client.CustomObjects.DeleteNamespacedCustomObjectAsyncHandler = (group, version, ns, plural, name, _) =>
        {
            deleteGroup = group;
            deleteVersion = version;
            deleteNamespace = ns;
            deletePlural = plural;
            deleteName = name;
            return Task.CompletedTask;
        };

        var provisioner = BuildProvisioner();

        await provisioner.DeletePulumiWorkspaceAsync(client, config);

        Assert.Equal("auto.pulumi.com", deleteGroup);
        Assert.Equal("v1alpha1", deleteVersion);
        Assert.Equal("system", deleteNamespace);
        Assert.Equal("workspaces", deletePlural);
        Assert.Equal("operator-generated-workspace", deleteName);
    }

    [Fact]
    public async Task DeletePulumiWorkspaceAsync_IgnoresMissingWorkspace()
    {
        var client = new FakeKubernetesClient();
        client.CustomObjects.DeleteNamespacedCustomObjectAsyncHandler = (_, _, _, _, _, _) =>
            throw CreateNotFound();

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        await provisioner.DeletePulumiWorkspaceAsync(client, config);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsFalseWhenWorkspaceMissing()
    {
        var client = new FakeKubernetesClient();
        client.CustomObjects.GetNamespacedCustomObjectAsyncHandler = (_, _, _, _, _, _) =>
            throw CreateNotFound();

        var provisioner = BuildProvisioner();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", config);

        Assert.False(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsFalseWhenWorkspaceMatchesConfig()
    {
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" }
        };
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(BuildWorkspace(config, "sa")));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", config);

        Assert.False(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsFalseWhenOnlyWorkspaceHashIsMissing()
    {
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var workspace = BuildWorkspace(config, "sa");
        RemoveWorkspaceInputHash(workspace);
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", config);

        Assert.False(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsFalseWhenWorkspaceHashIsMissingAndOperatorDefaultsPodTemplate()
    {
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var workspace = BuildWorkspace(config, "sa");
        RemoveWorkspaceInputHash(workspace);
        workspace["spec"]!["podTemplate"]!["spec"]!.AsObject()["restartPolicy"] = "Never";
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", config);

        Assert.False(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsFalseWhenWorkspaceHashMatchesAndOperatorDefaultsPodTemplate()
    {
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var workspace = BuildWorkspace(config, "sa");
        workspace["spec"]!["podTemplate"]!["spec"]!.AsObject()["restartPolicy"] = "Never";
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", config);

        Assert.False(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsFalseWhenWorkspaceHashMatchesAndOperatorNormalizesOtherFields()
    {
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm =
            {
                Url = "https://git.example.com/repo",
                AuthenticationMethod = ScmAuthenticationMethod.AccessToken
            }
        };
        var workspace = BuildWorkspace(config, "sa");
        var spec = workspace["spec"]!.AsObject();
        spec["image"] = "operator-normalized-image";
        spec["git"]!.AsObject()["auth"]!.AsObject()["token"]!.AsObject()["optional"] = false;
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", config);

        Assert.False(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsFalseWhenOperatorOmitsOptionalWorkspaceFields()
    {
        var client = new FakeKubernetesClient();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var workspace = BuildWorkspace(config, "sa");
        var spec = workspace["spec"]!.AsObject();
        spec.Remove("resources");
        spec.Remove("stacks");
        spec["git"]!.AsObject().Remove("ref");
        spec["git"]!.AsObject().Remove("auth");
        RemoveWorkspaceInputHash(workspace);
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", config);

        Assert.False(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsTrueWhenWorkspaceGitUrlIsStale()
    {
        var client = new FakeKubernetesClient();
        var previousConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/old" }
        };
        var currentConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/new" }
        };
        var workspace = BuildWorkspace(previousConfig, "sa");
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", currentConfig);

        Assert.True(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsTrueWhenWorkspaceInputHashIsStale()
    {
        var client = new FakeKubernetesClient();
        var previousConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm =
            {
                Url = "https://git.example.com/repo",
                AuthenticationMethod = ScmAuthenticationMethod.AccessToken,
                AccessToken = "old-token"
            }
        };
        var currentConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm =
            {
                Url = "https://git.example.com/repo",
                AuthenticationMethod = ScmAuthenticationMethod.AccessToken,
                AccessToken = "new-token"
            }
        };
        var workspace = BuildWorkspace(previousConfig, "sa");
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", currentConfig);

        Assert.True(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsTrueWhenHttpProxyIsDisabled()
    {
        var client = new FakeKubernetesClient();
        var previousConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" },
            HttpProxy =
            {
                Enabled = true,
                Hostname = "moje.proxy",
                Port = 8080,
                NoProxy = "google.com"
            }
        };
        var currentConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" },
            HttpProxy =
            {
                Enabled = false
            }
        };
        var workspace = BuildWorkspace(previousConfig, "sa");
        RemoveWorkspaceInputHash(workspace);
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", currentConfig);

        Assert.True(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsTrueWhenWorkspaceImageIsStale()
    {
        var client = new FakeKubernetesClient();
        var previousConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Pulumi = { Images = { RuntimeVersion = "3.1.0" } },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var currentConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Pulumi = { Images = { RuntimeVersion = "3.2.1" } },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var workspace = BuildWorkspace(previousConfig, "sa");
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", currentConfig);

        Assert.True(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsTrueWhenWorkspacePodTemplateInputHashIsStale()
    {
        var client = new FakeKubernetesClient();
        var previousConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes =
            {
                Namespaces = { System = { Name = "system" } },
                SecurityContextRunAsId = 1001
            },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var currentConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes =
            {
                Namespaces = { System = { Name = "system" } },
                SecurityContextRunAsId = 2002
            },
            Scm = { Url = "https://git.example.com/repo" }
        };
        var workspace = BuildWorkspace(previousConfig, "sa");
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", currentConfig);

        Assert.True(changed);
    }

    [Fact]
    public async Task HasWorkspaceInputsChangedAsync_ReturnsTrueWhenWorkspaceGitAuthInputIsStale()
    {
        var client = new FakeKubernetesClient();
        var previousConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm =
            {
                Url = "https://git.example.com/repo",
                AuthenticationMethod = ScmAuthenticationMethod.UsernamePassword,
                Username = "old-user",
                Password = "old-password"
            }
        };
        var currentConfig = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm =
            {
                Url = "https://git.example.com/repo",
                AuthenticationMethod = ScmAuthenticationMethod.AccessToken
            }
        };
        var workspace = BuildWorkspace(previousConfig, "sa");
        client.CustomObjects.ListNamespacedCustomObjectAsyncHandler = (_, _, _, _, _) =>
            Task.FromResult<object>(BuildWorkspaceList(workspace));

        var provisioner = BuildProvisioner();

        var changed = await provisioner.HasWorkspaceInputsChangedAsync(client, "sa", currentConfig);

        Assert.True(changed);
    }

    private static PulumiStackProvisioner BuildProvisioner()
    {
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var operatorInfoProvider = new FakeOperatorInfoProvider();
        var manifestBuilder = new PulumiStackManifestBuilder(operatorProvisioner, operatorInfoProvider);
        return new PulumiStackProvisioner(new NullLogger<PulumiStackProvisioner>(), manifestBuilder);
    }

    private static JsonObject BuildWorkspace(
        OperatorConfig config,
        string serviceAccountName,
        string name = DataPlaneConstants.WorkspaceName)
    {
        var stack = new PulumiStackManifestBuilder(
            new FakePulumiOperatorProvisioner(),
            new FakeOperatorInfoProvider()).BuildStack(config, serviceAccountName);
        var stackSpec = stack["spec"]!.AsObject();
        var workspaceSpec = stackSpec["workspaceTemplate"]!
            .AsObject()["spec"]!
            .DeepClone()
            .AsObject();

        workspaceSpec["serviceAccountName"] = serviceAccountName;
        workspaceSpec["stacks"] = new JsonArray(
            new JsonObject
            {
                ["name"] = DataPlaneConstants.StackName
            });

        if (config.FluxCD?.Enabled == true)
        {
            workspaceSpec["flux"] = new JsonObject
            {
                ["dir"] = config.Environment.Name
            };
        }
        else
        {
            var git = new JsonObject
            {
                ["url"] = config.Scm.Url,
                ["dir"] = config.Environment.Name,
                ["ref"] = $"refs/heads/{DataPlaneConstants.ScmGitRepositoryDefaultBranch}"
            };

            if (config.Scm.AuthenticationMethod == ScmAuthenticationMethod.AccessToken)
            {
                git["auth"] = new JsonObject
                {
                    ["token"] = BuildSecretKeySelector(DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthToken)
                };
            }
            else if (config.Scm.AuthenticationMethod == ScmAuthenticationMethod.UsernamePassword)
            {
                git["auth"] = new JsonObject
                {
                    ["username"] = BuildSecretKeySelector(DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthUsername),
                    ["password"] = BuildSecretKeySelector(DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthPassword)
                };
            }

            workspaceSpec["git"] = git;
        }

        return new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["name"] = name,
                ["labels"] = new JsonObject
                {
                    ["app.kubernetes.io/name"] = DataPlaneConstants.BootstrapAppLabelValue,
                    ["app.kubernetes.io/part-of"] = DataPlaneConstants.PartOfDataPlaneLabelValue
                },
                ["ownerReferences"] = new JsonArray(
                    new JsonObject
                    {
                        ["apiVersion"] = "pulumi.com/v1",
                        ["kind"] = "Stack",
                        ["name"] = DataPlaneConstants.StackName
                    })
            },
            ["spec"] = workspaceSpec
        };
    }

    private static JsonObject BuildWorkspaceList(params JsonObject[] workspaces)
    {
        var items = new JsonArray();
        foreach (var workspace in workspaces)
            items.Add(workspace.DeepClone());

        return new JsonObject
        {
            ["items"] = items
        };
    }

    private static JsonObject BuildSecretKeySelector(string key)
    {
        return new JsonObject
        {
            ["name"] = DataPlaneConstants.OperatorConfigSecretName,
            ["key"] = key
        };
    }

    private static void RemoveWorkspaceInputHash(JsonObject workspace)
    {
        var annotations = workspace["spec"]?["podTemplate"]?["metadata"]?["annotations"] as JsonObject;
        annotations?.Remove(DataPlaneConstants.WorkspaceInputHashAnnotation);
    }

    private sealed class FakePulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        public Task ApplyCrdManifestsAsync(IKubernetesClient client) => Task.CompletedTask;
        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace) => Task.CompletedTask;
        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout) => Task.CompletedTask;
        public Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public string GetOperatorImage(OperatorConfig config) => "operator-image";
        public List<k8s.IKubernetesObject<V1ObjectMeta>> BuildRbacResources(string targetNamespace) => [];
        public List<k8s.IKubernetesObject<V1ObjectMeta>> BuildOperatorDeploymentResources(OperatorConfig config) => [];
        public List<System.Text.Json.Nodes.JsonObject> BuildCrdManifests() => [];
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
