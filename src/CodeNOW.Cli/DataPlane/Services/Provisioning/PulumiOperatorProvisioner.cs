using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.Common.Yaml;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json.Nodes;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Provisions Pulumi operator manifests and related resources.
/// </summary>
public interface IPulumiOperatorProvisioner
{
    /// <summary>
    /// Applies CRD manifests for the operator.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    Task ApplyCrdManifestsAsync(IKubernetesClient client);
    /// <summary>
    /// Applies RBAC manifests for the operator.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="targetNamespace">Namespace where operator RBAC should be bound.</param>
    Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace);
    /// <summary>
    /// Applies the operator deployment manifest.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config);
    /// <summary>
    /// Waits for the operator deployment to be ready.
    /// </summary>
    /// <param name="client">Kubernetes client used to read resources.</param>
    /// <param name="namespaceName">Namespace containing the operator deployment.</param>
    /// <param name="timeout">Maximum wait time before failing.</param>
    Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout);
    /// <summary>
    /// Creates or updates the operator configuration secret.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config);
    /// <summary>
    /// Returns the operator image reference resolved against the registry configuration.
    /// </summary>
    /// <param name="config">Operator configuration settings.</param>
    string GetOperatorImage(OperatorConfig config);
}

/// <summary>
/// Applies Pulumi operator manifests and waits for readiness.
/// </summary>
internal sealed class PulumiOperatorProvisioner : IPulumiOperatorProvisioner
{
    /// <summary>
    /// Base name for the operator controller deployment.
    /// </summary>
    internal const string OperatorDeploymentBaseName = "controller-manager";
    /// <summary>
    /// Prefix for Pulumi operator resource names.
    /// </summary>
    internal const string PulumiOperatorNamePrefix = "cn-pulumi-";
    /// <summary>
    /// Prefix for Pulumi workspace resources.
    /// </summary>
    internal const string PulumiWorkspaceNamePrefix = "pulumi-";
    /// <summary>
    /// Relative path to embedded Pulumi operator manifests on disk.
    /// </summary>
    internal const string PulumiOperatorManifestsRelativePath = "DataPlane/Manifests/PulumiOperator";
    /// <summary>
    /// Embedded resource root for Pulumi operator manifests.
    /// </summary>
    internal const string PulumiOperatorManifestsResourceRoot = "DataPlane/Manifests/PulumiOperator/";
    private readonly ILogger<PulumiOperatorProvisioner> logger;
    private readonly YamlToJsonConverter yamlToJsonConverter;
    private readonly DataPlaneConfigSecretBuilder configSecretBuilder;
    private readonly string operatorImage;
    private readonly string operatorVersion;

    /// <summary>
    /// Creates a Pulumi operator provisioner.
    /// </summary>
    public PulumiOperatorProvisioner(
        ILogger<PulumiOperatorProvisioner> logger,
        YamlToJsonConverter yamlToJsonConverter,
        DataPlaneConfigSecretBuilder configSecretBuilder,
        IPulumiOperatorInfoProvider operatorInfoProvider)
    {
        this.logger = logger;
        this.yamlToJsonConverter = yamlToJsonConverter;
        this.configSecretBuilder = configSecretBuilder;
        var info = operatorInfoProvider.GetInfo();
        operatorImage = info.OperatorImage;
        operatorVersion = info.OperatorVersion;
    }

    /// <inheritdoc />
    public Task ApplyCrdManifestsAsync(IKubernetesClient client)
    {
        return ApplyCrdManifestsInternalAsync(client);
    }

