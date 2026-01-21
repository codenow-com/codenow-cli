using System.Text.Json;
using System.Text.Json.Nodes;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.Adapters.Kubernetes;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Reads stack status and workspace logs.
/// </summary>
internal sealed class StackStatusReader
{
    private readonly IKubernetesClient client;
    private readonly KubernetesReadExecutor executor;
    private readonly PodLogReader podLogReader;
    private readonly PodStatusReader podStatusReader;

    /// <summary>
    /// Creates a reader for stack status and workspace logs.
    /// </summary>
    public StackStatusReader(IKubernetesClient client, KubernetesReadExecutor executor)
    {
        this.client = client;
        this.executor = executor;
        podLogReader = new PodLogReader(client, executor);
        podStatusReader = new PodStatusReader(client, executor);
    }

    /// <summary>
    /// Returns the stack custom resource status and workspace pod status.
    /// </summary>
    /// <param name="query">Namespace and name of the stack resource.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Aggregated stack status.</returns>
    public async Task<StackStatus> GetStackStatusAsync(ManagementQuery query, CancellationToken cancellationToken)
    {
        var statusResult = await podStatusReader.GetPodStatusAsync(
            query.Namespace,
            DataPlaneConstants.WorkspacePodLabelSelector,
            cancellationToken);
        var workspaceStatus = statusResult.StatusText;

        return await executor.ExecuteAsync(async () =>
        {
            var ready = "Unknown";
            var reconcilingReason = "Unknown";
            var dryRun = "Disabled";

            var stackObj = await client.CustomObjects.GetNamespacedCustomObjectAsync(
                group: "pulumi.com",
                version: "v1",
                namespaceParameter: query.Namespace,
                plural: "stacks",
                name: string.IsNullOrWhiteSpace(query.Name) ? DataPlaneConstants.StackName : query.Name,
                cancellationToken: cancellationToken);

            var element = ExtractJsonElement(stackObj);
            if (element is not null)
            {
                if (TryGetConditionStatus(element.Value, "Ready", out var readyStatus))
                    ready = readyStatus;
                if (TryGetConditionReason(element.Value, "Reconciling", out var reason))
                    reconcilingReason = reason;
                if (TryGetStackPreview(element.Value, out var preview))
                    dryRun = preview ? "Enabled" : "Disabled";
            }

            return new StackStatus(workspaceStatus, ready, reconcilingReason, dryRun);
        }, ex =>
        {
            if (ex is k8s.Autorest.HttpOperationException httpEx &&
                httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new StackStatus(workspaceStatus, "Unknown", "Unknown", "Disabled");
            }

            return new StackStatus(workspaceStatus, "Unknown", "Unknown", "Disabled");
        }, "Failed to load stack status.");
    }

    /// <summary>
    /// Reads workspace pod logs for the stack.
    /// </summary>
    /// <param name="query">Namespace and tail lines for the log request.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Log read result.</returns>
    public async Task<LogReadResult> GetWorkspaceLogsAsync(
        ManagementLogQuery query,
        CancellationToken cancellationToken)
    {
        return await podLogReader.ReadLogsAsync(
            query,
            DataPlaneConstants.WorkspacePodLabelSelector,
            "Failed to read workspace logs.",
            cancellationToken);
    }

    private static bool TryGetConditionStatus(JsonElement root, string conditionType, out string status)
    {
        status = "Unknown";
        if (!root.TryGetProperty("status", out var statusObj))
            return false;
        if (!statusObj.TryGetProperty("conditions", out var conditions) ||
            conditions.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var condition in conditions.EnumerateArray())
        {
            if (!condition.TryGetProperty("type", out var typeProp))
                continue;
            if (!string.Equals(typeProp.GetString(), conditionType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (condition.TryGetProperty("status", out var statusProp))
                status = statusProp.GetString() ?? "Unknown";
            return true;
        }

        return false;
    }

    private static bool TryGetConditionReason(JsonElement root, string conditionType, out string reason)
    {
        reason = "Unknown";
        if (!root.TryGetProperty("status", out var statusObj))
            return false;
        if (!statusObj.TryGetProperty("conditions", out var conditions) ||
            conditions.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var condition in conditions.EnumerateArray())
        {
            if (!condition.TryGetProperty("type", out var typeProp))
                continue;
            if (!string.Equals(typeProp.GetString(), conditionType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (condition.TryGetProperty("reason", out var reasonProp))
                reason = reasonProp.GetString() ?? "Unknown";
            return true;
        }

        return false;
    }

    private static JsonElement? ExtractJsonElement(object? stackObj)
    {
        return stackObj switch
        {
            JsonElement element => element,
            JsonObject obj => obj.AsObject().ToJsonString() is { } json
                ? JsonDocument.Parse(json).RootElement
                : (JsonElement?)null,
            _ => null
        };
    }

    private static bool TryGetStackPreview(JsonElement root, out bool preview)
    {
        preview = false;
        if (!root.TryGetProperty("spec", out var spec))
            return false;
        if (!spec.TryGetProperty("preview", out var previewProp))
            return false;
        if (previewProp.ValueKind == JsonValueKind.True)
        {
            preview = true;
            return true;
        }
        if (previewProp.ValueKind == JsonValueKind.False)
        {
            preview = false;
            return true;
        }

        if (previewProp.ValueKind == JsonValueKind.String)
        {
            var raw = previewProp.GetString();
            if (bool.TryParse(raw, out var parsed))
            {
                preview = parsed;
                return true;
            }
        }

        return false;
    }
}
