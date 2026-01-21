using k8s.Models;
using k8s;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Abstraction over the Kubernetes API client for testability.
/// </summary>
public interface IKubernetesClient
{
    /// <summary>Core V1 API surface.</summary>
    IKubernetesCoreClient CoreV1 { get; }
    /// <summary>Apps V1 API surface.</summary>
    IKubernetesAppsClient AppsV1 { get; }
    /// <summary>Custom objects API surface.</summary>
    IKubernetesCustomObjectsClient CustomObjects { get; }
    /// <summary>API extensions V1 API surface.</summary>
    IKubernetesApiextensionsClient ApiextensionsV1 { get; }
    /// <summary>Version API surface.</summary>
    IKubernetesVersionClient Version { get; }

    /// <summary>
    /// Applies the given Kubernetes object using server-side apply.
    /// </summary>
    Task ApplyAsync(IKubernetesObject<V1ObjectMeta> kubeObj);
}

/// <summary>
/// Core V1 API subset used by the application.
/// </summary>
public interface IKubernetesCoreClient
{
    Task<V1NodeList> ListNodeAsync(CancellationToken cancellationToken = default);
    Task<V1PodList> ListPodForAllNamespacesAsync(string? labelSelector = null, CancellationToken cancellationToken = default);
    Task<V1PodList> ListNamespacedPodAsync(string namespaceName, string? labelSelector = null, CancellationToken cancellationToken = default);
    Task<Stream> ReadNamespacedPodLogAsync(string name, string namespaceParameter, int? tailLines = null, CancellationToken cancellationToken = default);
    Task PatchNamespaceAsync(V1Patch patch, string name, string? fieldManager = null, bool? force = null);
    Task<V1Secret> ReadNamespacedSecretAsync(string name, string namespaceParameter);
    Task ReplaceNamespacedSecretAsync(V1Secret body, string name, string namespaceParameter);
    Task CreateNamespacedSecretAsync(V1Secret body, string namespaceParameter);
    Task<V1PersistentVolumeClaim> ReadNamespacedPersistentVolumeClaimAsync(string name, string namespaceParameter);
    Task PatchNamespacedPersistentVolumeClaimAsync(V1Patch patch, string name, string namespaceParameter);
    Task CreateNamespacedPersistentVolumeClaimAsync(V1PersistentVolumeClaim body, string namespaceParameter);
}

/// <summary>
/// Apps V1 API subset used by the application.
/// </summary>
public interface IKubernetesAppsClient
{
    Task<V1Deployment> ReadNamespacedDeploymentAsync(string name, string namespaceParameter);
}

/// <summary>
/// Custom objects API subset used by the application.
/// </summary>
public interface IKubernetesCustomObjectsClient
{
    Task<object> GetNamespacedCustomObjectAsync(
        string group,
        string version,
        string namespaceParameter,
        string plural,
        string name,
        CancellationToken cancellationToken = default);

    Task PatchNamespacedCustomObjectAsync(
        V1Patch patch,
        string group,
        string version,
        string namespaceParameter,
        string plural,
        string name,
        CancellationToken cancellationToken = default,
        string? fieldManager = null,
        bool? force = null);
}

/// <summary>
/// API extensions V1 subset used by the application.
/// </summary>
public interface IKubernetesApiextensionsClient
{
    Task PatchCustomResourceDefinitionAsync(
        V1Patch patch,
        string name,
        string? fieldManager = null,
        bool? force = null);
}

/// <summary>
/// Version API subset used by the application.
/// </summary>
public interface IKubernetesVersionClient
{
    Task<VersionInfo> GetCodeAsync(CancellationToken cancellationToken = default);
}