    /// <inheritdoc />
    public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace)
    {
        return ApplyRbacManifestsInternalAsync(client, targetNamespace);
    }

    /// <inheritdoc />
    public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config)
    {
        return ApplyOperatorDeploymentInternalAsync(client, config);
    }

    /// <inheritdoc />
    public async Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
    {
        var deploymentName = EnsurePrefixed(OperatorDeploymentBaseName);
        await WaitForDeploymentReadyAsync(client, namespaceName, deploymentName, timeout);
    }

    /// <inheritdoc />
    public async Task CreateDataPlaneConfigSecretAsync(
        IKubernetesClient client,
        OperatorConfig config)
    {
        var secret = configSecretBuilder.Build(config);
        KubernetesManifestTools.ApplyLabels(secret.Metadata, ProvisioningCommonTools.BootstrapLabels);

        try
        {
            var existing = await client.CoreV1.ReadNamespacedSecretAsync(
                DataPlaneConstants.OperatorConfigSecretName,
                config.Kubernetes.Namespaces.System.Name);
            secret.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;

            await client.CoreV1.ReplaceNamespacedSecretAsync(
                secret,
                DataPlaneConstants.OperatorConfigSecretName,
                config.Kubernetes.Namespaces.System.Name);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.CoreV1.CreateNamespacedSecretAsync(
                secret,
                config.Kubernetes.Namespaces.System.Name);
        }
    }

    /// <inheritdoc />
    public string GetOperatorImage(OperatorConfig config)
    {
        return ProvisioningCommonTools.ResolveImage(config, operatorImage);
    }

    private async Task ApplyCrdManifestsInternalAsync(IKubernetesClient client)
    {
        logger.LogInformation("Applying CRDs...");
        foreach (var (fileName, yaml) in ProvisioningCommonTools.GetYamlDocuments(
                     PulumiOperatorManifestsResourceRoot,
                     PulumiOperatorManifestsRelativePath,
                     "crd"))
        {
            logger.LogInformation("Applying CRD file: {file}", fileName);

            foreach (var jsonObj in yamlToJsonConverter.ConvertAll(yaml))
            {
                var crdName = jsonObj.GetRequiredString("metadata.name");
                var patch = new V1Patch(jsonObj.ToJsonString(), V1Patch.PatchType.ApplyPatch);

                await client.ApiextensionsV1.PatchCustomResourceDefinitionAsync(
                    patch,
                    crdName,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);

                logger.LogInformation("Applied CRD {name}.", crdName);
            }
        }
    }

    private async Task ApplyRbacManifestsInternalAsync(IKubernetesClient client, string targetNamespace)
    {
        logger.LogInformation("Applying RBAC to namespace {ns}...", targetNamespace);
        foreach (var (fileName, yaml) in ProvisioningCommonTools.GetYamlDocuments(
                     PulumiOperatorManifestsResourceRoot,
                     PulumiOperatorManifestsRelativePath,
                     "rbac"))
        {
            logger.LogInformation("Applying RBAC file: {file}", fileName);

            foreach (var jsonObj in yamlToJsonConverter.ConvertAll(yaml))
            {
                var kind = jsonObj.GetRequiredString("kind");
                var rbacName = EnsurePrefixed(jsonObj.GetRequiredString("metadata.name"));
                jsonObj.Set("metadata.name", rbacName);

                KubernetesManifestTools.NormalizeMetadataMaps(jsonObj);
                ProvisioningCommonTools.ApplyOperatorLabels(
                    jsonObj,
                    "metadata",
                    DataPlaneConstants.PulumiOperatorAppLabelValue,
                    operatorVersion);

                if (kind is "ServiceAccount" or "Role" or "RoleBinding")
                {
                    jsonObj.Set("metadata.namespace", targetNamespace);
                }

                if (kind is "RoleBinding" or "ClusterRoleBinding")
                {
                    if (jsonObj["subjects"] is JsonArray subjects)
                    {
                        for (int i = 0; i < subjects.Count; i++)
                        {
                            if (jsonObj.TryGetString($"subjects[{i}].name", out var subjectName))
                            {
                                jsonObj.Set($"subjects[{i}].name", EnsurePrefixed(subjectName));
                            }
                            jsonObj.Set($"subjects[{i}].namespace", targetNamespace);
                        }
                    }
                }

                if (kind is "RoleBinding" or "ClusterRoleBinding")
                {
                    if (jsonObj.TryGetString("roleRef.name", out var roleRefName))
                    {
                        jsonObj.Set("roleRef.name", EnsurePrefixed(roleRefName));
                    }
                }

                if (kind is "Role" or "ClusterRole")
                {
                    NormalizeServiceAccountResourceNames(jsonObj);
                }

                var kubeObj = KubernetesManifestTools.DeserializeByKind(jsonObj.ToJsonString(), kind);
                await client.ApplyAsync(kubeObj);

                logger.LogInformation("Applied RBAC {kind} '{name}'.", kind, rbacName);
            }
        }
    }

    private async Task ApplyOperatorDeploymentInternalAsync(IKubernetesClient client, OperatorConfig config)
    {
        logger.LogInformation("Applying operator deployment to namespace {ns}...", config.Kubernetes.Namespaces.System.Name);
        var yaml = ProvisioningCommonTools.GetYamlDocument(
            PulumiOperatorManifestsResourceRoot,
            PulumiOperatorManifestsRelativePath,
            "manager/manager.yaml");

        foreach (var jsonObj in yamlToJsonConverter.ConvertAll(yaml))
        {
            var kind = jsonObj.GetRequiredString("kind");
            var name = EnsurePrefixed(jsonObj.GetRequiredString("metadata.name"));
            jsonObj.Set("metadata.name", name);
            var targetNamespace = config.Kubernetes.Namespaces.System.Name;

            if (kind == "Namespace")
                continue;

            KubernetesManifestTools.NormalizeMetadataMaps(jsonObj);
            ProvisioningCommonTools.ApplyOperatorLabels(
                jsonObj,
                "metadata",
                DataPlaneConstants.PulumiOperatorAppLabelValue,
                operatorVersion);

            if (kind is "Deployment")
            {
                ProvisioningCommonTools.ApplyOperatorLabels(
                    jsonObj,
                    "spec.template.metadata",
                    DataPlaneConstants.PulumiOperatorAppLabelValue,
                    operatorVersion);
                ApplyDeploymentPodSpecDefaults(jsonObj, targetNamespace, config);
                ApplyDeploymentSecurityContext(jsonObj, config.Kubernetes.SecurityContextRunAsId);
                ProvisioningCommonTools.ApplyServiceAccountAttachments(jsonObj);
                jsonObj.Set("spec.template.spec.containers[0].image", GetOperatorImage(config));
            }
            else if (kind is "Service")
            {
                jsonObj.Set("metadata.name", name);
                jsonObj.Set("metadata.namespace", targetNamespace);
            }
            else
            {
                logger.LogWarning("Unsupported operator manifest kind '{kind}', skipping.", kind);
                continue;
            }

            var kubeObj = KubernetesManifestTools.DeserializeByKind(jsonObj.ToJsonString(), kind);
            await client.ApplyAsync(kubeObj);

            logger.LogInformation("Applied operator {kind} '{name}'.", kind, name);
        }
    }

    private static void NormalizeServiceAccountResourceNames(JsonObject jsonObj)
    {
        if (jsonObj["rules"] is not JsonArray rules)
            return;

        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (!RuleTargetsServiceAccounts(rule))
                continue;

            if (rule["resourceNames"] is not JsonArray resourceNames)
                continue;

            for (var i = 0; i < resourceNames.Count; i++)
            {
                if (resourceNames[i] is JsonValue nameValue &&
                    nameValue.TryGetValue<string>(out var resourceName))
                {
                    resourceNames[i] = EnsurePrefixed(resourceName);
                }
            }
        }
    }

    private static bool RuleTargetsServiceAccounts(JsonObject rule)
    {
        if (rule["resources"] is not JsonArray resources)
            return false;

        return resources
            .Select(resource => resource?.GetValue<string>())
            .Any(resource =>
                resource is not null &&
                resource.StartsWith("serviceaccounts", StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyDeploymentPodSpecDefaults(JsonObject jsonObj, string targetNamespace, OperatorConfig config)
    {
        jsonObj.Set("metadata.namespace", targetNamespace);
        jsonObj.Set("spec.template.metadata.namespace", targetNamespace);
        jsonObj.Set("spec.template.spec.imagePullSecrets[0].name", KubernetesConstants.SystemImagePullSecret);
        jsonObj.Set("spec.template.spec.automountServiceAccountToken", false);
        if (jsonObj["spec"]?["template"]?["spec"] is JsonObject podSpec)
            ProvisioningCommonTools.ApplySystemNodePlacement(podSpec, config);
        if (jsonObj.TryGetString("spec.template.spec.serviceAccountName", out var serviceAccountName))
        {
            jsonObj.Set("spec.template.spec.serviceAccountName", EnsurePrefixed(serviceAccountName));
        }
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
        {
            var podSpecObj = JsonManifestEditor.EnsureObjectPath(jsonObj, "spec.template.spec");
            var containers = JsonManifestEditor.EnsureArray(podSpecObj, "containers");
            JsonObject container;
            if (containers.Count == 0 || containers[0] is not JsonObject existingContainer)
            {
                container = new JsonObject();
                if (containers.Count == 0)
                    containers.Add((JsonNode)container);
                else
                    containers[0] = container;
            }
            else
            {
                container = existingContainer;
            }

            var volumeMounts = JsonManifestEditor.EnsureArray(container, "volumeMounts");
            ProvisioningCommonTools.EnsureVolumeMount(
                volumeMounts,
                "ca-certificates",
                $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}",
                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert,
                true);

            var volumes = JsonManifestEditor.EnsureArray(podSpecObj, "volumes");
            ProvisioningCommonTools.EnsureSecretVolumeWithItem(
                volumes,
                "ca-certificates",
                DataPlaneConstants.OperatorConfigSecretName,
                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert);
        }
    }

    private static void ApplyDeploymentSecurityContext(JsonObject jsonObj, int runAsId)
    {
        jsonObj.Set("spec.template.spec.securityContext.runAsUser", runAsId);
        jsonObj.Set("spec.template.spec.securityContext.runAsGroup", runAsId);
        jsonObj.Set("spec.template.spec.securityContext.fsGroup", runAsId);
        jsonObj.Set("spec.template.spec.containers[0].securityContext.readOnlyRootFilesystem", true);
        jsonObj.Set("spec.template.spec.containers[0].securityContext.seccompProfile.type", "RuntimeDefault");
    }

    private static string EnsurePrefixed(string name)
    {
        const string prefix = PulumiOperatorNamePrefix;
        if (name.StartsWith(prefix, StringComparison.Ordinal))
            return name;

        const string pulumiPrefix = PulumiWorkspaceNamePrefix;
        if (name.StartsWith(pulumiPrefix, StringComparison.Ordinal))
            name = name[pulumiPrefix.Length..];

        return prefix + name;
    }

    private static Task WaitForDeploymentReadyAsync(
        IKubernetesClient client,
        string namespaceName,
        string deploymentName,
        TimeSpan timeout)
    {
        return ProvisioningCommonTools.WaitForDeploymentReadyAsync(client, namespaceName, deploymentName, timeout);
    }
}
