
using System.Text.Json.Serialization;

namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Root configuration for the data plane operator.
/// </summary>
public sealed class OperatorConfig
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public ContainerRegistryConfig ContainerRegistry { get; set; }
        = new();

    public NpmRegistryConfig NpmRegistry { get; set; }
        = new();

    public KubernetesConfig Kubernetes { get; set; }
        = new();

    public HttpProxyConfig HttpProxy { get; set; }
        = new();

    public S3Config S3 { get; set; }
    = new();

    public ScmConfig Scm { get; set; }
    = new();

    public EnvironmentConfig Environment { get; set; }
        = new();

    public PulumiConfig Pulumi { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FluxCDConfig? FluxCD { get; set; }

    public SecurityConfig Security { get; set; } = new();

}

/// <summary>
/// Container registry connection settings.
/// </summary>
public sealed class ContainerRegistryConfig
{
    public string Hostname { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>
/// Kubernetes-related settings for the operator.
/// </summary>
public sealed class KubernetesConfig
{
    public KubernetesNamespacesConfig Namespaces { get; set; }
        = new();
    public KubernetesNodeLabelsConfig NodeLabels { get; set; }
        = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StorageClass { get; set; }
    public PodPlacementMode PodPlacementMode { get; set; } = PodPlacementMode.NodeSelectorAndTaints;
    public int SecurityContextRunAsId { get; set; } = 10001;

}

/// <summary>
/// Namespaces used by the operator.
/// </summary>
public sealed class KubernetesNamespacesConfig
{
    public KubernetesNamespaceConfig System { get; set; } = new();
    public KubernetesNamespaceConfig Cni { get; set; } = new();
    public KubernetesNamespaceConfig CiPipelines { get; set; } = new();
}

public sealed class KubernetesNamespaceConfig
{
    public string Name { get; set; } = "";
    public bool IsDedicatedRelativeTo(KubernetesNamespaceConfig systemNamespace) =>
        !string.Equals(Name, systemNamespace.Name, StringComparison.Ordinal);
}

/// <summary>
/// Node labels used for workload placement.
/// </summary>
public sealed class KubernetesNodeLabelsConfig
{
    public KubernetesNodeLabelConfig System { get; set; }
        = new();

    public KubernetesNodeLabelConfig Application { get; set; }
        = new();
}

/// <summary>
/// Key/value label definition for node selection.
/// </summary>
public sealed class KubernetesNodeLabelConfig
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// NPM registry connection settings.
/// </summary>
public sealed class NpmRegistryConfig
{
    public string Url { get; set; } = "";
    public string AccessToken { get; set; } = "";
}

/// <summary>
/// Scm repository access settings.
/// </summary>
public sealed class ScmConfig
{
    public string Url { get; set; } = "";
    public ScmAuthenticationMethod AuthenticationMethod { get; set; } = ScmAuthenticationMethod.UsernamePassword;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessToken { get; set; }
}


/// <summary>
/// HTTP proxy settings for outbound connections.
/// </summary>
public sealed class HttpProxyConfig
{
    public bool Enabled { get; set; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hostname { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Port { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NoProxy { get; set; }
}

/// <summary>
/// S3 storage settings.
/// </summary>
public sealed class S3Config
{
    public bool Enabled { get; set; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Bucket { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Region { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public S3AuthenticationMethod? AuthenticationMethod { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessKey { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecretKey { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IAMRole { get; set; }
}

/// <summary>
/// Environment settings.
/// </summary>
public sealed class EnvironmentConfig
{
    public string Name { get; set; } = "";
}

/// <summary>
/// Pulumi runtime configuration.
/// </summary>
public sealed class PulumiConfig
{
    public PulumiImagesConfig Images { get; set; } = new();

    /// <summary>
    /// Determines whether Pulumi operator CRDs should be installed.
    /// </summary>
    public bool InstallCrds { get; set; } = true;

    /// <summary>
    /// Pulumi passphrase for secrets encryption.
    /// </summary>
    public string Passphrase { get; set; } = "";
}

/// <summary>
/// Pulumi image versions.
/// </summary>
public sealed class PulumiImagesConfig
{
    /// <summary>
    /// Pulumi container image version (tag).
    /// Example: 3.213.0-nonroot
    /// </summary>
    public string RuntimeVersion { get; set; } = "";

    /// <summary>
    /// Pulumi plugins image version (tag).
    /// Example: 1.0.2
    /// </summary>
    public string PluginsVersion { get; set; } = "";
}

/// <summary>
/// FluxCD configuration.
/// </summary>
public sealed class FluxCDConfig
{
    /// <summary>
    /// Enables FluxCD source-controller provisioning.
    /// </summary>
    public bool Enabled { get; set; } = false;
    /// <summary>
    /// Determines whether FluxCD CRDs should be installed.
    /// </summary>
    public bool InstallCrds { get; set; } = true;

    public FluxCDImagesConfig Images { get; set; } = new();
}

/// <summary>
/// FluxCD image versions.
/// </summary>
public sealed class FluxCDImagesConfig
{
    /// <summary>
    /// FluxCD source-controller image version (tag).
    /// Example: v1.7.4
    /// </summary>
    public string SourceControllerVersion { get; set; } = "";
}

/// <summary>
/// Security-related settings.
/// </summary>
public sealed class SecurityConfig
{
    /// <summary>
    /// Base64-encoded custom CA bundle (PEM/CRT content).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomCaBase64 { get; set; }
}
