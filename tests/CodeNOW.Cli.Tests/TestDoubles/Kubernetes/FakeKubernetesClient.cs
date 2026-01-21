using System.Text;
using CodeNOW.Cli.Adapters.Kubernetes;
using k8s.Models;
using k8s;

namespace CodeNOW.Cli.Tests.TestDoubles.Kubernetes;

public sealed class FakeKubernetesClient : IKubernetesClient
{
    public FakeKubernetesClient()
    {
        CoreV1 = new FakeCoreV1Client();
        AppsV1 = new FakeAppsV1Client();
        CustomObjects = new FakeCustomObjectsClient();
        ApiextensionsV1 = new FakeApiextensionsClient();
        Version = new FakeVersionClient();
    }

    public FakeCoreV1Client CoreV1 { get; }
    IKubernetesCoreClient IKubernetesClient.CoreV1 => CoreV1;

    public FakeAppsV1Client AppsV1 { get; }
    IKubernetesAppsClient IKubernetesClient.AppsV1 => AppsV1;

    public FakeCustomObjectsClient CustomObjects { get; }
    IKubernetesCustomObjectsClient IKubernetesClient.CustomObjects => CustomObjects;

    public FakeApiextensionsClient ApiextensionsV1 { get; }
    IKubernetesApiextensionsClient IKubernetesClient.ApiextensionsV1 => ApiextensionsV1;

    public FakeVersionClient Version { get; }
    IKubernetesVersionClient IKubernetesClient.Version => Version;

    public List<IKubernetesObject<V1ObjectMeta>> AppliedObjects { get; } = new();

    public Task ApplyAsync(IKubernetesObject<V1ObjectMeta> kubeObj)
    {
        AppliedObjects.Add(kubeObj);
        return Task.CompletedTask;
    }
}

public sealed class FakeCoreV1Client : IKubernetesCoreClient
{
    public Func<CancellationToken, Task<V1NodeList>>? ListNodeAsyncHandler { get; set; }
    public Func<string?, CancellationToken, Task<V1PodList>>? ListPodForAllNamespacesAsyncHandler { get; set; }
    public Func<string, string?, CancellationToken, Task<V1PodList>>? ListNamespacedPodAsyncHandler { get; set; }
    public Func<string, string, int?, CancellationToken, Task<Stream>>? ReadNamespacedPodLogAsyncHandler { get; set; }
    public Func<V1Patch, string, string?, bool?, Task>? PatchNamespaceAsyncHandler { get; set; }
    public Func<string, string, Task<V1Secret>>? ReadNamespacedSecretAsyncHandler { get; set; }
    public Func<V1Secret, string, string, Task>? ReplaceNamespacedSecretAsyncHandler { get; set; }
    public Func<V1Secret, string, Task>? CreateNamespacedSecretAsyncHandler { get; set; }
    public Func<string, string, Task<V1PersistentVolumeClaim>>? ReadNamespacedPersistentVolumeClaimAsyncHandler { get; set; }
    public Func<V1Patch, string, string, Task>? PatchNamespacedPersistentVolumeClaimAsyncHandler { get; set; }
    public Func<V1PersistentVolumeClaim, string, Task>? CreateNamespacedPersistentVolumeClaimAsyncHandler { get; set; }

    public Task<V1NodeList> ListNodeAsync(CancellationToken cancellationToken = default)
        => ListNodeAsyncHandler?.Invoke(cancellationToken)
            ?? Task.FromResult(new V1NodeList());

    public Task<V1PodList> ListPodForAllNamespacesAsync(
        string? labelSelector = null,
        CancellationToken cancellationToken = default)
        => ListPodForAllNamespacesAsyncHandler?.Invoke(labelSelector, cancellationToken)
            ?? Task.FromResult(new V1PodList());

    public Task<V1PodList> ListNamespacedPodAsync(
        string namespaceName,
        string? labelSelector = null,
        CancellationToken cancellationToken = default)
        => ListNamespacedPodAsyncHandler?.Invoke(namespaceName, labelSelector, cancellationToken)
            ?? Task.FromResult(new V1PodList());

