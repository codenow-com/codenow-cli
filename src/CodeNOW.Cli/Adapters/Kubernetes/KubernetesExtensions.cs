using k8s;
using k8s.Models;

namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Extension helpers for applying Kubernetes resources.
/// </summary>
public static class KubernetesExtnsions
{
    /// <summary>
    /// Applies the given Kubernetes object using server-side apply.
    /// </summary>
    /// <param name="client">Kubernetes API client.</param>
    /// <param name="kubeObj">Resource instance to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> or <paramref name="kubeObj"/> is null.</exception>
    /// <exception cref="NotSupportedException">Thrown when the resource kind is not supported.</exception>
    public static async Task ApplyAsync(this k8s.Kubernetes client, IKubernetesObject<V1ObjectMeta> kubeObj)
    {
        if (client is null)
            throw new ArgumentNullException(nameof(client));
        if (kubeObj is null)
            throw new ArgumentNullException(nameof(kubeObj));

        var kind = kubeObj.Kind;
        var apiVersion = kubeObj.ApiVersion;
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(apiVersion))
        {
            (kind, apiVersion) = kubeObj switch
            {
                V1Deployment => ("Deployment", "apps/v1"),
                V1ServiceAccount => ("ServiceAccount", "v1"),
                V1Service => ("Service", "v1"),
                V1Role => ("Role", "rbac.authorization.k8s.io/v1"),
                V1RoleBinding => ("RoleBinding", "rbac.authorization.k8s.io/v1"),
                V1ClusterRole => ("ClusterRole", "rbac.authorization.k8s.io/v1"),
                V1ClusterRoleBinding => ("ClusterRoleBinding", "rbac.authorization.k8s.io/v1"),
                V1CustomResourceDefinition => ("CustomResourceDefinition", "apiextensions.k8s.io/v1"),
                _ => (kind, apiVersion)
            };

            if (!string.IsNullOrWhiteSpace(kind))
                kubeObj.Kind = kind;
            if (!string.IsNullOrWhiteSpace(apiVersion))
                kubeObj.ApiVersion = apiVersion;
        }

        var json = KubernetesJson.Serialize(kubeObj);
        var ns = kubeObj.Namespace();
        var name = kubeObj.Name();

        var patch = new V1Patch(
            json,
            V1Patch.PatchType.ApplyPatch); // server-side apply

        switch (kind)
        {
            case "Deployment":
                await client.AppsV1.PatchNamespacedDeploymentAsync(
                    patch,
                    name,
                    ns,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            case "ServiceAccount":
                await client.CoreV1.PatchNamespacedServiceAccountAsync(
                    patch,
                    name,
                    ns,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            case "Service":
                await client.CoreV1.PatchNamespacedServiceAsync(
                    patch,
                    name,
                    ns,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            case "Role":
                await client.RbacAuthorizationV1.PatchNamespacedRoleAsync(
                    patch,
                    name,
                    ns,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            case "RoleBinding":
                await client.RbacAuthorizationV1.PatchNamespacedRoleBindingAsync(
                    patch,
                    name,
                    ns,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            case "ClusterRole":
                await client.RbacAuthorizationV1.PatchClusterRoleAsync(
                    patch,
                    name,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            case "ClusterRoleBinding":
                await client.RbacAuthorizationV1.PatchClusterRoleBindingAsync(
                    patch,
                    name,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            case "CustomResourceDefinition":
                await client.ApiextensionsV1.PatchCustomResourceDefinitionAsync(
                    patch,
                    name,
                    fieldManager: KubernetesConstants.FieldManager,
                    force: true);
                break;

            default:
                throw new NotSupportedException(
                    $"ApplyAsync not implemented for kind '{kind}'");
        }
    }
}
