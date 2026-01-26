using System.Diagnostics;
using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.DataPlane.Models;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Shared helpers for provisioning manifest handling and common resource mutations.
/// </summary>
internal static class ProvisioningCommonTools
{
    /// <summary>
    /// Bootstrap label set applied to data plane resources.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BootstrapLabels =
        new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = DataPlaneConstants.BootstrapAppLabelValue,
            ["app.kubernetes.io/managed-by"] = KubernetesConstants.LabelValues.ManagedBy,
            ["app.kubernetes.io/part-of"] = DataPlaneConstants.PartOfDataPlaneLabelValue
        };

    /// <summary>
    /// Adds HTTP proxy environment variables to a container when proxy is enabled.
    /// </summary>
    public static void EnsureProxyEnv(JsonObject container, OperatorConfig config)
    {
        if (!config.HttpProxy.Enabled)
            return;
        if (string.IsNullOrWhiteSpace(config.HttpProxy.Hostname) || !config.HttpProxy.Port.HasValue)
            return;

        var proxyValue = $"{config.HttpProxy.Hostname}:{config.HttpProxy.Port.Value}";
        var env = JsonManifestEditor.EnsureArray(container, "env");
        JsonManifestEditor.EnsureEnvVar(env, "HTTP_PROXY", proxyValue);
        JsonManifestEditor.EnsureEnvVar(env, "HTTPS_PROXY", proxyValue);
        if (!string.IsNullOrWhiteSpace(config.HttpProxy.NoProxy))
            JsonManifestEditor.EnsureEnvVar(env, "NO_PROXY", config.HttpProxy.NoProxy);
    }


    /// <summary>
    /// Reads embedded resource text or returns null when not present.
    /// </summary>
    public static string? ReadEmbeddedResourceText(string resourceName)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Returns YAML document payloads from embedded resources or the local filesystem.
    /// </summary>
    public static IEnumerable<(string Name, string Yaml)> GetYamlDocuments(
        string resourceRoot,
        string relativeRoot,
        string subdir)
    {
        var prefix = resourceRoot + subdir.Trim('/').Replace('\\', '/') + "/";
        var resources = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) &&
                           name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (resources.Count > 0)
        {
            foreach (var resource in resources)
            {
                var content = ReadEmbeddedResourceText(resource);
                if (content is not null)
                    yield return (resource, content);
            }
            yield break;
        }

        var folder = Path.Combine(AppContext.BaseDirectory, relativeRoot, subdir);
        if (!Directory.Exists(folder))
            yield break;

        foreach (var file in Directory.EnumerateFiles(folder, "*.yaml", SearchOption.AllDirectories))
            yield return (file, File.ReadAllText(file));
    }

    /// <summary>
    /// Returns a YAML document payload from embedded resources or the local filesystem.
    /// </summary>
    public static string GetYamlDocument(string resourceRoot, string relativeRoot, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var resourceName = resourceRoot + normalized;
        var embedded = ReadEmbeddedResourceText(resourceName);
        if (embedded is not null)
            return embedded;

        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            relativeRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                $"Manifest file not found at '{filePath}' and embedded resource '{resourceName}' is missing.");

        return File.ReadAllText(filePath);
    }

    /// <summary>
    /// Applies standard operator labels to a manifest JSON object.
    /// </summary>
    public static void ApplyOperatorLabels(JsonObject jsonObj, string metadataPath, string operatorName, string operatorVersion)
    {
        var operatorLabels = new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = operatorName,
            ["app.kubernetes.io/instance"] = operatorName,
            ["app.kubernetes.io/managed-by"] = KubernetesConstants.LabelValues.ManagedBy,
            ["app.kubernetes.io/part-of"] = DataPlaneConstants.PartOfDataPlaneLabelValue,
            ["app.kubernetes.io/version"] = operatorVersion
        };

        KubernetesManifestTools.ApplyLabels(jsonObj, metadataPath, operatorLabels);
    }

    /// <summary>
    /// Builds a projected service account volume with token, CA certificate, and namespace projection.
    /// </summary>
    public static JsonObject BuildServiceAccountVolume()
    {
        // defaultMode 292 is octal 0444 (read-only)
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

    /// <summary>
    /// Builds a read-only volume mount for the projected service account volume.
    /// </summary>
    public static JsonObject BuildServiceAccountVolumeMount()
    {
        var mount = new JsonObject();
        mount.Set("mountPath", "/var/run/secrets/kubernetes.io/serviceaccount");
        mount.Set("name", "serviceaccount-token");
        mount.Set("readOnly", true);
        return mount;
    }

    /// <summary>
    /// Adds the projected service account volume and its mount to the first container.
    /// </summary>
    public static void ApplyServiceAccountAttachments(JsonObject jsonObj)
    {
        var serviceAccountVolume = BuildServiceAccountVolume();
        var serviceAccountVolumeMount = BuildServiceAccountVolumeMount();

        var podSpec = JsonManifestEditor.EnsureObjectPath(jsonObj, "spec.template.spec");
        if (podSpec["containers"] is not JsonArray containers)
        {
            containers = new JsonArray();
            podSpec["containers"] = containers;
        }

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
        if (!JsonManifestEditor.HasNamedObject(volumeMounts, "serviceaccount-token"))
            volumeMounts.Add((JsonNode)serviceAccountVolumeMount);

        var volumes = JsonManifestEditor.EnsureArray(podSpec, "volumes");
        if (!JsonManifestEditor.HasNamedObject(volumes, "serviceaccount-token"))
            volumes.Add((JsonNode)serviceAccountVolume);
    }

    /// <summary>
    /// Applies tolerations and node affinity for system node placement when configured.
    /// </summary>
    public static void ApplySystemNodePlacement(JsonObject podSpec, OperatorConfig config)
    {
        if (config.Kubernetes.PodPlacementMode != PodPlacementMode.NodeSelectorAndTaints)
            return;

        var tolerations = new JsonArray();
        tolerations.Add((JsonNode)new JsonObject
        {
            ["effect"] = "NoExecute",
            ["key"] = config.Kubernetes.NodeLabels.System.Key,
            ["operator"] = "Equal",
            ["value"] = config.Kubernetes.NodeLabels.System.Value
        });
        podSpec["tolerations"] = tolerations;

        var values = new JsonArray();
        values.Add((JsonNode)JsonValue.Create(config.Kubernetes.NodeLabels.System.Value));

        var matchExpressions = new JsonArray();
        matchExpressions.Add((JsonNode)new JsonObject
        {
            ["key"] = config.Kubernetes.NodeLabels.System.Key,
            ["operator"] = "In",
            ["values"] = values
        });

        var nodeSelectorTerms = new JsonArray();
        nodeSelectorTerms.Add((JsonNode)new JsonObject
        {
            ["matchExpressions"] = matchExpressions
        });

        podSpec["affinity"] = new JsonObject
        {
            ["nodeAffinity"] = new JsonObject
            {
                ["requiredDuringSchedulingIgnoredDuringExecution"] = new JsonObject
                {
                    ["nodeSelectorTerms"] = nodeSelectorTerms
                }
            }
        };
    }

    /// <summary>
    /// Resolves an image reference against the configured container registry.
    /// </summary>
    public static string ResolveImage(OperatorConfig config, string image)
    {
        if (string.IsNullOrWhiteSpace(config.ContainerRegistry.Hostname))
            return image;

        return $"{config.ContainerRegistry.Hostname.TrimEnd('/')}/{image.TrimStart('/')}";
    }

    /// <summary>
    /// Extracts a tag from a container image reference if present.
    /// </summary>
    public static string? TryGetImageTag(string image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;

        var atIndex = image.LastIndexOf('@');
        if (atIndex >= 0)
            image = image[..atIndex];

        var lastColon = image.LastIndexOf(':');
        var lastSlash = image.LastIndexOf('/');
        if (lastColon > lastSlash && lastColon + 1 < image.Length)
            return image[(lastColon + 1)..];

        return null;
    }

    /// <summary>
    /// Waits for a deployment to reach its desired replica count.
    /// </summary>
    public static async Task WaitForDeploymentReadyAsync(
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
