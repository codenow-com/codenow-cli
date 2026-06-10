using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Provisions Pulumi stack resources and related RBAC.
/// </summary>
public interface IPulumiStackProvisioner
{
    /// <summary>
    /// Builds all RBAC resources for the Pulumi stack without applying them.
    /// </summary>
    List<IKubernetesObject<V1ObjectMeta>> BuildPulumiStackRbacResources(
        string namespaceName,
        string serviceAccountName,
        OperatorConfig config,
        IEnumerable<string> targetNamespaces);

    /// <summary>
    /// Applies RBAC required by the Pulumi stack.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="namespaceName">Namespace where the stack runs.</param>
    /// <param name="serviceAccountName">Service account used by the stack.</param>
    /// <param name="config">Operator configuration settings.</param>
    /// <param name="targetNamespaces">Namespaces where stack RBAC should be granted.</param>
    Task ApplyPulumiStackRbacAsync(
        IKubernetesClient client,
        string namespaceName,
        string serviceAccountName,
        OperatorConfig config,
        IEnumerable<string> targetNamespaces);

    /// <summary>
    /// Applies the stack custom resource.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="serviceAccountName">Service account used by the stack.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task ApplyPulumiStackAsync(IKubernetesClient client, string serviceAccountName, OperatorConfig config);

    /// <summary>
    /// Determines whether existing retained workspace inputs differ from the desired bootstrap state.
    /// </summary>
    Task<bool> HasWorkspaceInputsChangedAsync(
        IKubernetesClient client,
        string serviceAccountName,
        OperatorConfig config);

    /// <summary>
    /// Deletes the retained Pulumi workspace so the operator recreates it from the current stack spec.
    /// </summary>
    /// <param name="client">Kubernetes client used to delete resources.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task DeletePulumiWorkspaceAsync(IKubernetesClient client, OperatorConfig config);

    /// <summary>
    /// Creates or updates the Pulumi state PVC when using local state (S3 disabled).
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task CreatePulumiStatePvcAsync(IKubernetesClient client, OperatorConfig config);

    /// <summary>
    /// Builds the Pulumi state PVC resource without applying it.
    /// </summary>
    /// <param name="config">Operator configuration settings.</param>
    V1PersistentVolumeClaim BuildPulumiStatePvcResource(OperatorConfig config);
}

/// <summary>
/// Orchestrates Pulumi stack provisioning steps and RBAC setup.
/// </summary>
internal sealed class PulumiStackProvisioner : IPulumiStackProvisioner
{
    private readonly ILogger<PulumiStackProvisioner> logger;
    private readonly PulumiStackManifestBuilder manifestBuilder;
    private sealed record WorkspaceSnapshot(string Name, JsonObject Spec);

    /// <summary>
    /// Creates a Pulumi stack provisioner.
    /// </summary>
    public PulumiStackProvisioner(
        ILogger<PulumiStackProvisioner> logger,
        PulumiStackManifestBuilder manifestBuilder)
    {
        this.logger = logger;
        this.manifestBuilder = manifestBuilder;
    }