    public Task<Stream> ReadNamespacedPodLogAsync(
        string name,
        string namespaceParameter,
        int? tailLines = null,
        CancellationToken cancellationToken = default)
        => ReadNamespacedPodLogAsyncHandler?.Invoke(name, namespaceParameter, tailLines, cancellationToken)
            ?? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(string.Empty)));

    public Task PatchNamespaceAsync(V1Patch patch, string name, string? fieldManager = null, bool? force = null)
        => PatchNamespaceAsyncHandler?.Invoke(patch, name, fieldManager, force) ?? Task.CompletedTask;

    public Task<V1Secret> ReadNamespacedSecretAsync(string name, string namespaceParameter)
        => ReadNamespacedSecretAsyncHandler?.Invoke(name, namespaceParameter)
            ?? Task.FromResult(new V1Secret());

    public Task ReplaceNamespacedSecretAsync(V1Secret body, string name, string namespaceParameter)
        => ReplaceNamespacedSecretAsyncHandler?.Invoke(body, name, namespaceParameter) ?? Task.CompletedTask;

    public Task CreateNamespacedSecretAsync(V1Secret body, string namespaceParameter)
        => CreateNamespacedSecretAsyncHandler?.Invoke(body, namespaceParameter) ?? Task.CompletedTask;

    public Task<V1PersistentVolumeClaim> ReadNamespacedPersistentVolumeClaimAsync(string name, string namespaceParameter)
        => ReadNamespacedPersistentVolumeClaimAsyncHandler?.Invoke(name, namespaceParameter)
            ?? Task.FromResult(new V1PersistentVolumeClaim());

    public Task PatchNamespacedPersistentVolumeClaimAsync(V1Patch patch, string name, string namespaceParameter)
        => PatchNamespacedPersistentVolumeClaimAsyncHandler?.Invoke(patch, name, namespaceParameter) ?? Task.CompletedTask;

    public Task CreateNamespacedPersistentVolumeClaimAsync(V1PersistentVolumeClaim body, string namespaceParameter)
        => CreateNamespacedPersistentVolumeClaimAsyncHandler?.Invoke(body, namespaceParameter) ?? Task.CompletedTask;
}

public sealed class FakeAppsV1Client : IKubernetesAppsClient
{
    public Func<string, string, Task<V1Deployment>>? ReadNamespacedDeploymentAsyncHandler { get; set; }

    public Task<V1Deployment> ReadNamespacedDeploymentAsync(string name, string namespaceParameter)
        => ReadNamespacedDeploymentAsyncHandler?.Invoke(name, namespaceParameter)
            ?? Task.FromResult(new V1Deployment());
}

public sealed class FakeCustomObjectsClient : IKubernetesCustomObjectsClient
{
    public Func<string, string, string, string, string, CancellationToken, Task<object>>?
        GetNamespacedCustomObjectAsyncHandler
    { get; set; }

    public Func<V1Patch, string, string, string, string, string, CancellationToken, string?, bool?, Task>?
        PatchNamespacedCustomObjectAsyncHandler
    { get; set; }

    public Task<object> GetNamespacedCustomObjectAsync(
        string group,
        string version,
        string namespaceParameter,
        string plural,
        string name,
        CancellationToken cancellationToken = default)
        => GetNamespacedCustomObjectAsyncHandler?.Invoke(
            group, version, namespaceParameter, plural, name, cancellationToken)
            ?? Task.FromResult<object>(new object());

    public Task PatchNamespacedCustomObjectAsync(
        V1Patch patch,
        string group,
        string version,
        string namespaceParameter,
        string plural,
        string name,
        CancellationToken cancellationToken = default,
        string? fieldManager = null,
        bool? force = null)
        => PatchNamespacedCustomObjectAsyncHandler?.Invoke(
            patch, group, version, namespaceParameter, plural, name, cancellationToken, fieldManager, force)
            ?? Task.CompletedTask;
}

public sealed class FakeApiextensionsClient : IKubernetesApiextensionsClient
{
    public Func<V1Patch, string, string?, bool?, Task>? PatchCustomResourceDefinitionAsyncHandler { get; set; }

    public Task PatchCustomResourceDefinitionAsync(
        V1Patch patch,
        string name,
        string? fieldManager = null,
        bool? force = null)
        => PatchCustomResourceDefinitionAsyncHandler?.Invoke(patch, name, fieldManager, force)
            ?? Task.CompletedTask;
}

public sealed class FakeVersionClient : IKubernetesVersionClient
{
    public Func<CancellationToken, Task<VersionInfo>>? GetCodeAsyncHandler { get; set; }

    public Task<VersionInfo> GetCodeAsync(CancellationToken cancellationToken = default)
        => GetCodeAsyncHandler?.Invoke(cancellationToken)
            ?? Task.FromResult(new VersionInfo());
}
