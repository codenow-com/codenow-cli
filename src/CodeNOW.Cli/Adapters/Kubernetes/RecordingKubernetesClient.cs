using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using k8s;
using k8s.Models;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Represents a single Kubernetes API operation recorded during bootstrap.
/// </summary>
public sealed record RecordedOperation(
    string ApiGroup,
    string Resource,
    string Verb,
    string? ResourceName = null);

/// <summary>
/// A non-connecting Kubernetes client that records all operations performed during bootstrap.
/// Used by <c>--show-permissions-only</c> to derive the minimum ClusterRole dynamically.
/// </summary>
internal sealed class RecordingKubernetesClient : IKubernetesClient
{
    private readonly List<RecordedOperation> operations = [];
    private readonly List<IKubernetesObject<V1ObjectMeta>> appliedObjects = [];

    /// <summary>All recorded API operations.</summary>
    public IReadOnlyList<RecordedOperation> Operations => operations;

    /// <summary>All objects passed to <see cref="ApplyAsync"/>.</summary>
    public IReadOnlyList<IKubernetesObject<V1ObjectMeta>> AppliedObjects => appliedObjects;

    public IKubernetesCoreClient CoreV1 { get; }
    public IKubernetesAppsClient AppsV1 { get; }
    public IKubernetesCustomObjectsClient CustomObjects { get; }
    public IKubernetesApiextensionsClient ApiextensionsV1 { get; }
    public IKubernetesVersionClient Version { get; }

    public RecordingKubernetesClient()
    {
        CoreV1 = new RecordingCoreV1Client(this);
        AppsV1 = new RecordingAppsV1Client(this);
        CustomObjects = new RecordingCustomObjectsClient(this);
        ApiextensionsV1 = new RecordingApiextensionsClient(this);
        Version = new RecordingVersionClient();
    }

    public Task ApplyAsync(IKubernetesObject<V1ObjectMeta> kubeObj)
    {
        appliedObjects.Add(kubeObj);
        var (apiGroup, resource) = ResolveGroupResource(kubeObj);
        Record(apiGroup, resource, "get", kubeObj.Metadata?.Name);
        Record(apiGroup, resource, "create", kubeObj.Metadata?.Name);
        Record(apiGroup, resource, "patch", kubeObj.Metadata?.Name);
        Record(apiGroup, resource, "update", kubeObj.Metadata?.Name);
        return Task.CompletedTask;
    }

    internal void Record(string apiGroup, string resource, string verb, string? resourceName = null)
    {
        operations.Add(new RecordedOperation(apiGroup, resource, verb, resourceName));
    }

    private static (string ApiGroup, string Resource) ResolveGroupResource(IKubernetesObject<V1ObjectMeta> obj) => obj switch
    {
        V1Namespace => ("", "namespaces"),
        V1ServiceAccount => ("", "serviceaccounts"),
        V1Secret => ("", "secrets"),
        V1Service => ("", "services"),
        V1PersistentVolumeClaim => ("", "persistentvolumeclaims"),
        V1Deployment => ("apps", "deployments"),
        V1ClusterRole => ("rbac.authorization.k8s.io", "clusterroles"),
        V1ClusterRoleBinding => ("rbac.authorization.k8s.io", "clusterrolebindings"),
        V1Role => ("rbac.authorization.k8s.io", "roles"),
        V1RoleBinding => ("rbac.authorization.k8s.io", "rolebindings"),
        _ => ("", "unknown")
    };

    private sealed class RecordingCoreV1Client(RecordingKubernetesClient parent) : IKubernetesCoreClient
    {
        public Task<V1NodeList> ListNodeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new V1NodeList());

        public Task<V1PodList> ListPodForAllNamespacesAsync(string? labelSelector = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new V1PodList());

        public Task<V1PodList> ListNamespacedPodAsync(string namespaceName, string? labelSelector = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new V1PodList());

