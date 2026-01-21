using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.Common.Yaml;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
    private readonly ILogger<PulumiOperatorProvisioner> logger;
    private readonly YamlToJsonConverter yamlToJsonConverter;
    private readonly string operatorImage;
    private readonly string operatorVersion;

    /// <summary>
    /// Creates a Pulumi operator provisioner.
    /// </summary>
    public PulumiOperatorProvisioner(
        ILogger<PulumiOperatorProvisioner> logger,
        YamlToJsonConverter yamlToJsonConverter,
        IOperatorInfoProvider operatorInfoProvider)
    {
        this.logger = logger;
        this.yamlToJsonConverter = yamlToJsonConverter;
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
        var deploymentName = EnsurePrefixed(DataPlaneConstants.OperatorDeploymentBaseName);
        await WaitForDeploymentReadyAsync(client, namespaceName, deploymentName, timeout);
    }

    /// <inheritdoc />
    public string GetOperatorImage(OperatorConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ContainerRegistry.Hostname))
            return operatorImage;

        return $"{config.ContainerRegistry.Hostname.TrimEnd('/')}/{operatorImage.TrimStart('/')}";
    }

    private IEnumerable<string> GetOperatorPath(params string[] parts)
    {
        var all = new List<string> { AppContext.BaseDirectory, "DataPlane", "Manifests", "Operator" };
        all.AddRange(parts);
        var root = Path.Combine(all.ToArray());

        if (File.Exists(root))
            return [root];

        return Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories);
    }

    private async Task ApplyCrdManifestsInternalAsync(IKubernetesClient client)
    {
        logger.LogInformation("Applying CRDs...");

        var files = GetOperatorPath("crd");

        foreach (var file in files)
        {
            logger.LogInformation("Applying CRD file: {file}", file);

            var yaml = File.ReadAllText(file);

            foreach (var jsonObj in yamlToJsonConverter.ConvertAll(yaml))
            {
                var name = jsonObj.GetRequiredString("metadata.name");
                var patch = new V1Patch(jsonObj.ToJsonString(), V1Patch.PatchType.ApplyPatch);

                await client.ApiextensionsV1.PatchCustomResourceDefinitionAsync(
                    patch,
                    name,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);

                logger.LogInformation("Applied CRD {name}.", name);
            }
        }
    }

    private async Task ApplyRbacManifestsInternalAsync(IKubernetesClient client, string targetNamespace)
    {
        logger.LogInformation("Applying RBAC to namespace {ns}...", targetNamespace);

        var files = GetOperatorPath("rbac");

        foreach (var file in files)
        {
            logger.LogInformation("Applying RBAC file: {file}", file);
            var yaml = File.ReadAllText(file);

            foreach (var jsonObj in yamlToJsonConverter.ConvertAll(yaml))
            {
                var kind = jsonObj.GetRequiredString("kind");
                var name = EnsurePrefixed(jsonObj.GetRequiredString("metadata.name"));
                jsonObj.Set("metadata.name", name);

                KubernetesManifestTools.NormalizeMetadataMaps(jsonObj);
                SetOperatorLabels(jsonObj);

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

                logger.LogInformation("Applied RBAC {kind} '{name}'.", kind, name);
            }
        }
    }

    private async Task ApplyOperatorDeploymentInternalAsync(IKubernetesClient client, OperatorConfig config)
    {
        logger.LogInformation("Applying operator deployment to namespace {ns}...", config.Kubernetes.Namespaces.System.Name);

        var file = GetOperatorPath("manager", "manager.yaml").First();

        var yaml = File.ReadAllText(file);

        foreach (var jsonObj in yamlToJsonConverter.ConvertAll(yaml))
        {
            var kind = jsonObj.GetRequiredString("kind");
            var name = EnsurePrefixed(jsonObj.GetRequiredString("metadata.name"));
            jsonObj.Set("metadata.name", name);
            var targetNamespace = config.Kubernetes.Namespaces.System.Name;

            if (kind == "Namespace")
                continue;

            KubernetesManifestTools.NormalizeMetadataMaps(jsonObj);
            SetOperatorLabels(jsonObj);

            if (kind is "Deployment")
            {
                SetOperatorLabels(jsonObj, "spec.template.metadata");
                ApplyDeploymentNamespaces(jsonObj, targetNamespace);
                ApplyDeploymentSecurityContext(jsonObj, config.Kubernetes.SecurityContextRunAsId);
                ApplyServiceAccountAttachments(jsonObj);
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

    private static void ApplyDeploymentNamespaces(JsonObject jsonObj, string targetNamespace)
    {
        jsonObj.Set("metadata.namespace", targetNamespace);
        jsonObj.Set("spec.template.metadata.namespace", targetNamespace);
        jsonObj.Set("spec.template.spec.imagePullSecrets[0].name", KubernetesConstants.SystemImagePullSecret);
        jsonObj.Set("spec.template.spec.automountServiceAccountToken", false);
        if (jsonObj.TryGetString("spec.template.spec.serviceAccountName", out var serviceAccountName))
        {
            jsonObj.Set("spec.template.spec.serviceAccountName", EnsurePrefixed(serviceAccountName));
        }
    }

    private static void ApplyDeploymentSecurityContext(JsonObject jsonObj, int runAsId)
    {
        jsonObj.Set("spec.template.spec.securityContext.runAsUser", runAsId);
        jsonObj.Set("spec.template.spec.securityContext.runAsGroup", runAsId);
        jsonObj.Set("spec.template.spec.containers[0].securityContext.readOnlyRootFilesystem", true);
        jsonObj.Set("spec.template.spec.containers[0].securityContext.seccompProfile.type", "RuntimeDefault");
    }

    private static void ApplyServiceAccountAttachments(JsonObject jsonObj)
    {
        var serviceAccountVolume = BuildServiceAccountVolume();
        var serviceAccountVolumeMount = BuildServiceAccountVolumeMount();

        jsonObj.Set("spec.template.spec.containers[0].volumeMounts[0]", serviceAccountVolumeMount);
        jsonObj.Set("spec.template.spec.volumes[0]", serviceAccountVolume);
    }

    private static JsonObject BuildServiceAccountVolume()
    {
        return JsonNode.Parse("""
        {
          "name": "serviceaccount-token",
          "projected": {
            "defaultMode": 292,
            "sources": [
              {
                "serviceAccountToken": {
                  "expirationSeconds": 3607,
                  "path": "token"
                }
              },
              {
                "configMap": {
                  "items": [
                    {
                      "key": "ca.crt",
                      "path": "ca.crt"
                    }
                  ],
                  "name": "kube-root-ca.crt"
                }
              },
              {
                "downwardAPI": {
                  "items": [
                    {
                      "fieldRef": {
                        "apiVersion": "v1",
                        "fieldPath": "metadata.namespace"
                      },
                      "path": "namespace"
                    }
                  ]
                }
              }
            ]
          }
        }
        """)?.AsObject() ?? throw new InvalidOperationException("Failed to build service account volume JSON.");
    }

    private static JsonObject BuildServiceAccountVolumeMount()
    {
        var mount = new JsonObject();
        mount.Set("mountPath", "/var/run/secrets/kubernetes.io/serviceaccount");
        mount.Set("name", "serviceaccount-token");
        mount.Set("readOnly", true);
        return mount;
    }

    private void SetOperatorLabels(JsonObject jsonObj)
    {
        SetOperatorLabels(jsonObj, "metadata");
    }

    private void SetOperatorLabels(JsonObject jsonObj, string metadataPath)
    {
        var operatorLabels = new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = KubernetesConstants.LabelValues.OperatorName,
            ["app.kubernetes.io/instance"] = KubernetesConstants.LabelValues.OperatorName,
            ["app.kubernetes.io/managed-by"] = KubernetesConstants.LabelValues.ManagedBy,
            ["app.kubernetes.io/part-of"] = KubernetesConstants.LabelValues.PartOf,
            ["app.kubernetes.io/version"] = operatorVersion
        };

        KubernetesManifestTools.ApplyLabels(jsonObj, metadataPath, operatorLabels);
    }

    private static string EnsurePrefixed(string name)
    {
        const string prefix = DataPlaneConstants.PulumiOperatorNamePrefix;
        if (name.StartsWith(prefix, StringComparison.Ordinal))
            return name;

        const string pulumiPrefix = DataPlaneConstants.PulumiWorkspaceNamePrefix;
        if (name.StartsWith(pulumiPrefix, StringComparison.Ordinal))
            name = name[pulumiPrefix.Length..];

        return prefix + name;
    }

    private static async Task WaitForDeploymentReadyAsync(
        IKubernetesClient client,
        string namespaceName,
        string deploymentName,
        TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, namespaceName);
            var desired = deployment.Spec?.Replicas ?? 1;
            var ready = deployment.Status?.ReadyReplicas ?? 0;

            if (ready >= desired && desired > 0)
                return;

            if (sw.Elapsed >= timeout)
                throw new TimeoutException($"Deployment '{deploymentName}' in namespace '{namespaceName}' not ready after {timeout}.");

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
