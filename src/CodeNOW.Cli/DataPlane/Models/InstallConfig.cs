using System.Text.Json.Serialization;

namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>Registry credentials for an Installer-created image pull Secret.</summary>
public sealed record InstallRegistryCredentials
{
    [JsonPropertyName("username")] public string? Username { get; init; }
    [JsonPropertyName("password")] public string? Password { get; init; }
}

/// <summary>Optional HTTP proxy for the Installer and both Job types.</summary>
public sealed record InstallHttpProxy
{
    [JsonPropertyName("hostname")] public string? Hostname { get; init; }
    [JsonPropertyName("port")] public int Port { get; init; }
    [JsonPropertyName("noProxyHostnames")] public string? NoProxyHostnames { get; init; }
}

/// <summary>The <c>cn dp install</c> document, defined by <c>cn-data-plane-install.schema.json</c>.</summary>
public sealed record InstallConfig
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    [JsonPropertyName("controlPlaneUrl")] public string? ControlPlaneUrl { get; init; }
    [JsonPropertyName("authorizationToken")] public string? AuthorizationToken { get; init; }
    [JsonPropertyName("imagePullSecret")] public string? ImagePullSecret { get; init; }
    [JsonPropertyName("containerRegistrySystemHostname")] public string? ContainerRegistrySystemHostname { get; init; }
    [JsonPropertyName("containerRegistrySystem")] public InstallRegistryCredentials? ContainerRegistrySystem { get; init; }
    [JsonPropertyName("httpProxy")] public InstallHttpProxy? HttpProxy { get; init; }
    [JsonPropertyName("namespaceName")] public string? NamespaceName { get; init; }
}

/// <summary>Unknown properties are rejected, matching <c>additionalProperties: false</c>.</summary>
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(InstallConfig))]
public sealed partial class InstallConfigJsonContext : JsonSerializerContext;
