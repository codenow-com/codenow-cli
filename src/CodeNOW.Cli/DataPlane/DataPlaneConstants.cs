namespace CodeNOW.Cli.DataPlane;

/// <summary>
/// Shared constants for the data plane feature set.
/// </summary>
public static class DataPlaneConstants
{
    /// <summary>
    /// Default Pulumi stack name used by the data plane.
    /// </summary>
    public const string StackName = "cn-data-plane";

    /// <summary>
    /// Operator info file name bundled with the manifests.
    /// </summary>
    public const string OperatorInfoFileName = "operator-info.json";

    /// <summary>
    /// Default data plane operator configuration file name.
    /// </summary>
    public const string OperatorConfigFileName = "cn-data-plane-operator.json";

    /// <summary>
    /// JSON schema URL for the operator configuration.
    /// </summary>
    public const string OperatorConfigSchemaV1Url =
        "https://codenow-com.github.io/codenow-cli/schemas/cn-data-plane-operator-config.v1.schema.json";

    /// <summary>
    /// Base name for the operator controller deployment.
    /// </summary>
    public const string OperatorDeploymentBaseName = "controller-manager";

    /// <summary>
    /// Secret name for Pulumi operator configuration.
    /// </summary>
    public const string PulumiOperatorConfigSecretName = "cn-data-plane-config";

    /// <summary>
    /// Data plane config secret keys.
    /// </summary>
    /// <summary>
    /// SCM token for system integrations.
    /// </summary>
    public const string DataPlaneConfigKeyScmSystemAuthToken = "cn_scm_system_auth_token";
    /// <summary>
    /// SCM password for system integrations.
    /// </summary>
    public const string DataPlaneConfigKeyScmSystemAuthPassword = "cn_scm_system_auth_password";
    /// <summary>
    /// SCM username for system integrations.
    /// </summary>
    public const string DataPlaneConfigKeyScmSystemAuthUsername = "cn_scm_system_auth_username";
    /// <summary>
    /// Pulumi passphrase used to encrypt state.
    /// </summary>
    public const string DataPlaneConfigKeyPulumiPassphrase = "cn_pulumi_passphrase";
    /// <summary>
    /// Custom CA certificate file name.
    /// </summary>
    public const string DataPlaneConfigKeyPkiCustomCaCert = "cn_pki_custom_ca.cert";
    /// <summary>
    /// Target namespace for the data plane.
    /// </summary>
    public const string DataPlaneConfigKeyTargetNamespace = "cn_target_namespace";
    /// <summary>
    /// Enable dedicated namespace for CNI components.
    /// </summary>
    public const string DataPlaneConfigKeyCniDedicatedNamespaceEnabled = "cn_cni_dedicated_namespace_enabled";
    /// <summary>
    /// Enable dedicated namespace for CI pipelines.
    /// </summary>
    public const string DataPlaneConfigKeyCiPipelinesDedicatedNamespaceEnabled = "cn_ci_pipelines_dedicated_namespace_enabled";
    /// <summary>
    /// Dedicated namespace name for CNI components.
    /// </summary>
    public const string DataPlaneConfigKeyCniDedicatedNamespaceName = "cn_cni_dedicated_namespace_name";
    /// <summary>
    /// Dedicated namespace name for CI pipelines.
    /// </summary>
    public const string DataPlaneConfigKeyCiPipelinesDedicatedNamespaceName = "cn_ci_pipelines_dedicated_namespace_name";
    /// <summary>
    /// Node label key for system workloads.
    /// </summary>
    public const string DataPlaneConfigKeyNodePlacementSystemNodeLabelKey = "cn_node_placement_system_node_label_key";
    /// <summary>
    /// Node label value for system workloads.
    /// </summary>
    public const string DataPlaneConfigKeyNodePlacementSystemNodeLabelValue = "cn_node_placement_system_node_label_value";
    /// <summary>
    /// Node label key for application workloads.
    /// </summary>
    public const string DataPlaneConfigKeyNodePlacementApplicationNodeLabelKey = "cn_node_placement_application_node_label_key";
    /// <summary>
    /// Node label value for application workloads.
    /// </summary>
    public const string DataPlaneConfigKeyNodePlacementApplicationNodeLabelValue = "cn_node_placement_application_node_label_value";
    /// <summary>
    /// Username for system container registry access.
    /// </summary>
    public const string DataPlaneConfigKeyContainerRegistrySystemUsername = "cn_container_registry_system_username";
    /// <summary>
    /// Password for system container registry access.
    /// </summary>
    public const string DataPlaneConfigKeyContainerRegistrySystemPassword = "cn_container_registry_system_password";
    /// <summary>
    /// Enable HTTP proxy.
    /// </summary>
    public const string DataPlaneConfigKeyHttpProxyEnabled = "cn_http_proxy_enabled";
    /// <summary>
    /// HTTP proxy hostname.
    /// </summary>
    public const string DataPlaneConfigKeyHttpProxyHostname = "cn_http_proxy_hostname";
    /// <summary>
    /// HTTP proxy port.
    /// </summary>
    public const string DataPlaneConfigKeyHttpProxyPort = "cn_http_proxy_port";
    /// <summary>
    /// no_proxy hostname list.
    /// </summary>
    public const string DataPlaneConfigKeyHttpProxyNoProxyHostnames = "cn_http_proxy_no_proxy_hostnames";
    /// <summary>
    /// Enable S3 storage.
    /// </summary>
    public const string DataPlaneConfigKeyS3Enabled = "cn_s3_enabled";
    /// <summary>
    /// S3 access key.
    /// </summary>
    public const string DataPlaneConfigKeyS3StorageAccessKey = "cn_s3_storage_access_key";
    /// <summary>
    /// S3 secret key.
    /// </summary>
    public const string DataPlaneConfigKeyS3StorageSecretKey = "cn_s3_storage_secret_key";
    /// <summary>
    /// Role for S3 storage access.
    /// </summary>
    public const string DataPlaneConfigKeyS3StorageAccessRole = "cn_s3_storage_access_role";
    /// <summary>
    /// S3 storage region.
    /// </summary>
    public const string DataPlaneConfigKeyS3StorageRegion = "cn_s3_storage_region";
    /// <summary>
    /// Workload placement mode across nodes.
    /// </summary>
    public const string DataPlaneConfigKeyNodePlacementMode = "cn_node_placement_mode";
    /// <summary>
    /// File name for NPM config in the secret.
    /// </summary>
    public const string DataPlaneConfigKeyNpmrc = ".npmrc";
    /// <summary>
    /// File name for container registry config in the secret.
    /// </summary>
    public const string DataPlaneConfigKeyContainerRegistryConfigJson = "config.json";