    /// <summary>
    /// Builds all RBAC resources for the Pulumi stack without applying them.
    /// </summary>
    public List<IKubernetesObject<V1ObjectMeta>> BuildPulumiStackRbacResources(
        string namespaceName,
        string serviceAccountName,
        OperatorConfig config,
        IEnumerable<string> targetNamespaces)
    {
        var resources = new List<IKubernetesObject<V1ObjectMeta>>();

        var clusterRoleBindingName = $"{namespaceName}:{serviceAccountName}:system:auth-delegator";
        var stackClusterRoleAdmin = serviceAccountName + "-admin";
        Dictionary<string, string>? annotations = null;
        if (config.S3.Enabled
            && config.S3.AuthenticationMethod == S3AuthenticationMethod.IAMRole
            && !string.IsNullOrWhiteSpace(config.S3.IAMRole))
        {
            annotations = new Dictionary<string, string>
            {
                ["eks.amazonaws.com/role-arn"] = config.S3.IAMRole
            };
        }

        var serviceAccount = new V1ServiceAccount
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceAccountName,
                NamespaceProperty = namespaceName,
                Annotations = annotations
            }
        };
        manifestBuilder.ApplyStackLabels(serviceAccount.Metadata);
        resources.Add(serviceAccount);

        var clusterRoleBinding = new V1ClusterRoleBinding
        {
            Metadata = new V1ObjectMeta
            {
                Name = clusterRoleBindingName
            },
            RoleRef = new V1RoleRef
            {
                ApiGroup = "rbac.authorization.k8s.io",
                Kind = "ClusterRole",
                Name = "system:auth-delegator"
            },
            Subjects =
            [
                new Rbacv1Subject
                {
                    Kind = "ServiceAccount",
                    Name = serviceAccountName,
                    NamespaceProperty = namespaceName
                }
            ]
        };
        manifestBuilder.ApplyStackLabels(clusterRoleBinding.Metadata);
        resources.Add(clusterRoleBinding);

        var stackClusterRole = new V1ClusterRole
        {
            Metadata = new V1ObjectMeta
            {
                Name = stackClusterRoleAdmin
            },
            Rules =
            [
                new V1PolicyRule
                {
                    ApiGroups = ["admissionregistration.k8s.io"],
                    Resources = ["mutatingwebhookconfigurations", "validatingwebhookconfigurations"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["apiextensions.k8s.io"],
                    Resources = ["customresourcedefinitions"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["apiregistration.k8s.io"],
                    Resources = ["apiservices"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["rbac.authorization.k8s.io"],
                    Resources = ["clusterroles", "clusterrolebindings", "roles", "rolebindings"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["networking.k8s.io"],
                    Resources = ["networkpolicies"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["k8s.cni.cncf.io"],
                    Resources = ["network-attachment-definitions"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["operator.tekton.dev"],
                    Resources = ["*"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["external-secrets.io"],
                    Resources = ["clusterexternalsecrets", "clustersecretstores"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["postgresql.cnpg.io"],
                    Resources = ["clusterimagecatalogs"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["scheduling.k8s.io"],
                    Resources = ["priorityclasses"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["authentication.concierge.pinniped.dev"],
                    Resources = ["jwtauthenticators"],
                    Verbs = ["*"]
                },
                new V1PolicyRule
                {
                    ApiGroups = ["config.concierge.pinniped.dev"],
                    Resources = ["credentialissuers"],
                    Verbs = ["*"]
                }
            ]
        };
        manifestBuilder.ApplyStackLabels(stackClusterRole.Metadata);
        resources.Add(stackClusterRole);

        var stackClusterRoleBinding = new V1ClusterRoleBinding
        {
            Metadata = new V1ObjectMeta
            {
                Name = stackClusterRoleAdmin
            },
            RoleRef = new V1RoleRef
            {
                ApiGroup = "rbac.authorization.k8s.io",
                Kind = "ClusterRole",
                Name = stackClusterRoleAdmin
            },
            Subjects =
            [
                new Rbacv1Subject
                {
                    Kind = "ServiceAccount",
                    Name = serviceAccountName,
                    NamespaceProperty = namespaceName
                }
            ]
        };
        manifestBuilder.ApplyStackLabels(stackClusterRoleBinding.Metadata);
        resources.Add(stackClusterRoleBinding);

        var readerRoleSuffix = "-reader";
        var kubeSystemRole = new V1Role
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceAccountName + readerRoleSuffix,
                NamespaceProperty = "kube-system"
            },
            Rules =
            [
                new V1PolicyRule
                {
                    ApiGroups = ["rbac.authorization.k8s.io"],
                    Resources = ["roles", "rolebindings"],
                    ResourceNames =
                    [
                        "pinniped-concierge-kube-system-pod-read",
                        "pinniped-concierge-extension-apiserver-authentication-reader"
                    ],
                    Verbs = ["*"]
                }
            ]
        };
        manifestBuilder.ApplyStackLabels(kubeSystemRole.Metadata);
        resources.Add(kubeSystemRole);

        var kubeSystemRoleBinding = new V1RoleBinding
        {
            Metadata = new V1ObjectMeta
            {
                Name = serviceAccountName + readerRoleSuffix,
                NamespaceProperty = "kube-system"
            },
            RoleRef = new V1RoleRef
            {
                ApiGroup = "rbac.authorization.k8s.io",
                Kind = "Role",
                Name = serviceAccountName + readerRoleSuffix,
            },
            Subjects =
            [
                new Rbacv1Subject
                {
                    Kind = "ServiceAccount",
                    Name = serviceAccountName,
                    NamespaceProperty = namespaceName
                }
            ]
        };
        manifestBuilder.ApplyStackLabels(kubeSystemRoleBinding.Metadata);
        resources.Add(kubeSystemRoleBinding);

        var adminRoleSuffix = "-admin";
        foreach (var targetNamespace in targetNamespaces)
        {
            var namespaceRole = new V1Role
            {
                Metadata = new V1ObjectMeta
                {
                    Name = serviceAccountName + adminRoleSuffix,
                    NamespaceProperty = targetNamespace
                },
                Rules =
                [
                    new V1PolicyRule
                    {
                        ApiGroups = ["*"],
                        Resources = ["*"],
                        Verbs = ["*"]
                    }
                ]
            };
            manifestBuilder.ApplyStackLabels(namespaceRole.Metadata);
            resources.Add(namespaceRole);

            var namespaceRoleBinding = new V1RoleBinding
            {
                Metadata = new V1ObjectMeta
                {
                    Name = serviceAccountName + adminRoleSuffix,
                    NamespaceProperty = targetNamespace
                },
                RoleRef = new V1RoleRef
                {
                    ApiGroup = "rbac.authorization.k8s.io",
                    Kind = "Role",
                    Name = serviceAccountName + adminRoleSuffix,
                },
                Subjects =
                [
                    new Rbacv1Subject
                    {
                        Kind = "ServiceAccount",
                        Name = serviceAccountName,
                        NamespaceProperty = namespaceName
                    }
                ]
            };
            manifestBuilder.ApplyStackLabels(namespaceRoleBinding.Metadata);
            resources.Add(namespaceRoleBinding);
        }

        return resources;
    }

    /// <inheritdoc />
    public async Task ApplyPulumiStackRbacAsync(
        IKubernetesClient client,
        string namespaceName,
        string serviceAccountName,
        OperatorConfig config,
        IEnumerable<string> targetNamespaces)
    {
        logger.LogInformation(
            "Applying Pulumi stack RBAC in namespace {ns} ...",
            namespaceName);

        var resources = BuildPulumiStackRbacResources(namespaceName, serviceAccountName, config, targetNamespaces);

        foreach (var resource in resources)
            await client.ApplyAsync(resource);

        logger.LogInformation(
            "Pulumi stack RBAC applied in namespace {ns}.",
            namespaceName);
    }

    /// <inheritdoc />
    public async Task ApplyPulumiStackAsync(IKubernetesClient client, string serviceAccountName, OperatorConfig config)
    {
        var stackName = DataPlaneConstants.StackName;
        var stack = manifestBuilder.BuildStack(config, serviceAccountName);

        var patch = new V1Patch(
            stack.ToJsonString(),
            V1Patch.PatchType.ApplyPatch);

        await client.CustomObjects.PatchNamespacedCustomObjectAsync(
            patch,
            group: "pulumi.com",
            version: "v1",
            namespaceParameter: config.Kubernetes.Namespaces.System.Name,
            plural: "stacks",
            name: stackName,
            fieldManager: KubernetesConstants.FieldManager,
            force: true);
    }

    /// <inheritdoc />
    public async Task<bool> HasWorkspaceInputsChangedAsync(
        IKubernetesClient client,
        string serviceAccountName,
        OperatorConfig config)
    {
        var existing = await FindExistingWorkspaceAsync(client, config);
        if (existing is null)
            return false;

        var expectedSpec = BuildExpectedWorkspaceSpec(config, serviceAccountName);
        return !WorkspaceSpecMatches(expectedSpec, existing.Spec);
    }

    private JsonObject BuildExpectedWorkspaceSpec(OperatorConfig config, string serviceAccountName)
    {
        var stack = manifestBuilder.BuildStack(config, serviceAccountName);
        var stackSpec = stack["spec"]!.AsObject();
        var workspaceTemplateSpec = stackSpec["workspaceTemplate"]!
            .AsObject()["spec"]!
            .DeepClone()
            .AsObject();

        workspaceTemplateSpec["serviceAccountName"] = serviceAccountName;
        workspaceTemplateSpec["stacks"] = new JsonArray(
            new JsonObject
            {
                ["name"] = DataPlaneConstants.StackName
            });

        if (config.FluxCD?.Enabled == true)
        {
            workspaceTemplateSpec["flux"] = new JsonObject
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

            var gitAuth = BuildWorkspaceGitAuth(config);
            if (gitAuth is not null)
                git["auth"] = gitAuth;

            workspaceTemplateSpec["git"] = git;
        }

        return workspaceTemplateSpec;
    }

    private static JsonObject? BuildWorkspaceGitAuth(OperatorConfig config)
    {
        var secretName = DataPlaneConstants.OperatorConfigSecretName;
        return config.Scm.AuthenticationMethod switch
        {
            ScmAuthenticationMethod.AccessToken => new JsonObject
            {
                ["token"] = BuildSecretKeySelector(
                    secretName,
                    DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthToken)
            },
            ScmAuthenticationMethod.UsernamePassword => new JsonObject
            {
                ["username"] = BuildSecretKeySelector(
                    secretName,
                    DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthUsername),
                ["password"] = BuildSecretKeySelector(
                    secretName,
                    DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthPassword)
            },
            _ => null
        };
    }

    private static JsonObject BuildSecretKeySelector(string secretName, string key)
    {
        return new JsonObject
        {
            ["name"] = secretName,
            ["key"] = key
        };
    }

    private static bool WorkspaceSpecMatches(JsonObject expected, JsonObject existing)
    {
        if (!ProxyEnvMatches(expected["podTemplate"], existing["podTemplate"]))
            return false;

        var expectedHash = GetPodTemplateInputHash(expected["podTemplate"]);
        var existingHash = GetPodTemplateInputHash(existing["podTemplate"]);
        if (expectedHash is not null && existingHash is not null)
            return string.Equals(expectedHash, existingHash, StringComparison.Ordinal);

        foreach (var propertyName in new[] { "image", "resources" })
        {
            if (existing[propertyName] is not null &&
                !JsonEquals(expected[propertyName], existing[propertyName]))
                return false;
        }

        if (!PodTemplateMatches(expected["podTemplate"], existing["podTemplate"]))
            return false;

        if (existing["serviceAccountName"] is not null &&
            !JsonEquals(expected["serviceAccountName"], existing["serviceAccountName"]))
            return false;

        if (!SourceMatches(expected, existing))
            return false;

        return WorkspaceStacksMatch(expected["stacks"] as JsonArray, existing["stacks"] as JsonArray);
    }

    private static bool PodTemplateMatches(JsonNode? expected, JsonNode? existing)
    {
        if (JsonEquals(expected, existing))
            return true;

        var existingHash = GetPodTemplateInputHash(existing);
        if (existingHash is null)
            return true;

        var expectedWithoutHash = expected?.DeepClone();
        RemovePodTemplateInputHash(expectedWithoutHash);
        var existingWithoutHash = existing?.DeepClone();
        RemovePodTemplateInputHash(existingWithoutHash);
        return JsonContains(expectedWithoutHash, existingWithoutHash);
    }

    private static bool ProxyEnvMatches(JsonNode? expectedPodTemplate, JsonNode? existingPodTemplate)
    {
        foreach (var containerPath in new[] { "containers", "initContainers" })
        {
            var expectedContainers = expectedPodTemplate?["spec"]?[containerPath] as JsonArray;
            var existingContainers = existingPodTemplate?["spec"]?[containerPath] as JsonArray;
            if (existingContainers is null)
                continue;

            foreach (var existingContainer in existingContainers.OfType<JsonObject>())
            {
                var containerName = existingContainer["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(containerName))
                    continue;

                var expectedContainer = expectedContainers?
                    .OfType<JsonObject>()
                    .FirstOrDefault(container => string.Equals(
                        container["name"]?.GetValue<string>(),
                        containerName,
                        StringComparison.Ordinal));

                foreach (var envName in new[] { "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY" })
                {
                    var expectedValue = GetEnvValue(expectedContainer, envName);
                    var existingValue = GetEnvValue(existingContainer, envName);
                    if (expectedValue is null && existingValue is null)
                        continue;
                    if (!string.Equals(expectedValue, existingValue, StringComparison.Ordinal))
                        return false;
                }
            }
        }

        return true;
    }

    private static string? GetEnvValue(JsonObject? container, string envName)
    {
        if (container?["env"] is not JsonArray env)
            return null;

        return env
            .OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(
                item["name"]?.GetValue<string>(),
                envName,
                StringComparison.Ordinal))?["value"]?.GetValue<string>();
    }

    private static bool SourceMatches(JsonObject expected, JsonObject existing)
    {
        if (expected["git"] is JsonObject expectedGit)
            return GitSourceMatches(expectedGit, existing["git"] as JsonObject);

        if (expected["flux"] is JsonObject expectedFlux)
            return JsonObjectContains(expectedFlux, existing["flux"] as JsonObject);

        return true;
    }

    private static bool GitSourceMatches(JsonObject expectedGit, JsonObject? existingGit)
    {
        if (existingGit is null)
            return true;

        if (existingGit["url"] is not null && !JsonEquals(expectedGit["url"], existingGit["url"]))
            return false;
        if (existingGit["dir"] is not null && !JsonEquals(expectedGit["dir"], existingGit["dir"]))
            return false;
        if (existingGit["ref"] is not null && !JsonEquals(expectedGit["ref"], existingGit["ref"]))
            return false;

        return existingGit["auth"] is not JsonObject existingAuth ||
               expectedGit["auth"] is JsonObject expectedAuth && JsonObjectContains(expectedAuth, existingAuth);
    }

    private static bool WorkspaceStacksMatch(JsonArray? expectedStacks, JsonArray? existingStacks)
    {
        if (expectedStacks is null || expectedStacks.Count == 0)
            return true;
        if (existingStacks is null)
            return true;

        foreach (var expectedStack in expectedStacks.OfType<JsonObject>())
        {
            var expectedName = expectedStack["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(expectedName))
                continue;

            var matchingStack = existingStacks
                .OfType<JsonObject>()
                .FirstOrDefault(stack => string.Equals(
                    stack["name"]?.GetValue<string>(),
                    expectedName,
                    StringComparison.Ordinal));

            if (matchingStack is null)
                return false;
        }

        return true;
    }

    private static bool JsonObjectContains(JsonObject expected, JsonObject? existing)
    {
        if (existing is null)
            return false;

        foreach (var (key, expectedValue) in expected)
        {
            if (!JsonEquals(expectedValue, existing[key]))
                return false;
        }

        return true;
    }

    private static bool JsonContains(JsonNode? expected, JsonNode? existing)
    {
        if (expected is null)
            return true;
        if (existing is null)
            return false;

        if (expected is JsonObject expectedObject)
        {
            if (existing is not JsonObject existingObject)
                return false;

            foreach (var (key, expectedValue) in expectedObject)
            {
                if (!JsonContains(expectedValue, existingObject[key]))
                    return false;
            }

            return true;
        }

        if (expected is JsonArray expectedArray)
        {
            if (existing is not JsonArray existingArray)
                return false;

            foreach (var expectedItem in expectedArray)
            {
                if (expectedItem is JsonObject expectedItemObject &&
                    expectedItemObject["name"]?.GetValue<string>() is { } expectedName)
                {
                    var matchingExistingItem = existingArray
                        .OfType<JsonObject>()
                        .FirstOrDefault(item => string.Equals(
                            item["name"]?.GetValue<string>(),
                            expectedName,
                            StringComparison.Ordinal));

                    if (!JsonContains(expectedItemObject, matchingExistingItem))
                        return false;

                    continue;
                }

                if (!existingArray.Any(existingItem => JsonEquals(expectedItem, existingItem)))
                    return false;
            }

            return true;
        }

        return JsonEquals(expected, existing);
    }

    private static bool JsonEquals(JsonNode? expected, JsonNode? existing)
    {
        if (expected is null || existing is null)
            return expected is null && existing is null;

        return string.Equals(CanonicalJson(expected), CanonicalJson(existing), StringComparison.Ordinal);
    }

    private static string? GetPodTemplateInputHash(JsonNode? podTemplate)
    {
        return podTemplate?["metadata"]?["annotations"]?[DataPlaneConstants.WorkspaceInputHashAnnotation]
            ?.GetValue<string>();
    }

    private static void RemovePodTemplateInputHash(JsonNode? podTemplate)
    {
        if (podTemplate?["metadata"] is not JsonObject metadata ||
            metadata["annotations"] is not JsonObject annotations)
            return;

        annotations.Remove(DataPlaneConstants.WorkspaceInputHashAnnotation);
        if (annotations.Count == 0)
            metadata.Remove("annotations");
    }

    private static JsonObject? ExtractSpec(object? customObject)
    {
        return customObject switch
        {
            JsonObject obj => obj["spec"] as JsonObject,
            JsonElement element when element.TryGetProperty("spec", out var spec) =>
                JsonNode.Parse(spec.GetRawText()) as JsonObject,
            _ => null
        };
    }

    private static async Task<WorkspaceSnapshot?> FindExistingWorkspaceAsync(
        IKubernetesClient client,
        OperatorConfig config)
    {
        try
        {
            var workspaceList = await client.CustomObjects.ListNamespacedCustomObjectAsync(
                group: "auto.pulumi.com",
                version: "v1alpha1",
                namespaceParameter: config.Kubernetes.Namespaces.System.Name,
                plural: "workspaces");

            var workspaces = ExtractItems(workspaceList)
                .Select(item => new
                {
                    Object = item,
                    Name = GetMetadataName(item),
                    Spec = item["spec"] as JsonObject
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Spec is not null)
                .ToList();

            var ownedWorkspace = workspaces.FirstOrDefault(item => HasStackOwnerReference(item.Object));
            if (ownedWorkspace is not null)
                return new WorkspaceSnapshot(ownedWorkspace.Name!, ownedWorkspace.Spec!);

            var stackWorkspace = workspaces.FirstOrDefault(item => HasStackReference(item.Spec));
            if (stackWorkspace is not null)
                return new WorkspaceSnapshot(stackWorkspace.Name!, stackWorkspace.Spec!);

            var namedWorkspace = workspaces.FirstOrDefault(item =>
                string.Equals(item.Name, DataPlaneConstants.WorkspaceName, StringComparison.Ordinal));
            if (namedWorkspace is not null)
                return new WorkspaceSnapshot(namedWorkspace.Name!, namedWorkspace.Spec!);

            var bootstrapWorkspaces = workspaces.Where(item => HasBootstrapLabels(item.Object)).ToList();
            if (bootstrapWorkspaces.Count == 1)
                return new WorkspaceSnapshot(bootstrapWorkspaces[0].Name!, bootstrapWorkspaces[0].Spec!);

            return null;
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static IEnumerable<JsonObject> ExtractItems(object? customObjectList)
    {
        JsonArray? items = customObjectList switch
        {
            JsonObject obj => obj["items"] as JsonArray,
            JsonElement element when element.TryGetProperty("items", out var itemsElement) =>
                JsonNode.Parse(itemsElement.GetRawText()) as JsonArray,
            _ => null
        };

        if (items is null)
            yield break;

        foreach (var item in items.OfType<JsonObject>())
            yield return item;
    }

    private static string? GetMetadataName(JsonObject customObject)
    {
        return customObject["metadata"]?["name"]?.GetValue<string>();
    }

    private static bool HasStackOwnerReference(JsonObject customObject)
    {
        if (customObject["metadata"]?["ownerReferences"] is not JsonArray ownerReferences)
            return false;

        return ownerReferences.OfType<JsonObject>().Any(ownerReference =>
            string.Equals(ownerReference["apiVersion"]?.GetValue<string>(), "pulumi.com/v1", StringComparison.Ordinal) &&
            string.Equals(ownerReference["kind"]?.GetValue<string>(), "Stack", StringComparison.Ordinal) &&
            string.Equals(ownerReference["name"]?.GetValue<string>(), DataPlaneConstants.StackName, StringComparison.Ordinal));
    }

    private static bool HasStackReference(JsonObject? spec)
    {
        return (spec?["stacks"] as JsonArray)?.OfType<JsonObject>().Any(stack =>
            string.Equals(stack["name"]?.GetValue<string>(), DataPlaneConstants.StackName, StringComparison.Ordinal)) == true;
    }

    private static bool HasBootstrapLabels(JsonObject customObject)
    {
        var labels = customObject["metadata"]?["labels"] as JsonObject;
        if (labels is null)
            return false;

        return ProvisioningCommonTools.BootstrapLabels.All(label =>
            string.Equals(labels[label.Key]?.GetValue<string>(), label.Value, StringComparison.Ordinal));
    }

    private static string CanonicalJson(JsonNode node)
    {
        var sb = new StringBuilder();
        WriteCanonicalJson(sb, node);
        return sb.ToString();
    }

    private static void WriteCanonicalJson(StringBuilder sb, JsonNode? node)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                sb.Append('{');
                var firstProperty = true;
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!firstProperty)
                        sb.Append(',');
                    firstProperty = false;
                    sb.Append('"');
                    sb.Append(JsonEncodedText.Encode(property.Key).ToString());
                    sb.Append('"');
                    sb.Append(':');
                    WriteCanonicalJson(sb, property.Value);
                }
                sb.Append('}');
                break;
            case JsonArray array:
                sb.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    WriteCanonicalJson(sb, array[i]);
                }
                sb.Append(']');
                break;
            default:
                sb.Append(node.ToJsonString());
                break;
        }
    }

    /// <inheritdoc />
    public async Task DeletePulumiWorkspaceAsync(IKubernetesClient client, OperatorConfig config)
    {
        var workspace = await FindExistingWorkspaceAsync(client, config);
        if (workspace is null)
        {
            logger.LogInformation(
                "Pulumi workspace for stack {stack} does not exist in namespace {ns}; skipping delete.",
                DataPlaneConstants.StackName,
                config.Kubernetes.Namespaces.System.Name);
            return;
        }

        try
        {
            await client.CustomObjects.DeleteNamespacedCustomObjectAsync(
                group: "auto.pulumi.com",
                version: "v1alpha1",
                namespaceParameter: config.Kubernetes.Namespaces.System.Name,
                plural: "workspaces",
                name: workspace.Name);

            logger.LogInformation(
                "Deleted Pulumi workspace {workspace} in namespace {ns}; the operator will recreate it from the current Stack.",
                workspace.Name,
                config.Kubernetes.Namespaces.System.Name);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "Pulumi workspace {workspace} does not exist in namespace {ns}; skipping delete.",
                workspace.Name,
                config.Kubernetes.Namespaces.System.Name);
        }
    }

    /// <inheritdoc />
    public async Task CreatePulumiStatePvcAsync(
        IKubernetesClient client,
        OperatorConfig config)
    {
        if (config.S3.Enabled)
            return;

        var pvc = manifestBuilder.BuildPulumiStatePvc(config);

        try
        {
            var existing = await client.CoreV1.ReadNamespacedPersistentVolumeClaimAsync(
                DataPlaneConstants.PulumiStatePvcName,
                config.Kubernetes.Namespaces.System.Name);
            var desiredStorage = pvc.Spec?.Resources?.Requests?["storage"];
            var existingStorage = existing.Spec?.Resources?.Requests?["storage"];
            var desiredValue = desiredStorage?.ToString();
            var existingValue = existingStorage?.ToString();

            if (!string.IsNullOrWhiteSpace(desiredValue) &&
                !string.Equals(desiredValue, existingValue, StringComparison.Ordinal))
            {
                var patchObj = new JsonObject
                {
                    ["spec"] = new JsonObject
                    {
                        ["resources"] = new JsonObject
                        {
                            ["requests"] = new JsonObject
                            {
                                ["storage"] = desiredValue
                            }
                        }
                    }
                };

                await client.CoreV1.PatchNamespacedPersistentVolumeClaimAsync(
                    new V1Patch(patchObj.ToJsonString(), V1Patch.PatchType.MergePatch),
                    DataPlaneConstants.PulumiStatePvcName,
                    config.Kubernetes.Namespaces.System.Name);
            }
            else
            {
                logger.LogInformation(
                    "PVC {pvc} already exists in namespace {ns}; skipping update to avoid immutable spec changes.",
                    DataPlaneConstants.PulumiStatePvcName,
                    config.Kubernetes.Namespaces.System.Name);
            }

            return;
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.CoreV1.CreateNamespacedPersistentVolumeClaimAsync(
                pvc,
                config.Kubernetes.Namespaces.System.Name);
        }
    }

    /// <inheritdoc />
    public V1PersistentVolumeClaim BuildPulumiStatePvcResource(OperatorConfig config)
    {
        return manifestBuilder.BuildPulumiStatePvc(config);
    }

}
