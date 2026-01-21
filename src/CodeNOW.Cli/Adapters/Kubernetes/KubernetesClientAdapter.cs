using k8s;
using k8s.Models;
using K8sClient = k8s.Kubernetes;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Adapts the concrete Kubernetes client to the testable abstraction.
/// </summary>
public sealed class KubernetesClientAdapter : IKubernetesClient
{
    private readonly K8sClient client;

    /// <summary>
    /// Creates a new adapter for the underlying Kubernetes client.
    /// </summary>
    /// <param name="client">Concrete Kubernetes client instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
    public KubernetesClientAdapter(K8sClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        CoreV1 = new CoreV1Adapter(client);
        AppsV1 = new AppsV1Adapter(client);
        CustomObjects = new CustomObjectsAdapter(client);
        ApiextensionsV1 = new ApiextensionsAdapter(client);
        Version = new VersionAdapter(client);
    }

    /// <inheritdoc />
    public IKubernetesCoreClient CoreV1 { get; }
    /// <inheritdoc />
    public IKubernetesAppsClient AppsV1 { get; }
    /// <inheritdoc />
    public IKubernetesCustomObjectsClient CustomObjects { get; }
    /// <inheritdoc />
    public IKubernetesApiextensionsClient ApiextensionsV1 { get; }
    /// <inheritdoc />
    public IKubernetesVersionClient Version { get; }

    /// <inheritdoc />
    public Task ApplyAsync(IKubernetesObject<V1ObjectMeta> kubeObj)
    {
        return client.ApplyAsync(kubeObj);
    }

    /// <summary>
    /// Wraps CoreV1 operations used by the application.
    /// </summary>
    private sealed class CoreV1Adapter : IKubernetesCoreClient
    {
        private readonly K8sClient client;

        public CoreV1Adapter(K8sClient client)
        {
            this.client = client;
        }

        public Task<V1NodeList> ListNodeAsync(CancellationToken cancellationToken = default)
            => client.CoreV1.ListNodeAsync(cancellationToken: cancellationToken);

        public Task<V1PodList> ListPodForAllNamespacesAsync(
            string? labelSelector = null,
            CancellationToken cancellationToken = default)
            => client.CoreV1.ListPodForAllNamespacesAsync(
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);

        public Task<V1PodList> ListNamespacedPodAsync(
            string namespaceName,
            string? labelSelector = null,
            CancellationToken cancellationToken = default)
            => client.CoreV1.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);

        public Task<Stream> ReadNamespacedPodLogAsync(
            string name,
            string namespaceParameter,
            int? tailLines = null,
            CancellationToken cancellationToken = default)
            => client.CoreV1.ReadNamespacedPodLogAsync(
                name,
                namespaceParameter,
                tailLines: tailLines,
                cancellationToken: cancellationToken);

        public Task PatchNamespaceAsync(
            V1Patch patch,
            string name,
            string? fieldManager = null,
            bool? force = null)
            => client.CoreV1.PatchNamespaceAsync(
                patch,
                name,
                fieldManager: fieldManager,
                force: force);

        public Task<V1Secret> ReadNamespacedSecretAsync(string name, string namespaceParameter)
            => client.CoreV1.ReadNamespacedSecretAsync(name, namespaceParameter);

        public Task ReplaceNamespacedSecretAsync(V1Secret body, string name, string namespaceParameter)
            => client.CoreV1.ReplaceNamespacedSecretAsync(body, name, namespaceParameter);

        public Task CreateNamespacedSecretAsync(V1Secret body, string namespaceParameter)
            => client.CoreV1.CreateNamespacedSecretAsync(body, namespaceParameter);

        public Task<V1PersistentVolumeClaim> ReadNamespacedPersistentVolumeClaimAsync(
            string name,
            string namespaceParameter)
            => client.CoreV1.ReadNamespacedPersistentVolumeClaimAsync(name, namespaceParameter);

        public Task PatchNamespacedPersistentVolumeClaimAsync(
            V1Patch patch,
            string name,
            string namespaceParameter)
            => client.CoreV1.PatchNamespacedPersistentVolumeClaimAsync(patch, name, namespaceParameter);

        public Task CreateNamespacedPersistentVolumeClaimAsync(
            V1PersistentVolumeClaim body,
            string namespaceParameter)
            => client.CoreV1.CreateNamespacedPersistentVolumeClaimAsync(body, namespaceParameter);
    }

    /// <summary>
    /// Wraps AppsV1 operations used by the application.
    /// </summary>
    private sealed class AppsV1Adapter : IKubernetesAppsClient
    {
        private readonly K8sClient client;

        public AppsV1Adapter(K8sClient client)
        {
            this.client = client;
        }

        public Task<V1Deployment> ReadNamespacedDeploymentAsync(string name, string namespaceParameter)
            => client.AppsV1.ReadNamespacedDeploymentAsync(name, namespaceParameter);
    }

    /// <summary>
    /// Wraps CustomObjects operations used by the application.
    /// </summary>
    private sealed class CustomObjectsAdapter : IKubernetesCustomObjectsClient
    {
        private readonly K8sClient client;

        public CustomObjectsAdapter(K8sClient client)
        {
            this.client = client;
        }

        public Task<object> GetNamespacedCustomObjectAsync(
            string group,
            string version,
            string namespaceParameter,
            string plural,
            string name,
            CancellationToken cancellationToken = default)
            => client.CustomObjects.GetNamespacedCustomObjectAsync(
                group,
                version,
                namespaceParameter,
                plural,
                name,
                cancellationToken: cancellationToken);

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
            => client.CustomObjects.PatchNamespacedCustomObjectAsync(
                patch,
                group,
                version,
                namespaceParameter,
                plural,
                name,
                fieldManager: fieldManager,
                force: force,
                cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Wraps Apiextensions operations used by the application.
    /// </summary>
    private sealed class ApiextensionsAdapter : IKubernetesApiextensionsClient
    {
        private readonly K8sClient client;

        public ApiextensionsAdapter(K8sClient client)
        {
            this.client = client;
        }

        public Task PatchCustomResourceDefinitionAsync(
            V1Patch patch,
            string name,
            string? fieldManager = null,
            bool? force = null)
            => client.ApiextensionsV1.PatchCustomResourceDefinitionAsync(
                patch,
                name,
                fieldManager: fieldManager,
                force: force);
    }

    /// <summary>
    /// Wraps Version operations used by the application.
    /// </summary>
    private sealed class VersionAdapter : IKubernetesVersionClient
    {
        private readonly K8sClient client;

        public VersionAdapter(K8sClient client)
        {
            this.client = client;
        }

        public Task<VersionInfo> GetCodeAsync(CancellationToken cancellationToken = default)
            => VersionOperationsExtensions.GetCodeAsync(client.Version, cancellationToken);
    }
}