    /// <summary>
    /// Label selector for the Pulumi operator pod.
    /// </summary>
    public const string OperatorPodLabelSelector =
        "app.kubernetes.io/name=cn-pulumi-kubernetes-operator,app.kubernetes.io/part-of=cn-data-plane";

    /// <summary>
    /// Label selector for the data plane workspace pod.
    /// </summary>
    public const string WorkspacePodLabelSelector =
        "app.kubernetes.io/name=cn-dp-manager,app.kubernetes.io/part-of=cn-data-plane";

    /// <summary>
    /// Default service account name used by the data plane.
    /// </summary>
    public const string ServiceAccountName = "cn-data-plane";

    /// <summary>
    /// PersistentVolumeClaim name used for Pulumi stack state.
    /// </summary>
    public const string PulumiStatePvcName = "cn-data-plane-state";

    /// <summary>
    /// Default Pulumi backend path for local state.
    /// </summary>
    public const string PulumiStatePath = "/pulumi-state";

    /// <summary>
    /// App name label used by the data plane stack.
    /// </summary>
    public const string PulumiStackLabel = "cn-dp-manager";

    /// <summary>
    /// Prefix for Pulumi operator resource names.
    /// </summary>
    public const string PulumiOperatorNamePrefix = "cn-pulumi-";

    /// <summary>
    /// Prefix for Pulumi workspace resources.
    /// </summary>
    public const string PulumiWorkspaceNamePrefix = "pulumi-";

    /// <summary>
    /// Default home directory for the Pulumi container.
    /// </summary>
    public const string PulumiHomePath = "/home/pulumi";

}
