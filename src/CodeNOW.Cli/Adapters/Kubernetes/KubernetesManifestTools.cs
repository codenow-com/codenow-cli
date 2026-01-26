using System.Text.Json;
using System.Text.Json.Nodes;
using CodeNOW.Cli.Common.Json;
using k8s;
using k8s.Models;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Utilities for Kubernetes manifest JSON handling and common metadata mutations.
/// </summary>
public static class KubernetesManifestTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Builds a JSON patch for creating or updating a namespace.
    /// </summary>
    /// <param name="name">Namespace name.</param>
    /// <param name="namespaceTypeLabelValue">Label value for <see cref="KubernetesConstants.Labels.NamespaceType"/>.</param>
    /// <param name="partOfLabelValue">Label value for <c>app.kubernetes.io/part-of</c>.</param>
    /// <param name="annotations">Optional annotations to include.</param>
    public static string BuildNamespacePatch(
        string name,
        string namespaceTypeLabelValue,
        string partOfLabelValue,
        IDictionary<string, string>? annotations = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Namespace name cannot be empty.", nameof(name));

        var metadata = new JsonObject
        {
            ["name"] = name,
            ["labels"] = new JsonObject
            {
                [KubernetesConstants.Labels.NamespaceType] = namespaceTypeLabelValue,
                ["app.kubernetes.io/managed-by"] = KubernetesConstants.LabelValues.ManagedBy,
                ["app.kubernetes.io/part-of"] = partOfLabelValue
            }
        };

        if (annotations is { Count: > 0 })
        {
            var annotObj = new JsonObject();
            foreach (var kv in annotations)
                annotObj[kv.Key] = kv.Value;

            metadata["annotations"] = annotObj;
        }

        var root = new JsonObject
        {
            ["apiVersion"] = "v1",
            ["kind"] = "Namespace",
            ["metadata"] = metadata
        };

        return root.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Ensures labels and annotations are serialized as string maps.
    /// </summary>
    public static void NormalizeMetadataMaps(JsonObject root)
    {
        if (root is null)
            throw new ArgumentNullException(nameof(root));

        if (root["metadata"] is not JsonObject metadata)
            return;

        NormalizeStringMap(metadata, "labels");
        NormalizeStringMap(metadata, "annotations");
    }

    /// <summary>
    /// Deserializes a manifest JSON payload to the Kubernetes model matching its kind.
    /// </summary>
    public static IKubernetesObject<V1ObjectMeta> DeserializeByKind(string json, string kind)
    {
        if (json is null)
            throw new ArgumentNullException(nameof(json));
        if (kind is null)
            throw new ArgumentNullException(nameof(kind));

        return kind switch
        {
            // Workloads / services
            "Deployment" => KubernetesJson.Deserialize<V1Deployment>(json),
            "Service" => KubernetesJson.Deserialize<V1Service>(json),

            // RBAC
            "ServiceAccount" => KubernetesJson.Deserialize<V1ServiceAccount>(json),
            "Role" => KubernetesJson.Deserialize<V1Role>(json),
            "RoleBinding" => KubernetesJson.Deserialize<V1RoleBinding>(json),
            "ClusterRole" => KubernetesJson.Deserialize<V1ClusterRole>(json),
            "ClusterRoleBinding" => KubernetesJson.Deserialize<V1ClusterRoleBinding>(json),

            // CRDs
            "CustomResourceDefinition" => KubernetesJson.Deserialize<V1CustomResourceDefinition>(json),

            _ => throw new NotSupportedException($"Unsupported Kubernetes kind: {kind}")
        };
    }

    /// <summary>
    /// Applies labels to Kubernetes object metadata.
    /// </summary>
    public static void ApplyLabels(V1ObjectMeta? metadata, IReadOnlyDictionary<string, string> labels)
    {
        if (metadata is null)
            return;

        metadata.Labels ??= new Dictionary<string, string>();
        foreach (var pair in labels)
            metadata.Labels[pair.Key] = pair.Value;
    }

    /// <summary>
    /// Applies labels to a manifest JSON object at the given metadata path.
    /// </summary>
    public static void ApplyLabels(JsonObject root, string metadataPath, IReadOnlyDictionary<string, string> labels)
    {
        var metadata = JsonManifestEditor.EnsureObjectPath(root, metadataPath);
        var target = JsonManifestEditor.EnsureObjectPath(metadata, "labels");

        foreach (var pair in labels)
            target[pair.Key] = pair.Value;
    }

    private static void NormalizeStringMap(JsonObject metadata, string key)
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        if (metadata[key] is not JsonObject map)
            return;

        var updates = new Dictionary<string, JsonNode?>();
        foreach (var kv in map)
        {
            updates[kv.Key] = CoerceToStringNode(kv.Value);
        }

        foreach (var kv in updates)
        {
            map[kv.Key] = kv.Value;
        }
    }

    private static JsonNode? CoerceToStringNode(JsonNode? node)
    {
        if (node == null)
            return null;

        if (node is JsonValue val)
        {
            if (val.TryGetValue<string>(out var s))
                return JsonValue.Create(s);
            if (val.TryGetValue<bool>(out var b))
                return JsonValue.Create(b ? "true" : "false");
            if (val.TryGetValue<long>(out var l))
                return JsonValue.Create(l.ToString());
            if (val.TryGetValue<double>(out var d))
                return JsonValue.Create(d.ToString());
        }

        return JsonValue.Create(node.ToJsonString());
    }
}
