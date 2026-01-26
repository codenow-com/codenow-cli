namespace CodeNOW.Cli.Adapters.Kubernetes;

/// <summary>
/// Constants used when interacting with Kubernetes resources.
/// </summary>
public static class KubernetesConstants
{
    /// <summary>
    /// Field manager name used for server-side apply operations.
    /// </summary>
    public const string FieldManager = "codenow.com/cli";

    /// <summary>
    /// Default image pull secret name for system namespace resources.
    /// </summary>
    public const string SystemImagePullSecret = "cn-system-image-pull-secret";

    /// <summary>
    /// Namespace label keys.
    /// </summary>
    /// <summary>
    /// Label keys used on Kubernetes resources.
    /// </summary>
    public static class Labels
    {
        /// <summary>
        /// Label key describing the namespace type.
        /// </summary>
        public const string NamespaceType = "codenow.com/namespace-type";

        /// <summary>
        /// Label key used to express a node selector annotation.
        /// </summary>
        public const string PodNodeSelector = "scheduler.alpha.kubernetes.io/node-selector";
    }

    /// <summary>
    /// Common label values for managed resources.
    /// </summary>
    public static class LabelValues
    {

        /// <summary>
        /// Managed-by label value for CLI-managed resources.
        /// </summary>
        public const string ManagedBy = "cn-cli";


    }
}
