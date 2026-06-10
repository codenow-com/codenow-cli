using System.Text;
using System.Text.Json;
using k8s;
using k8s.Models;

using CodeNOW.Cli.Adapters.Kubernetes;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Serializes Kubernetes resource objects to multi-document YAML output.
/// </summary>
public static class BootstrapManifestPrinter
{
    /// <summary>
    /// Serializes a list of Kubernetes objects to a multi-document YAML string separated by "---".
    /// </summary>
    public static string ToYaml(IReadOnlyList<IKubernetesObject<V1ObjectMeta>> resources)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < resources.Count; i++)
        {
            if (i > 0)
                sb.AppendLine("---");

            var json = KubernetesJson.Serialize(resources[i]);
            var yaml = JsonToYaml(json);
            sb.Append(yaml);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds a ClusterRole that grants the minimum permissions needed to deploy the given resources.
    /// Rules are derived dynamically from recorded operations and applied objects.
    /// </summary>
    public static V1ClusterRole BuildDeployerClusterRole(
        string name,
        IReadOnlyList<RecordedOperation> operations,
        IReadOnlyList<IKubernetesObject<V1ObjectMeta>> appliedObjects)
    {
        // Group operations by (apiGroup, resource) → verbs + resourceNames
        var grouped = new Dictionary<(string ApiGroup, string Resource), (HashSet<string> Verbs, HashSet<string> ResourceNames)>();

        foreach (var op in operations)
        {
            var key = (op.ApiGroup, op.Resource);
            if (!grouped.TryGetValue(key, out var entry))
            {
                entry = (new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
                grouped[key] = entry;
            }

            entry.Verbs.Add(op.Verb);
            if (!string.IsNullOrWhiteSpace(op.ResourceName))
                entry.ResourceNames.Add(op.ResourceName);
        }

        // Collect escalation rules from ClusterRoles being applied
        var escalationRules = new List<V1PolicyRule>();
        foreach (var obj in appliedObjects)
        {
            if (obj is V1ClusterRole clusterRole && clusterRole.Rules is not null)
                escalationRules.AddRange(clusterRole.Rules);
        }

        var rules = new List<V1PolicyRule>();

        foreach (var ((apiGroup, resource), (verbs, resourceNames)) in grouped
                     .OrderBy(r => r.Key.ApiGroup, StringComparer.Ordinal)
                     .ThenBy(r => r.Key.Resource, StringComparer.Ordinal))
        {
            var sortedVerbs = verbs.OrderBy(v => v, StringComparer.Ordinal).ToList();
            var sortedNames = resourceNames.OrderBy(n => n, StringComparer.Ordinal).ToList();

            if (apiGroup == "rbac.authorization.k8s.io")
            {
                // RBAC needs list/watch (unscoped) + scoped verbs with escalate/bind/delete
                continue; // handled below
            }

            if (resource == "secrets")
            {
                // Secrets: scoped get/patch/update + unscoped create
                // Add well-known managed secrets
                if (!resourceNames.Contains("cn-system-secrets"))
                    sortedNames.Add("cn-system-secrets");
                if (!resourceNames.Contains("cn-tenant-secrets"))
                    sortedNames.Add("cn-tenant-secrets");
                sortedNames.Sort(StringComparer.Ordinal);

                rules.Add(new V1PolicyRule
                {
                    ApiGroups = [apiGroup],
                    Resources = [resource],
                    ResourceNames = sortedNames,
                    Verbs = ["get", "patch", "update"]
                });
                rules.Add(new V1PolicyRule
                {
                    ApiGroups = [apiGroup],
                    Resources = [resource],
                    Verbs = ["create"]
                });
            }
            else
            {
                var rule = new V1PolicyRule
                {
                    ApiGroups = [apiGroup],
                    Resources = [resource],
                    Verbs = sortedVerbs
                };
                if (sortedNames.Count > 0)
                    rule.ResourceNames = sortedNames;
                rules.Add(rule);
            }
        }

        // Pods (not created by bootstrap but needed for monitoring/exec)
        rules.Add(new V1PolicyRule
        {
            ApiGroups = [""],
            Resources = ["pods", "pods/log"],
            Verbs = ["get", "list", "delete", "watch"]
        });
        rules.Add(new V1PolicyRule
        {
            ApiGroups = [""],
            Resources = ["pods/exec"],
            ResourceNames = [DataPlaneConstants.WorkspaceName + "-0"],
            Verbs = ["*"]
        });

        // RBAC rules
        var rbacEntries = grouped
            .Where(r => r.Key.ApiGroup == "rbac.authorization.k8s.io")
            .ToList();
        if (rbacEntries.Count > 0)
        {
            var allRbacNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, (_, names)) in rbacEntries)
                foreach (var n in names)
                    allRbacNames.Add(n);

            rules.Add(new V1PolicyRule
            {
                ApiGroups = ["rbac.authorization.k8s.io"],
                Resources = ["clusterroles", "clusterrolebindings", "roles", "rolebindings"],
                Verbs = ["list", "watch"]
            });
            rules.Add(new V1PolicyRule
            {
                ApiGroups = ["rbac.authorization.k8s.io"],
                Resources = ["clusterroles", "clusterrolebindings", "roles", "rolebindings"],
                ResourceNames = allRbacNames.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                Verbs = ["get", "create", "patch", "update", "delete", "escalate", "bind"]
            });
        }

        // Add escalation rules from ClusterRoles being bound
        rules.AddRange(escalationRules);

        return new V1ClusterRole
        {
            ApiVersion = "rbac.authorization.k8s.io/v1",
            Kind = "ClusterRole",
            Metadata = new V1ObjectMeta { Name = name },
            Rules = rules
        };
    }

    private static string JsonToYaml(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        WriteElement(sb, doc.RootElement, 0, false);
        return sb.ToString();
    }

    private static void WriteElement(StringBuilder sb, JsonElement element, int indent, bool isArrayItem)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var first = true;
                foreach (var prop in element.EnumerateObject())
                {
                    if (isArrayItem && first)
                    {
                        sb.Append($"{prop.Name}: ");
                        first = false;
                        WriteInlineOrBlock(sb, prop.Value, indent + 2);
                    }
                    else
                    {
                        sb.Append(new string(' ', indent));
                        sb.Append($"{prop.Name}: ");
                        first = false;
                        WriteInlineOrBlock(sb, prop.Value, indent + 2);
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    sb.Append(new string(' ', indent));
                    sb.Append("- ");
                    WriteElement(sb, item, indent + 2, true);
                }
                break;

            case JsonValueKind.String:
                var str = element.GetString() ?? "";
                if (str.Contains(':') || str.Contains('#') || str.Contains('{') || str.Contains('}') ||
                    str.Contains('[') || str.Contains(']') || str.Contains(',') || str.Contains('&') ||
                    str.Contains('*') || str.Contains('?') || str.Contains('|') || str.Contains('-') ||
                    str.Contains('<') || str.Contains('>') || str.Contains('=') || str.Contains('!') ||
                    str.Contains('%') || str.Contains('@') || str.Contains('`') || str.Length == 0 ||
                    str == "true" || str == "false" || str == "null" ||
                    double.TryParse(str, out _))
                    sb.AppendLine($"\"{EscapeYamlString(str)}\"");
                else
                    sb.AppendLine(str);
                break;

            case JsonValueKind.Number:
                sb.AppendLine(element.GetRawText());
                break;

            case JsonValueKind.True:
                sb.AppendLine("true");
                break;

            case JsonValueKind.False:
                sb.AppendLine("false");
                break;

            case JsonValueKind.Null:
                sb.AppendLine("null");
                break;
        }
    }

    private static void WriteInlineOrBlock(StringBuilder sb, JsonElement value, int indent)
    {
        if (value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine();
            WriteElement(sb, value, indent, false);
        }
        else
        {
            WriteElement(sb, value, indent, false);
        }
    }

    private static string EscapeYamlString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
