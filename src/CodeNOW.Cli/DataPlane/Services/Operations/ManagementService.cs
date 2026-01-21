using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Implements data plane management operations using the Kubernetes API.
/// </summary>
public class ManagementService : IManagementService
{
    private readonly IKubernetesClient client;
    private readonly OperatorStatusReader operatorStatusReader;
    private readonly StackStatusReader stackStatusReader;
    private readonly KubernetesStatusReader kubernetesStatusReader;

    /// <summary>
    /// Creates a management service for operator and stack operations.
    /// </summary>
    public ManagementService(
        ILogger<ManagementService> logger,
        IKubernetesClientFactory kubernetesClientFactory,
        KubernetesConnectionOptions connectionOptions)
    {
        client = kubernetesClientFactory.Create(logger, connectionOptions);
        var executor = new KubernetesReadExecutor(logger);
        operatorStatusReader = new OperatorStatusReader(client, executor);
        stackStatusReader = new StackStatusReader(client, executor);
        kubernetesStatusReader = new KubernetesStatusReader(client, executor);
    }

    /// <inheritdoc />
    public Task<string> GetClusterVersionAsync(CancellationToken cancellationToken = default)
    {
        return kubernetesStatusReader.GetClusterVersionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<ClusterResources> GetClusterResourcesAsync(CancellationToken cancellationToken = default)
    {
        return kubernetesStatusReader.GetClusterResourcesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperatorStatus> GetOperatorStatusAsync(CancellationToken cancellationToken = default)
    {
        return operatorStatusReader.GetOperatorStatusAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<StackStatus> GetStackStatusAsync(
        ManagementQuery query,
        CancellationToken cancellationToken = default)
    {
        return stackStatusReader.GetStackStatusAsync(ResolveQuery(query), cancellationToken);
    }

    /// <inheritdoc />
    public Task<LogReadResult> GetWorkspaceLogsAsync(
        ManagementLogQuery query,
        CancellationToken cancellationToken = default)
    {
        return stackStatusReader.GetWorkspaceLogsAsync(ResolveLogQuery(query), cancellationToken);
    }

    /// <inheritdoc />
    public Task<LogReadResult> GetOperatorLogsAsync(
        ManagementLogQuery query,
        CancellationToken cancellationToken = default)
    {
        return operatorStatusReader.GetOperatorLogsAsync(ResolveLogQuery(query), cancellationToken);
    }

    /// <inheritdoc />
    public async Task RequestReconcileAsync(
        ManagementQuery query,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveQuery(query);

        var patchObj = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["annotations"] = new JsonObject
                {
                    ["pulumi.com/reconciliation-request"] = DateTime.UtcNow.ToString("O")
                }
            }
        };

        var patch = new V1Patch(patchObj.ToJsonString(), V1Patch.PatchType.MergePatch);
        await client.CustomObjects.PatchNamespacedCustomObjectAsync(
            patch,
            group: "pulumi.com",
            version: "v1",
            namespaceParameter: resolved.Namespace,
            plural: "stacks",
            name: resolved.Name,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ToggleStackPreviewAsync(
        StackStatus stackStatus,
        ManagementQuery query,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveQuery(query);

        var enabled = string.Equals(stackStatus.DryRun, "Enabled", StringComparison.OrdinalIgnoreCase);
        var newValue = !enabled;

        var patchObj = new JsonObject
        {
            ["spec"] = new JsonObject
            {
                ["preview"] = newValue
            }
        };

        var patch = new V1Patch(patchObj.ToJsonString(), V1Patch.PatchType.MergePatch);
        await client.CustomObjects.PatchNamespacedCustomObjectAsync(
            patch,
            group: "pulumi.com",
            version: "v1",
            namespaceParameter: resolved.Namespace,
            plural: "stacks",
            name: resolved.Name,
            cancellationToken: cancellationToken);

        return newValue;
    }

    private static ManagementQuery ResolveQuery(ManagementQuery query)
    {
        var namespaceName = ResolveStackNamespace(query.Namespace);
        var name = string.IsNullOrWhiteSpace(query.Name) ? DataPlaneConstants.StackName : query.Name;
        return new ManagementQuery(namespaceName, name);
    }

    private static ManagementLogQuery ResolveLogQuery(ManagementLogQuery query)
    {
        var namespaceName = ResolveStackNamespace(query.Namespace);
        return new ManagementLogQuery(namespaceName, query.TailLines);
    }

    private static string ResolveStackNamespace(string operatorNamespace)
    {
        if (string.IsNullOrWhiteSpace(operatorNamespace) ||
            operatorNamespace.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return "cn-data-plane-system";

        return operatorNamespace;
    }
}