        public Task<Stream> ReadNamespacedPodLogAsync(string name, string namespaceParameter, int? tailLines = null, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task PatchNamespaceAsync(V1Patch patch, string name, string? fieldManager = null, bool? force = null)
        {
            parent.Record("", "namespaces", "get", name);
            parent.Record("", "namespaces", "create", name);
            parent.Record("", "namespaces", "patch", name);
            parent.Record("", "namespaces", "update", name);
            return Task.CompletedTask;
        }

        public Task<V1Secret> ReadNamespacedSecretAsync(string name, string namespaceParameter)
        {
            parent.Record("", "secrets", "get", name);
            // Throw NotFound so bootstrap takes the create path
            throw CreateNotFound();
        }

        public Task ReplaceNamespacedSecretAsync(V1Secret body, string name, string namespaceParameter)
        {
            parent.Record("", "secrets", "update", name);
            return Task.CompletedTask;
        }

        public Task CreateNamespacedSecretAsync(V1Secret body, string namespaceParameter)
        {
            parent.Record("", "secrets", "create", body.Metadata?.Name);
            return Task.CompletedTask;
        }

        public Task<V1PersistentVolumeClaim> ReadNamespacedPersistentVolumeClaimAsync(string name, string namespaceParameter)
        {
            parent.Record("", "persistentvolumeclaims", "get", name);
            throw CreateNotFound();
        }

        public Task PatchNamespacedPersistentVolumeClaimAsync(V1Patch patch, string name, string namespaceParameter)
        {
            parent.Record("", "persistentvolumeclaims", "patch", name);
            return Task.CompletedTask;
        }

        public Task CreateNamespacedPersistentVolumeClaimAsync(V1PersistentVolumeClaim body, string namespaceParameter)
        {
            parent.Record("", "persistentvolumeclaims", "create", body.Metadata?.Name);
            return Task.CompletedTask;
        }

        private static k8s.Autorest.HttpOperationException CreateNotFound()
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            var wrapper = new k8s.Autorest.HttpResponseMessageWrapper(response, string.Empty);
            return new k8s.Autorest.HttpOperationException("Not Found") { Response = wrapper };
        }
    }

    private sealed class RecordingAppsV1Client(RecordingKubernetesClient parent) : IKubernetesAppsClient
    {
        public Task<V1Deployment> ReadNamespacedDeploymentAsync(string name, string namespaceParameter)
        {
            parent.Record("apps", "deployments", "get", name);
            return Task.FromResult(new V1Deployment
            {
                Status = new V1DeploymentStatus
                {
                    ReadyReplicas = 1,
                    Replicas = 1,
                    UpdatedReplicas = 1,
                    AvailableReplicas = 1,
                    Conditions =
                    [
                        new V1DeploymentCondition
                        {
                            Type = "Available",
                            Status = "True"
                        }
                    ]
                },
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Selector = new V1LabelSelector()
                }
            });
        }
    }

    private sealed class RecordingCustomObjectsClient(RecordingKubernetesClient parent) : IKubernetesCustomObjectsClient
    {
        public Task<object> GetNamespacedCustomObjectAsync(
            string group, string version, string namespaceParameter, string plural, string name,
            CancellationToken cancellationToken = default)
        {
            parent.Record(group, plural, "get", name);
            return Task.FromResult<object>(new object());
        }

        public Task PatchNamespacedCustomObjectAsync(
            V1Patch patch, string group, string version, string namespaceParameter,
            string plural, string name, CancellationToken cancellationToken = default,
            string? fieldManager = null, bool? force = null)
        {
            parent.Record(group, plural, "get", name);
            parent.Record(group, plural, "create", name);
            parent.Record(group, plural, "patch", name);
            parent.Record(group, plural, "update", name);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingApiextensionsClient(RecordingKubernetesClient parent) : IKubernetesApiextensionsClient
    {
        public Task PatchCustomResourceDefinitionAsync(V1Patch patch, string name, string? fieldManager = null, bool? force = null)
        {
            parent.Record("apiextensions.k8s.io", "customresourcedefinitions", "get", name);
            parent.Record("apiextensions.k8s.io", "customresourcedefinitions", "create", name);
            parent.Record("apiextensions.k8s.io", "customresourcedefinitions", "patch", name);
            parent.Record("apiextensions.k8s.io", "customresourcedefinitions", "update", name);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingVersionClient : IKubernetesVersionClient
    {
        public Task<VersionInfo> GetCodeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new VersionInfo());
    }
}

/// <summary>
/// Factory that always returns the same pre-created recording client.
/// </summary>
internal sealed class RecordingKubernetesClientFactory(RecordingKubernetesClient client) : IKubernetesClientFactory
{
    public IKubernetesClient Create(Microsoft.Extensions.Logging.ILogger logger, KubernetesConnectionOptions options)
        => client;
}
