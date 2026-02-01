using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.Common.Yaml;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Provisions FluxCD CRDs, source-controller resources, and GitRepository configuration.
/// </summary>
public interface IFluxCDProvisioner
{
    /// <summary>
    /// Applies the FluxCD CustomResourceDefinitions.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task ApplyCrdManifestsAsync(IKubernetesClient client, OperatorConfig config);
    /// <summary>
    /// Applies FluxCD source-controller resources and configures the GitRepository/credentials.
    /// </summary>
    /// <param name="client">Kubernetes client used to apply resources.</param>
    /// <param name="config">Operator configuration settings.</param>
    Task ApplySourceControllerAsync(IKubernetesClient client, OperatorConfig config);
    /// <summary>
    /// Waits for the source-controller deployment to be ready.
    /// </summary>
    /// <param name="client">Kubernetes client used to read resources.</param>
    /// <param name="namespaceName">Namespace containing the source-controller deployment.</param>
    /// <param name="timeout">Maximum wait time before failing.</param>
    Task WaitForSourceControllerReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout);
}

/// <summary>
/// Applies FluxCD CRDs, source-controller manifests, and GitRepository resources.
/// </summary>
internal sealed class FluxCDProvisioner : IFluxCDProvisioner
{
    /// <summary>
    /// Secret name for FluxCD GitRepository credentials.
    /// </summary>
    private const string FluxcdGitRepositorySecretName = "cn-fluxcd-git-credentials";
    /// <summary>
    /// FluxCD source-controller resource name.
    /// </summary>
    internal const string FluxcdSourceControllerName = "cn-fluxcd-source-controller";
    /// <summary>
    /// FluxCD GitRepository resource name for data plane sources.
    /// </summary>
    internal const string FluxcdGitRepositoryName = "cn-data-plane";

    /// <summary>
    /// Common labels applied to FluxCD GitRepository resources.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> FluxcdGitRepositoryLabels =
        ProvisioningCommonTools.BootstrapLabels;
    private readonly ILogger<FluxCDProvisioner> logger;
    private readonly YamlToJsonConverter yamlToJsonConverter;
    private readonly string sourceControllerImage;
    private readonly string sourceControllerVersion;

    /// <summary>
    /// Creates a FluxCD provisioner.
    /// </summary>
    public FluxCDProvisioner(
        ILogger<FluxCDProvisioner> logger,
        YamlToJsonConverter yamlToJsonConverter,
        IFluxCDInfoProvider fluxcdInfoProvider)
    {
        this.logger = logger;
        this.yamlToJsonConverter = yamlToJsonConverter;
        var info = fluxcdInfoProvider.GetInfo();
        sourceControllerImage = info.SourceControllerImage;
        sourceControllerVersion = info.SourceControllerVersion;
    }

