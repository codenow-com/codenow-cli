using System.Net;
using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Provisions Pulumi stack resources and related RBAC.
/// </summary>
public interface IPulumiStackProvisioner
{
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
    /// Creates or updates the Pulumi state PVC when using local state (S3 disabled).
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task CreatePulumiStatePvcAsync(IKubernetesClient client, OperatorConfig config);
}

/// <summary>
/// Orchestrates Pulumi stack provisioning steps and RBAC setup.
/// </summary>
internal sealed class PulumiStackProvisioner : IPulumiStackProvisioner
{
    private readonly ILogger<PulumiStackProvisioner> logger;
    private readonly PulumiStackManifestBuilder manifestBuilder;

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

        await client.ApplyAsync(serviceAccount);
        await client.ApplyAsync(clusterRoleBinding);
        await client.ApplyAsync(stackClusterRole);
        await client.ApplyAsync(stackClusterRoleBinding);
        await client.ApplyAsync(kubeSystemRole);
        await client.ApplyAsync(kubeSystemRoleBinding);

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

            await client.ApplyAsync(namespaceRole);
            await client.ApplyAsync(namespaceRoleBinding);
        }

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

}
