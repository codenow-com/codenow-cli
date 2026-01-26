using System.Net;
using System.Text;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Tasks created when provisioning namespaces.
/// </summary>
/// <param name="SystemNamespace">Task for the system namespace.</param>
/// <param name="CniNamespace">Task for the CNI namespace.</param>
/// <param name="CiPipelinesNamespace">Task for the CI pipelines namespace.</param>
public sealed record NamespaceProvisioningTasks(Task SystemNamespace, Task CniNamespace, Task CiPipelinesNamespace);

/// <summary>
/// Provisions Kubernetes namespaces required by the data plane.
/// </summary>
public interface INamespaceProvisioner
{
    /// <summary>
    /// Starts namespace provisioning tasks.
    /// </summary>
    /// <param name="client">Kubernetes client used for apply operations.</param>
    /// <param name="config">Operator configuration containing namespace settings.</param>
    /// <returns>Tasks for each namespace provisioning step.</returns>
    NamespaceProvisioningTasks StartNamespaceProvisioning(IKubernetesClient client, OperatorConfig config);
}

/// <summary>
/// Implements namespace provisioning using the Kubernetes API.
/// </summary>
internal sealed class NamespaceProvisioner : INamespaceProvisioner
{
    private readonly ILogger<NamespaceProvisioner> _logger;

    /// <summary>
    /// Creates a namespace provisioner.
    /// </summary>
    public NamespaceProvisioner(ILogger<NamespaceProvisioner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public NamespaceProvisioningTasks StartNamespaceProvisioning(IKubernetesClient client, OperatorConfig config)
    {
        IDictionary<string, string>? systemAnnotations = null;
        IDictionary<string, string>? appAnnotations = null;
        if (config.Kubernetes.PodPlacementMode == PodPlacementMode.PodNodeSelector)
        {
            systemAnnotations = new Dictionary<string, string>
            {
                [KubernetesConstants.Labels.PodNodeSelector] =
                    $"{config.Kubernetes.NodeLabels.System.Key}={config.Kubernetes.NodeLabels.System.Value}"
            };
            appAnnotations = new Dictionary<string, string>
            {
                [KubernetesConstants.Labels.PodNodeSelector] =
                    $"{config.Kubernetes.NodeLabels.Application.Key}={config.Kubernetes.NodeLabels.Application.Value}"
            };
        }

        var systemNamespaceTask = CreateNamespaceAsync(
            client,
            config.Kubernetes.Namespaces.System.Name,
            config.ContainerRegistry.Hostname,
            config.ContainerRegistry.Username,
            config.ContainerRegistry.Password,
            annotations: systemAnnotations
        );
        var ciPipelinesNamespaceTask =
            config.Kubernetes.Namespaces.CiPipelines.IsDedicatedRelativeTo(config.Kubernetes.Namespaces.System)
                ? CreateNamespaceAsync(
                    client,
                    config.Kubernetes.Namespaces.CiPipelines.Name,
                    config.ContainerRegistry.Hostname,
                    config.ContainerRegistry.Username,
                    config.ContainerRegistry.Password,
                    annotations: appAnnotations)
                : Task.CompletedTask;
        var cniNamespaceTask =
            config.Kubernetes.Namespaces.Cni.IsDedicatedRelativeTo(config.Kubernetes.Namespaces.System)
                ? CreateNamespaceAsync(
                    client,
                    config.Kubernetes.Namespaces.Cni.Name,
                    config.ContainerRegistry.Hostname,
                    config.ContainerRegistry.Username,
                    config.ContainerRegistry.Password,
                    annotations: appAnnotations)
                : Task.CompletedTask;

        return new NamespaceProvisioningTasks(systemNamespaceTask, cniNamespaceTask, ciPipelinesNamespaceTask);
    }

    /// <summary>
    /// Creates or updates a namespace and applies image pull secret.
    /// </summary>
    private async Task CreateNamespaceAsync(
        IKubernetesClient client,
        string namespaceName,
        string containerRegistryHostname,
        string containerRegistryUsername,
        string containerRegistryPassword,
        IDictionary<string, string>? annotations = null)
    {
        _logger.LogInformation("Applying namespace {ns}...", namespaceName);

        var patchJson = KubernetesManifestTools.BuildNamespacePatch(
            namespaceName,
            DataPlaneConstants.NamespaceTypeSystemLabelValue,
            DataPlaneConstants.PartOfDataPlaneLabelValue,
            annotations);

        await client.CoreV1.PatchNamespaceAsync(
            new V1Patch(patchJson, V1Patch.PatchType.ApplyPatch),
            namespaceName,
            fieldManager: KubernetesConstants.FieldManager,
            force: true
        );

        await CreateImagePullSecretAsync(
           client,
           namespaceName,
           KubernetesConstants.SystemImagePullSecret,
           containerRegistryHostname,
           containerRegistryUsername,
           containerRegistryPassword
       );

        _logger.LogInformation("Namespace {ns} applied.", namespaceName);
    }

    /// <summary>
    /// Creates or replaces the image pull secret in the target namespace.
    /// </summary>
    private async Task CreateImagePullSecretAsync(
        IKubernetesClient client,
        string namespaceName,
        string secretName,
        string registryHostname,
        string username,
        string password)
    {
        _logger.LogInformation("Applying docker-registry secret {secret} in namespace {ns}...",
            secretName, namespaceName);

        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        var dockerConfigJson =
            $"{{\"auths\":{{\"{registryHostname}\":{{\"username\":\"{username}\",\"password\":\"{password}\",\"auth\":\"{auth}\"}}}}}}";

        var secret = new V1Secret
        {
            Metadata = new V1ObjectMeta
            {
                Name = secretName,
                NamespaceProperty = namespaceName
            },
            Type = "kubernetes.io/dockerconfigjson",
            Data = new Dictionary<string, byte[]>
            {
                [".dockerconfigjson"] = Encoding.UTF8.GetBytes(dockerConfigJson)
            }
        };
        KubernetesManifestTools.ApplyLabels(secret.Metadata, ProvisioningCommonTools.BootstrapLabels);
        try
        {
            var existing = await client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
            secret.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;

            await client.CoreV1.ReplaceNamespacedSecretAsync(secret, secretName, namespaceName);
            _logger.LogInformation("Secret {secret} replaced in {ns}.", secretName, namespaceName);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            await client.CoreV1.CreateNamespacedSecretAsync(secret, namespaceName);
            _logger.LogInformation("Secret {secret} created in {ns}.", secretName, namespaceName);
        }
    }
}