    /// <inheritdoc />
    public async Task ApplyCrdManifestsAsync(IKubernetesClient client, OperatorConfig config)
    {
        logger.LogInformation("Applying FluxCD CRDs...");
        var yaml = ProvisioningCommonTools.GetYamlDocument(
            FluxCDInfoProvider.FluxcdManifestsResourceRoot,
            FluxCDInfoProvider.FluxcdManifestsRelativePath,
            FluxCDInfoProvider.FluxcdSourceControllerManifestFileName);
        var documents = yamlToJsonConverter.ConvertAll(yaml).ToList();

        foreach (var jsonObj in documents)
        {
            var kind = jsonObj.GetRequiredString("kind");
            if (kind != "CustomResourceDefinition")
                continue;

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

    /// <inheritdoc />
    public async Task ApplySourceControllerAsync(IKubernetesClient client, OperatorConfig config)
    {
        logger.LogInformation("Applying FluxCD source-controller manifests...");
        var yaml = ProvisioningCommonTools.GetYamlDocument(
            FluxCDInfoProvider.FluxcdManifestsResourceRoot,
            FluxCDInfoProvider.FluxcdManifestsRelativePath,
            FluxCDInfoProvider.FluxcdSourceControllerManifestFileName);

        var documents = yamlToJsonConverter.ConvertAll(yaml).ToList();
        var versionOverride = config.FluxCD?.Images.SourceControllerVersion;
        var operatorVersion = string.IsNullOrWhiteSpace(versionOverride)
            ? sourceControllerVersion
            : versionOverride;
        var baseImage = GetImageWithoutTag(sourceControllerImage);
        var resolvedImage = ProvisioningCommonTools.ResolveImage(config, $"{baseImage}:{operatorVersion}");

        await ApplyRbacAsync(client, config, operatorVersion);

        foreach (var jsonObj in documents)
        {
            var kind = jsonObj.GetRequiredString("kind");
            if (kind == "CustomResourceDefinition")
                continue;

            if (kind == "Namespace")
                continue;

            var targetNamespace = config.Kubernetes.Namespaces.System.Name;
            var resourceName = FluxcdSourceControllerName;
            jsonObj.Set("metadata.name", resourceName);
            KubernetesManifestTools.NormalizeMetadataMaps(jsonObj);
            ProvisioningCommonTools.ApplyOperatorLabels(jsonObj, "metadata", resourceName, operatorVersion);

            switch (kind)
            {
                case "ServiceAccount":
                    jsonObj.Set("metadata.namespace", targetNamespace);
                    break;
                case "Service":
                    jsonObj.Set("metadata.namespace", targetNamespace);
                    jsonObj.Set("spec.selector.app", resourceName);
                    break;
                case "Deployment":
                    jsonObj.Set("metadata.namespace", targetNamespace);
                    jsonObj.Set("spec.selector.matchLabels.app", resourceName);
                    jsonObj.Set("spec.template.metadata.labels.app", resourceName);
                    ProvisioningCommonTools.ApplyOperatorLabels(
                        jsonObj,
                        "spec.template.metadata",
                        resourceName,
                        operatorVersion);
                    jsonObj.Set("spec.template.spec.serviceAccountName", resourceName);
                    jsonObj.Set("spec.template.spec.securityContext.runAsUser", config.Kubernetes.SecurityContextRunAsId);
                    jsonObj.Set("spec.template.spec.securityContext.runAsGroup", config.Kubernetes.SecurityContextRunAsId);
                    jsonObj.Set("spec.template.spec.securityContext.fsGroup", config.Kubernetes.SecurityContextRunAsId);
                    jsonObj.Set("spec.template.spec.imagePullSecrets[0].name", KubernetesConstants.SystemImagePullSecret);
                    jsonObj.Set("spec.template.spec.automountServiceAccountToken", false);
                    if (jsonObj["spec"]?["template"]?["spec"] is JsonObject podSpec)
                    {
                        ProvisioningCommonTools.ApplySystemNodePlacement(podSpec, config);
                        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
                        {
                            var container = EnsureFirstContainer(jsonObj);
                            var volumeMounts = JsonManifestEditor.EnsureArray(container, "volumeMounts");
                            ProvisioningCommonTools.EnsureVolumeMount(
                                volumeMounts,
                                "ca-certificates",
                                $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}",
                                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert,
                                true);

                            var volumes = JsonManifestEditor.EnsureArray(podSpec, "volumes");
                            ProvisioningCommonTools.EnsureSecretVolumeWithItem(
                                volumes,
                                "ca-certificates",
                                DataPlaneConstants.OperatorConfigSecretName,
                                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert);
                        }
                    }
                    ProvisioningCommonTools.ApplyServiceAccountAttachments(jsonObj);
                    UpdateContainerArg(
                        jsonObj,
                        "--storage-adv-addr=",
                        $"--storage-adv-addr={FluxcdSourceControllerName}.$(RUNTIME_NAMESPACE).svc.cluster.local.");
                    jsonObj.Set("spec.template.spec.containers[0].image", resolvedImage);
                    ApplyProxyEnv(jsonObj, config);
                    break;
                default:
                    logger.LogWarning("Unsupported source-controller manifest kind '{kind}', skipping.", kind);
                    continue;
            }

            var kubeObj = KubernetesManifestTools.DeserializeByKind(jsonObj.ToJsonString(), kind);
            await client.ApplyAsync(kubeObj);

            logger.LogInformation("Applied source-controller {kind} '{name}'.", kind, resourceName);
        }

        await ApplyGitRepositoryCredentialsSecretAsync(client, config);
        await ApplyGitRepositoryAsync(client, config);
    }

    /// <inheritdoc />
    public Task WaitForSourceControllerReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
    {
        return ProvisioningCommonTools.WaitForDeploymentReadyAsync(
            client,
            namespaceName,
            FluxcdSourceControllerName,
            timeout);
    }

    /// <summary>
    /// Replaces matching container args with the provided value.
    /// </summary>
    private static void UpdateContainerArg(JsonObject jsonObj, string prefix, string replacement)
    {
        if (jsonObj["spec"]?["template"]?["spec"]?["containers"] is not JsonArray containers)
            return;

        foreach (var container in containers.OfType<JsonObject>())
        {
            if (container["args"] is not JsonArray args)
                continue;

            for (var i = 0; i < args.Count; i++)
            {
                if (args[i] is not JsonValue argValue ||
                    !argValue.TryGetValue<string>(out var arg) ||
                    string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (arg.StartsWith(prefix, StringComparison.Ordinal))
                    args[i] = replacement;
            }
        }
    }

    /// <summary>
    /// Applies HTTP proxy environment variables to the first container when enabled.
    /// </summary>
    private static void ApplyProxyEnv(JsonObject jsonObj, OperatorConfig config)
    {
        var container = EnsureFirstContainer(jsonObj);
        ProvisioningCommonTools.EnsureProxyEnv(container, config);
    }

    /// <summary>
    /// Ensures the first container exists and returns it.
    /// </summary>
    private static JsonObject EnsureFirstContainer(JsonObject jsonObj)
    {
        var containers = JsonManifestEditor.EnsureArrayPath(jsonObj, "spec.template.spec.containers");
        while (containers.Count < 1)
            containers.Add((JsonNode)new JsonObject());

        if (containers[0] is not JsonObject container)
        {
            container = new JsonObject();
            containers[0] = container;
        }

        return container;
    }

    /// <summary>
    /// Strips tag/digest from an image reference.
    /// </summary>
    private static string GetImageWithoutTag(string image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return image;

        var withoutDigest = image.Split('@')[0];
        var lastColon = withoutDigest.LastIndexOf(':');
        var lastSlash = withoutDigest.LastIndexOf('/');
        if (lastColon > lastSlash)
            return withoutDigest[..lastColon];

        return withoutDigest;
    }

    /// <summary>
    /// Applies the FluxCD CRD controller ClusterRole and binding.
    /// </summary>
    private async Task ApplyRbacAsync(IKubernetesClient client, OperatorConfig config, string operatorVersion)
    {
        var yaml = ProvisioningCommonTools.GetYamlDocument(
            FluxCDInfoProvider.FluxcdManifestsResourceRoot,
            FluxCDInfoProvider.FluxcdManifestsRelativePath,
            "rbac.yaml");
        var documents = yamlToJsonConverter.ConvertAll(yaml).ToList();
        var resourceName = FluxcdSourceControllerName;

        foreach (var jsonObj in documents)
        {
            var kind = jsonObj.GetRequiredString("kind");
            if (kind != "ClusterRole")
                continue;

            if (!jsonObj.TryGetString("metadata.name", out var name) ||
                !string.Equals(name, "crd-controller", StringComparison.Ordinal))
            {
                continue;
            }

            jsonObj.Set("metadata.name", "cn-crd-controller");
            KubernetesManifestTools.NormalizeMetadataMaps(jsonObj);
            ProvisioningCommonTools.ApplyOperatorLabels(jsonObj, "metadata", resourceName, operatorVersion);
            var kubeObj = KubernetesManifestTools.DeserializeByKind(jsonObj.ToJsonString(), kind);
            await client.ApplyAsync(kubeObj);

            logger.LogInformation("Applied FluxCD {kind} '{name}'.", kind, "cn-crd-controller");
        }

        var binding = BuildCrdControllerBinding(config, resourceName, operatorVersion);
        var bindingObj = KubernetesManifestTools.DeserializeByKind(binding.ToJsonString(), "ClusterRoleBinding");
        await client.ApplyAsync(bindingObj);
        logger.LogInformation("Applied FluxCD ClusterRoleBinding '{name}'.", "cn-crd-controller");
    }

    /// <summary>
    /// Builds a ClusterRoleBinding for the FluxCD CRD controller role.
    /// </summary>
    private static JsonObject BuildCrdControllerBinding(
        OperatorConfig config,
        string resourceName,
        string operatorVersion)
    {
        var binding = new JsonObject
        {
            ["apiVersion"] = "rbac.authorization.k8s.io/v1",
            ["kind"] = "ClusterRoleBinding",
            ["metadata"] = new JsonObject
            {
                ["name"] = "cn-crd-controller"
            },
            ["roleRef"] = new JsonObject
            {
                ["apiGroup"] = "rbac.authorization.k8s.io",
                ["kind"] = "ClusterRole",
                ["name"] = "cn-crd-controller"
            }
        };

        binding.Set("subjects[0].kind", "ServiceAccount");
        binding.Set("subjects[0].name", FluxcdSourceControllerName);
        binding.Set("subjects[0].namespace", config.Kubernetes.Namespaces.System.Name);
        ProvisioningCommonTools.ApplyOperatorLabels(binding, "metadata", resourceName, operatorVersion);
        return binding;
    }

    /// <summary>
    /// Creates or updates the FluxCD GitRepository credentials Secret.
    /// </summary>
    private async Task ApplyGitRepositoryCredentialsSecretAsync(
        IKubernetesClient client,
        OperatorConfig config)
    {
        var targetNamespace = config.Kubernetes.Namespaces.System.Name;
        var scm = config.Scm;
        if (scm.AuthenticationMethod == ScmAuthenticationMethod.AccessToken &&
            string.IsNullOrWhiteSpace(scm.AccessToken))
        {
            logger.LogWarning(
                "SCM access token is empty; skipping FluxCD GitRepository credentials Secret.");
            return;
        }

        if (scm.AuthenticationMethod == ScmAuthenticationMethod.UsernamePassword &&
            string.IsNullOrWhiteSpace(scm.Username) &&
            string.IsNullOrWhiteSpace(scm.Password))
        {
            logger.LogWarning(
                "SCM username/password are empty; skipping FluxCD GitRepository credentials Secret.");
            return;
        }

        logger.LogInformation(
            "Applying FluxCD GitRepository credentials Secret '{name}' in namespace '{ns}'...",
            FluxcdGitRepositorySecretName,
            targetNamespace);

        var secret = new V1Secret
        {
            Metadata = new V1ObjectMeta
            {
                Name = FluxcdGitRepositorySecretName,
                NamespaceProperty = targetNamespace
            },
            Type = "Opaque",
            Data = new Dictionary<string, byte[]>()
        };

        KubernetesManifestTools.ApplyLabels(secret.Metadata, FluxcdGitRepositoryLabels);

        switch (scm.AuthenticationMethod)
        {
            case ScmAuthenticationMethod.AccessToken:
                secret.Data["bearerToken"] = Encoding.UTF8.GetBytes(scm.AccessToken ?? string.Empty);
                break;
            case ScmAuthenticationMethod.UsernamePassword:
                secret.Data["username"] = Encoding.UTF8.GetBytes(scm.Username ?? string.Empty);
                secret.Data["password"] = Encoding.UTF8.GetBytes(scm.Password ?? string.Empty);
                break;
            default:
                logger.LogWarning(
                    "Unsupported SCM authentication method '{method}'; skipping FluxCD GitRepository credentials Secret.",
                    scm.AuthenticationMethod);
                return;
        }

        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
            secret.Data["ca.crt"] = Convert.FromBase64String(config.Security.CustomCaBase64);

        try
        {
            var existing = await client.CoreV1.ReadNamespacedSecretAsync(
                FluxcdGitRepositorySecretName,
                targetNamespace);
            secret.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;

            await client.CoreV1.ReplaceNamespacedSecretAsync(
                secret,
                FluxcdGitRepositorySecretName,
                targetNamespace);
            logger.LogInformation(
                "Secret '{name}' replaced in namespace '{ns}'.",
                FluxcdGitRepositorySecretName,
                targetNamespace);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.CoreV1.CreateNamespacedSecretAsync(secret, targetNamespace);
            logger.LogInformation(
                "Secret '{name}' created in namespace '{ns}'.",
                FluxcdGitRepositorySecretName,
                targetNamespace);
        }
    }

    /// <summary>
    /// Creates or updates the FluxCD GitRepository custom resource.
    /// </summary>
    private async Task ApplyGitRepositoryAsync(
        IKubernetesClient client,
        OperatorConfig config)
    {
        var targetNamespace = config.Kubernetes.Namespaces.System.Name;
        var gitRepository = new JsonObject
        {
            ["apiVersion"] = "source.toolkit.fluxcd.io/v1",
            ["kind"] = "GitRepository",
            ["metadata"] = new JsonObject
            {
                ["name"] = FluxcdGitRepositoryName,
                ["namespace"] = targetNamespace
            },
            ["spec"] = new JsonObject
            {
                ["interval"] = $"{DataPlaneConstants.ScmGitRepositorySyncIntervalSeconds}s",
                ["url"] = config.Scm.Url,
                ["ref"] = new JsonObject
                {
                    ["branch"] = DataPlaneConstants.ScmGitRepositoryDefaultBranch
                },
            }
        };

        KubernetesManifestTools.ApplyLabels(gitRepository, "metadata", FluxcdGitRepositoryLabels);
        gitRepository.Set("spec.secretRef.name", FluxcdGitRepositorySecretName);
        gitRepository.Set("spec.sparseCheckout[0]", config.Environment.Name);
        var patch = new V1Patch(gitRepository.ToJsonString(), V1Patch.PatchType.ApplyPatch);
        await client.CustomObjects.PatchNamespacedCustomObjectAsync(
            patch,
            group: "source.toolkit.fluxcd.io",
            version: "v1",
            namespaceParameter: targetNamespace,
            plural: "gitrepositories",
            name: FluxcdGitRepositoryName,
            fieldManager: KubernetesConstants.FieldManager,
            force: true);

        logger.LogInformation("Applied FluxCD GitRepository '{name}'.", FluxcdGitRepositoryName);
    }

}
